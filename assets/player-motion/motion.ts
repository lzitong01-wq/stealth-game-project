/**
 * 玩家动作状态机。
 *
 * 覆盖的动作：
 *   idle   站立待机（轻微呼吸）
 *   walk   步行（WASD，默认速度）
 *   run    疾走（按住 Shift）
 *   crouch 下蹲（按住 Ctrl；可蹲行）
 *   cover  贴墙躲藏（靠近可躲藏墙时自动进入）
 *   roll   翻滚换位（贴墙时按 Space，从一侧掩体滚到另一侧）
 *
 * 全部为纯逻辑：不读输入设备、不碰渲染、不依赖引擎。
 * 输入由调用方每帧传入，输出是姿态 + 根位移 + 朝向。
 */

import {
  clamp,
  clonePose,
  easeInOut,
  easeOutCubic,
  lerp,
  lerpPose,
  restPose,
  type Euler,
  type Pose,
} from './pose.ts';
import type { JointName, Vec3 } from './skeleton.ts';

export type MotionState = 'idle' | 'walk' | 'run' | 'crouch' | 'cover' | 'roll';

export const MOTION_STATES: readonly MotionState[] = [
  'idle',
  'walk',
  'run',
  'crouch',
  'cover',
  'roll',
];

/** 每个状态的显示名，HUD 直接用。 */
export const STATE_LABEL: Record<MotionState, string> = {
  idle: '待机',
  walk: '步行',
  run: '疾走',
  crouch: '下蹲',
  cover: '贴墙躲藏',
  roll: '翻滚换位',
};

/** 每帧传入的外部输入。 */
export interface MotionInput {
  /** 左右输入，-1..1（右为正）。 */
  moveX: number;
  /** 前后输入，-1..1（前为正）。 */
  moveZ: number;
  crouch: boolean;
  sprint: boolean;
  /** 翻滚键是否「刚按下」。 */
  rollPressed: boolean;
  /** 附近是否存在可躲藏的墙。 */
  coverAvailable: boolean;
  /** 墙在角色哪一侧：-1 左、1 右。 */
  coverSide: -1 | 1;
  /** 到墙的水平距离（米）。 */
  coverDistance: number;
  /**
   * 墙的表面法线（由墙指向角色）。
   * 只靠 coverSide 在「正对墙」时会退化，判断不出该往哪脱离、该朝哪站。
   */
  coverNormal?: Vec3;
}

/** 动作参数。集中放这里，方便美术/策划调。 */
export const MOTION_CONFIG = {
  walkSpeed: 1.7,
  runSpeed: 4.3,
  crouchSpeed: 0.85,
  /** 步频（每秒完整步态周期数）。 */
  walkCycleHz: 0.95,
  runCycleHz: 1.55,
  crouchCycleHz: 0.6,
  /** 转身速度（度/秒）。 */
  turnSpeed: 420,
  /** 进入下蹲的过渡时间（秒）。 */
  crouchBlend: 0.28,
  /** 进入/退出贴墙的过渡时间。 */
  coverBlend: 0.34,
  /** 翻滚时长。 */
  rollDuration: 0.58,
  /**
   * 翻滚横向位移（米）。
   * 取 1.5 = 2 ×（走廊半宽 1.0 - 身体半径 0.25），
   * 保证从一侧贴墙正好滚到另一侧贴墙——「两墙之间用翻滚换位」靠这个成立。
   */
  rollDistance: 1.5,
  /** 触发贴墙的最大距离。 */
  coverEnterDistance: 1.15,
  /** 贴近到这个距离以内才真正进入贴墙（更远只是「附近有墙」）。 */
  coverSnapDistance: 0.45,
  /** 翻滚结束后，距离墙多近算「落到对面掩体」。 */
  rollCoverSnap: 0.6,
  /** 贴墙时可移动的最大速度（超过则脱离）。 */
  coverExitSpeed: 0.6,
  /**
   * 贴墙时朝墙推的方向阈值：超过它算「想贴住」，低于它算「想离开」。
   * 松手（无输入）时保持贴墙，不会自己弹开。
   */
  coverHoldDot: 0.55,
  /** 蹲下时骨盆下降量（米）。 */
  crouchDrop: 0.38,
} as const;

/** 运行时状态。由 createMotion 创建，stepMotion 原地更新。 */
export interface MotionRuntime {
  state: MotionState;
  /** 进入当前状态后经过的时间。 */
  stateTime: number;
  /** 上一状态与切换瞬间的姿态，用于过渡。 */
  prevState: MotionState;
  prevPose: Pose;
  /** 当前状态自己生成的「目标姿态」。 */
  targetPose: Pose;
  /** 过渡进度 0..1。 */
  blend: number;
  blendDuration: number;
  /** 最终输出姿态（已混合）。 */
  pose: Pose;

  /** 步态相位（弧度），保证停走切换时不跳变。 */
  gaitPhase: number;
  /** 当前水平速度（m/s）。 */
  speed: number;
  /** 朝向（度，绕 Y）。 */
  facing: number;
  /** 根位置（脚底世界坐标）。 */
  rootPos: Vec3;
  /** 根整体旋转（度）。翻滚时绕 Z 轴转。 */
  rootEuler: Euler;

  /** 下蹲混合量 0..1（不是布尔，保证起身过渡连续）。 */
  crouchAmount: number;
  /** 贴墙混合量 0..1。 */
  coverAmount: number;
  /** 翻滚进度 0..1。 */
  rollT: number;
  /**
   * 翻滚位移方向（水平单位向量，指向「远离起手时那面墙」）。
   * 必须锁存：翻滚横跨走廊时 coverNormal 会在中途翻到对面墙，
   * 每帧重算会让位移方向来回抽。
   */
  rollVec: Vec3;
  /** 翻滚方向：-1 左、1 右（相对角色，只影响翻转的转向）。 */
  rollDir: -1 | 1;
  /** 翻滚前的贴墙侧。 */
  rollFromSide: -1 | 1;
  /**
   * 当前贴的是哪一侧的墙。
   * 单独记一份，避免淡出 cover 时 input.coverSide 已经翻到对面，
   * 导致姿态在一帧内左右翻转。
   */
  coverSide: -1 | 1;
  /** 上一次的翻滚键状态，用于检测「刚按下」。 */
  wasRollPressed: boolean;
}

export function createMotion(spawn: Vec3 = [0, 0, 0]): MotionRuntime {
  const p = restPose();
  return {
    state: 'idle',
    stateTime: 0,
    prevState: 'idle',
    prevPose: clonePose(p),
    targetPose: clonePose(p),
    blend: 1,
    blendDuration: 0,
    pose: clonePose(p),
    gaitPhase: 0,
    speed: 0,
    facing: 0,
    rootPos: spawn,
    rootEuler: [0, 0, 0],
    crouchAmount: 0,
    coverAmount: 0,
    rollT: 0,
    rollVec: [0, 0, 1],
    rollDir: 1,
    rollFromSide: 1,
    coverSide: 1,
    wasRollPressed: false,
  };
}

function setState(rt: MotionRuntime, next: MotionState, blendDuration: number): void {
  if (rt.state === next) return;
  rt.prevState = rt.state;
  rt.prevPose = clonePose(rt.pose);
  rt.state = next;
  rt.stateTime = 0;
  rt.blend = 0;
  rt.blendDuration = Math.max(0.0001, blendDuration);
}

/* ------------------------------------------------------------------ *
 * 各状态的目标姿态
 * ------------------------------------------------------------------ */

/**
 * 设置一条手臂的姿态。
 *
 * ⚠️ 这里有个必须记住的几何事实：上臂/前臂骨段沿 ±X 伸展（见 BONE_OFFSET），
 * 所以「绕 X 轴旋转」只会让手臂自转，完全不改变它的指向。
 * 把摆臂角度写在 shoulder.x 上是无效的 —— 这正是早期版本手臂一直是 T-pose 的原因。
 *
 * 正确写法：
 *   1. 用 Z 旋转把手臂从平举转到自然下垂（欧拉顺序 Ry*Rx*Rz，Rz 先作用）
 *   2. 再用手臂侧的 X 旋转做前后摆（此时手臂已沿 -Y，X 旋转才产生矢状面摆动）
 *
 * @param side  -1 = 沿 +X 那条（shoulderL），1 = 沿 -X 那条（shoulderR）
 * @param down  垂下角度：0 = 平举，90 = 完全垂在体侧
 * @param swing 前后摆（度，正 = 向后）
 * @param bend  肘弯曲（度，正 = 前臂向前）
 * @param tuck  肘外展（度，正 = 前臂向外张开）
 */
function poseArm(p: Pose, side: -1 | 1, down: number, swing: number, bend: number, tuck = 0): void {
  const sh: JointName = side < 0 ? 'shoulderL' : 'shoulderR';
  const el: JointName = side < 0 ? 'elbowL' : 'elbowR';
  p[sh] = [swing, 0, side * down];
  p[el] = [-bend, 0, side * tuck];
}

/** 站立待机：手臂自然下垂，极轻微呼吸。 */
function poseIdle(t: number): Pose {
  const p = restPose();
  const breathe = Math.sin(t * 1.6) * 1.2;
  p.chest = [breathe, 0, 0];
  p.head = [-breathe * 0.5, 0, 0];
  poseArm(p, -1, 74, breathe * 0.8, 14, 7);
  poseArm(p, 1, 74, breathe * 0.8, 14, 7);
  return p;
}

/**
 * 步态公共部分：走路/跑步/蹲行。
 * @param phase 当前步态相位（**弧度**，由 stepMotion 累加，直接用不再换算）
 * @param amp   摆动幅度（度）
 * @param kneeAmp 膝盖弯曲幅度
 * @param lean  躯干前倾（度）
 * @param crouch 蹲下混合量 0..1
 */
function poseGait(phase: number, amp: number, kneeAmp: number, lean: number, crouch: number): Pose {
  const p = restPose();
  const a = phase;

  // 左右腿相位相反
  const swingL = Math.sin(a) * amp;
  const swingR = Math.sin(a + Math.PI) * amp;

  // 支撑腿膝盖抬起时弯曲，摆动腿更弯
  const kneeL = Math.max(0, -Math.sin(a)) * kneeAmp;
  const kneeR = Math.max(0, -Math.sin(a + Math.PI)) * kneeAmp;

  // 腿：摆动 + 蹲下屈曲（髋前屈 62°、膝后折 108°、踝背屈）
  p.hipL = [swingL + crouch * 62, 0, 0];
  p.hipR = [swingR + crouch * 62, 0, 0];
  p.kneeL = [-kneeL - crouch * 108, 0, 0];
  p.kneeR = [-kneeR - crouch * 108, 0, 0];
  p.ankleL = [kneeL * 0.35 + crouch * 46, 0, 0];
  p.ankleR = [kneeR * 0.35 + crouch * 46, 0, 0];

  // 手臂反向摆动：左腿前摆时右臂前摆（对侧摆臂）
  // 摆幅比腿小，符合真实步态；蹲下时手臂前收、肘弯明显加大，避免"蹲着的 T-pose"
  const armSwingL = -swingL * 0.85;
  const armSwingR = -swingR * 0.85;
  const armDown = 74 - crouch * 10;
  const armBend = 14 + crouch * 50;
  const armCrouchSwing = -crouch * 18;
  poseArm(p, -1, armDown, armSwingL + armCrouchSwing, armBend, 7);
  poseArm(p, 1, armDown, armSwingR + armCrouchSwing, armBend, 7);

  // 躯干：跑步前倾 + 步态带来的轻微上下起伏（用脊柱俯仰近似）
  p.spine = [lean * 0.45 + crouch * 10, 0, 0];
  p.chest = [lean * 0.35 + crouch * 8, 0, Math.sin(a) * 2.5];
  p.neck = [-lean * 0.5, 0, 0];
  p.head = [-lean * 0.35, 0, 0];

  // 蹲下时重心后移一点，别显得要往前栽
  if (crouch > 0.001) {
    p.pelvis = [-crouch * 12, 0, 0];
  }
  return p;
}

/**
 * 贴墙躲藏：侧身靠墙，内侧手臂上举扶墙，外侧手臂收紧，身体略侧倾。
 * @param side -1 左墙、1 右墙
 * @param t 状态时间，用于轻微呼吸
 */
function poseCover(side: -1 | 1, t: number): Pose {
  const p = restPose();
  const breathe = Math.sin(t * 1.4) * 0.8;

  // 身体侧倾（绕 Z），并稍微压低重心
  p.pelvis = [-6, 0, side * -8];
  p.spine = [4, 0, side * -6];
  p.chest = [2 + breathe, 0, side * -5];
  p.neck = [-4, 0, side * 3];
  p.head = [-2, 0, side * 4];

  // 注意骨架命名与实际左右相反：shoulderL 的骨段沿 +X，
  // 而角色朝向 0° 时「右方」也是 +X。所以「墙在 +X 侧」要抬的是 shoulderL。
  // 内侧（靠墙那侧）手臂上举扶墙：down 取负值 = 抬到平举以上
  poseArm(p, (side * -1) as -1 | 1, -22, -6, 22, 24);

  // 外侧手臂收紧在身前，肘弯得多一些，像个"缩起来"的躲藏姿势
  poseArm(p, side, 58, -34, 82, 42);

  // 双腿微屈，内侧腿承担重心
  p.hipL = [10, 0, 0];
  p.hipR = [14, 0, 0];
  p.kneeL = [-18, 0, 0];
  p.kneeR = [-24, 0, 0];
  p.ankleL = [8, 0, 0];
  p.ankleR = [10, 0, 0];
  return p;
}

/** 翻滚：团身抱膝。整体旋转由 rootEuler 负责，这里只管四肢。 */
function poseRoll(t: number): Pose {
  const p = restPose();
  // 起手 0.12s 内收身，结束前 0.15s 展开
  const tuck = t < 0.12 ? easeOutCubic(t / 0.12) : t > 0.43 ? 1 - easeInOut((t - 0.43) / 0.15) : 1;
  p.pelvis = [-20 * tuck, 0, 0];
  p.spine = [28 * tuck, 0, 0];
  p.chest = [22 * tuck, 0, 0];
  p.neck = [26 * tuck, 0, 0];
  p.head = [18 * tuck, 0, 0];
  p.hipL = [96 * tuck, 0, 0];
  p.hipR = [92 * tuck, 0, 0];
  p.kneeL = [-128 * tuck, 0, 0];
  p.kneeR = [-124 * tuck, 0, 0];
  p.ankleL = [34 * tuck, 0, 0];
  p.ankleR = [32 * tuck, 0, 0];
  // 抱膝：手臂前伸环抱小腿（tuck=0 时恢复自然下垂）
  const armDown = 74 - 40 * tuck;
  const armSwing = -52 * tuck;
  const armBend = 20 + 100 * tuck;
  const armTuck = 7 + 16 * tuck;
  poseArm(p, -1, armDown, armSwing, armBend, armTuck);
  poseArm(p, 1, armDown, armSwing, armBend, armTuck);
  return p;
}

/* ------------------------------------------------------------------ *
 * 主循环
 * ------------------------------------------------------------------ */

/**
 * 推进一帧。
 * @param rt    运行时状态（会被原地更新）
 * @param input 本帧输入
 * @param dt    帧间隔（秒）
 */
export function stepMotion(rt: MotionRuntime, input: MotionInput, dt: number): MotionRuntime {
  const cfg = MOTION_CONFIG;
  const step = clamp(dt, 0, 0.1);
  rt.stateTime += step;

  // ---- 1. 输入归一化 ----
  let ix = clamp(input.moveX, -1, 1);
  let iz = clamp(input.moveZ, -1, 1);
  const mag = Math.hypot(ix, iz);
  if (mag > 1) {
    ix /= mag;
    iz /= mag;
  }
  const wantsMove = Math.hypot(ix, iz) > 0.12;

  // ---- 2. 目标朝向：朝输入方向转 ----
  // 贴墙时朝向交给「沿墙方向」处理，翻滚时朝向交给整体旋转，
  // 两者都不能被输入方向覆盖，否则会跟第 6 步打架。
  if (wantsMove && rt.state !== 'cover' && rt.state !== 'roll') {
    const targetFacing = (Math.atan2(ix, iz) * 180) / Math.PI;
    let diff = targetFacing - rt.facing;
    while (diff > 180) diff -= 360;
    while (diff < -180) diff += 360;
    const maxTurn = cfg.turnSpeed * step;
    rt.facing += clamp(diff, -maxTurn, maxTurn);
  }

  // ---- 3. 下蹲混合 ----
  const crouchTarget = input.crouch ? 1 : 0;
  if (rt.crouchAmount < crouchTarget) {
    rt.crouchAmount = Math.min(crouchTarget, rt.crouchAmount + step / cfg.crouchBlend);
  } else if (rt.crouchAmount > crouchTarget) {
    rt.crouchAmount = Math.max(crouchTarget, rt.crouchAmount - step / cfg.crouchBlend);
  }

  // ---- 4. 贴墙判定 ----
  // 贴近墙且没在下蹲 → 可贴墙。贴墙时角色站定，脱离方式：
  //   1. 按 Ctrl 下蹲（贴墙时下蹲 = 起身离开）
  //   2. 朝背离墙 / 沿墙的方向推方向键
  //   3. 按 Space 翻滚到对面
  // 第 2 条是必需的：否则贴墙后速度被锁为 0，玩家会被永久粘在墙上。
  // 用「墙的法线」而不是「左右侧」判断方向：正对墙时左右会退化。
  const coverNormal: Vec3 = input.coverNormal ?? (input.coverSide > 0 ? [-1, 0, 0] : [1, 0, 0]);
  // 输入与「墙→角色」同向 = 在往墙里推（正）；反向 = 想离开（负）
  const pushIntoWall = -(ix * coverNormal[0] + iz * coverNormal[2]);
  const wantsLeaveCover = wantsMove && pushIntoWall < 0.4;
  const nearCover =
    input.coverAvailable &&
    input.coverDistance <= cfg.coverSnapDistance &&
    !input.crouch &&
    !wantsLeaveCover;

  // ---- 5. 翻滚：优先级最高 ----
  const rollEdge = input.rollPressed && !rt.wasRollPressed;
  rt.wasRollPressed = input.rollPressed;

  if (rt.state === 'roll') {
    rt.rollT += step / cfg.rollDuration;
    if (rt.rollT >= 1) {
      rt.rollT = 1;
      // 翻滚结束：落到对面墙附近就继续贴墙，否则起身
      if (input.coverAvailable && input.coverDistance <= cfg.rollCoverSnap) {
        rt.coverSide = input.coverSide;
        setState(rt, 'cover', cfg.coverBlend * 0.5);
        rt.coverAmount = 1;
      } else {
        setState(rt, 'idle', 0.22);
        rt.coverAmount = 0;
      }
    }
  } else if (rollEdge && (rt.state === 'cover' || (nearCover && rt.speed < cfg.coverExitSpeed))) {
    setState(rt, 'roll', 0.1);
    rt.rollT = 0;
    rt.rollFromSide = input.coverSide;
    rt.coverSide = input.coverSide;
    // 位移方向 = 墙的法线方向（法线本身就是「由墙指向角色」= 远离墙），锁存下来
    rt.rollVec = [coverNormal[0], 0, coverNormal[2]];
    // 翻转方向跟随位移方向相对角色的左右
    const yawR = (rt.facing * Math.PI) / 180;
    const rightX = Math.cos(yawR);
    const rightZ = -Math.sin(yawR);
    rt.rollDir = rt.rollVec[0] * rightX + rt.rollVec[2] * rightZ >= 0 ? 1 : -1;
    rt.coverAmount = 1;
    rt.speed = 0;
  }

  // ---- 6. 状态迁移（翻滚期间锁定） ----
  if (rt.state !== 'roll') {
    const wantCrouch = rt.crouchAmount > 0.5;
    if (nearCover) {
      if (rt.state !== 'cover') {
        setState(rt, 'cover', cfg.coverBlend);
      }
      rt.coverSide = input.coverSide;
      rt.coverAmount = Math.min(1, rt.coverAmount + step / cfg.coverBlend);
    } else {
      if (rt.state === 'cover') {
        setState(rt, wantCrouch ? 'crouch' : wantsMove ? (input.sprint ? 'run' : 'walk') : 'idle', cfg.coverBlend);
      }
      rt.coverAmount = Math.max(0, rt.coverAmount - step / cfg.coverBlend);

      if (wantCrouch) {
        if (rt.state !== 'crouch') setState(rt, 'crouch', cfg.crouchBlend);
      } else if (wantsMove) {
        const next: MotionState = input.sprint ? 'run' : 'walk';
        if (rt.state !== next) setState(rt, next, 0.2);
      } else if (rt.state !== 'idle') {
        setState(rt, 'idle', 0.24);
      }
    }
  }

  // ---- 6.5 贴墙转身：转成「沿墙侧身」，而不是正面贴着墙 ----
  // 只把朝向拧十几度是假侧身，看起来还是正面撞墙。
  // 这里真的把朝向转到沿墙方向，再据此重算"墙在左还是右"，
  // 保证扶墙的手臂始终是靠墙那一只。
  if (rt.state === 'cover') {
    // 沿墙方向 = normal × up
    const tx = -coverNormal[2];
    const tz = coverNormal[0];
    const fwdX = Math.sin((rt.facing * Math.PI) / 180);
    const fwdZ = Math.cos((rt.facing * Math.PI) / 180);
    // 两个沿墙方向里选离当前朝向更近的，避免 180° 猛转
    const sgn = fwdX * tx + fwdZ * tz >= 0 ? 1 : -1;
    const targetFacing = (Math.atan2(tx * sgn, tz * sgn) * 180) / Math.PI;
    let d = targetFacing - rt.facing;
    while (d > 180) d -= 360;
    while (d < -180) d += 360;
    const maxTurn = cfg.turnSpeed * step;
    rt.facing += clamp(d, -maxTurn, maxTurn);

    // normal 由墙指向角色：与「角色右方」同向 → 说明墙在角色左侧
    const yawR = (rt.facing * Math.PI) / 180;
    const dotRight = coverNormal[0] * Math.cos(yawR) + coverNormal[2] * -Math.sin(yawR);
    rt.coverSide = dotRight > 0 ? -1 : 1;
  }

  // ---- 7. 速度 ----
  let targetSpeed = 0;
  if (rt.state === 'walk') targetSpeed = cfg.walkSpeed;
  else if (rt.state === 'run') targetSpeed = cfg.runSpeed;
  else if (rt.state === 'crouch') targetSpeed = wantsMove ? cfg.crouchSpeed : 0;
  else if (rt.state === 'cover') targetSpeed = 0; // 贴墙站定，靠 Ctrl 脱离
  else if (rt.state === 'roll') targetSpeed = cfg.rollDistance / cfg.rollDuration;

  // 加减速平滑（0.12s 左右到目标速度）
  const accel = targetSpeed > rt.speed ? 14 : 18;
  rt.speed += clamp(targetSpeed - rt.speed, -accel * step, accel * step);

  // ---- 8. 位移 ----
  const yawRad = (rt.facing * Math.PI) / 180;
  const fwd: Vec3 = [Math.sin(yawRad), 0, Math.cos(yawRad)];

  if (rt.state === 'roll') {
    // 翻滚：沿「远离起手墙」的方向横移，起手快、收尾缓
    const prevT = clamp(rt.rollT - step / cfg.rollDuration, 0, 1);
    const delta = easeInOut(rt.rollT) - easeInOut(prevT);
    const move = cfg.rollDistance * delta;
    rt.rootPos = [
      rt.rootPos[0] + rt.rollVec[0] * move,
      rt.rootPos[1],
      rt.rootPos[2] + rt.rollVec[2] * move,
    ];
  } else if (rt.speed > 0.001) {
    const moveMag = rt.state === 'cover' ? 0 : rt.speed;
    rt.rootPos = [
      rt.rootPos[0] + fwd[0] * moveMag * step,
      rt.rootPos[1],
      rt.rootPos[2] + fwd[2] * moveMag * step,
    ];
  }

  // ---- 9. 步态相位 ----
  if (rt.state === 'walk' || rt.state === 'run' || rt.state === 'crouch') {
    const hz =
      rt.state === 'run' ? cfg.runCycleHz : rt.state === 'crouch' ? cfg.crouchCycleHz : cfg.walkCycleHz;
    rt.gaitPhase += hz * step * 2 * Math.PI;
    if (rt.gaitPhase > Math.PI * 2) rt.gaitPhase -= Math.PI * 2;
  }

  // ---- 10. 生成目标姿态 ----
  let target: Pose;
  if (rt.state === 'roll') {
    target = poseRoll(clamp(rt.rollT * cfg.rollDuration, 0, 1));
  } else if (rt.state === 'cover') {
    target = poseCover(rt.coverSide, rt.stateTime);
  } else if (rt.state === 'crouch') {
    target = poseGait(rt.gaitPhase, 14, 26, 6, rt.crouchAmount);
  } else if (rt.state === 'run') {
    target = poseGait(rt.gaitPhase, 46, 74, 14, rt.crouchAmount);
  } else if (rt.state === 'walk') {
    target = poseGait(rt.gaitPhase, 30, 44, 5, rt.crouchAmount);
  } else {
    target = poseIdle(rt.stateTime);
    // 待机也可能处于半蹲（刚松手），叠加蹲姿
    if (rt.crouchAmount > 0.001) {
      const c = poseGait(rt.gaitPhase, 0, 0, 0, rt.crouchAmount);
      target = lerpPose(target, c, rt.crouchAmount);
    }
  }
  rt.targetPose = target;

  // ---- 11. 过渡混合 ----
  if (rt.blend < 1) {
    rt.blend = Math.min(1, rt.blend + step / rt.blendDuration);
  }
  const k = easeInOut(rt.blend);
  rt.pose = k >= 1 ? clonePose(target) : lerpPose(rt.prevPose, target, k);

  // ---- 12. 根整体旋转（翻滚） ----
  if (rt.state === 'roll') {
    const t = clamp(rt.rollT, 0, 1);
    // 侧翻 360°，与位移同步，收尾正好回正
    rt.rootEuler = [0, 0, -360 * easeInOut(t) * rt.rollDir];
  } else if (rt.state === 'cover') {
    // 已经真的转成沿墙侧身了，这里不再额外拧朝向（以前那 18° 是为了假装侧身）
    rt.rootEuler = [0, rt.facing, 0];
  } else {
    rt.rootEuler = [0, rt.facing, 0];
  }

  // 蹲下时骨盆整体下降（不改骨长，只压根高度，视觉上更自然）
  return rt;
}

/** 当前根节点（脚底）应该画在哪。蹲下时整体下沉。 */
export function rootHeightOffset(rt: MotionRuntime): number {
  return -MOTION_CONFIG.crouchDrop * rt.crouchAmount;
}

/** 供 HUD 显示的一行摘要。 */
export function describeMotion(rt: MotionRuntime): string {
  const spd = rt.speed.toFixed(2);
  const blend = (rt.blend * 100).toFixed(0);
  return `${STATE_LABEL[rt.state]} · ${spd} m/s · 朝向 ${rt.facing.toFixed(0)}° · 衔接 ${blend}%`;
}
