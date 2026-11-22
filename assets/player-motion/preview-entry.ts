/**
 * 独立动作预览器入口。
 *
 * 不启动引擎、不需要 WebGPU：只把动作系统画在 body 上的 canvas 里。
 * 用途：
 *   1. 验证动作逻辑与关节衔接（浏览器直接打开即可看）
 *   2. 无 GPU / 无头环境下也能截图取证
 *
 * 支持 URL 参数：
 *   ?script=1   自动演示（默认开）
 *   ?freeze=14  推进到第 14 秒后冻结，便于截图固定姿态
 */

import { createMotion, stepMotion, type MotionRuntime } from './motion.ts';
import { createMotionView } from './view.ts';
import { createCorridorProbe, createKeyboardInput, toMotionInput } from './input.ts';

const params = new URLSearchParams(location.search);
/** ?at=12 → 用固定步长快进到第 12 秒并冻结，适合截图取证。 */
const atSecond = params.has('at') ? Number(params.get('at')) : null;
const freezeAt = params.has('freeze') ? Number(params.get('freeze')) : null;
const autoScript = params.get('script') !== '0';

const stage = document.createElement('div');
stage.style.cssText =
  'position:relative;width:100vw;height:100vh;margin:0;background:#0a1114;overflow:hidden;';
document.body.style.margin = '0';
document.body.style.background = '#0a1114';
document.body.appendChild(stage);

const view = createMotionView(stage);
if (view === null) throw new Error('canvas 2d context unavailable');

// 调试行：确认 URL 参数与当前状态（截图取证时很有用）
const debug = document.createElement('div');
debug.style.cssText =
  'position:absolute;right:10px;bottom:8px;color:#7fe3d4;font:11px ui-monospace,monospace;opacity:.75;pointer-events:none;';
debug.textContent = `search="${location.search}" at=${params.get('at')} auto=${autoScript}`;
stage.appendChild(debug);

const probe = createCorridorProbe();
const keyboard = createKeyboardInput(window);
const runtime: MotionRuntime = createMotion([0, 0, 0]);

/**
 * 自动演示脚本：按时间轴直接给出 MotionInput 的运动意图。
 * 注意这里用的是 moveX/moveZ（-1..1），不是键盘的 forward/back，
 * 因此不走 toMotionInput，避免字段语义错位。
 */
function scriptedIntent(t: number): {
  moveX: number;
  moveZ: number;
  crouch: boolean;
  sprint: boolean;
  rollPressed: boolean;
} {
  if (t < 2.5) return { moveX: 0, moveZ: 0, crouch: false, sprint: false, rollPressed: false };
  if (t < 6) return { moveX: 0, moveZ: 1, crouch: false, sprint: false, rollPressed: false };
  if (t < 8.5) return { moveX: 0, moveZ: 1, crouch: false, sprint: true, rollPressed: false };
  if (t < 11) return { moveX: 0, moveZ: 0, crouch: true, sprint: false, rollPressed: false };
  if (t < 14.2) return { moveX: 1, moveZ: 0, crouch: false, sprint: false, rollPressed: false };
  if (t < 14.5) return { moveX: 0, moveZ: 0, crouch: false, sprint: false, rollPressed: true };
  if (t < 18) return { moveX: 0, moveZ: 0, crouch: false, sprint: false, rollPressed: false };
  if (t < 19) return { moveX: -1, moveZ: 0, crouch: false, sprint: false, rollPressed: false };
  return { moveX: 0, moveZ: 0, crouch: false, sprint: false, rollPressed: false };
}

/** 把「运动意图 + 掩体信息」合成 stepMotion 需要的输入。 */
function composeInput(intent: ReturnType<typeof scriptedIntent>) {
  const cover = probe.query(runtime.rootPos, runtime.facing);
  return {
    moveX: intent.moveX,
    moveZ: intent.moveZ,
    crouch: intent.crouch,
    sprint: intent.sprint,
    rollPressed: intent.rollPressed,
    coverAvailable: cover.available,
    coverSide: cover.side,
    coverDistance: cover.distance,
    coverNormal: cover.normal,
  };
}

/**
 * 推进一帧并限制在走廊内（模拟墙体阻挡）。
 * 位置被钳制时同时压掉速度——否则会「顶着墙还在加速」，HUD 上的速度是假的。
 */
function tick(intent: ReturnType<typeof scriptedIntent>, dt: number): void {
  stepMotion(runtime, composeInput(intent), dt);
  const clamped = probe.clamp(runtime.rootPos);
  if (clamped !== runtime.rootPos) {
    runtime.rootPos = clamped;
    runtime.speed *= 0.3;
  }
}

let scriptTime = 0;
let lastMs = performance.now();
let frozen = false;

// 固定步长快进：截图与自动化验证用，不受真实帧率影响
if (atSecond !== null) {
  const dt = 1 / 60;
  const steps = Math.round(atSecond / dt);
  for (let i = 0; i < steps; i++) {
    scriptTime = i * dt;
    tick(scriptedIntent(scriptTime), dt);
  }
  scriptTime = atSecond;
  frozen = true;
}

function frame(): void {
  const now = performance.now();
  const dt = Math.min(0.05, (now - lastMs) / 1000);
  lastMs = now;

  if (freezeAt !== null && scriptTime >= freezeAt) frozen = true;

  if (!frozen) {
    scriptTime += dt;
    if (autoScript) {
      tick(scriptedIntent(scriptTime), dt);
    } else {
      const cover = probe.query(runtime.rootPos, runtime.facing);
      stepMotion(runtime, toMotionInput(keyboard.read(), cover), dt);
      const clamped = probe.clamp(runtime.rootPos);
      if (clamped !== runtime.rootPos) {
        runtime.rootPos = clamped;
        runtime.speed *= 0.3;
      }
    }
  }

  const cover = probe.query(runtime.rootPos, runtime.facing);
  view!.render(runtime, { ...cover, walls: probe.walls(runtime.rootPos) });
  requestAnimationFrame(frame);
}

requestAnimationFrame(frame);
