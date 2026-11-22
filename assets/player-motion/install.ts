/**
 * 接入层：把玩家动作系统挂到引擎的 Update 系统上。
 *
 * 合并方式（给负责合并的人）：
 *   1. 把整个 assets/player-motion/ 目录拷进目标工程
 *   2. 在任意 engine realm 插件的 apply(ctx) 里加一行：
 *        installMotionDemo(ctx);
 *   3. 不需要改 forge.json —— 这不是独立的 Pack root
 *
 * 视图有三种：
 *   mesh3d   引擎真 3D 白模（MeshFilter + MeshRenderer + 等距相机），默认
 *   canvas2d canvas 手绘火柴人（不依赖 GPU，用于排查渲染问题）
 *   none     只要动作数据，不画任何东西
 */

import { Update, type World } from '@forgeax/engine/ecs';

import { createMotion, stepMotion, type MotionRuntime } from './motion.ts';
import { createMotionView } from './view.ts';
import { createMesh3D, type Mesh3DHandle } from './mesh3d.ts';
import { createCorridorProbe, createKeyboardInput, toMotionInput } from './input.ts';

const SYSTEM_NAME = 'player-motion-step';

/** 最小可用的 ctx 形状，避免绑定具体插件类型。 */
interface MotionHostContext {
  readonly world: World;
  effect?(factory: () => () => void): void;
}

export type MotionViewMode = 'mesh3d' | 'canvas2d' | 'none';

export interface InstallMotionOptions {
  /** 视图形态，默认 mesh3d（引擎内 3D 白模）。 */
  view?: MotionViewMode;
  /** canvas2d 视图的挂载点，默认 #game-ui。 */
  mount?: string;
  /** 出生点，默认原点。 */
  spawn?: readonly [number, number, number];
  /**
   * 走廊半宽（演示掩体用），默认 1.0m。
   * 与 MOTION_CONFIG.rollDistance 是配套的：1.0 - 身体半径 0.3 = 0.7，
   * 翻滚 1.5m 正好从一侧贴墙到另一侧，改一个就要同步改另一个。
   */
  corridorHalfWidth?: number;
  /** mesh3d：是否自带相机（主项目已有相机时传 false）。 */
  ownCamera?: boolean;
  /** mesh3d：是否自带光照。 */
  ownLight?: boolean;
}

export interface MotionHandle {
  readonly runtime: MotionRuntime;
  dispose(): void;
}

/**
 * 安装动作演示。返回句柄便于测试或手动卸载。
 */
export function installMotionDemo(ctx: MotionHostContext, options: InstallMotionOptions = {}): MotionHandle {
  const mode: MotionViewMode = options.view ?? 'mesh3d';
  const mountSelector = options.mount ?? '#game-ui';

  let canvasView: ReturnType<typeof createMotionView> = null;
  if (mode === 'canvas2d') {
    const host = document.querySelector(mountSelector);
    canvasView = host instanceof HTMLElement ? createMotionView(host) : null;
    if (canvasView === null) {
      console.warn(`[player-motion] mount point ${mountSelector} not found; falls back to mesh3d`);
    }
  }

  let meshView: Mesh3DHandle | null = null;
  if (mode === 'mesh3d' || (mode === 'canvas2d' && canvasView === null)) {
    try {
      meshView = createMesh3D({
        world: ctx.world,
        ownCamera: options.ownCamera,
        ownLight: options.ownLight,
      });
    } catch (err) {
      console.warn('[player-motion] mesh3d 创建失败，动作逻辑仍在运行：', err);
      meshView = null;
    }
  }

  const keyboard = createKeyboardInput(window);
  const probe = createCorridorProbe(options.corridorHalfWidth ?? 1.0);
  const spawn = options.spawn ?? [0, 0, 0];
  const runtime = createMotion([spawn[0], spawn[1], spawn[2]]);

  let lastMs = performance.now();

  const removeSystem = () => {
    try {
      ctx.world.removeSystem(Update, SYSTEM_NAME).unwrap();
    } catch {
      // 已卸载时忽略
    }
  };

  ctx.world
    .addSystem(Update, {
      name: SYSTEM_NAME,
      queries: [],
      fn: () => {
        const now = performance.now();
        const dt = Math.min(0.05, (now - lastMs) / 1000);
        lastMs = now;

        const cover = probe.query(runtime.rootPos, runtime.facing);
        const input = toMotionInput(keyboard.read(), cover);
        stepMotion(runtime, input, dt);
        const clamped = probe.clamp(runtime.rootPos);
        if (clamped !== runtime.rootPos) {
          runtime.rootPos = clamped;
          runtime.speed *= 0.3; // 撞墙：速度被墙体吃掉，避免 HUD 显示假速度
        }

        meshView?.sync(runtime);
        canvasView?.render(runtime, { ...cover, walls: probe.walls(runtime.rootPos) });
      },
    })
    .unwrap();

  let disposed = false;
  const dispose = () => {
    if (disposed) return;
    disposed = true;
    keyboard.dispose();
    canvasView?.dispose();
    meshView?.dispose();
    removeSystem();
  };

  ctx.effect?.(() => dispose);

  return { runtime, dispose };
}

/**
 * 只要动作数据、不要任何视图的便捷入口。
 * 适合把动作系统当纯逻辑模块嵌进主工程（自己驱动角色模型）。
 */
export function createMotionOnly(spawn: readonly [number, number, number] = [0, 0, 0]): MotionRuntime {
  return createMotion([spawn[0], spawn[1], spawn[2]]);
}
