/**
 * 动作演示的场景探针与输入采集。
 *
 * 这里刻意把「场景」抽象成最小接口：
 *   给一个世界坐标，回答「附近有没有可躲藏的墙、在哪一侧、多远」。
 * 这样动作系统完全不依赖具体关卡；将来接真实场景时，
 * 只要替换 CoverProbe 的实现（射线检测或触发器），动作代码不用动。
 */

import type { Vec3 } from './skeleton.ts';

/** 可躲藏掩体的查询接口。 */
export interface CoverProbe {
  query(position: Vec3, facingDeg: number): {
    available: boolean;
    side: -1 | 1;
    distance: number;
  };
  /** 把角色限制在走廊内，模拟墙体阻挡。返回修正后的位置。 */
  clamp(position: Vec3, bodyRadius?: number): Vec3;
}

/**
 * 演示用掩体：两条沿 Z 轴延伸的平行墙，位于 x = ±halfWidth。
 * 角色在两墙之间移动，靠近任一侧即视为可贴墙。
 */
export function createCorridorProbe(halfWidth = 1.6, trigger = 1.15): CoverProbe {
  const inside = (halfWidth: number) => (px: number) => Math.min(halfWidth, Math.max(-halfWidth, px));

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
      const side: -1 | 1 = dotRight > 0 ? 1 : -1;

      return {
        available: distance <= trigger,
        side,
        distance,
      };
    },
    clamp(position, bodyRadius = 0.3) {
      const limit = Math.max(0, halfWidth - bodyRadius);
      const px = inside(limit)(position[0]);
      return px === position[0] ? position : [px, position[1], position[2]];
    },
  };
}

/** 键盘输入状态。 */
export interface RawInput {
  forward: boolean;
  back: boolean;
  left: boolean;
  right: boolean;
  crouch: boolean;
  sprint: boolean;
  rollPressed: boolean;
}

export function createKeyboardInput(target: Window | HTMLElement = window): {
  read(): RawInput;
  dispose(): void;
} {
  const down = new Set<string>();
  let rollQueued = false;

  const onKeyDown = (e: Event) => {
    const ev = e as KeyboardEvent;
    const code = ev.code;
    if (
      code === 'Space' ||
      code === 'KeyW' ||
      code === 'KeyA' ||
      code === 'KeyS' ||
      code === 'KeyD' ||
      code === 'ShiftLeft' ||
      code === 'ControlLeft' ||
      code === 'ArrowUp' ||
      code === 'ArrowDown' ||
      code === 'ArrowLeft' ||
      code === 'ArrowRight'
    ) {
      // 只阻止空格滚动，其余交给页面
      if (code === 'Space') ev.preventDefault();
    }
    if (code === 'Space' && !down.has('Space')) rollQueued = true;
    down.add(code);
  };

  const onKeyUp = (e: Event) => {
    down.delete((e as KeyboardEvent).code);
  };

  const onBlur = () => down.clear();

  target.addEventListener('keydown', onKeyDown as EventListener);
  target.addEventListener('keyup', onKeyUp as EventListener);
  window.addEventListener('blur', onBlur);

  return {
    read(): RawInput {
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
      target.removeEventListener('keydown', onKeyDown as EventListener);
      target.removeEventListener('keyup', onKeyUp as EventListener);
      window.removeEventListener('blur', onBlur);
      down.clear();
    },
  };
}

/** 把原始按键翻译成 MotionInput。相机固定朝 +Z，所以输入即世界方向。 */
export function toMotionInput(
  raw: RawInput,
  probeResult: { available: boolean; side: -1 | 1; distance: number },
) {
  return {
    moveX: (raw.right ? 1 : 0) - (raw.left ? 1 : 0),
    moveZ: (raw.forward ? 1 : 0) - (raw.back ? 1 : 0),
    crouch: raw.crouch,
    sprint: raw.sprint,
    rollPressed: raw.rollPressed,
    coverAvailable: probeResult.available,
    coverSide: probeResult.side,
    coverDistance: probeResult.distance,
  };
}
