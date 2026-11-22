/**
 * 3D 白模视图（2.5D 等距视角）。
 *
 * 为什么不用 canvas 火柴人：项目定位是 2.5D，需要看到「有体积的人站在有墙的空间里」，
 * 而不是投影到平面的线条。这里把 19 个关节解算出的世界坐标拼成盒体白模，
 * 用等距投影 + 画家算法 + 方向光做出立体感——不依赖 WebGPU/相机/材质管线，
 * 所以在无 GPU 环境里也能跑、能截图取证。
 *
 * 姿态与盒体都来自同一份 `SolvedPose`：换到引擎里的 MeshFilter/MeshRenderer
 * 时，关节位置和朝向的计算完全一致，只是绘制后端不同。
 *
 * 参考了两位协作者的做法：
 *   - Tomcasso（Unity `LowPolySceneSetup.cs`）：每个骨段一个 Cube 拼人形、材质分色
 *   - ZDD-cy（天工 0.1.7 `main.ts`）：45° 等距斜俯视、HUD 挂 DOM 层
 */

import type { JointName, Vec3 } from './skeleton.ts';
import { solvePose, type SolvedPose } from './pose.ts';
import { MOTION_CONFIG, STATE_LABEL, describeMotion, type MotionRuntime } from './motion.ts';

/* ------------------------------------------------------------------ *
 * 基础向量（保持本文件零引擎依赖）
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

/* ------------------------------------------------------------------ *
 * 配色（与 2D 版保持同一套主题：暗青 + 暖黄）
 * ------------------------------------------------------------------ */

const BG = '#070d10';
const GRID = 'rgba(90, 160, 155, 0.22)';
const GRID_AXIS = 'rgba(120, 200, 190, 0.30)';
const WALL_FACE = [116, 48, 52] as const;
const WALL_TOP = [176, 82, 84] as const;
const BODY = [30, 92, 118] as const;
const LIMB = [24, 70, 92] as const;
const HEAD = [206, 168, 100] as const;
const FOOT = [18, 52, 68] as const;
const TEXT = '#d8e8e4';
const TEXT_DIM = 'rgba(216, 232, 228, 0.55)';
const ACCENT = '#e8c987';
const WALL_EDGE = 'rgba(224, 82, 82, 0.85)';

/** 主光方向（世界空间，指向光源）。固定不变，保证截图可复现。 */
const LIGHT = norm([0.55, 0.78, 0.32]);

/* ------------------------------------------------------------------ *
 * 投影
 * ------------------------------------------------------------------ */

/** 等距相机参数：偏航 + 俯角（负值 = 从上往下看）。 */
const CAM = { yaw: -0.68, pitch: -0.74 };

interface Cam {
  /** 相机对准的世界点。 */
  target: Vec3;
  /** 每米对应的像素数。 */
  scale: number;
  /** 画面中心（像素）。 */
  cx: number;
  cy: number;
}

/** 世界坐标 → 屏幕坐标 + 深度（深度越大越远）。 */
function project(p: Vec3, cam: Cam): [number, number, number] {
  const q = sub(p, cam.target);
  const cy = Math.cos(CAM.yaw);
  const sy = Math.sin(CAM.yaw);
  // 绕 Y 转（偏航）
  const x1 = q[0] * cy - q[2] * sy;
  const z1 = q[0] * sy + q[2] * cy;
  // 绕 X 转（俯角）
  const cp = Math.cos(CAM.pitch);
  const sp = Math.sin(CAM.pitch);
  const y2 = q[1] * cp - z1 * sp;
  const z2 = q[1] * sp + z1 * cp;
  return [cam.cx + x1 * cam.scale, cam.cy - y2 * cam.scale, z2];
}

/* ------------------------------------------------------------------ *
 * 几何：一个「从 A 到 B 的盒体」
 * ------------------------------------------------------------------ */

interface Quad {
  pts: [number, number][];
  depth: number;
  fill: string;
  stroke?: string;
}

type RGB = readonly [number, number, number];

function rgba(c: RGB, shade: number, alpha: number): string {
  const k = Math.max(0, Math.min(1.35, shade));
  const r = Math.round(Math.min(255, c[0] * k));
  const g = Math.round(Math.min(255, c[1] * k));
  const b = Math.round(Math.min(255, c[2] * k));
  return `rgba(${r},${g},${b},${alpha})`;
}

/**
 * 生成 A→B 盒体的 6 个面（世界坐标四角）。
 * 截面朝向由骨段方向 d 与世界上方向推出，退化时（竖直骨段）换一个参考轴。
 */
function boxFaces(a: Vec3, b: Vec3, w: number, h: number): Vec3[][] {
  const d = norm(sub(b, a));
  let right = cross(d, [0, 1, 0]);
  if (len(right) < 1e-4) right = [1, 0, 0];
  right = norm(right);
  const up = norm(cross(right, d));
  const hw = w / 2;
  const hh = h / 2;

  const corner = (p: Vec3, sx: number, sy: number): Vec3 =>
    add(add(p, mul(right, sx * hw)), mul(up, sy * hh));

  const a0 = corner(a, -1, -1);
  const a1 = corner(a, 1, -1);
  const a2 = corner(a, 1, 1);
  const a3 = corner(a, -1, 1);
  const b0 = corner(b, -1, -1);
  const b1 = corner(b, 1, -1);
  const b2 = corner(b, 1, 1);
  const b3 = corner(b, -1, 1);

  return [
    [a0, a1, a2, a3], // A 端
    [b3, b2, b1, b0], // B 端
    [a0, a3, b3, b0],
    [a1, b1, b2, a2],
    [a0, b0, b1, a1],
    [a3, a2, b2, b3],
  ];
}

/** 把世界四面体投影成屏幕四边形，并按法线算明暗。 */
function pushBox(
  out: Quad[],
  corners: Vec3[][],
  color: RGB,
  cam: Cam,
  alpha: number,
  outline?: string,
): void {
  for (const quad of corners) {
    const n = norm(
      cross(sub(quad[1], quad[0]), sub(quad[3], quad[0])),
    );
    // 双向光照：法线朝背光面时取绝对值，避免出现纯黑面
    const lambert = Math.abs(dot(n, LIGHT));
    const shade = 0.42 + 0.72 * lambert;
    const pts = quad.map((p) => {
      const s = project(p, cam);
      return [s[0], s[1]] as [number, number];
    });
    const depth = quad.reduce((acc, p) => acc + project(p, cam)[2], 0) / 4;
    out.push({ pts, depth, fill: rgba(color, shade, alpha), stroke: outline });
  }
}

/* ------------------------------------------------------------------ *
 * 角色白模定义（比例参考 Tomcasso 的低模人形）
 * ------------------------------------------------------------------ */

interface PartDef {
  from: JointName;
  to: JointName;
  w: number;
  h: number;
  color: RGB;
}

const PARTS: readonly PartDef[] = [
  // 躯干：骨盆 → 胸（一根粗盒，覆盖 spine）
  { from: 'pelvis', to: 'chest', w: 0.34, h: 0.21, color: BODY },
  // 颈
  { from: 'chest', to: 'neck', w: 0.12, h: 0.12, color: BODY },
  // 上臂 / 前臂
  { from: 'shoulderL', to: 'elbowL', w: 0.095, h: 0.095, color: LIMB },
  { from: 'elbowL', to: 'handL', w: 0.082, h: 0.082, color: LIMB },
  { from: 'shoulderR', to: 'elbowR', w: 0.095, h: 0.095, color: LIMB },
  { from: 'elbowR', to: 'handR', w: 0.082, h: 0.082, color: LIMB },
  // 大腿 / 小腿 / 脚
  { from: 'hipL', to: 'kneeL', w: 0.145, h: 0.145, color: LIMB },
  { from: 'kneeL', to: 'ankleL', w: 0.125, h: 0.125, color: LIMB },
  { from: 'hipR', to: 'kneeR', w: 0.145, h: 0.145, color: LIMB },
  { from: 'kneeR', to: 'ankleR', w: 0.125, h: 0.125, color: LIMB },
  // 脚：用一个略扁的盒，从踝向前
  { from: 'ankleL', to: 'footL', w: 0.12, h: 0.075, color: FOOT },
  { from: 'ankleR', to: 'footR', w: 0.12, h: 0.075, color: FOOT },
];

/** 头部：用球体表达（投影后是圆），比盒子更像人。 */
const HEAD_RADIUS = 0.125;

/* ------------------------------------------------------------------ *
 * 场景
 * ------------------------------------------------------------------ */

const WALL_HALF = 1.0;
const WALL_HEIGHT = 1.65;
const WALL_THICK = 0.26;

/** 把任意四边形投影并加明暗（墙面/顶面这类矩形用它，不能套 boxFaces）。 */
function pushRect(
  out: Quad[],
  p0: Vec3,
  p1: Vec3,
  p2: Vec3,
  p3: Vec3,
  color: RGB,
  cam: Cam,
  alpha: number,
  outline?: string,
  shadeMul = 1,
): void {
  const quad = [p0, p1, p2, p3];
  const n = norm(cross(sub(p1, p0), sub(p3, p0)));
  const shade = (0.42 + 0.72 * Math.abs(dot(n, LIGHT))) * shadeMul;
  const pts = quad.map((p) => {
    const s = project(p, cam);
    return [s[0], s[1]] as [number, number];
  });
  const depth = quad.reduce((acc, p) => acc + project(p, cam)[2], 0) / 4;
  out.push({ pts, depth, fill: rgba(color, shade, alpha), stroke: outline });
}

function pushScene(out: Quad[], cam: Cam, walls: readonly { a: Vec3; b: Vec3 }[], root: Vec3): void {
  const z0 = root[2] - 2.5;
  const z1 = root[2] + 5;
  const SEG = 0.9; // 墙面分块宽度，让"墙"不像一整块纯色平板

  // 按相机深度把两面墙分开：远的画实，近的半透明。
  // 否则近侧墙会整块盖住角色下半身（画家算法"正确"，但读不出动作）。
  const ordered = walls
    .map((w) => ({ w, depth: project([w.a[0], WALL_HEIGHT * 0.5, root[2]], cam)[2] }))
    .sort((a, b) => b.depth - a.depth);

  ordered.forEach(({ w }, idx) => {
    const isFar = idx === 0;
    const cx = w.a[0];
    const innerX = cx > 0 ? cx - WALL_THICK : cx + WALL_THICK;

    // 近侧墙只画墙顶轮廓，不画立面：等同于 2.5D 游戏里「切掉前景遮挡」，
    // 否则再半透明也会把角色的腿染成暗红，读不清迈步。
    if (!isFar) {
      pushRect(
        out,
        [innerX, WALL_HEIGHT, z0],
        [innerX, WALL_HEIGHT, z1],
        [cx, WALL_HEIGHT, z1],
        [cx, WALL_HEIGHT, z0],
        WALL_TOP,
        cam,
        0.75,
        WALL_EDGE,
      );
      pushRect(
        out,
        [innerX, WALL_HEIGHT, z0],
        [innerX, WALL_HEIGHT, z1],
        [innerX, WALL_HEIGHT - 0.05, z1],
        [innerX, WALL_HEIGHT - 0.05, z0],
        [230, 150, 140],
        cam,
        0.75,
      );
      return;
    }

    // 远侧墙：立面按段绘制，交替深浅形成砌块感
    for (let z = z0; z < z1 - 1e-6; z += SEG) {
      const zz = Math.min(z + SEG, z1);
      const k = Math.floor((z - root[2]) / SEG) % 2 === 0 ? 1 : 0.84;
      pushRect(
        out,
        [innerX, 0, z],
        [innerX, 0, zz],
        [innerX, WALL_HEIGHT, zz],
        [innerX, WALL_HEIGHT, z],
        WALL_FACE,
        cam,
        0.9,
        undefined,
        k,
      );
    }

    // 顶面（带描边，负责勾出墙的轮廓）
    pushRect(
      out,
      [innerX, WALL_HEIGHT, z0],
      [innerX, WALL_HEIGHT, z1],
      [cx, WALL_HEIGHT, z1],
      [cx, WALL_HEIGHT, z0],
      WALL_TOP,
      cam,
      0.92,
      WALL_EDGE,
    );

    // 墙顶外沿的一条亮线，强化"立起来"的感觉
    pushRect(
      out,
      [innerX, WALL_HEIGHT, z0],
      [innerX, WALL_HEIGHT, z1],
      [innerX, WALL_HEIGHT - 0.045, z1],
      [innerX, WALL_HEIGHT - 0.045, z0],
      [230, 150, 140],
      cam,
      0.9,
    );
  });
}

/** 地面网格：以角色为中心铺一片，跟着人走。 */
function drawGrid(
  ctx: CanvasRenderingContext2D,
  cam: Cam,
  root: Vec3,
  halfX = 3,
  back = 3,
  front = 7,
  step = 0.5,
): void {
  const z0 = Math.floor((root[2] - back) / step) * step;
  const z1 = root[2] + front;
  ctx.lineWidth = 1;
  for (let x = -halfX; x <= halfX + 1e-6; x += step) {
    const a = project([x, 0, z0], cam);
    const b = project([x, 0, z1], cam);
    ctx.strokeStyle = Math.abs(x) < 1e-6 ? GRID_AXIS : GRID;
    ctx.beginPath();
    ctx.moveTo(a[0], a[1]);
    ctx.lineTo(b[0], b[1]);
    ctx.stroke();
  }
  for (let z = z0; z <= z1 + 1e-6; z += step) {
    const a = project([-halfX, 0, z], cam);
    const b = project([halfX, 0, z], cam);
    ctx.beginPath();
    ctx.moveTo(a[0], a[1]);
    ctx.lineTo(b[0], b[1]);
    ctx.stroke();
  }
}

/** 脚下阴影：投影到地面的椭圆。 */
function drawShadow(ctx: CanvasRenderingContext2D, cam: Cam, root: Vec3, radius: number): void {
  const c = project([root[0], 0.005, root[2]], cam);
  ctx.save();
  ctx.beginPath();
  // 等距投影下圆形压成椭圆
  ctx.ellipse(c[0], c[1], radius * cam.scale, radius * cam.scale * 0.45, 0, 0, Math.PI * 2);
  ctx.fillStyle = 'rgba(0, 0, 0, 0.42)';
  ctx.fill();
  ctx.restore();
}

/* ------------------------------------------------------------------ *
 * 视图接口
 * ------------------------------------------------------------------ */

export interface MotionView3D {
  render(
    rt: MotionRuntime,
    input: {
      coverAvailable: boolean;
      coverSide: -1 | 1;
      coverDistance: number;
      walls?: readonly { a: Vec3; b: Vec3 }[];
    },
  ): void;
  dispose(): void;
}

export function createMotionView3D(parent: HTMLElement, width = 760, height = 540): MotionView3D | null {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  canvas.style.cssText =
    `position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);` +
    `width:${width}px;height:${height}px;border-radius:12px;` +
    `box-shadow:0 18px 60px rgba(0,0,0,0.55);`;
  parent.appendChild(canvas);
  const ctx = canvas.getContext('2d');
  if (!ctx) {
    canvas.remove();
    return null;
  }

  const cam: Cam = { target: [0, 0.85, 0], scale: 164, cx: width * 0.52, cy: height * 0.66 };

  function render(
    rt: MotionRuntime,
    input: {
      coverAvailable: boolean;
      coverSide: -1 | 1;
      coverDistance: number;
      walls?: readonly { a: Vec3; b: Vec3 }[];
    },
  ): void {
    ctx.fillStyle = BG;
    ctx.fillRect(0, 0, width, height);

    // 相机跟随角色（保持人在画面中心，地面网格随之滚动）
    cam.target = [rt.rootPos[0], 0.82, rt.rootPos[2]];

    drawGrid(ctx, cam, rt.rootPos);
    drawShadow(ctx, cam, rt.rootPos, 0.3);

    const quads: Quad[] = [];
    if (input.walls && input.walls.length > 0) pushScene(quads, cam, input.walls, rt.rootPos);

    // 关节解算：蹲下时整体压低（与 2D 版同一套约定）
    const dropped: Vec3 = [
      rt.rootPos[0],
      rt.rootPos[1] - MOTION_CONFIG.crouchDrop * rt.crouchAmount,
      rt.rootPos[2],
    ];
    const solved = solvePose(rt.pose, dropped, rt.rootEuler);

    for (const part of PARTS) {
      const a = solved.positions[part.from];
      const b = solved.positions[part.to];
      if (len(sub(b, a)) < 1e-4) continue;
      pushBox(quads, boxFaces(a, b, part.w, part.h), part.color, cam, 1);
    }

    // 排序：远的先画
    quads.sort((p, q) => q.depth - p.depth);
    for (const q of quads) {
      ctx.beginPath();
      ctx.moveTo(q.pts[0][0], q.pts[0][1]);
      for (let i = 1; i < q.pts.length; i++) ctx.lineTo(q.pts[i][0], q.pts[i][1]);
      ctx.closePath();
      ctx.fillStyle = q.fill;
      ctx.fill();
      if (q.stroke) {
        ctx.strokeStyle = q.stroke;
        ctx.lineWidth = 1.2;
        ctx.stroke();
      }
    }

    // 头：球体投影 = 圆，带径向渐变
    const headPos = solved.positions.head;
    const hs = project(headPos, cam);
    const hr = HEAD_RADIUS * cam.scale;
    const grad = ctx.createRadialGradient(
      hs[0] - hr * 0.35,
      hs[1] - hr * 0.4,
      hr * 0.15,
      hs[0],
      hs[1],
      hr,
    );
    grad.addColorStop(0, 'rgba(255, 236, 190, 0.98)');
    grad.addColorStop(1, rgba(HEAD, 0.62, 1));
    ctx.beginPath();
    ctx.arc(hs[0], hs[1], hr, 0, Math.PI * 2);
    ctx.fillStyle = grad;
    ctx.fill();
    ctx.strokeStyle = 'rgba(90, 70, 40, 0.5)';
    ctx.lineWidth = 1;
    ctx.stroke();

    drawHud(ctx, rt, solved, cam, input);
  }

  return {
    render,
    dispose() {
      canvas.remove();
    },
  };
}

/* ------------------------------------------------------------------ *
 * HUD：状态条 + 右上角小俯视
 * ------------------------------------------------------------------ */

function drawHud(
  ctx: CanvasRenderingContext2D,
  rt: MotionRuntime,
  solved: SolvedPose,
  cam: Cam,
  input: { coverAvailable: boolean; coverSide: -1 | 1; coverDistance: number; walls?: readonly { a: Vec3; b: Vec3 }[] },
): void {
  const W = ctx.canvas.width;
  const H = ctx.canvas.height;

  // 顶部信息条
  ctx.fillStyle = 'rgba(8, 16, 20, 0.72)';
  ctx.fillRect(0, 0, W, 34);
  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  ctx.fillStyle = ACCENT;
  ctx.font = '600 14px system-ui, sans-serif';
  ctx.fillText('玩家动作 · 3D 白模', 14, 17);
  ctx.fillStyle = TEXT;
  ctx.font = '12px ui-monospace, monospace';
  ctx.fillText(describeMotion(rt), 150, 17);

  // 右上角俯视图（看朝向 / 贴墙方位 / 翻滚位移）
  drawMiniTop(ctx, rt, cam, W - 168, 44, 152);

  // 底部提示 + 衔接条
  ctx.textAlign = 'left';
  ctx.fillStyle = TEXT_DIM;
  ctx.font = '11px system-ui, sans-serif';
  ctx.fillText('WASD 移动 · Shift 疾走 · Ctrl 下蹲 · 靠近墙自动贴墙 · Space 翻滚换位', 14, H - 42);

  const barW = 180;
  const bx = 14;
  const by = H - 24;
  ctx.fillStyle = 'rgba(120, 200, 190, 0.18)';
  ctx.beginPath();
  ctx.roundRect(bx, by, barW, 6, 3);
  ctx.fill();
  ctx.fillStyle = '#7fe3d4';
  ctx.beginPath();
  ctx.roundRect(bx, by, barW * Math.min(1, rt.blend), 6, 3);
  ctx.fill();
  ctx.fillStyle = TEXT_DIM;
  ctx.font = '10px system-ui, sans-serif';
  ctx.fillText('衔接进度', bx + barW + 8, by + 3);

  ctx.textAlign = 'right';
  ctx.fillStyle = ACCENT;
  ctx.font = '600 15px system-ui, sans-serif';
  ctx.fillText(STATE_LABEL[rt.state], W - 14, H - 26);

  // 贴墙提示
  if (rt.state === 'cover') {
    ctx.textAlign = 'center';
    ctx.fillStyle = 'rgba(224, 82, 82, 0.9)';
    ctx.font = '11px system-ui, sans-serif';
    ctx.fillText('已贴墙 · 按 Space 翻滚到对面 · 推方向键脱身', W / 2, H - 26);
  }
}

/** 小俯视图：角色朝向 + 两面墙。 */
function drawMiniTop(
  ctx: CanvasRenderingContext2D,
  rt: MotionRuntime,
  cam: Cam,
  x: number,
  y: number,
  size: number,
): void {
  ctx.save();
  ctx.fillStyle = 'rgba(8, 16, 20, 0.78)';
  ctx.beginPath();
  ctx.roundRect(x, y, size, size, 8);
  ctx.fill();
  ctx.strokeStyle = 'rgba(120, 200, 190, 0.3)';
  ctx.lineWidth = 1;
  ctx.stroke();

  const cx = x + size / 2;
  const cy = y + size * 0.62;
  const s = 44; // 每米像素

  // 墙
  if (Math.abs(rt.rootPos[0]) < 5) {
    for (const wx of [-WALL_HALF, WALL_HALF]) {
      const px = cx + (wx - rt.rootPos[0]) * s;
      const active = rt.state === 'cover' && Math.abs(Math.abs(wx) - WALL_HALF) < 1e-6;
      ctx.beginPath();
      ctx.strokeStyle = active ? 'rgba(224,82,82,0.95)' : 'rgba(224,82,82,0.42)';
      ctx.lineWidth = active ? 3 : 1.5;
      ctx.moveTo(px, y + 8);
      ctx.lineTo(px, y + size - 8);
      ctx.stroke();
    }
  }

  // 身体圆
  ctx.beginPath();
  ctx.fillStyle = 'rgba(127,227,212,0.14)';
  ctx.arc(cx, cy, 0.25 * s, 0, Math.PI * 2);
  ctx.fill();
  ctx.strokeStyle = 'rgba(127,227,212,0.5)';
  ctx.lineWidth = 1;
  ctx.stroke();

  // 朝向箭头
  const yaw = (rt.facing * Math.PI) / 180;
  const ax = cx + Math.sin(yaw) * 0.55 * s;
  const ay = cy - Math.cos(yaw) * 0.55 * s;
  ctx.beginPath();
  ctx.strokeStyle = ACCENT;
  ctx.lineWidth = 2;
  ctx.moveTo(cx, cy);
  ctx.lineTo(ax, ay);
  ctx.stroke();
  ctx.beginPath();
  ctx.fillStyle = ACCENT;
  ctx.arc(ax, ay, 2.6, 0, Math.PI * 2);
  ctx.fill();

  ctx.fillStyle = TEXT_DIM;
  ctx.font = '9px system-ui, sans-serif';
  ctx.textAlign = 'left';
  ctx.fillText('俯视 · 朝向', x + 7, y + 12);
  ctx.restore();
}
