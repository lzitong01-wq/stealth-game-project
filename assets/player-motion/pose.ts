/**
 * 姿态表达与正向运动学（FK）。
 *
 * 这一层是纯数学：输入一组关节角度，输出每个关节的世界坐标。
 * 不依赖引擎，因此可以单独跑单测，也方便移植到别的项目。
 */

import {
  BONE_OFFSET,
  FK_ORDER,
  JOINT_PARENT,
  JOINTS,
  type JointName,
  type Vec3,
} from './skeleton.ts';

/** 欧拉角（度），顺序 X→Y→Z，旋转顺序 Ry * Rx * Rz。 */
export type Euler = Vec3;

/** 一套完整姿态：每个关节一个局部欧拉角。 */
export type Pose = Record<JointName, Euler>;

export const DEG = Math.PI / 180;

/** 全零姿态：直立站好，四肢按 BONE_OFFSET 展开。 */
export function restPose(): Pose {
  const p = {} as Pose;
  for (const j of JOINTS) p[j] = [0, 0, 0];
  return p;
}

/** 复制一份，避免共享引用被原地改。 */
export function clonePose(p: Pose): Pose {
  const out = {} as Pose;
  for (const j of JOINTS) out[j] = [p[j][0], p[j][1], p[j][2]];
  return out;
}

/** 只写一个关节，其余保持不变（避免分配整份新对象）。 */
export function withJoint(p: Pose, j: JointName, e: Euler): Pose {
  p[j] = e;
  return p;
}

export function clamp(x: number, lo: number, hi: number): number {
  return x < lo ? lo : x > hi ? hi : x;
}

export function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

/** 平滑过渡：用于动作之间的衔接，避免关节角突变。 */
export function easeInOut(t: number): number {
  const x = clamp(t, 0, 1);
  return x < 0.5 ? 2 * x * x : 1 - Math.pow(-2 * x + 2, 2) / 2;
}

export function easeOutCubic(t: number): number {
  const x = clamp(t, 0, 1);
  return 1 - Math.pow(1 - x, 3);
}

/** 逐关节线性插值两套姿态。角度不做过零环绕处理（本项目动作幅度远小于 180°）。 */
export function lerpPose(a: Pose, b: Pose, t: number): Pose {
  const k = clamp(t, 0, 1);
  const out = {} as Pose;
  for (const j of JOINTS) {
    const ea = a[j];
    const eb = b[j];
    out[j] = [lerp(ea[0], eb[0], k), lerp(ea[1], eb[1], k), lerp(ea[2], eb[2], k)];
  }
  return out;
}

/** 3x3 行主序矩阵。 */
export type Mat3 = readonly [
  number, number, number,
  number, number, number,
  number, number, number,
];

export const IDENTITY3: Mat3 = [1, 0, 0, 0, 1, 0, 0, 0, 1];

/** 欧拉角（度）转 3x3 旋转矩阵，顺序 Ry * Rx * Rz。 */
export function eulerToMat3(e: Euler): Mat3 {
  const rx = e[0] * DEG;
  const ry = e[1] * DEG;
  const rz = e[2] * DEG;
  const cx = Math.cos(rx), sx = Math.sin(rx);
  const cy = Math.cos(ry), sy = Math.sin(ry);
  const cz = Math.cos(rz), sz = Math.sin(rz);

  // Rz
  const z00 = cz, z01 = -sz, z02 = 0;
  const z10 = sz, z11 = cz, z12 = 0;
  const z20 = 0, z21 = 0, z22 = 1;
  // Rx
  const x00 = 1, x01 = 0, x02 = 0;
  const x10 = 0, x11 = cx, x12 = -sx;
  const x20 = 0, x21 = sx, x22 = cx;
  // Ry
  const y00 = cy, y01 = 0, y02 = sy;
  const y10 = 0, y11 = 1, y12 = 0;
  const y20 = -sy, y21 = 0, y22 = cy;

  // Rx * Rz
  const a00 = x00 * z00 + x01 * z10 + x02 * z20;
  const a01 = x00 * z01 + x01 * z11 + x02 * z21;
  const a02 = x00 * z02 + x01 * z12 + x02 * z22;
  const a10 = x10 * z00 + x11 * z10 + x12 * z20;
  const a11 = x10 * z01 + x11 * z11 + x12 * z21;
  const a12 = x10 * z02 + x11 * z12 + x12 * z22;
  const a20 = x20 * z00 + x21 * z10 + x22 * z20;
  const a21 = x20 * z01 + x21 * z11 + x22 * z21;
  const a22 = x20 * z02 + x21 * z12 + x22 * z22;

  // Ry * (Rx * Rz)
  return [
    y00 * a00 + y01 * a10 + y02 * a20,
    y00 * a01 + y01 * a11 + y02 * a21,
    y00 * a02 + y01 * a12 + y02 * a22,
    y10 * a00 + y11 * a10 + y12 * a20,
    y10 * a01 + y11 * a11 + y12 * a21,
    y10 * a02 + y11 * a12 + y12 * a22,
    y20 * a00 + y21 * a10 + y22 * a20,
    y20 * a01 + y21 * a11 + y22 * a21,
    y20 * a02 + y21 * a12 + y22 * a22,
  ];
}

export function mulMat3(a: Mat3, b: Mat3): Mat3 {
  return [
    a[0] * b[0] + a[1] * b[3] + a[2] * b[6],
    a[0] * b[1] + a[1] * b[4] + a[2] * b[7],
    a[0] * b[2] + a[1] * b[5] + a[2] * b[8],
    a[3] * b[0] + a[4] * b[3] + a[5] * b[6],
    a[3] * b[1] + a[4] * b[4] + a[5] * b[7],
    a[3] * b[2] + a[4] * b[5] + a[5] * b[8],
    a[6] * b[0] + a[7] * b[3] + a[8] * b[6],
    a[6] * b[1] + a[7] * b[4] + a[8] * b[7],
    a[6] * b[2] + a[7] * b[5] + a[8] * b[8],
  ];
}

export function mulMat3Vec3(m: Mat3, v: Vec3): Vec3 {
  return [
    m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
    m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
    m[6] * v[0] + m[7] * v[1] + m[8] * v[2],
  ];
}

export function addVec3(a: Vec3, b: Vec3): Vec3 {
  return [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
}

/** 世界姿态求解结果。 */
export interface SolvedPose {
  /** 每个关节的世界坐标。 */
  readonly positions: Record<JointName, Vec3>;
  /** 每个关节的世界旋转矩阵，绘制朝向时可用。 */
  readonly rotations: Record<JointName, Mat3>;
}

/**
 * 正向运动学。
 *
 * @param pose       关节局部角度
 * @param rootPos    根节点（脚底）世界位置
 * @param rootEuler  根节点整体旋转（度）。翻滚时改这里的 X/Z 让整个人转起来。
 */
export function solvePose(pose: Pose, rootPos: Vec3, rootEuler: Euler = [0, 0, 0]): SolvedPose {
  const positions = {} as Record<JointName, Vec3>;
  const rotations = {} as Record<JointName, Mat3>;
  const rootMat = eulerToMat3(rootEuler);

  for (const j of FK_ORDER) {
    const parent = JOINT_PARENT[j];
    const localMat = eulerToMat3(pose[j]);

    if (parent === null) {
      // 根：偏移在根空间里，随后叠加整体旋转
      const offset = mulMat3Vec3(rootMat, BONE_OFFSET[j]);
      positions[j] = addVec3(rootPos, offset);
      rotations[j] = mulMat3(rootMat, localMat);
    } else {
      const parentMat = rotations[parent];
      positions[j] = addVec3(positions[parent], mulMat3Vec3(parentMat, BONE_OFFSET[j]));
      rotations[j] = mulMat3(parentMat, localMat);
    }
  }

  return { positions, rotations };
}

/** 取某个关节的世界坐标（找不到时返回根位置，避免绘制崩溃）。 */
export function jointPos(solved: SolvedPose, j: JointName): Vec3 {
  return solved.positions[j] ?? [0, 0, 0];
}
