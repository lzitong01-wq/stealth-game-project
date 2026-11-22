import { describe, expect, it } from 'vitest';

import { BONE_SEGMENTS, type Vec3 } from '../skeleton.ts';
import { restPose, solvePose, lerpPose } from '../pose.ts';
import { createCorridorProbe, type CoverProbe } from '../input.ts';
import {
  MOTION_CONFIG,
  STATE_LABEL,
  createMotion,
  stepMotion,
  type MotionInput,
  type MotionRuntime,
} from '../motion.ts';

const IDLE: MotionInput = {
  moveX: 0,
  moveZ: 0,
  crouch: false,
  sprint: false,
  rollPressed: false,
  coverAvailable: false,
  coverSide: 1,
  coverDistance: 99,
};

function input(patch: Partial<MotionInput> = {}): MotionInput {
  return { ...IDLE, ...patch };
}

/** 以 60fps 推进若干秒。 */
function advance(rt: MotionRuntime, seconds: number, patch: Partial<MotionInput> = {}): MotionRuntime {
  const dt = 1 / 60;
  const steps = Math.round(seconds / dt);
  for (let i = 0; i < steps; i++) stepMotion(rt, input(patch), dt);
  return rt;
}

/**
 * 用真实探针驱动：每帧查询掩体信息并做边界钳制。
 * 这样才能验证「贴墙 → 翻滚 → 落到对面墙」的完整闭环，
 * 手动传固定 coverDistance 是测不出位移和换侧的。
 */
function driveWithProbe(
  rt: MotionRuntime,
  probe: CoverProbe,
  seconds: number,
  patch: Partial<MotionInput> = {},
): MotionRuntime {
  const dt = 1 / 60;
  const steps = Math.max(1, Math.round(seconds / dt));
  for (let i = 0; i < steps; i++) {
    const c = probe.query(rt.rootPos, rt.facing);
    stepMotion(
      rt,
      {
        ...input(patch),
        coverAvailable: c.available,
        coverSide: c.side,
        coverDistance: c.distance,
        coverNormal: c.normal,
      },
      dt,
    );
    const clamped = probe.clamp(rt.rootPos);
    if (clamped !== rt.rootPos) {
      rt.rootPos = clamped;
      rt.speed *= 0.3;
    }
  }
  return rt;
}

describe('骨架与 FK', () => {
  it('站立时头顶接近 1.75m，脚底接近地面', () => {
    const solved = solvePose(restPose(), [0, 0, 0], [0, 0, 0]);
    const head = solved.positions.head;
    const foot = solved.positions.footL;
    expect(head[1]).toBeGreaterThan(1.6);
    expect(head[1]).toBeLessThan(1.85);
    expect(Math.abs(foot[1])).toBeLessThan(0.05);
  });

  it('每根骨段都有非零长度（没有重合关节）', () => {
    const solved = solvePose(restPose(), [0, 0, 0], [0, 0, 0]);
    for (const [a, b] of BONE_SEGMENTS) {
      const pa = solved.positions[a];
      const pb = solved.positions[b];
      const d = Math.hypot(pa[0] - pb[0], pa[1] - pb[1], pa[2] - pb[2]);
      expect(d, `${a}->${b} 长度`).toBeGreaterThan(0.01);
    }
  });

  it('根整体旋转 90° 后，前向位移会转到侧向', () => {
    const pose = restPose();
    const forward = solvePose(pose, [0, 0, 0], [0, 0, 0]).positions.footL;
    const turned = solvePose(pose, [0, 0, 0], [0, 90, 0]).positions.footL;
    expect(Math.abs(forward[0])).toBeGreaterThan(0.05);
    expect(Math.abs(turned[2])).toBeGreaterThan(0.05);
  });

  it('姿态插值在两端取值正确', () => {
    const a = restPose();
    const b = restPose();
    b.hipL = [90, 0, 0];
    expect(lerpPose(a, b, 0).hipL[0]).toBeCloseTo(0);
    expect(lerpPose(a, b, 1).hipL[0]).toBeCloseTo(90);
    expect(lerpPose(a, b, 0.5).hipL[0]).toBeCloseTo(45);
  });
});

describe('动作状态机', () => {
  it('初始化后处于待机且姿态稳定', () => {
    const rt = createMotion([0, 0, 0]);
    expect(rt.state).toBe('idle');
    expect(rt.speed).toBe(0);
    expect(rt.crouchAmount).toBe(0);
  });

  it('有前后输入时进入步行，并且步态相位推进', () => {
    const rt = advance(createMotion([0, 0, 0]), 0.6, { moveZ: 1 });
    expect(rt.state).toBe('walk');
    expect(rt.speed).toBeGreaterThan(1);
    expect(rt.gaitPhase).not.toBe(0);
  });

  it('按住 Shift 进入疾走，速度高于步行', () => {
    const walk = advance(createMotion([0, 0, 0]), 1.0, { moveZ: 1 });
    const run = advance(createMotion([0, 0, 0]), 1.0, { moveZ: 1, sprint: true });
    expect(run.state).toBe('run');
    expect(run.speed).toBeGreaterThan(walk.speed + 1);
  });

  it('按 Ctrl 后进入下蹲，且蹲下混合量在过渡时间内到位', () => {
    const rt = createMotion([0, 0, 0]);
    advance(rt, 0.1, { crouch: true });
    expect(rt.crouchAmount).toBeGreaterThan(0);
    expect(rt.crouchAmount).toBeLessThan(1);
    advance(rt, MOTION_CONFIG.crouchBlend + 0.1, { crouch: true });
    expect(rt.state).toBe('crouch');
    expect(rt.crouchAmount).toBeCloseTo(1, 1);
  });

  it('靠近可躲藏墙时自动进入贴墙躲藏', () => {
    const rt = advance(createMotion([0, 0, 0]), 0.6, {
      coverAvailable: true,
      coverSide: 1,
      coverDistance: 0.3,
    });
    expect(rt.state).toBe('cover');
    expect(rt.coverAmount).toBeGreaterThan(0.8);
  });

  it('贴墙时按翻滚会进入翻滚，并在结束后回到贴墙', () => {
    const rt = advance(createMotion([0, 0, 0]), 0.6, {
      coverAvailable: true,
      coverSide: 1,
      coverDistance: 0.3,
    });
    expect(rt.state).toBe('cover');

    stepMotion(rt, input({ coverAvailable: true, coverSide: 1, coverDistance: 0.3, rollPressed: true }), 1 / 60);
    expect(rt.state).toBe('roll');

    // 翻滚过程中根节点应当绕 Z 轴转起来
    advance(rt, MOTION_CONFIG.rollDuration * 0.5, {
      coverAvailable: true,
      coverSide: 1,
      coverDistance: 0.3,
    });
    expect(Math.abs(rt.rootEuler[2])).toBeGreaterThan(30);

    advance(rt, MOTION_CONFIG.rollDuration, {
      coverAvailable: true,
      coverSide: 1,
      coverDistance: 0.3,
    });
    expect(rt.state).toBe('cover');
    expect(Math.abs(rt.rootEuler[2])).toBeLessThan(25);
  });

  it('翻滚产生横向位移，且方向背离墙侧', () => {
    const rt = advance(createMotion([0, 0, 0]), 0.6, {
      coverAvailable: true,
      coverSide: 1,
      coverDistance: 0.3,
    });
    const before = [...rt.rootPos] as Vec3;
    stepMotion(rt, input({ coverAvailable: true, coverSide: 1, coverDistance: 0.3, rollPressed: true }), 1 / 60);
    advance(rt, MOTION_CONFIG.rollDuration, { coverAvailable: true, coverSide: 1, coverDistance: 0.3 });
    const moved = Math.hypot(rt.rootPos[0] - before[0], rt.rootPos[2] - before[2]);
    expect(moved).toBeGreaterThan(0.8);
  });

  it('动作切换有过渡：切换瞬间混合进度从 0 开始', () => {
    const rt = createMotion([0, 0, 0]);
    advance(rt, 0.5);
    stepMotion(rt, input({ moveZ: 1 }), 1 / 60);
    expect(rt.blend).toBeLessThan(0.5);
    expect(rt.state).toBe('walk');
  });

  it('松手后回到待机，速度归零', () => {
    const rt = advance(createMotion([0, 0, 0]), 1.0, { moveZ: 1 });
    advance(rt, 0.6);
    expect(rt.state).toBe('idle');
    expect(rt.speed).toBeLessThan(0.05);
  });

  it('步态摆动幅度足够：前后腿角度差明显（回归：相位单位曾误用弧度×2π）', () => {
    const rt = advance(createMotion([0, 0, 0]), 1.0, { moveZ: 1, sprint: true });
    expect(rt.state).toBe('run');
    // 连续采样一段时间，取最大摆幅，避免正好落在步态过零点
    let maxSwing = 0;
    for (let i = 0; i < 120; i++) {
      stepMotion(rt, input({ moveZ: 1, sprint: true }), 1 / 60);
      maxSwing = Math.max(maxSwing, Math.abs(rt.pose.hipL[0] - rt.pose.hipR[0]));
    }
    expect(maxSwing).toBeGreaterThan(30);
  });

  it('每个状态都有中文标签', () => {
    for (const s of ['idle', 'walk', 'run', 'crouch', 'cover', 'roll'] as const) {
      expect(STATE_LABEL[s].length).toBeGreaterThan(0);
    }
  });
});

/**
 * 手臂姿态回归。
 * 曾经的缺陷：手臂骨段沿 ±X 伸展，绕 X 旋转只让手臂自转、不改变指向，
 * 所以写在 shoulder.x 上的摆臂角度完全无效，所有状态下手臂都是 T-pose 平举。
 */
describe('手臂姿态', () => {
  it('待机时手臂自然下垂：手明显低于肩，且没有水平张开', () => {
    const rt = advance(createMotion([0, 0, 0]), 0.5);
    const s = solvePose(rt.pose, [0, 0, 0], [0, 0, 0]);
    const dy = s.positions.shoulderL[1] - s.positions.handL[1];
    const dx = Math.abs(s.positions.handL[0] - s.positions.shoulderL[0]);
    expect(dy, '手应低于肩').toBeGreaterThan(0.35);
    expect(dx, '手臂不应水平张开').toBeLessThan(0.4);
  });

  it('走路时手臂前后摆动', () => {
    const rt = advance(createMotion([0, 0, 0]), 1.0, { moveZ: 1 });
    let minZ = Infinity;
    let maxZ = -Infinity;
    for (let i = 0; i < 150; i++) {
      stepMotion(rt, input({ moveZ: 1 }), 1 / 60);
      const s = solvePose(rt.pose, [0, 0, 0], [0, 0, 0]);
      minZ = Math.min(minZ, s.positions.handL[2]);
      maxZ = Math.max(maxZ, s.positions.handL[2]);
    }
    expect(maxZ - minZ, '手的 Z 坐标应来回移动').toBeGreaterThan(0.15);
  });

  it('蹲下时手臂收在身前，不再是蹲着的 T-pose', () => {
    const rt = advance(createMotion([0, 0, 0]), 1.0, { crouch: true });
    expect(rt.state).toBe('crouch');
    const s = solvePose(rt.pose, [0, 0, 0], [0, 0, 0]);
    const dx = Math.abs(s.positions.handL[0] - s.positions.shoulderL[0]);
    const dy = s.positions.shoulderL[1] - s.positions.handL[1];
    expect(dx, '蹲下时手不应向外张开').toBeLessThan(0.45);
    expect(dy, '蹲下时手不应与肩齐平').toBeGreaterThan(0.2);
  });
});

/**
 * 贴墙与翻滚的核心行为。
 * 曾经的缺陷：
 *   1. 贴墙时速度锁为 0，且只有按 Ctrl 能脱离 → 玩家被永久粘在墙上
 *   2. 翻滚位移用的是「角色右方」，贴墙侧身后会滚错方向
 *   3. 翻滚距离 1.45 < 墙间距 3.2 → 永远滚不到对面
 */
describe('贴墙与翻滚', () => {
  const RIGHT_WALL: Partial<MotionInput> = {
    coverAvailable: true,
    coverSide: 1,
    coverDistance: 0.25,
    coverNormal: [-1, 0, 0],
  };

  it('贴墙后转为沿墙侧身，而不是正对墙', () => {
    const rt = advance(createMotion([0, 0, 0]), 1.2, RIGHT_WALL);
    expect(rt.state).toBe('cover');
    // 沿墙 = 朝 ±Z，则 sin(yaw) ≈ 0
    expect(Math.abs(Math.sin((rt.facing * Math.PI) / 180))).toBeLessThan(0.2);
  });

  it('贴墙时朝墙推方向键仍然保持贴墙', () => {
    const rt = advance(createMotion([0, 0, 0]), 0.6, RIGHT_WALL);
    expect(rt.state).toBe('cover');
    // 墙在 +X（法线指向 -X），朝 +X 推 = 想贴住
    advance(rt, 0.6, { ...RIGHT_WALL, moveX: 1 });
    expect(rt.state).toBe('cover');
  });

  it('贴墙后朝背离墙的方向推可以脱身', () => {
    const rt = advance(createMotion([0, 0, 0]), 0.6, RIGHT_WALL);
    expect(rt.state).toBe('cover');
    advance(rt, 0.6, { ...RIGHT_WALL, moveX: -1 });
    expect(rt.state).not.toBe('cover');
  });

  it('翻滚从一侧贴墙跨到另一侧，并重新贴住对面墙', () => {
    const probe = createCorridorProbe(1.0);
    // 从右墙边起手（半宽 1.0 - 身体半径 0.25 = 0.75）
    const rt = createMotion([0.75, 0, 0]);
    driveWithProbe(rt, probe, 0.8);
    expect(rt.state).toBe('cover');
    expect(rt.rootPos[0]).toBeGreaterThan(0.6);

    driveWithProbe(rt, probe, 1 / 60, { rollPressed: true });
    expect(rt.state).toBe('roll');

    driveWithProbe(rt, probe, 0.9);
    expect(rt.rootPos[0], '应跨到走廊另一侧').toBeLessThan(0);
    expect(rt.state, '落地应贴住对面墙').toBe('cover');
    expect(Math.abs(rt.rootPos[0])).toBeGreaterThan(0.6);
  });

  it('翻滚位移方向始终背离起手时的墙', () => {
    const probe = createCorridorProbe(1.0);
    const rt = createMotion([0.75, 0, 0]);
    driveWithProbe(rt, probe, 0.8);
    const before = rt.rootPos[0];
    driveWithProbe(rt, probe, 1 / 60, { rollPressed: true });
    // 翻滚中即使经过走廊中线，也不能反向抽回
    let minX = before;
    for (let i = 0; i < 40; i++) {
      driveWithProbe(rt, probe, 1 / 60);
      minX = Math.min(minX, rt.rootPos[0]);
    }
    expect(minX).toBeLessThan(before - 0.3);
  });
});
