# 玩家动作系统 · Unity 版（2.5D 白模）

从仓库里的天工引擎版本（`assets/player-motion/`，TypeScript）迁移过来的 Unity 实现。
纯逻辑 + 白模，只做动作，不含跳跃 / 美术 / 关卡。

- **引擎**：Unity **2022.3.62f3c1**（LTS）
- **渲染**：内置管线（Built-in），Standard shader
- **输入**：旧输入系统（`activeInputHandler = 0`），不依赖 Input System 包

---

## 一、怎么打开

1. Unity Hub → Add → 选中本目录（`unity-player-motion`）
2. 用 **2022.3.62f3c1** 打开（其他 2022.3.x 应该也能开，会自动升级）
3. 菜单 **Tools → Player Motion → 生成白模场景**（会创建并保存 `Assets/Scenes/PlayerMotion.unity`）
4. 按 **Play**

场景里已经包含：地面、两面走廊墙、方向光、等距相机、角色白模。

### 操作

| 按键 | 动作 |
|---|---|
| WASD / 方向键 | 移动 |
| Shift | 疾走 |
| Ctrl | 下蹲（蹲伏） |
| 靠近墙（≤0.45m） | 自动贴墙躲藏 |
| 贴墙时 Space | 翻滚到对面墙 |
| 贴墙时推方向键 | 脱身 |

---

## 二、动作与参数

| 动作 | 触发 | 关键参数 |
|---|---|---|
| 待机 | 无输入 | 呼吸起伏，站高 1.70 m，手臂自然垂在体侧 |
| 步行 | WASD | 1.70 m/s，步频 0.95 Hz，手臂反向摆动 |
| 疾走 | Shift | 4.30 m/s，摆幅加大、躯干前倾 |
| 下蹲 | Ctrl | 骨盆降 **0.35 m**；**髋前屈 60° / 膝后折 100° / 踝背屈 28°**；蹲行 0.85 m/s；躯干前倾、双臂前伸扶膝 |
| 贴墙躲藏 | 靠近墙 ≤0.45 m | **真侧身**（朝向转到沿墙），靠墙侧手上举扶墙，外侧手屈肘收紧 |
| 翻滚换位 | 贴墙时 Space | 0.58 s，团身抱膝，整体侧翻 360°，横移 **1.5 m 落到对面墙**并贴住 |

所有状态切换走统一混合通道：切换瞬间记录旧姿态，用 `EaseInOut` 在过渡时间内插值。

---

## 三、文件结构

```
unity-player-motion/
├── Assets/
│   ├── Scripts/PlayerMotion/
│   │   ├── Skeleton.cs              23 个关节的父子关系 + 骨偏移（脚拆掌/趾/跟三段）
│   │   ├── PoseSolver.cs            FK 求解 + 姿态插值 + 缓动（持缓冲区，零 GC）
│   │   ├── MotionConfig.cs          全部可调参数（Inspector 可见）
│   │   ├── MotionTypes.cs           状态枚举 / 输入结构 / 运行时状态
│   │   ├── MotionSystem.cs          六态状态机 + 各姿态生成（核心）
│   │   ├── CorridorProbe.cs         走廊式掩体探针（query / clamp / walls）
│   │   ├── MotionInputReader.cs     键盘采集
│   │   ├── WhiteboxRig.cs           3D 白模（每骨段一个 Cube，每帧只写 TRS）
│   │   └── PlayerMotionController.cs  MonoBehaviour 驱动 + 相机跟随
│   ├── Editor/
│   │   ├── WhiteboxSceneBuilder.cs  一键生成演示场景
│   │   └── MotionValidation.cs      27 项断言校验（可在 batchmode 跑）
│   └── Scenes/PlayerMotion.unity    生成的演示场景
├── Packages/                        包清单
├── ProjectSettings/                 项目设置
└── .gitignore                       Unity 生成的目录不进仓库
```

**依赖方向是单向的**：`Skeleton → PoseSolver → MotionSystem`，
`CorridorProbe` / `WhiteboxRig` 只读它们，`PlayerMotionController` 是唯一碰场景的地方。
**动作逻辑完全不依赖 GameObject**，可以在 EditMode 里直接算。

---

## 四、怎么验证

### 方式 A：编辑器菜单

**Tools → Player Motion → 运行动作校验**，Console 会打出 27 条 PASS/FAIL。

### 方式 B：命令行（CI 可用）

```bash
Unity.exe -batchmode -quit \
  -projectPath <项目路径> \
  -executeMethod PlayerMotion.EditorTools.MotionValidation.RunBatch \
  -logFile validate.log
```

失败时返回非零退出码。

### 当前校验结果（27/27 通过）

```
站高 1.700m | 脚底 y=0.005
蹲下：膝在髋前 0.415m、低 0.135m | 踝高漂移 0.002m | 脚掌水平差 0.004m
蹲伏：脊柱前倾 27.7°、颈反向 -25.0°
待机：手低于肩 0.511m | 肘弯曲：手在肩前 0.238m
贴墙：侧身 facing=0.0° | 背离推可脱身 | 翻滚跨到 x=-0.70 并贴住对面
```

---

## 五、和天工引擎版的关系

天工引擎版在 `assets/player-motion/`（分支 `player-motion`），是 TypeScript 实现，含 canvas 与引擎两套视图。
**两版的动作语义、参数、几何完全一致**，可以互相对照：

| | 天工引擎版 | 本版 |
|---|---|---|
| 语言 | TypeScript | C# |
| 关节数 | 23 | 23（同一套枚举与偏移） |
| 旋转顺序 | Ry·Rx·Rz | Unity `Quaternion.Euler` 同为 Z→X→Y，数值可直接对应 |
| 白模 | MeshFilter + MeshRenderer | Cube + Sphere（每帧写 TRS） |
| 测试 | vitest 30 条 | Editor 校验 27 条 |

迁移时保留了原版的所有几何约定和踩过的坑，**没有重新设计动作**，所以两边的数值可以逐项对照。

---

## 六、迭代中修掉的坑（迁移时一并保留）

这些是原版调试过程中发现并修正的真实缺陷，C# 版从设计上就避开了：

1. **手臂 T-pose**：骨段沿 ±X，绕 X 旋转只自转、不改指向 —— 摆臂必须用 Z（垂下）+ X（前后摆）
2. **肘从不弯曲**：同一个几何坑，elbow 的 X 旋转无效，必须用 **Y 旋转**把 ±X 转到 ±Z
3. **蹲伏是「跪」不是「蹲」**：`hip +62°` 会把膝盖甩到身后、`knee -108°` 违反生理方向
4. **蹲下脚悬空**：`crouchDrop` 必须等于「髋到踝垂直距离的缩短量」，否则脚穿地或悬空
5. **贴墙被粘住**：贴墙时速度锁 0，必须允许「朝背离/沿墙方向推」脱身，方向判断要用**墙法线**（正对墙时左右会退化为 0）
6. **贴墙不是真侧身**：只拧朝向 18° 是假的，要真的转到沿墙方向
7. **翻滚方向错**：不能用「角色右方」，要用锁存的「远离起手墙」法线方向
8. **翻滚够不到对面**：1.45m < 墙间距，改 1.5m 并把走廊半宽定为 1.0
9. **脚只有一段**：蹲下就是一块斜板，拆成 掌 / 趾 / 跟 三段
10. **静止蹲着还在摆腿**：摆幅要按实际速度缩放，停住即定住

---

## 七、已知限制

- **没有物理**：走廊边界是 `CorridorProbe.Clamp` 模拟，不是碰撞体。接真实关卡时换掉 Query/Clamp 实现（射线或触发器）即可，动作逻辑不用改
- **贴墙是站定，不能沿墙横移**：需要在 Cover 状态单独开一条沿墙移动通道
- **`RollDistance` 与关卡宽度绑定**：写死 1.5m，真实关卡应按「到对面墙的实际距离」动态算
- **蹲下时脚相对 RootPos 前移约 0.19m**：屈髋下蹲的几何必然，动作层无 IK，接真实项目应由 IK/物理锁脚
- **骨架命名与实际左右相反**：`ShoulderL` 骨段在 +X（角色朝向 0° 时的右方），改动会波及全部姿态函数，暂以注释标注
- **白模无 LOD / 无蒙皮**：接真实角色时，把 `Pose` 的关节角度写进 SkinnedMeshRenderer 的骨骼旋转，状态机与过渡逻辑原样保留
- **未接入 Animator**：这是程序化动作（零 AnimationClip），与 Mecanim 状态机是两条路，二选一

---

## 八、扩展点

想换成真实角色而不是白模，只需要替换 `WhiteboxRig`：

```csharp
// 每帧拿到 23 个关节的世界位置与旋转
var solver = new PoseSolver();
solver.Solve(rt.Pose, rootPos, rt.RootEuler);

// 写进你的骨骼
boneTransforms[(int)Joint.Head].position = solver.Positions[(int)Joint.Head];
```

`MotionSystem` 完全不知道白模的存在，换成骨骼只是换一个消费者。
