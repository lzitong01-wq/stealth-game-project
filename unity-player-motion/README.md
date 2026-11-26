# 玩家动作系统 · Unity 版（2.5D 白模）

从早期的天工引擎原型（TypeScript）迁移过来的 Unity 实现，**现在以本版为准**。
纯逻辑 + 白模，只做动作，不含跳跃 / 美术 / 关卡。

- **引擎**：Unity **2022.3.62f3c1**（LTS）
- **渲染**：内置管线（Built-in），Standard shader
- **输入**：旧输入系统（`activeInputHandler = 0`），不依赖 Input System 包

---

## 一、怎么打开

1. Unity Hub → Add → 选中项目根目录（仓库里叫 `unity-player-motion/`）
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
| 翻滚换位 | 贴墙时 Space | 0.58 s，团身抱膝，整体侧翻 360°，横移 **1.4 m 落到对面墙**并贴住 |

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
│   │   └── MotionValidation.cs      44 项断言校验（可在 batchmode 跑）
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

**Tools → Player Motion → 运行动作校验**，Console 会打出 44 条 PASS/FAIL。

### 方式 B：命令行（CI 可用）

```bash
Unity.exe -batchmode -quit \
  -projectPath <项目路径> \
  -executeMethod PlayerMotion.EditorTools.MotionValidation.RunBatch \
  -logFile validate.log
```

失败时返回非零退出码。

### 当前校验结果（44/44 通过）

```
站高 1.700m | 脚底 y=0.005
蹲下：膝在髋前 0.415m、低 0.135m | 踝高漂移 0.002m | 脚掌水平差 0.004m
蹲伏：脊柱前倾 27.7°、颈反向 -25.0°
待机：手低于肩 0.511m | 肘弯曲：手在肩前 0.238m
贴墙：侧身 facing=0.0° | 身体外缘 1.00 ≤ 墙面 1.00（不嵌墙）
      脚落地：踝高 0.060（与站立一致）| 脚尖 0.001 / 脚跟 0.005 | 脚掌水平差 0.005m
翻滚：落点 -0.70（精确）| 贴住对面 | 朝向全程跟随 Facing（偏差 0.0°）
输入：相机朝 -Z 时 W → (0.00, -1.00)、D → (-1.00, 0.00)
      实际摆法 W → (0.57, -0.82)，与相机水平前向一致
贴墙：手 x=±1.01（墙面 ±1.00，手掌贴面不穿墙）| 身体外缘 1.00 ≤ 墙面 1.00
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
| 测试 | vitest 30 条 | Editor 校验 44 条 |

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
8. **翻滚够不到对面**：1.45m < 墙间距，改 1.4m 并把走廊半宽定为 1.0
9. **脚只有一段**：蹲下就是一块斜板，拆成 掌 / 趾 / 跟 三段
10. **静止蹲着还在摆腿**：摆幅要按实际速度缩放，停住即定住

---

## 七、本轮复查修掉的问题（C# 版特有）

迁移完成后又逐文件复查了一遍，这几处是**C# 版自己引入**、原版没有的：

1. **翻滚时朝向被重置**：`RootEuler` 只写了 Z 轴（翻转量），把 `Facing` 丢了。
   欧拉顺序是 `Ry·Rx·Rz`，必须先绕局部 Z 翻转、再整体转朝向。
   原来的写法在贴墙朝向 180°（沿 -Z 那面墙）时，一按 Space 人会猛地转回世界 +Z，
   翻完再跳回来 —— 两次可见的转向突跳。现在两者合并，偏差实测 0.0°。
2. **贴墙时半个人嵌进墙里**：场景生成器把墙的**中心**放在 `x = ±HalfWidth`，
   墙内表面因此跑到 0.87；而角色被限制在 0.7、身体半径 0.3，外缘正好 1.0 —— 穿进去 13cm。
   改为按**内表面**定位（中心 = `HalfWidth + 厚度/2`），现在外缘 1.00 与墙面 1.00 相切。
3. **贴墙时脚悬空**：贴墙是微屈膝站姿，腿一弯踝就抬高，但根没有同步下沉，
   脚离地 5.2cm、脚跟离地 8.2cm（看着像踮脚）。
   加了 `MotionConfig.coverLegDrop`，和蹲下的 `crouchDrop` 同一个机制；
   同时把左右腿角度改成一致（原来左右差 4°，怎么调都有一条腿悬空），
   ankle 取 `-(骨盆-6 + 髋10 + 膝18)` 让脚掌净旋转归零。
   现在踝高与站立完全一致（差 0.000m），脚尖 0.001、脚跟 0.005。
4. **待机半蹲时每帧分配数组**：`new Vector3[23]` 在 `Step` 里，几十字节但每帧都分配。
   改用运行时自带的 `ScratchPose` 缓冲。
5. **翻滚距离与走廊尺寸不匹配**：写 1.5m 而实际两侧贴墙间距是 2×0.7=1.4，
   多出的 0.1m 靠 `Clamp` 硬吃掉——功能能跑，但参数是假的。改为 1.4m，落点精确到 -0.70。
6. **退出 Play 时报错**：`OnDestroy` 里用 `Destroy(material)`，退出播放模式时会警告
   「Destroy may not be called from edit mode」。按 `Application.isPlaying` 分支处理。

7. **按 W 人往镜头里跑（前后相反）**：相机摆在角色 **+Z 侧朝 -Z** 俯视，
   屏幕上方对应世界 `(0.57, -0.82)`；但 `MotionInputReader` 直接把按键当**世界方向**，
   按 W 得到 `(0, +1)`，与屏幕上方点积 −0.82 —— 正好相反。
   原 TS 版是 canvas 正投影、相机恒朝 +Z，所以那边「输入即世界方向」成立；
   迁到 Unity 后相机变斜，这层转换被漏掉了。
   新增 `ToWorldMove()`：屏幕右方 = `up × forward`，把按键投影到相机的水平前向/右向；
   没有相机信息时退化为世界方向。现在 W 永远朝屏幕上方。

8. **翻滚时角色整个消失**：`RootEuler` 是绕 **脚底** 转的，转到 180° 时骨盆跑到
   `rootPos.y - 0.95`，整个人沉到地面以下，画面上直接没人。
   改为绕**重心**（抬高 0.9m）翻转 —— 纯 Y 轴朝向旋转绕同一条竖线上的任意点结果相同，
   所以非翻滚状态完全不受影响。
9. **贴墙时手穿出墙面**：算下来 `手 x = 肩 + 上臂水平分量 + 前臂水平分量`，三处都溢：
   上臂抬 42° 时水平伸出 0.21m；前臂只折 38°，还留一大半指向外侧；
   身体侧倾 8° 又把肩从 0.87 推到 1.0。三处叠加后手顶到 x=1.37（穿墙 0.37m）。
   修法是三处一起收：上臂抬到 -75°（几乎竖直）、前臂折到 90°（真正沿墙）、
   身体侧倾降到 2°（侧身感本来就该由「朝向沿墙」提供，不是靠歪身子）。
   现在手 x=1.01、墙面 1.00，手掌正好按在墙上。

前八条都加了断言，下次改坏了会直接 FAIL。

---

## 八、已知限制

- **没有物理**：走廊边界是 `CorridorProbe.Clamp` 模拟，不是碰撞体。接真实关卡时换掉 Query/Clamp 实现（射线或触发器）即可，动作逻辑不用改
- **贴墙是站定，不能沿墙横移**：需要在 Cover 状态单独开一条沿墙移动通道
- **`RollDistance` 与关卡宽度绑定**：写死 1.4m = 2×(1.0-0.3)，真实关卡应按「到对面墙的实际距离」动态算
- **蹲下时脚相对 RootPos 前移约 0.19m**：屈髋下蹲的几何必然，动作层无 IK，接真实项目应由 IK/物理锁脚
- **骨架命名与实际左右相反**：`ShoulderL` 骨段在 +X（角色朝向 0° 时的右方），改动会波及全部姿态函数，暂以注释标注
- **白模无 LOD / 无蒙皮**：接真实角色时，把 `Pose` 的关节角度写进 SkinnedMeshRenderer 的骨骼旋转，状态机与过渡逻辑原样保留
- **未接入 Animator**：这是程序化动作（零 AnimationClip），与 Mecanim 状态机是两条路，二选一

---

## 九、扩展点

想换成真实角色而不是白模，只需要替换 `WhiteboxRig`：

```csharp
// 每帧拿到 23 个关节的世界位置与旋转
var solver = new PoseSolver();
solver.Solve(rt.Pose, rootPos, rt.RootEuler);

// 写进你的骨骼
boneTransforms[(int)Joint.Head].position = solver.Positions[(int)Joint.Head];
```

`MotionSystem` 完全不知道白模的存在，换成骨骼只是换一个消费者。

---

## 十、用 AI 直接操控编辑器（MCP for Unity）

工程已接好 **MCP for Unity v10.3.0**（`com.coplaydev.unity-mcp`，MIT）。
配好之后可以直接用自然语言让 AI 建物体、改脚本、读 Console、跑测试，不用手点菜单。

### 已做的配置

| 项 | 值 |
|---|---|
| Unity 包 | `com.coplaydev.unity-mcp` **v10.3.0**（`Packages/manifest.json` 里 pin 了 tag） |
| 桥接端口 | **6400**（默认值，冲突时包会自动顺延） |
| 自启 | 已开启（EditorPrefs `MCPForUnity.AutoStartOnLoad = 1`），打开编辑器就起桥接 |
| 客户端 | WorkBuddy，走 **stdio**，配置在 `~/.workbuddy/mcp.json` 的 `unityMCP` |
| Python 服务 | PyPI `mcpforunityserver`，用 `uvx` 拉起，**48 个工具** |

### 怎么用

1. Unity 打开本项目（桥接自动启动，状态见 **Window → MCP for Unity**，显示 `Connected`）
2. WorkBuddy 里首次使用要在连接器管理页对 `unityMCP` 点**信任**
3. 直接说需求，例如：
   - 「把 Player 的 WalkSpeed 调到 2.0，然后读一下 Console 有没有报错」
   - 「在当前场景里放 3 个红色方块，围成三角形」
   - 「跑一遍 PlayerMotion 的校验，把失败项列出来」

### 常用工具（共 48 个，这里是最常用的）

| 工具 | 用途 |
|---|---|
| `manage_scene` | 新建/打开/保存场景、查层级 |
| `manage_gameobject` | 增删改查 GameObject、设父子 |
| `create_script` / `apply_text_edits` | 写/改 C# 脚本（带 Roslyn 校验） |
| `read_console` | 读 Console 日志与报错 |
| `manage_editor` | 播放/暂停/停止、查编辑器状态 |
| `execute_menu_item` | 执行任意菜单项（包括本项目的 Tools 菜单） |
| `manage_camera` | 截图（含多视角环绕） |
| `batch_execute` | 一次跑多个工具调用 |

### 手动启停

- **Window → MCP for Unity** → `Start Server` / `Stop Server`
- 关掉自启：同一窗口的 Advanced 里取消 `Auto Start on Load`，
  或删注册表 `HKCU\Software\Unity Technologies\Unity Editor 5.x` 下的
  `MCPForUnity.AutoStartOnLoad_h*`

### 排错

| 现象 | 处理 |
|---|---|
| MCP 面板显示 `Disconnected` | 确认 Unity 已加载完成（编译中不会起桥接），点 Start Server |
| WorkBuddy 里看不到工具 | 检查 `~/.workbuddy/mcp.json` 的 `unityMCP` 是否还在，改完要重启 WorkBuddy |
| 端口被占 | 包会顺延到下一个可用端口；也可在 MCP 窗口里手动指定 |
| `uvx` 找不到 | 确认 `C:/Users/25920/AppData/Roaming/Python/Python313/Scripts/uvx.exe` 存在 |

> 注：MCP 只是「让 AI 能操作编辑器」，跑不跑 MCP 都不影响项目本身。
> 没配 MCP 时，菜单 **Tools → Player Motion** 下的功能照样能用。
