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
import { clamp, clonePose, easeInOut, easeOutCubic, lerpPose, restPose, } from './pose.js';
export const MOTION_STATES = [
    'idle',
    'walk',
    'run',
    'crouch',
    'cover',
    'roll',
];
/** 每个状态的显示名，HUD 直接用。 */
export const STATE_LABEL = {
    idle: '待机',
    walk: '步行',
    run: '疾走',
    crouch: '下蹲',
    cover: '贴墙躲藏',
    roll: '翻滚换位',
};
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
    /** 翻滚横向位移（米）。 */
    rollDistance: 1.45,
    /** 触发贴墙的最大距离。 */
    coverEnterDistance: 1.15,
    /** 贴墙时可移动的最大速度（超过则脱离）。 */
    coverExitSpeed: 0.6,
    /** 蹲下时骨盆下降量（米）。 */
    crouchDrop: 0.38,
};
export function createMotion(spawn = [0, 0, 0]) {
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
        rollDir: 1,
        rollFromSide: 1,
        wasRollPressed: false,
    };
}
function setState(rt, next, blendDuration) {
    if (rt.state === next)
        return;
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
/** 站立待机：极轻微呼吸与手臂自然下垂。 */
function poseIdle(t) {
    const p = restPose();
    const breathe = Math.sin(t * 1.6) * 1.2;
    p.chest = [breathe, 0, 0];
    p.head = [-breathe * 0.5, 0, 0];
    p.shoulderL = [0, 0, -6];
    p.shoulderR = [0, 0, 6];
    p.elbowL = [0, 0, 10];
    p.elbowR = [0, 0, -10];
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
function poseGait(phase, amp, kneeAmp, lean, crouch) {
    const p = restPose();
    const a = phase;
    // 左右腿相位相反
    const swingL = Math.sin(a) * amp;
    const swingR = Math.sin(a + Math.PI) * amp;
    // 支撑腿膝盖抬起时弯曲，摆动腿更弯
    const kneeL = Math.max(0, -Math.sin(a)) * kneeAmp;
    const kneeR = Math.max(0, -Math.sin(a + Math.PI)) * kneeAmp;
    // 手臂反向摆动
    const armL = -swingL * 0.55;
    const armR = -swingR * 0.55;
    p.hipL = [swingL + crouch * 62, 0, 0];
    p.hipR = [swingR + crouch * 62, 0, 0];
    p.kneeL = [-kneeL - crouch * 108, 0, 0];
    p.kneeR = [-kneeR - crouch * 108, 0, 0];
    p.ankleL = [kneeL * 0.35 + crouch * 46, 0, 0];
    p.ankleR = [kneeR * 0.35 + crouch * 46, 0, 0];
    p.shoulderL = [armL, 0, -8];
    p.shoulderR = [armR, 0, 8];
    p.elbowL = [0, 0, 14 + Math.max(0, armL) * 0.4];
    p.elbowR = [0, 0, -14 - Math.max(0, armR) * 0.4];
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
function poseCover(side, t) {
    const p = restPose();
    const breathe = Math.sin(t * 1.4) * 0.8;
    // 身体侧倾（绕 Z），并稍微压低重心
    p.pelvis = [-6, 0, side * -8];
    p.spine = [4, 0, side * -6];
    p.chest = [2 + breathe, 0, side * -5];
    p.neck = [-4, 0, side * 3];
    p.head = [-2, 0, side * 4];
    // 内侧手臂上举贴墙
    const inner = side < 0 ? 'shoulderL' : 'shoulderR';
    const innerElbow = side < 0 ? 'elbowL' : 'elbowR';
    p[inner] = [110, 0, side * 18];
    p[innerElbow] = [0, 0, side * 70];
    // 外侧手臂收紧在身前
    const outer = side < 0 ? 'shoulderR' : 'shoulderL';
    const outerElbow = side < 0 ? 'elbowR' : 'elbowL';
    p[outer] = [32, 0, side * 10];
    p[outerElbow] = [0, 0, side * -55];
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
function poseRoll(t) {
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
    p.shoulderL = [58 * tuck, 0, -22 * tuck];
    p.shoulderR = [58 * tuck, 0, 22 * tuck];
    p.elbowL = [0, 0, 96 * tuck];
    p.elbowR = [0, 0, -96 * tuck];
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
export function stepMotion(rt, input, dt) {
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
    if (wantsMove) {
        const targetFacing = (Math.atan2(ix, iz) * 180) / Math.PI;
        let diff = targetFacing - rt.facing;
        while (diff > 180)
            diff -= 360;
        while (diff < -180)
            diff += 360;
        const maxTurn = cfg.turnSpeed * step;
        rt.facing += clamp(diff, -maxTurn, maxTurn);
    }
    // ---- 3. 下蹲混合 ----
    const crouchTarget = input.crouch ? 1 : 0;
    if (rt.crouchAmount < crouchTarget) {
        rt.crouchAmount = Math.min(crouchTarget, rt.crouchAmount + step / cfg.crouchBlend);
    }
    else if (rt.crouchAmount > crouchTarget) {
        rt.crouchAmount = Math.max(crouchTarget, rt.crouchAmount - step / cfg.crouchBlend);
    }
    // ---- 4. 贴墙判定 ----
    // 贴近墙（<= 0.45m）且没在下蹲 → 可贴墙。贴墙时角色站定，按 Ctrl 蹲下即可脱离。
    const nearCover = input.coverAvailable && input.coverDistance <= 0.45 && !input.crouch;
    // ---- 5. 翻滚：优先级最高 ----
    const rollEdge = input.rollPressed && !rt.wasRollPressed;
    rt.wasRollPressed = input.rollPressed;
    if (rt.state === 'roll') {
        rt.rollT += step / cfg.rollDuration;
        if (rt.rollT >= 1) {
            rt.rollT = 1;
            // 翻滚结束：如果换到的那侧仍有掩体，回到 cover，否则待机
            if (input.coverAvailable && input.coverDistance <= cfg.coverEnterDistance) {
                setState(rt, 'cover', cfg.coverBlend * 0.5);
                rt.coverAmount = 1;
            }
            else {
                setState(rt, 'idle', 0.22);
                rt.coverAmount = 0;
            }
        }
    }
    else if (rollEdge && (rt.state === 'cover' || (nearCover && rt.speed < cfg.coverExitSpeed))) {
        setState(rt, 'roll', 0.1);
        rt.rollT = 0;
        rt.rollFromSide = input.coverSide;
        rt.rollDir = input.coverSide > 0 ? -1 : 1; // 向远离墙的一侧滚
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
            rt.coverAmount = Math.min(1, rt.coverAmount + step / cfg.coverBlend);
        }
        else {
            if (rt.state === 'cover') {
                setState(rt, wantCrouch ? 'crouch' : wantsMove ? (input.sprint ? 'run' : 'walk') : 'idle', cfg.coverBlend);
            }
            rt.coverAmount = Math.max(0, rt.coverAmount - step / cfg.coverBlend);
            if (wantCrouch) {
                if (rt.state !== 'crouch')
                    setState(rt, 'crouch', cfg.crouchBlend);
            }
            else if (wantsMove) {
                const next = input.sprint ? 'run' : 'walk';
                if (rt.state !== next)
                    setState(rt, next, 0.2);
            }
            else if (rt.state !== 'idle') {
                setState(rt, 'idle', 0.24);
            }
        }
    }
    // ---- 7. 速度 ----
    let targetSpeed = 0;
    if (rt.state === 'walk')
        targetSpeed = cfg.walkSpeed;
    else if (rt.state === 'run')
        targetSpeed = cfg.runSpeed;
    else if (rt.state === 'crouch')
        targetSpeed = wantsMove ? cfg.crouchSpeed : 0;
    else if (rt.state === 'cover')
        targetSpeed = 0; // 贴墙站定，靠 Ctrl 脱离
    else if (rt.state === 'roll')
        targetSpeed = cfg.rollDistance / cfg.rollDuration;
    // 加减速平滑（0.12s 左右到目标速度）
    const accel = targetSpeed > rt.speed ? 14 : 18;
    rt.speed += clamp(targetSpeed - rt.speed, -accel * step, accel * step);
    // ---- 8. 位移 ----
    const yawRad = (rt.facing * Math.PI) / 180;
    const fwd = [Math.sin(yawRad), 0, Math.cos(yawRad)];
    if (rt.state === 'roll') {
        // 翻滚：横向位移由进度曲线控制，起手快、收尾缓
        const prevT = clamp(rt.rollT - step / cfg.rollDuration, 0, 1);
        const delta = easeInOut(rt.rollT) - easeInOut(prevT);
        const sideX = Math.cos(yawRad) * rt.rollDir;
        const sideZ = -Math.sin(yawRad) * rt.rollDir;
        rt.rootPos = [
            rt.rootPos[0] + sideX * cfg.rollDistance * delta,
            rt.rootPos[1],
            rt.rootPos[2] + sideZ * cfg.rollDistance * delta,
        ];
    }
    else if (rt.speed > 0.001) {
        const moveMag = rt.state === 'cover' ? 0 : rt.speed;
        rt.rootPos = [
            rt.rootPos[0] + fwd[0] * moveMag * step,
            rt.rootPos[1],
            rt.rootPos[2] + fwd[2] * moveMag * step,
        ];
    }
    // ---- 9. 步态相位 ----
    if (rt.state === 'walk' || rt.state === 'run' || rt.state === 'crouch') {
        const hz = rt.state === 'run' ? cfg.runCycleHz : rt.state === 'crouch' ? cfg.crouchCycleHz : cfg.walkCycleHz;
        rt.gaitPhase += hz * step * 2 * Math.PI;
        if (rt.gaitPhase > Math.PI * 2)
            rt.gaitPhase -= Math.PI * 2;
    }
    // ---- 10. 生成目标姿态 ----
    let target;
    if (rt.state === 'roll') {
        target = poseRoll(clamp(rt.rollT * cfg.rollDuration, 0, 1));
    }
    else if (rt.state === 'cover') {
        target = poseCover(input.coverSide, rt.stateTime);
    }
    else if (rt.state === 'crouch') {
        target = poseGait(rt.gaitPhase, 14, 26, 6, rt.crouchAmount);
    }
    else if (rt.state === 'run') {
        target = poseGait(rt.gaitPhase, 46, 74, 14, rt.crouchAmount);
    }
    else if (rt.state === 'walk') {
        target = poseGait(rt.gaitPhase, 30, 44, 5, rt.crouchAmount);
    }
    else {
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
    }
    else if (rt.state === 'cover') {
        // 贴墙时身体朝墙略偏，视觉上更像"侧身靠墙"
        const lean = input.coverSide * -18 * rt.coverAmount;
        rt.rootEuler = [0, rt.facing + lean, 0];
    }
    else {
        rt.rootEuler = [0, rt.facing, 0];
    }
    // 蹲下时骨盆整体下降（不改骨长，只压根高度，视觉上更自然）
    return rt;
}
/** 当前根节点（脚底）应该画在哪。蹲下时整体下沉。 */
export function rootHeightOffset(rt) {
    return -MOTION_CONFIG.crouchDrop * rt.crouchAmount;
}
/** 供 HUD 显示的一行摘要。 */
export function describeMotion(rt) {
    const spd = rt.speed.toFixed(2);
    const blend = (rt.blend * 100).toFixed(0);
    return `${STATE_LABEL[rt.state]} · ${spd} m/s · 朝向 ${rt.facing.toFixed(0)}° · 衔接 ${blend}%`;
}
