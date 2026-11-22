/**
 * 动作演示的场景探针与输入采集。
 *
 * 这里刻意把「场景」抽象成最小接口：
 *   给一个世界坐标，回答「附近有没有可躲藏的墙、在哪一侧、多远」。
 * 这样动作系统完全不依赖具体关卡；将来接真实场景时，
 * 只要替换 CoverProbe 的实现（射线检测或触发器），动作代码不用动。
 */
/**
 * 演示用掩体：两条沿 Z 轴延伸的平行墙，位于 x = ±halfWidth。
 *
 * 尺寸是算过的，不是随手取的：
 *   - 贴墙位置 = halfWidth - bodyRadius = 1.0 - 0.25 = 0.75m
 *   - 距墙 = 0.25m，落在「可贴墙」阈值 0.45m 内
 *   - 翻滚位移 1.5m = 2 × 0.75，正好从一侧贴墙滚到另一侧贴墙，
 *     这样「在两个墙壁之间用翻滚转换躲藏位置」才真的成立。
 */
export function createCorridorProbe(halfWidth = 1.0, trigger = 1.15, bodyRadius = 0.3) {
    const clampAxis = (px) => Math.min(halfWidth - bodyRadius, Math.max(-(halfWidth - bodyRadius), px));
    return {
        query(position, facingDeg) {
            const px = position[0];
            const distLeft = px + halfWidth; // 到 x = -halfWidth
            const distRight = halfWidth - px; // 到 x = +halfWidth
            const useLeft = distLeft <= distRight;
            const distance = Math.max(0, useLeft ? distLeft : distRight);
            // 墙在世界 -X 或 +X 方向；判断它在角色左侧还是右侧
            const yaw = (facingDeg * Math.PI) / 180;
            const rightX = Math.cos(yaw);
            const rightZ = -Math.sin(yaw);
            const toWallX = useLeft ? -1 : 1;
            const dotRight = rightX * toWallX + rightZ * 0;
            const side = dotRight > 0 ? 1 : -1;
            // 法线由墙指向角色：靠左墙（-X）时是 +X，靠右墙（+X）时是 -X
            const normal = useLeft ? [1, 0, 0] : [-1, 0, 0];
            return {
                available: distance <= trigger,
                side,
                distance,
                normal,
            };
        },
        clamp(position, radius = bodyRadius) {
            const limit = Math.max(0, halfWidth - radius);
            const px = Math.min(limit, Math.max(-limit, position[0]));
            return px === position[0] ? position : [px, position[1], position[2]];
        },
        walls(position, span = 6) {
            const z0 = position[2] - span;
            const z1 = position[2] + span;
            return [-halfWidth, halfWidth].map((wallX) => ({
                a: [wallX, 0, z0],
                b: [wallX, 0, z1],
            }));
        },
    };
}
export function createKeyboardInput(target = window) {
    const down = new Set();
    let rollQueued = false;
    const onKeyDown = (e) => {
        const ev = e;
        const code = ev.code;
        if (code === 'Space' ||
            code === 'KeyW' ||
            code === 'KeyA' ||
            code === 'KeyS' ||
            code === 'KeyD' ||
            code === 'ShiftLeft' ||
            code === 'ControlLeft' ||
            code === 'ArrowUp' ||
            code === 'ArrowDown' ||
            code === 'ArrowLeft' ||
            code === 'ArrowRight') {
            // 只阻止空格滚动，其余交给页面
            if (code === 'Space')
                ev.preventDefault();
        }
        if (code === 'Space' && !down.has('Space'))
            rollQueued = true;
        down.add(code);
    };
    const onKeyUp = (e) => {
        down.delete(e.code);
    };
    const onBlur = () => down.clear();
    target.addEventListener('keydown', onKeyDown);
    target.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', onBlur);
    return {
        read() {
            const roll = rollQueued;
            rollQueued = false;
            return {
                forward: down.has('KeyW') || down.has('ArrowUp'),
                back: down.has('KeyS') || down.has('ArrowDown'),
                left: down.has('KeyA') || down.has('ArrowLeft'),
                right: down.has('KeyD') || down.has('ArrowRight'),
                crouch: down.has('ControlLeft') || down.has('ControlRight'),
                sprint: down.has('ShiftLeft') || down.has('ShiftRight'),
                rollPressed: roll,
            };
        },
        dispose() {
            target.removeEventListener('keydown', onKeyDown);
            target.removeEventListener('keyup', onKeyUp);
            window.removeEventListener('blur', onBlur);
            down.clear();
        },
    };
}
/** 把原始按键翻译成 MotionInput。相机固定朝 +Z，所以输入即世界方向。 */
export function toMotionInput(raw, probeResult) {
    return {
        moveX: (raw.right ? 1 : 0) - (raw.left ? 1 : 0),
        moveZ: (raw.forward ? 1 : 0) - (raw.back ? 1 : 0),
        crouch: raw.crouch,
        sprint: raw.sprint,
        rollPressed: raw.rollPressed,
        coverAvailable: probeResult.available,
        coverSide: probeResult.side,
        coverDistance: probeResult.distance,
        coverNormal: probeResult.normal,
    };
}
