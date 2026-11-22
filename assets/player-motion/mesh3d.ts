/**
 * 引擎内 3D 白模：把动作系统接进真实的渲染管线。
 *
 * 和 `view3d.ts` 的关系：
 *   - 两者的「关节从哪来、怎么解算」完全一致（都消费 `solvePose` 的结果）
 *   - `view3d.ts` 是 canvas 2D 手写等距渲染，用于无 GPU 环境验证与截图
 *   - 本文件走引擎真管线：每个骨段一个 MeshFilter + MeshRenderer 实体，
 *     合并进主项目后就是真正的 3D 白模，能被相机、光照、后期正常影响
 *
 * 白模规格参考 Tomcasso 的 `LowPolySceneSetup.cs`（每个关节挂一个 Cube，
 * 用 localScale 表达粗细），只是把「父子 Transform 层级 + 局部角度」
 * 换成了「世界位置 + 四元数」——因为动作层已经直接给出世界坐标，
 * 不需要再在引擎里重建一遍层级。
 *
 * 用法见 install.ts 的 `view: 'mesh3d'`。
 */

import { type World } from '@forgeax/engine/ecs';
import { Transform } from '@forgeax/engine/scene';
import {
  Camera,
  DirectionalLight,
  MeshFilter,
  MeshRenderer,
  Materials,
  setActiveCamera,
} from '@forgeax/engine/render';
import { createBoxGeometry, createSphereGeometry } from '@forgeax/engine/geometry';

import { type JointName, type Vec3 } from './skeleton.ts';
import { solvePose } from './pose.ts';
import { MOTION_CONFIG, type MotionRuntime } from './motion.ts';

/* ------------------------------------------------------------------ *
 * 向量与四元数
 * ------------------------------------------------------------------ */

const sub = (a: Vec3, b: Vec3): Vec3 => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const add = (a: Vec3, b: Vec3): Vec3 => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const mul = (a: Vec3, k: number): Vec3 => [a[0] * k, a[1] * k, a[2] * k];
const dot = (a: Vec3, b: Vec3): number => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a: Vec3, b: Vec3): Vec3 => [
  a[1] * b[2] - a[2] * b[1],
  a[2] * b[0] - a[0] * b[2],
  a[0] * b[1] - a[1] * b[0],
];
const len = (a: Vec3): number => Math.hypot(a[0], a[1], a[2]);
function norm(a: Vec3): Vec3 {
  const l = len(a);
  return l < 1e-6 ? [0, 1, 0] : [a[0] / l, a[1] / l, a[2] / l];
}

type Quat = readonly [number, number, number, number];

/** 把单位向量 from 旋到 to 的最短弧四元数（xyzw）。 */
function quatArc(from: Vec3, to: Vec3): Quat {
  const d = dot(from, to);
  if (d > 0.99999) return [0, 0, 0, 1];
  if (d < -0.99999) {
    // 恰好反向：任取一条垂直轴转 180°
    let axis = cross(from, [1, 0, 0]);
    if (len(axis) < 1e-4) axis = cross(from, [0, 0, 1]);
    const a = norm(axis);
    return [a[0], a[1], a[2], 0];
  }
  const c = cross(from, to);
  const s = Math.sqrt((1 + d) * 2);
  return [c[0] / s, c[1] / s, c[2] / s, s / 2];
}

/* ------------------------------------------------------------------ *
 * 白模部件（比例与 canvas 版一致，方便两套视图对照）
 * ------------------------------------------------------------------ */

interface PartDef {
  from: JointName;
  to: JointName;
  w: number;
  h: number;
  mat: 'body' | 'limb' | 'foot';
}

const PARTS: readonly PartDef[] = [
  { from: 'pelvis', to: 'chest', w: 0.34, h: 0.21, mat: 'body' },
  { from: 'chest', to: 'neck', w: 0.12, h: 0.12, mat: 'body' },
  { from: 'shoulderL', to: 'elbowL', w: 0.095, h: 0.095, mat: 'limb' },
  { from: 'elbowL', to: 'handL', w: 0.082, h: 0.082, mat: 'limb' },
  { from: 'shoulderR', to: 'elbowR', w: 0.095, h: 0.095, mat: 'limb' },
  { from: 'elbowR', to: 'handR', w: 0.082, h: 0.082, mat: 'limb' },
  { from: 'hipL', to: 'kneeL', w: 0.145, h: 0.145, mat: 'limb' },
  { from: 'kneeL', to: 'ankleL', w: 0.125, h: 0.125, mat: 'limb' },
  { from: 'hipR', to: 'kneeR', w: 0.145, h: 0.145, mat: 'limb' },
  { from: 'kneeR', to: 'ankleR', w: 0.125, h: 0.125, mat: 'limb' },
  { from: 'ankleL', to: 'footL', w: 0.12, h: 0.075, mat: 'foot' },
  { from: 'ankleR', to: 'footR', w: 0.12, h: 0.075, mat: 'foot' },
];

const HEAD_RADIUS = 0.125;

const COLORS = {
  body: [0.12, 0.36, 0.46, 1] as [number, number, number, number],
  limb: [0.09, 0.27, 0.36, 1] as [number, number, number, number],
  foot: [0.07, 0.2, 0.27, 1] as [number, number, number, number],
  head: [0.81, 0.66, 0.39, 1] as [number, number, number, number],
};

/* ------------------------------------------------------------------ *
 * 对外接口
 * ------------------------------------------------------------------ */

export interface Mesh3DOptions {
  world: World;
  /** 是否由本模块创建并激活一台等距相机。主项目已有相机时传 false。 */
  ownCamera?: boolean;
  /** 是否创建一盏平行光。主项目已有光照时传 false。 */
  ownLight?: boolean;
  /** 相机环绕距离（米）。 */
  cameraDistance?: number;
  /** 相机高度（米）。 */
  cameraHeight?: number;
  /** 光照强度。 */
  lightIntensity?: number;
}

export interface Mesh3DHandle {
  /** 每帧调用：用最新解算结果更新所有白模实体的 Transform。 */
  sync(rt: MotionRuntime): void;
  /** 相机是否跟随角色（默认开）。 */
  setCameraFollow(on: boolean): void;
  dispose(): void;
}

export function createMesh3D(options: Mesh3DOptions): Mesh3DHandle {
  const world = options.world;
  const cameraDistance = options.cameraDistance ?? 4.2;
  const cameraHeight = options.cameraHeight ?? 2.4;
  const cameraFollow = { on: true };

  // 共享几何：单位立方体（沿 +Y 拉长成骨段）+ 单位球（头）
  const boxRes = createBoxGeometry(1, 1, 1);
  if (!boxRes.ok) throw new Error(`box geometry failed: ${boxRes.error?.code ?? 'unknown'}`);
  const sphereRes = createSphereGeometry(1, 16, 12);
  if (!sphereRes.ok) throw new Error(`sphere geometry failed: ${sphereRes.error?.code ?? 'unknown'}`);

  const boxHandle = world.allocSharedRef('MeshAsset', boxRes.value);
  const sphereHandle = world.allocSharedRef('MeshAsset', sphereRes.value);

  const matHandles: Record<string, number> = {};
  for (const key of ['body', 'limb', 'foot', 'head'] as const) {
    const payload = Materials.standard({ baseColor: COLORS[key] });
    matHandles[key] = world.allocSharedRef('MaterialAsset', payload);
  }

  const spawnPart = (meshHandle: number, matHandle: number) => {
    const r = world.spawn(
      { component: Transform, data: { pos: [0, 0, 0], quat: [0, 0, 0, 1], scale: [1, 1, 1] } },
      { component: MeshFilter, data: { assetHandle: meshHandle } },
      { component: MeshRenderer, data: { materials: [matHandle], lightingChannels: 0xffffffff } },
    );
    if (!r.ok) throw new Error(`spawn part failed: ${r.error?.code ?? 'unknown'}`);
    return r.value as number;
  };

  const partEntities = PARTS.map((p) => ({
    def: p,
    entity: spawnPart(boxHandle, matHandles[p.mat]),
  }));
  const headEntity = spawnPart(sphereHandle, matHandles.head);

  /* ---- 相机 ---- */
  let cameraEntity = -1;
  if (options.ownCamera !== false) {
    // 宽高比从窗口推：插件里拿不到 renderer 的实际尺寸，
    // 写死 16/9 在窄窗口下会把白模拉变形。
    const aspect =
      typeof window !== 'undefined' && window.innerHeight > 0
        ? window.innerWidth / window.innerHeight
        : 16 / 9;
    const r = world.spawn(
      { component: Transform, data: { pos: [0, cameraHeight, cameraDistance], quat: [0, 0, 0, 1], scale: [1, 1, 1] } },
      {
        component: Camera,
        data: { fov: 38, aspect, near: 0.1, far: 200, projection: 0 },
      },
    );
    if (r.ok) {
      cameraEntity = r.value as number;
      setActiveCamera(world, cameraEntity);
    }
  }

  /* ---- 光照 ---- */
  let lightEntity = -1;
  if (options.ownLight !== false) {
    const r = world.spawn(
      { component: Transform, data: { pos: [0, 6, 0], quat: [0, 0, 0, 1], scale: [1, 1, 1] } },
      {
        component: DirectionalLight,
        data: {
          direction: [-0.42, -0.82, -0.38],
          color: [1, 0.97, 0.92],
          intensity: options.lightIntensity ?? 3.2,
          castShadow: true,
        },
      },
    );
    if (r.ok) lightEntity = r.value as number;
  }

  const writeTransform = (entity: number, pos: Vec3, quat: Quat, scale: Vec3): void => {
    world.set(entity, Transform, {
      pos: [pos[0], pos[1], pos[2]],
      quat: [quat[0], quat[1], quat[2], quat[3]],
      scale: [scale[0], scale[1], scale[2]],
    });
  };

  /* ---- 每帧同步 ---- */
  function sync(rt: MotionRuntime): void {
    const dropped: Vec3 = [
      rt.rootPos[0],
      rt.rootPos[1] - MOTION_CONFIG.crouchDrop * rt.crouchAmount,
      rt.rootPos[2],
    ];
    const solved = solvePose(rt.pose, dropped, rt.rootEuler);

    for (const { def, entity } of partEntities) {
      const a = solved.positions[def.from];
      const b = solved.positions[def.to];
      const delta = sub(b, a);
      const length = len(delta);
      if (length < 1e-4) continue;
      const dir = mul(delta, 1 / length);
      const mid = mul(add(a, b), 0.5);
      // 单位立方体沿 +Y 拉伸到骨段长度，再把 +Y 旋到骨段方向
      writeTransform(entity, mid, quatArc([0, 1, 0], dir), [def.w, length, def.h]);
    }

    const headPos = solved.positions.head;
    writeTransform(headEntity, headPos, [0, 0, 0, 1], [
      HEAD_RADIUS,
      HEAD_RADIUS,
      HEAD_RADIUS,
    ]);

    if (cameraEntity >= 0 && cameraFollow.on) {
      // 等距机位：角色后上方，始终看向角色胸口
      const yaw = -0.68;
      const camPos: Vec3 = [
        rt.rootPos[0] + Math.sin(yaw) * cameraDistance,
        cameraHeight,
        rt.rootPos[2] + Math.cos(yaw) * cameraDistance,
      ];
      const target: Vec3 = [rt.rootPos[0], 0.95, rt.rootPos[2]];
      const dir = norm(sub(target, camPos));
      // 引擎相机朝 -Z，所以把 (0,0,-1) 旋到 dir
      writeTransform(cameraEntity, camPos, quatArc([0, 0, -1], dir), [1, 1, 1]);
    }
  }

  let disposed = false;
  return {
    sync,
    setCameraFollow(on: boolean) {
      cameraFollow.on = on;
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const { entity } of partEntities) world.despawn(entity);
      world.despawn(headEntity);
      if (cameraEntity >= 0) world.despawn(cameraEntity);
      if (lightEntity >= 0) world.despawn(lightEntity);
      for (const h of [boxHandle, sphereHandle, ...Object.values(matHandles)]) {
        try {
          world.sharedRefs.release(h).unwrap();
        } catch {
          // 已释放时忽略
        }
      }
    },
  };
}
