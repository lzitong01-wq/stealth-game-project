/**
 * 接入层：把玩家动作系统挂到引擎的 Update 系统上。
 *
 * 合并方式（给负责合并的人）：
 *   1. 把整个 assets/player-motion/ 目录拷进目标工程
 *   2. 在任意 engine realm 插件的 apply(ctx) 里加一行：
 *        installMotionDemo(ctx, { mount: '#game-ui' });
 *
 * 动作逻辑本身（skeleton / pose / motion / input）不依赖引擎，
 * 只有这个文件和 view.ts 用到 DOM。
 */

import { Update, type World } from '@forgeax/engine/ecs';

import { createMotion, stepMotion, type MotionRuntime } from './motion.ts';
import { createMotionView } from './view.ts';
import { createCorridorProbe, createKeyboardInput, toMotionInput } from './input.ts';

const SYSTEM_NAME = 'player-motion-step';

/** 最小可用的 ctx 形状，避免绑定具体插件类型。 */
interface MotionHostContext {
  readonly world: World;
  effect?(factory: () => () => void): void;
}

export interface InstallMotionOptions {
  /** UI 挂载点，默认 #game-ui。 */
  mount?: string;
  /** 出生点，默认原点。 */
  spawn?: readonly [number, number, number];
  /**
   * 走廊半宽（演示掩体用），默认 1.0m。
   * 与 MOTION_CONFIG.rollDistance 是配套的：1.0 - 身体半径 0.25 = 0.75，
   * 翻滚 1.5m 正好从一侧贴墙到另一侧，改一个就要同步改另一个。
   */
  corridorHalfWidth?: number;
}

export interface MotionHandle {
  readonly runtime: MotionRuntime;
  dispose(): void;
}

/**
 * 安装动作演示。返回句柄便于测试或手动卸载。
 */
export function installMotionDemo(ctx: MotionHostContext, options: InstallMotionOptions = {}): MotionHandle {
  const mountSelector = options.mount ?? '#game-ui';
  const host = document.querySelector(mountSelector);

  const view = host instanceof HTMLElement ? createMotionView(host) : null;
  if (view === null) {
    console.warn(`[player-motion] mount point ${mountSelector} not found; skeleton view disabled`);
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
        view?.render(runtime, { ...cover, walls: probe.walls(runtime.rootPos) });
      },
    })
    .unwrap();

  let disposed = false;
  const dispose = () => {
    if (disposed) return;
    disposed = true;
    keyboard.dispose();
    view?.dispose();
    removeSystem();
  };

  ctx.effect?.(() => dispose);

  return { runtime, dispose };
}
