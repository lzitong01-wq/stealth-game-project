# 玩家动作系统（player-motion）

天工引擎（ForgeAX 0.4.8）上的玩家动作模块：步行、疾走、下蹲、贴墙躲藏、翻滚换位，
以及它们之间的**姿态衔接**。不含跳跃、美术和关卡。

骨架用**多关节火柴人**表达（19 个关节），可视化是 `#game-ui` 上的 canvas 双视图，
不依赖相机、光照、材质和 mesh——所以在没有美术资源、甚至没有 WebGPU 的环境里也能验证动作。

---

## 一、能做什么

| 动作 | 触发 | 关键细节 |
|---|---|---|
| 待机 | 无输入 | 呼吸起伏，手臂自然下垂 |
| 步行 | WASD | 1.7 m/s，步频 0.95 Hz，手臂反向摆动 |
| 疾走 | WASD + Shift | 4.3 m/s，步频 1.55 Hz，摆幅加大、躯干前倾 |
| 下蹲 | 按住 Ctrl | 骨盆下降 0.38 m，髋弯 62°、膝弯 108°，蹲行 0.85 m/s |
| 贴墙躲藏 | 靠近可躲藏墙（≤0.45 m） | 侧身、内侧手臂上举扶墙、外侧手臂收紧 |
| 翻滚换位 | 贴墙时按 Space | 0.58 s，团身抱膝，整体侧翻 360°，横移 1.45 m |

**衔接**是重点：所有状态切换都走统一的混合通道——切换瞬间记录旧姿态，
用 `easeInOut` 在 `blendDuration` 内插值到新姿态，HUD 会显示实时的「衔接进度」。

---

## 二、文件结构

```
assets/player-motion/
├── skeleton.ts        骨架定义：19 个关节的父子关系 + 局部偏移向量
├── pose.ts            姿态与 FK：欧拉角 → 世界坐标（纯数学，无引擎依赖）
├── motion.ts          动作状态机 + 姿态生成 + 过渡混合（核心）
├── input.ts           键盘采集 + 掩体探针（走廊式双墙）
├── view.ts            canvas 火柴人可视化（3/4 视图 + 俯视图）
├── install.ts         接入层：installMotionDemo(ctx) 一行挂到引擎
├── preview-entry.ts   独立预览器入口（不启动引擎）
├── build-preview.mjs  用 tsc 把上面的 TS 转译成浏览器可跑的 JS
├── preview/           转译产物 + index.html，可直接开来看
└── __tests__/         15 个单测，覆盖 FK、状态机、衔接、翻滚
```

依赖方向是单向的：`skeleton → pose → motion`，`input` 和 `view` 只读取它们，
`install` 是唯一碰引擎的地方。**动作逻辑完全可以在 Node 里单测**。

---

## 三、接入方式（给负责合并的人）

```ts
// 任何 engine realm 插件的 apply(ctx) 里
import { installMotionDemo } from './player-motion/install.ts';

installMotionDemo(ctx);
// 或指定挂载点 / 出生点 / 走廊宽度
installMotionDemo(ctx, { mount: '#game-ui', spawn: [0, 0, 0], corridorHalfWidth: 1.6 });
```

本项目里实际只改了两处：`assets/scene-owner.pack.ts` 加一行 import、一行调用。

**合并到别的工程时**：

1. 把整个 `assets/player-motion/` 目录拷过去
2. 在任意 engine 插件里加一行 `installMotionDemo(ctx)`
3. 不需要改 `forge.json`——这个模块不是独立的 Pack root，不占用 `roots` 名额

---

## 四、调参

全部集中在 `motion.ts` 的 `MOTION_CONFIG`：

```ts
walkSpeed / runSpeed / crouchSpeed    // 三档速度
walkCycleHz / runCycleHz              // 步频
turnSpeed                             // 转身速度（度/秒）
crouchBlend / coverBlend              // 下蹲、贴墙的过渡时间
rollDuration / rollDistance           // 翻滚时长与横移距离
crouchDrop                            // 蹲下时骨盆下降量
```

改动作姿态本身：`poseIdle / poseGait / poseCover / poseRoll` 四个函数，
每个返回一套 `Pose`（关节 → 欧拉角）。幅度、角度都写得很直白，改数值即可。

---

## 五、本地看效果

**方式 A：独立预览器（推荐，不需要引擎/GPU）**

```bash
node assets/player-motion/build-preview.mjs      # 重新转译到 preview/*.js（改源码后执行）
cd assets/player-motion/preview
python -m http.server 15175                      # 打开 http://127.0.0.1:15175/
```

- 默认自动演示：待机 → 步行 → 疾走 → 下蹲 → 走向墙 → 贴墙 → 翻滚
- `?at=7` 快进到第 7 秒并冻结（截图取证用）
- `?script=0` 改为键盘控制

**方式 B：引擎内**

```bash
node node_modules/@forgeax/engine/dist/bin/forgeax.mjs project build
node node_modules/@forgeax/engine/dist/bin/forgeax.mjs project preview --port 15173 --json
```

引擎内走的是同一份 `install.ts`，火柴人画在 `#game-ui` 上。

---

## 六、验证记录

- `npx vitest run assets/player-motion` → **15 个测试全过**（FK 关节长度、站姿高度、
  四态切换、蹲下过渡、贴墙、翻滚旋转与位移、衔接进度）
- `project check` → ok
- `project build` → ok，动作代码进入 `program-*.js` chunk（约 14.5 KB）
- 浏览器截图逐一确认：步行 1.70 m/s 摆腿、疾走 4.30 m/s 大幅摆腿前倾、
  下蹲屈膝、贴墙单手扶墙、翻滚团身翻转

修复过的两个真实缺陷（都有测试或截图兜底）：

1. **步态相位单位错用**：`gaitPhase` 已是弧度，`poseGait` 里又乘了 `2π`，
   导致摆腿幅度被压到 1° 以内。已修正，并加了回归测试。
2. **角色能穿墙且穿墙后仍判定贴墙**：给探针加了 `clamp` 边界钳制，
   并改为贴近 0.45 m 才触发贴墙、贴墙时站定、按 Ctrl 蹲下脱离。

---

## 七、参考了什么

- **Tomcasso（Unity，`LowPolyPoseDriver.cs`）**：借鉴了「零 AnimationClip、纯代码驱动
  关节旋转」的思路，以及蹲伏用髋膝角度组合表达、受击/死亡叠加姿态的做法。
  骨架比例和关节命名重新设计，C# 代码没有直接搬运。
- **ZDD-cy（天工引擎 0.1.7，`main.ts` / `skillbar.ts`）**：借鉴了引擎侧的
  系统注册（`world.addSystem(Update, ...)`）、相机相对移动的输入解算、
  以及 HUD 挂 `#game-ui` 的做法。他那边 FSM 管的是道具状态，这里是人物动作，语义不同。
- **官方**：`skills/forgeax-engine-ecs`（系统与调度）、`forgeax-engine-app`（插件装配与 cleanup）、
  `forgeax-engine-debug-draw`（对比过用线画的方案，最终选 canvas 以便脱离渲染管线验证）。

---

## 八、已知限制

- **没有物理**：走廊边界是探针里的 `clamp` 模拟，不是真实碰撞体。接真实关卡时，
  把 `CoverProbe` 换成射线/触发器实现即可，动作逻辑不用改。
- **火柴人不是骨骼网格**：要换成真实角色时，把 `Pose` 的关节角度写进 Skin 的关节旋转，
  `motion.ts` 的状态机和过渡逻辑可以原样保留。
- **未做根运动提取**：位移直接改 `rootPos`，没有 bake 成 root motion 曲线。
- **速度是逻辑速度**：撞墙时位置被钳制但速度仍在增长（下一帧会被状态机拉回），
  接真实物理时应改用碰撞反馈的速度。
