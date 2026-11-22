# 玩家动作系统（player-motion）

天工引擎（ForgeAX 0.4.8）上的玩家动作模块：步行、疾走、下蹲、贴墙躲藏、翻滚换位，
以及它们之间的**姿态衔接**。不含跳跃、美术和关卡。

骨架是 **23 个关节的层级**，可视化有两条路径，共用同一份关节解算结果：

- `mesh3d.ts`：**引擎内真 3D 白模**（`MeshFilter` + `MeshRenderer` + 等距相机 + 平行光）。
  合并进主项目后就是 2.5D 场景里的角色，能被相机、光照、后期正常影响。**默认走这条。**
- `view3d.ts`：canvas 2D 手写等距渲染（盒体六面 + 画家算法 + 方向光）。
  不依赖 WebGPU，用于无 GPU 环境验证与截图取证。

白模规格参考了 **Tomcasso 的 `LowPolySceneSetup.cs`**（每个关节挂一个 Cube、用 scale 表达粗细）
与 **ZDD-cy 的 45° 等距斜俯视**；差别在于动作层已经直接给出关节世界坐标，
所以不需要在引擎里重建父子层级，每帧把「骨段中点 + 朝向四元数 + 长度」写进 Transform 即可。

---

## 一、能做什么

| 动作 | 触发 | 关键细节 |
|---|---|---|
| 待机 | 无输入 | 呼吸起伏，手臂自然垂在体侧 |
| 步行 | WASD | 1.7 m/s，步频 0.95 Hz，手臂反向摆动 |
| 疾走 | WASD + Shift | 4.3 m/s，步频 1.55 Hz，摆幅加大、躯干前倾 |
| 下蹲 | 按住 Ctrl | 骨盆下降 0.35 m；**髋前屈 60°、膝后折 100°、踝背屈 28°**，蹲行 0.85 m/s，手臂前伸扶膝 |
| 贴墙躲藏 | 靠近可躲藏墙（≤0.45 m） | **真正侧身转为沿墙朝向**，靠墙那侧手臂上举扶墙，外侧手臂收紧 |
| 翻滚换位 | 贴墙时按 Space | 0.58 s，团身抱膝，整体侧翻 360°，横移 1.5 m 到**对面墙**并贴住 |

**衔接**是重点：所有状态切换都走统一的混合通道——切换瞬间记录旧姿态，
用 `easeInOut` 在 `blendDuration` 内插值到新姿态，HUD 会显示实时的「衔接进度」。

### 贴墙的进出条件（容易踩错，写清楚）

- **进入**：到墙距离 ≤ `coverSnapDistance`（0.45 m），且没按 Ctrl。
- **保持**：松手不动，或继续朝墙方向推方向键。
- **脱离**（三种，缺一不可）：
  1. 朝背离墙 / 沿墙的方向推方向键
  2. 按 Ctrl 下蹲
  3. 按 Space 翻滚到对面
- **贴墙时速度锁为 0**，所以「按方向键能走开」这条是必需的，
  否则玩家会被永久粘在墙上（早期版本就是这个缺陷）。

判断方向用的是**墙的法线**（`coverNormal`，由墙指向角色），不是「左/右」。
因为角色正对墙时左右会退化成 0，判断不出该往哪脱身。

---

## 二、文件结构

```
assets/player-motion/
├── skeleton.ts        骨架定义：23 个关节的父子关系 + 局部偏移向量（脚拆成 掌/趾/跟 三段）
├── pose.ts            姿态与 FK：欧拉角 → 世界坐标（纯数学，无引擎依赖）
├── motion.ts          动作状态机 + 姿态生成 + 过渡混合（核心）
├── input.ts           键盘采集 + 掩体探针（走廊式双墙）
├── view.ts            canvas 火柴人（2D 平面，保留作排查用）
├── view3d.ts          canvas 等距 3D 白模（盒体 + 深度排序 + 方向光，可截图验证）
├── mesh3d.ts          引擎真 3D 白模（MeshFilter + MeshRenderer + 相机 + 光照）
├── install.ts         接入层：installMotionDemo(ctx) 一行挂到引擎
├── preview-entry.ts   独立预览器入口（不启动引擎）
├── build-preview.mjs  用 tsc 转译成浏览器可跑的 JS，并生成 index.html
├── preview/           转译产物 + index.html，可直接开来看
└── __tests__/         29 个单测，覆盖 FK、状态机、衔接、翻滚换位、手臂姿态、蹲伏几何
```

依赖方向是单向的：`skeleton → pose → motion`，`input` 和 `view` 只读取它们，
`install` 是唯一碰引擎的地方。**动作逻辑完全可以在 Node 里单测**。

---

## 三、接入方式（给负责合并的人）

```ts
// 任何 engine realm 插件的 apply(ctx) 里
import { installMotionDemo } from './player-motion/install.ts';

installMotionDemo(ctx);                                  // 默认：引擎 3D 白模
installMotionDemo(ctx, { view: 'canvas2d' });            // 无 GPU 时的降级视图
installMotionDemo(ctx, { ownCamera: false });             // 主项目已有相机
installMotionDemo(ctx, { view: 'none' });                 // 只要动作数据
installMotionDemo(ctx, { spawn: [0, 0, 0], corridorHalfWidth: 1.0 });
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
coverSnapDistance                     // 多近才真正贴上去（默认 0.45m）
rollCoverSnap                         // 翻滚落地后多近算"贴住对面"（默认 0.6m）
coverHoldDot                          // 朝墙推的判定阈值
crouchDrop                            // 蹲下时骨盆下降量
```

⚠️ **`rollDistance` 和走廊宽度是配套的**：演示场景 `corridorHalfWidth = 1.0`、
身体半径 0.25，所以贴墙位置是 ±0.75，翻滚 1.5 m = 2 × 0.75，正好从一侧到另一侧。
只改其中一个，翻滚就会落在半路（不会贴住对面）。

改动作姿态本身：`poseIdle / poseGait / poseCover / poseRoll` 四个函数，
每个返回一套 `Pose`（关节 → 欧拉角）。幅度、角度都写得很直白，改数值即可。

---

## 五、本地看效果

**方式 A：独立预览器（推荐，不需要引擎/GPU）**

```bash
node assets/player-motion/build-preview.mjs      # 转译到 preview/*.js，并生成 index.html
cd assets/player-motion/preview
python -m http.server 15175                      # 打开 http://127.0.0.1:15175/
```

`build-preview.mjs` 会自动生成入口页，**不需要手写 index.html**；
它还会把所有相对 import 统一成同目录引用（早期版本把 `../x.ts` 原样输出成 `../x.js`，
浏览器会 404）。

- 默认自动演示：待机 → 步行 → 疾走 → 下蹲 → 走向墙 → 贴墙 → 翻滚 → 落地贴对面
- 预览器用的是 `view3d.ts`（canvas 等距 3D 白模），不需要 GPU
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

- `npx vitest run assets/player-motion` → **29 个测试全过**（FK 关节长度、站姿高度、
  六态切换、蹲伏几何（膝朝前/脚贴地/脚掌水平）、贴墙侧身、贴墙脱身、翻滚跨到对面墙、手臂姿态、衔接进度）
- `project check` → ok
- `project build` → ok，动作代码进入 `program-*.js` chunk（约 15.4 KB）
- 浏览器截图逐一确认：步行 1.70 m/s 摆腿、疾走 4.30 m/s 大幅摆腿前倾、
  下蹲屈膝、贴墙单手扶墙、翻滚团身翻转

修复过的真实缺陷（每一条都有测试或截图兜底）：

1. **步态相位单位错用**：`gaitPhase` 已是弧度，`poseGait` 里又乘了 `2π`，
   导致摆腿幅度被压到 1° 以内。已修正，并加了回归测试。
2. **角色能穿墙且穿墙后仍判定贴墙**：给探针加了 `clamp` 边界钳制，
   并改为贴近 0.45 m 才触发贴墙。
3. **手臂永远是 T-pose**：手臂骨段沿 ±X 伸展，绕 X 轴旋转只让手臂自转、
   不改变指向——原来写在 `shoulder.x` 上的摆臂角度完全无效。改为
   「Z 旋转垂下 + X 旋转前后摆」（`poseArm()` 统一处理），并加了手部高度的回归测试。
4. **贴墙后无法移动脱身**：贴墙时 `targetSpeed = 0`，且只有按 Ctrl 能脱离，
   玩家会被永久粘在墙上。改为朝背离墙 / 沿墙方向推方向键即可脱身，
   方向判断基于墙的法线（正对墙时「左右」会退化）。
5. **贴墙不是真侧身**：原来只把朝向拧 18° 假装侧身。现在真的把朝向转到沿墙方向，
   并按重算后的左右关系决定哪只手扶墙。
6. **翻滚方向与距离都不对**：位移原本用「角色右方」，侧身后会滚错方向；
   距离 1.45 m 小于墙间距，永远到不了对面。改为锁存「远离起手墙」的法线方向、
   滚 1.5 m，并把演示走廊半宽改为 1.0 —— 现在能真的从一侧滚到另一侧并贴住。
7. **撞墙后速度不衰减**：位置被 `clamp` 但速度继续增长，HUD 显示假速度。
   改为钳制发生时同步压掉速度。
8. **蹲伏是「跪」不是「蹲」**：`hip` 用了 +62°（大腿向后转），膝盖被甩到身体后方；
   `knee` 也用负值（向前折），违反膝关节只能向后弯的生理方向。
   改为髋 -60° / 膝 +100° / 踝 -28°，并重算 `crouchDrop = 0.35`，
   使髋到踝的垂直距离正好缩短同样的量 —— 脚踝留在原高度、脚掌贴地。
9. **脚只有一段**：踝→脚一个盒子，蹲下时就是一块斜板，看不出脚掌贴地。
   拆成「踝→跖趾 / 跖趾→脚尖 / 踝→脚跟」三段，脚长 0.25m，脚底落在 y≈0.005。
10. **静止蹲着还在摆腿**：`gaitPhase` 在 crouch 状态无条件推进，站着不动腿也在小幅摆。
   改为摆幅按实际速度缩放，停住即定住。

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
  把 `CoverProbe` 换成射线/触发器实现即可（实现 `query / clamp / walls` 三个方法），动作逻辑不用改。
- **贴墙是「站定」而非「沿墙横移」**：贴住后速度锁 0，只能通过翻滚或推方向键离开。
  真实潜行游戏通常允许贴墙横移，这需要在 `cover` 状态里单独接一条沿墙移动通道。
- **`rollDistance` 与关卡宽度绑定**：演示用固定 1.5 m；真实关卡宽度不一时，
  应该改成「按当前到对面墙的距离动态计算」，现在还没做。
- **火柴人不是骨骼网格**：要换成真实角色时，把 `Pose` 的关节角度写进 Skin 的关节旋转，
  `motion.ts` 的状态机和过渡逻辑可以原样保留。
- **未做根运动提取**：位移直接改 `rootPos`，没有 bake 成 root motion 曲线。
- **骨架命名与实际左右相反**：`shoulderL` 的骨段在 +X（角色朝向 0° 时的右方）。
  这是早期留下的命名，改动会波及 `skeleton.ts` 与所有姿态函数，暂以注释标注（`poseCover` 里已处理）。
- **速度是逻辑速度**：撞墙时已同步压速，但它仍不是物理求解出来的值，
  接真实物理后应改用碰撞反馈的速度。
- **`mesh3d` 没在无 GPU 环境验证过渲染**：本机无头浏览器 `requestAdapter()` 返回 null，
  截图只能验证 `view3d`（canvas 版）。`mesh3d` 的构建、类型、实体装配都验过
  （spawn 多组件、共享 mesh 复用、每帧 Transform 写入均有实测），
  但**最终画面需要在有 GPU 的浏览器里确认**。材质参数名（`baseColor`）与光照强度
  是最可能需要微调的两处。
- **`mesh3d` 的相机宽高比取自 `window`**：插件里拿不到 renderer 真实尺寸，
  窗口与画布比例不一致时会轻微变形。主项目已有相机时请传 `ownCamera: false`。
- **蹲下时脚会相对 `rootPos` 前移约 0.19m**：这是「屈髋下蹲」的几何必然
  （膝盖前伸、脚在髋下方偏前）。动作层没有 IK，所以 `rootPos` 不完全等于脚底中心。
  接真实项目时应由 IK 或物理把脚锁到地面。
- **贴墙朝向是「转到沿墙」，不是「贴上去」**：角色与墙面之间仍有 0.3m（身体半径）。
  真实项目里应由碰撞体/胶囊把角色压到墙面，动作层只负责姿态。
