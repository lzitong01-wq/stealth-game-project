/**
 * 火柴人可视化（调试用）。
 *
 * 为什么用 canvas 而不是 3D 白模：
 * 动作系统的核心是「关节角度」和「状态衔接」，这些用侧视图看最清楚；
 * 而且 canvas 不依赖相机、光照、材质、mesh，不会因为渲染管线细节卡住验证。
 * 后续要接真实模型时，只需要把 Pose 交给 Skin/骨骼，不必改动作逻辑。
 *
 * 两个视图：
 *   左 · 侧视（角色始终朝屏幕右）—— 看走路摆腿、蹲下屈膝、翻滚团身
 *   右 · 俯视 —— 看朝向、贴墙方位、翻滚的横向位移
 */
import { BONE_SEGMENTS, } from './skeleton.js';
import { MOTION_CONFIG, STATE_LABEL, describeMotion, } from './motion.js';
const BG = 'rgba(12, 20, 24, 0.82)';
const PANEL_BORDER = 'rgba(120, 200, 190, 0.35)';
const BONE_COLOR = '#7fe3d4';
const JOINT_COLOR = '#e8c987';
const HEAD_COLOR = '#ffd98a';
const GHOST_COLOR = 'rgba(127, 227, 212, 0.22)';
const WALL_COLOR = '#e05252';
const TEXT = '#d8e8e4';
const TEXT_DIM = 'rgba(216, 232, 228, 0.55)';
const ACCENT = '#e8c987';
/**
 * 3/4 视角投影（等轴风格）：
 *   屏幕 X = 左右分量 + 前后分量的一部分
 *   屏幕 Y = 高度 - 前后分量的一部分
 * 纯侧视会把左右张开的四肢压成一条线，纯前视又看不出走路的前后摆腿，
 * 3/4 视角两者兼顾，最适合看动作。
 */
function projectSide(p, root, fwd) {
    const dx = p[0] - root[0];
    const dz = p[2] - root[2];
    const along = dx * fwd[0] + dz * fwd[2]; // 前后
    const side = dx * fwd[2] - dz * fwd[0]; // 左右
    return [side + along * 0.42, p[1] - root[1] - along * 0.26];
}
/** 俯视投影：沿角色右方 → 屏幕 X；前方 → 屏幕 -Y（前朝上）。 */
function projectTop(p, root, fwd) {
    const dx = p[0] - root[0];
    const dz = p[2] - root[2];
    const along = dx * fwd[0] + dz * fwd[2];
    const side = dx * fwd[2] - dz * fwd[0];
    return [side, -along];
}
function drawBone(ctx, a, b, color, width) {
    ctx.beginPath();
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.lineCap = 'round';
    ctx.moveTo(a[0], a[1]);
    ctx.lineTo(b[0], b[1]);
    ctx.stroke();
}
function drawSkeleton(ctx, solved, view, root, fwd, top, ghost) {
    const proj = (j) => {
        const p = solved.positions[j];
        const q = top ? projectTop(p, root, fwd) : projectSide(p, root, fwd);
        return [view.x + view.w * 0.5 + q[0] * view.scale, view.y + view.h * 0.58 - q[1] * view.scale];
    };
    const boneColor = ghost ? GHOST_COLOR : BONE_COLOR;
    const jointColor = ghost ? GHOST_COLOR : JOINT_COLOR;
    // 躯干加粗，四肢细一些，看起来更像人
    const thick = ['pelvis', 'spine', 'chest', 'neck', 'head'];
    for (const [a, b] of BONE_SEGMENTS) {
        const isThick = thick.includes(a) && thick.includes(b);
        const isLimb = !isThick && !thick.includes(a);
        drawBone(ctx, proj(a), proj(b), boneColor, isThick ? 5 : isLimb ? 3 : 4);
    }
    // 关节
    if (!ghost) {
        for (const [name] of Object.entries(solved.positions)) {
            const [x, y] = proj(name);
            ctx.beginPath();
            ctx.fillStyle = name === 'head' ? HEAD_COLOR : jointColor;
            ctx.arc(x, y, name === 'head' ? 4.5 : 2.4, 0, Math.PI * 2);
            ctx.fill();
        }
        // 头画成圆圈，一眼能看出朝向
        const [hx, hy] = proj('head');
        ctx.beginPath();
        ctx.strokeStyle = HEAD_COLOR;
        ctx.lineWidth = 2;
        ctx.arc(hx, hy - 6, 7, 0, Math.PI * 2);
        ctx.stroke();
    }
}
/** 画地面/参考线与视图边框。 */
function drawFrame(ctx, view, title) {
    ctx.strokeStyle = 'rgba(120, 200, 190, 0.16)';
    ctx.lineWidth = 1;
    ctx.strokeRect(view.x, view.y, view.w, view.h);
    ctx.fillStyle = TEXT_DIM;
    ctx.font = '11px system-ui, sans-serif';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'top';
    ctx.fillText(title, view.x + 8, view.y + 7);
}
/** 在侧视图里画一条地面线。 */
function drawGround(ctx, view, rootY, scale) {
    const y = view.y + view.h * 0.58 - rootY * scale;
    ctx.beginPath();
    ctx.strokeStyle = 'rgba(120, 200, 190, 0.25)';
    ctx.lineWidth = 1;
    ctx.moveTo(view.x + 6, y);
    ctx.lineTo(view.x + view.w - 6, y);
    ctx.stroke();
}
/** 俯视图里画掩体墙（贴墙时显示）。 */
function drawCoverWall(ctx, view, side, distance, scale, active) {
    const cx = view.x + view.w * 0.5 + side * distance * scale;
    ctx.beginPath();
    ctx.strokeStyle = active ? WALL_COLOR : 'rgba(224, 82, 82, 0.35)';
    ctx.lineWidth = active ? 3 : 1.5;
    ctx.moveTo(cx, view.y + 14);
    ctx.lineTo(cx, view.y + view.h - 14);
    ctx.stroke();
    if (active) {
        ctx.fillStyle = WALL_COLOR;
        ctx.font = '10px system-ui, sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'bottom';
        ctx.fillText('掩体', cx, view.y + view.h - 4);
    }
}
export function createMotionView(parent) {
    const canvas = document.createElement('canvas');
    canvas.width = 460;
    canvas.height = 400;
    canvas.style.cssText =
        'position:absolute;left:16px;top:16px;width:460px;height:400px;pointer-events:none;font-family:system-ui,sans-serif;';
    parent.appendChild(canvas);
    const ctx = canvas.getContext('2d');
    if (!ctx) {
        canvas.remove();
        return null;
    }
    const sideView = { x: 12, y: 34, w: 212, h: 300, scale: 96 };
    const topView = { x: 236, y: 34, w: 212, h: 300, scale: 96 };
    function render(rt, input) {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        // 背板
        ctx.fillStyle = BG;
        ctx.beginPath();
        ctx.roundRect(0, 0, canvas.width, canvas.height, 10);
        ctx.fill();
        ctx.strokeStyle = PANEL_BORDER;
        ctx.lineWidth = 1;
        ctx.stroke();
        // 标题与状态
        ctx.textAlign = 'left';
        ctx.textBaseline = 'top';
        ctx.fillStyle = ACCENT;
        ctx.font = '600 13px system-ui, sans-serif';
        ctx.fillText('玩家动作', 14, 10);
        ctx.fillStyle = TEXT;
        ctx.font = '11px system-ui, sans-serif';
        ctx.fillText(describeMotion(rt), 92, 11);
        drawFrame(ctx, sideView, '侧视 · 动作');
        drawFrame(ctx, topView, '俯视 · 朝向与位移');
        // 地面
        drawGround(ctx, sideView, 0, sideView.scale);
        const yawRad = (rt.facing * Math.PI) / 180;
        const fwd = [Math.sin(yawRad), 0, Math.cos(yawRad)];
        // 俯视掩体
        if (input.coverAvailable) {
            drawCoverWall(ctx, topView, input.coverSide, input.coverDistance, topView.scale, rt.state === 'cover');
        }
        // 上一状态残留姿态（幽灵），直观展示"衔接"
        if (rt.blend < 1 && rt.prevState !== rt.state) {
            // 用当前求解器画一份旧姿态，位置一致，只显示微弱轮廓
            const { solvePose } = motionInternals;
            const solvedPrev = solvePose(rt.prevPose, rt.rootPos, rt.rootEuler);
            drawSkeleton(ctx, solvedPrev, sideView, rt.rootPos, fwd, false, true);
            drawSkeleton(ctx, solvedPrev, topView, rt.rootPos, fwd, true, true);
        }
        const { solvePose } = motionInternals;
        // 蹲下时整体压低：改根位置的 Y
        const dropped = [rt.rootPos[0], rt.rootPos[1] - MOTION_CONFIG.crouchDrop * rt.crouchAmount, rt.rootPos[2]];
        const solved = solvePose(rt.pose, dropped, rt.rootEuler);
        drawSkeleton(ctx, solved, sideView, rt.rootPos, fwd, false, false);
        drawSkeleton(ctx, solved, topView, rt.rootPos, fwd, true, false);
        // 侧视图：朝向箭头
        ctx.beginPath();
        ctx.strokeStyle = 'rgba(232, 201, 135, 0.7)';
        ctx.lineWidth = 1.5;
        const ax = sideView.x + sideView.w - 26;
        const ay = sideView.y + sideView.h - 22;
        ctx.moveTo(ax - 16, ay);
        ctx.lineTo(ax, ay);
        ctx.lineTo(ax - 5, ay - 4);
        ctx.moveTo(ax, ay);
        ctx.lineTo(ax - 5, ay + 4);
        ctx.stroke();
        ctx.fillStyle = TEXT_DIM;
        ctx.font = '10px system-ui, sans-serif';
        ctx.textAlign = 'right';
        ctx.fillText('前', ax - 20, ay - 6);
        // 操作提示
        ctx.textAlign = 'left';
        ctx.fillStyle = TEXT_DIM;
        ctx.font = '11px system-ui, sans-serif';
        ctx.fillText('WASD 移动 · Shift 疾走 · Ctrl 下蹲 · 靠近墙自动贴墙 · Space 翻滚换位', 14, 350);
        // 状态条
        const barW = 150;
        const barH = 6;
        const bx = 14;
        const by = 372;
        ctx.fillStyle = 'rgba(120, 200, 190, 0.18)';
        ctx.beginPath();
        ctx.roundRect(bx, by, barW, barH, 3);
        ctx.fill();
        ctx.fillStyle = BONE_COLOR;
        ctx.beginPath();
        ctx.roundRect(bx, by, barW * Math.min(1, rt.blend), barH, 3);
        ctx.fill();
        ctx.fillStyle = TEXT_DIM;
        ctx.font = '10px system-ui, sans-serif';
        ctx.fillText('衔接进度', bx + barW + 8, by - 2);
        // 当前状态大字
        ctx.textAlign = 'right';
        ctx.fillStyle = ACCENT;
        ctx.font = '600 14px system-ui, sans-serif';
        ctx.fillText(STATE_LABEL[rt.state], canvas.width - 14, 348);
    }
    return {
        render,
        dispose() {
            canvas.remove();
        },
    };
}
/**
 * 避免循环 import：这里只引用 solvePose，
 * 把它集中在一个对象里，方便以后替换实现。
 */
import { solvePose as solvePoseImpl } from './pose.js';
const motionInternals = { solvePose: solvePoseImpl };
