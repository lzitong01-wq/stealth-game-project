// 动作系统校验（编辑器工具，无需 Test Framework 包）。
//
// 菜单：Tools/Player Motion/运行动作校验
// 命令行：Unity.exe -batchmode -quit -projectPath <项目> -executeMethod PlayerMotion.EditorTools.MotionValidation.RunBatch -logFile <日志>
//
// 断言内容对应 TypeScript 版的 30 个 vitest 用例，重点覆盖三类曾经出过错的地方：
//   1. 骨架与 FK（站高、骨段长度）
//   2. 蹲伏几何（膝朝前、踝不漂移、脚掌水平）
//   3. 手臂姿态（自然下垂、肘真的会弯）
//   4. 贴墙与翻滚（侧身、可脱身、跨到对面墙）

using UnityEditor;
using UnityEngine;

namespace PlayerMotion.EditorTools
{
    public static class MotionValidation
    {
        private static int _fail;
        private static int _pass;

        [MenuItem("Tools/Player Motion/运行动作校验")]
        public static void Run()
        {
            _fail = 0;
            _pass = 0;

            var cfg = new MotionConfig();
            var solver = new PoseSolver();

            ValidateSkeleton(solver);
            ValidateCrouch(cfg, solver);
            ValidateArms(cfg, solver);
            ValidateInputDirection();
            ValidateCoverAndRoll(cfg);

            string summary = $"[PlayerMotion] 校验完成：通过 {_pass} 项，失败 {_fail} 项";
            if (_fail == 0) Debug.Log(summary);
            else Debug.LogError(summary);
        }

        /// <summary>命令行入口：失败时以非零退出码结束，便于 CI 判断。</summary>
        public static void RunBatch()
        {
            Run();
            if (Application.isBatchMode) EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        // ---------------------------------------------------------------- 骨架 / FK

        private static void ValidateSkeleton(PoseSolver solver)
        {
            var rest = PoseUtil.RestPose();
            solver.Solve(rest, Vector3.zero, Vector3.zero);

            float headY = solver.Positions[(int)Joint.Head].y;
            float footY = solver.Positions[(int)Joint.FootL].y;
            Check(headY > 1.6f && headY < 1.85f, $"站高 {headY:F3}m 应在 1.6~1.85m");
            Check(Mathf.Abs(footY) < 0.05f, $"站立时脚底 y={footY:F3} 应接近 0");

            bool bonesOk = true;
            foreach (var (a, b) in Skeleton.BoneSegments)
            {
                float d = Vector3.Distance(solver.Positions[(int)a], solver.Positions[(int)b]);
                if (d <= 0.01f)
                {
                    bonesOk = false;
                    Debug.LogError($"  骨段 {a}->{b} 长度仅 {d:F4}m，关节重合");
                }
            }
            Check(bonesOk, "所有骨段长度 > 0.01m（无重合关节）");

            // 左右腿镜像对称
            float lx = solver.Positions[(int)Joint.HipL].x;
            float rx = solver.Positions[(int)Joint.HipR].x;
            Check(lx > 0f && rx < 0f && Mathf.Abs(lx + rx) < 0.01f, "左右髋关于中线对称");
        }

        // ---------------------------------------------------------------- 蹲伏几何

        private static void ValidateCrouch(MotionConfig cfg, PoseSolver solver)
        {
            var rt = MotionRuntime.Create(Vector3.zero);
            var input = MotionInput.None;
            input.Crouch = true;
            for (int i = 0; i < 90; i++) MotionSystem.Step(rt, input, 1f / 60f, cfg);

            Check(rt.State == MotionState.Crouch, $"按住 Ctrl 应进入 Crouch，实际 {rt.State}");
            Check(rt.CrouchAmount > 0.98f, $"蹲下混合量应到位，实际 {rt.CrouchAmount:F3}");

            // 站立基线
            solver.Solve(PoseUtil.RestPose(), Vector3.zero, Vector3.zero);
            float standAnkleY = solver.Positions[(int)Joint.AnkleL].y;

            // 蹲下（与视图层一样，用动作层算好的 RootDrop 整体压低）
            var root = new Vector3(0f, -rt.RootDrop, 0f);
            solver.Solve(rt.Pose, root, rt.RootEuler);

            var hip = solver.Positions[(int)Joint.HipL];
            var knee = solver.Positions[(int)Joint.KneeL];
            var ankle = solver.Positions[(int)Joint.AnkleL];
            var toe = solver.Positions[(int)Joint.ToeL];
            var heel = solver.Positions[(int)Joint.HeelL];

            // 回归：曾经 hip 用 +62°，膝盖被甩到身体后方（那是跪不是蹲）
            Check(knee.z - hip.z > 0.2f, $"蹲下时膝盖应向前（髋前 {knee.z - hip.z:F3}m）");
            Check(hip.y - knee.y > 0.1f, $"蹲下时膝盖应低于髋（低 {hip.y - knee.y:F3}m）");
            Check(Mathf.Abs(ankle.y - standAnkleY) < 0.05f,
                $"蹲下后踝高漂移 {Mathf.Abs(ankle.y - standAnkleY):F3}m，应 < 0.05（脚掌贴地）");
            Check(toe.y > -0.04f && heel.y > -0.04f, $"蹲下时脚不应穿地（脚尖 {toe.y:F3} / 脚跟 {heel.y:F3}）");
            Check(Mathf.Abs(toe.y - heel.y) < 0.05f, $"脚掌应接近水平（脚尖与脚跟差 {Mathf.Abs(toe.y - heel.y):F3}m）");

            // 躯干：前倾 + 颈反向抬起
            Check(rt.Pose[(int)Joint.Spine].x > 12f, $"蹲伏脊柱应前倾（{rt.Pose[(int)Joint.Spine].x:F1}°）");
            Check(rt.Pose[(int)Joint.Neck].x < -10f, $"蹲伏颈部应反向抬起（{rt.Pose[(int)Joint.Neck].x:F1}°）");

            // 脚部三段都要有独立坐标
            var foot = solver.Positions[(int)Joint.FootL];
            Check(Vector3.Distance(toe, foot) > 0.05f, "脚尖与跖趾应分离（脚拆三段）");
            Check(Vector3.Distance(heel, foot) > 0.1f, "脚跟与跖趾应分离（脚拆三段）");
        }

        // ---------------------------------------------------------------- 手臂

        private static void ValidateArms(MotionConfig cfg, PoseSolver solver)
        {
            // 待机：手臂自然下垂
            var rtIdle = MotionRuntime.Create(Vector3.zero);
            for (int i = 0; i < 40; i++) MotionSystem.Step(rtIdle, MotionInput.None, 1f / 60f, cfg);
            solver.Solve(rtIdle.Pose, Vector3.zero, rtIdle.RootEuler);

            var sh = solver.Positions[(int)Joint.ShoulderL];
            var hand = solver.Positions[(int)Joint.HandL];
            Check(sh.y - hand.y > 0.35f, $"待机时手应低于肩（低 {sh.y - hand.y:F3}m，不能是 T-pose）");
            Check(Mathf.Abs(hand.x - sh.x) < 0.4f, $"待机时手臂不应水平张开（横距 {Mathf.Abs(hand.x - sh.x):F3}m）");

            // 肘弯曲：蹲下时前臂前折，手应落在肩前方
            var rtCrouch = MotionRuntime.Create(Vector3.zero);
            var input = MotionInput.None;
            input.Crouch = true;
            for (int i = 0; i < 90; i++) MotionSystem.Step(rtCrouch, input, 1f / 60f, cfg);
            var root = new Vector3(0f, -rtCrouch.RootDrop, 0f);
            solver.Solve(rtCrouch.Pose, root, rtCrouch.RootEuler);

            var sh2 = solver.Positions[(int)Joint.ShoulderL];
            var hand2 = solver.Positions[(int)Joint.HandL];
            // 回归：肘弯曲曾写在 X 旋转上（前臂骨段沿 X，只自转不折），导致肘永远是直的
            Check(hand2.z - sh2.z > 0.12f, $"肘弯曲应生效：手在肩前方 {hand2.z - sh2.z:F3}m");
        }

        // ---------------------------------------------------------------- 输入方向

        /// <summary>
        /// WASD 必须按相机朝向解算。
        ///
        /// 回归：曾经直接把按键当世界方向，而相机摆在 +Z 侧朝 -Z 看，
        /// 结果按 W 人是往镜头里跑的 —— 手感就是「前后完全相反」。
        /// </summary>
        private static void ValidateInputDirection()
        {
            // 相机朝 +Z：屏幕上方 = 世界 +Z，屏幕右 = 世界 +X
            MotionInputReader.ToWorldMove(0f, 1f, Vector3.forward, out float wx, out float wz);
            Check(Mathf.Abs(wx) < 0.01f && Mathf.Abs(wz - 1f) < 0.01f,
                $"相机朝 +Z 时按 W 应朝 +Z（得到 {wx:F2},{wz:F2}）");

            // 相机朝 -Z：屏幕上方 = 世界 -Z，屏幕右 = 世界 -X
            MotionInputReader.ToWorldMove(0f, 1f, Vector3.back, out wx, out wz);
            Check(Mathf.Abs(wx) < 0.01f && Mathf.Abs(wz + 1f) < 0.01f,
                $"相机朝 -Z 时按 W 应朝 -Z（得到 {wx:F2},{wz:F2}）");

            MotionInputReader.ToWorldMove(1f, 0f, Vector3.back, out wx, out wz);
            Check(Mathf.Abs(wx + 1f) < 0.01f && Mathf.Abs(wz) < 0.01f,
                $"相机朝 -Z 时按 D 应朝 -X（得到 {wx:F2},{wz:F2}）");

            // 用 PlayerMotionController 的实际相机摆法验证：
            //   相机位置 = (sin(yaw)*D, H, cos(yaw)*D)，看向 (0, lookHeight, 0)
            const float yawDeg = -35f, dist = 2.6f, height = 5.0f, lookY = 0.9f;
            float yaw = yawDeg * Mathf.Deg2Rad;
            var camPos = new Vector3(Mathf.Sin(yaw) * dist, height, Mathf.Cos(yaw) * dist);
            var camForward = new Vector3(0f, lookY, 0f) - camPos;
            MotionInputReader.ToWorldMove(0f, 1f, camForward, out wx, out wz);

            var expect = new Vector3(camForward.x, 0f, camForward.z).normalized;
            Check(Mathf.Abs(wx - expect.x) < 0.02f && Mathf.Abs(wz - expect.z) < 0.02f,
                $"按 W 应与相机水平前向一致（得到 {wx:F2},{wz:F2}，期望 {expect.x:F2},{expect.z:F2}）");

            // 最关键的一条：相机在 +Z 侧朝 -Z 看，W 的 z 分量必须为负。
            // 修复前这里是 +1，正是「前后相反」的现象。
            Check(wz < 0f,
                $"相机在 +Z 侧朝 -Z 看时按 W 的 z 必须为负（得到 {wz:F2}；>0 就是前后反了）");

            // 没有相机时退化为世界方向（不能崩、不能给全零）
            MotionInputReader.ToWorldMove(1f, 1f, Vector3.zero, out wx, out wz);
            Check(Mathf.Abs(wx - 1f) < 0.01f && Mathf.Abs(wz - 1f) < 0.01f,
                $"无相机信息时应退化为世界方向（得到 {wx:F2},{wz:F2}）");
        }

        // ---------------------------------------------------------------- 贴墙与翻滚

        private static void ValidateCoverAndRoll(MotionConfig cfg)
        {
            var probe = new CorridorProbe();

            // 走廊右侧起点（贴墙位置 = 半宽 - 身体半径）
            float startX = probe.HalfWidth - probe.BodyRadius;
            var rt = MotionRuntime.Create(new Vector3(startX, 0f, 0f));
            Drive(rt, probe, cfg, 0.8f);

            Check(rt.State == MotionState.Cover, $"靠近墙应进入 Cover，实际 {rt.State}");
            Check(Mathf.Abs(Mathf.Sin(rt.Facing * Mathf.Deg2Rad)) < 0.2f,
                $"贴墙应为侧身（沿墙朝向），实际 facing={rt.Facing:F1}°");

            // 朝墙推：保持贴墙
            Drive(rt, probe, cfg, 0.5f, 1f, 0f);
            Check(rt.State == MotionState.Cover, $"朝墙推应保持贴墙，实际 {rt.State}");

            // 背离墙推：应脱身（回归：曾经被永久粘住）
            Drive(rt, probe, cfg, 0.5f, -1f, 0f);
            Check(rt.State != MotionState.Cover, $"背离墙推应脱身，实际仍在 {rt.State}");

            // 翻滚跨到对面
            var rt2 = MotionRuntime.Create(new Vector3(startX, 0f, 0f));
            Drive(rt2, probe, cfg, 0.8f);
            Check(rt2.State == MotionState.Cover, "翻滚前应处于 Cover");

            // 按一次翻滚键（单帧）
            var c = probe.Query(rt2.RootPos, rt2.Facing);
            var pressInput = new MotionInput
            {
                RollPressed = true,
                CoverAvailable = c.Available,
                CoverSide = c.Side,
                CoverDistance = c.Distance,
                CoverNormal = c.Normal,
            };
            MotionSystem.Step(rt2, pressInput, 1f / 60f, cfg);
            Check(rt2.State == MotionState.Roll, $"按 Space 应进入 Roll，实际 {rt2.State}");

            Drive(rt2, probe, cfg, 0.9f);
            Check(rt2.RootPos.x < 0f, $"翻滚应跨到走廊另一侧，实际 x={rt2.RootPos.x:F2}");
            Check(Mathf.Abs(rt2.RootPos.x) > 0.5f, $"翻滚落点应贴近对面墙，实际 |x|={Mathf.Abs(rt2.RootPos.x):F2}");
            Check(rt2.State == MotionState.Cover, $"落地应贴住对面墙，实际 {rt2.State}");

            // 落点精度：rollDistance 应正好等于两侧贴墙位置之差，不能让 Clamp 去兜底
            float expectX = -(probe.HalfWidth - probe.BodyRadius);
            Check(Mathf.Abs(rt2.RootPos.x - expectX) < 0.12f,
                $"翻滚落点应约 {expectX:F2}，实际 {rt2.RootPos.x:F2}（rollDistance 与走廊宽度需配套）");

            // 回归：翻滚时整体朝向不能被重置。
            // RootEuler 曾只写 Z 轴，贴墙朝向 180°（沿 -Z 那面墙）时，人会在翻滚瞬间猛地转回世界 +Z。
            var rt4 = MotionRuntime.Create(new Vector3(-startX, 0f, 0f));
            rt4.Facing = 180f;
            Drive(rt4, probe, cfg, 0.5f);
            Check(rt4.State == MotionState.Cover, $"背向贴墙应进入 Cover，实际 {rt4.State}");

            var c4 = probe.Query(rt4.RootPos, rt4.Facing);
            MotionSystem.Step(rt4, new MotionInput
            {
                RollPressed = true,
                CoverAvailable = c4.Available,
                CoverSide = c4.Side,
                CoverDistance = c4.Distance,
                CoverNormal = c4.Normal,
            }, 1f / 60f, cfg);
            Check(rt4.State == MotionState.Roll, $"应进入 Roll，实际 {rt4.State}");

            float maxYawDrift = 0f;
            for (int i = 0; i < 34; i++)
            {
                var cc = probe.Query(rt4.RootPos, rt4.Facing);
                MotionSystem.Step(rt4, new MotionInput
                {
                    CoverAvailable = cc.Available,
                    CoverSide = cc.Side,
                    CoverDistance = cc.Distance,
                    CoverNormal = cc.Normal,
                }, 1f / 60f, cfg);
                maxYawDrift = Mathf.Max(maxYawDrift, Mathf.Abs(Mathf.DeltaAngle(rt4.RootEuler.y, rt4.Facing)));
            }
            Check(maxYawDrift < 0.5f,
                $"翻滚时整体朝向应跟随 Facing（facing=180° 下最大偏差 {maxYawDrift:F1}°，>0 说明朝向被重置）");

            // 贴墙站姿的落地质量：
            //   1) 身体外缘不能越过墙内表面（否则半个人嵌进墙里）
            //   2) 脚掌要真正踩在地上（腿微屈会让脚离地，靠 RootDrop 补回来）
            var solver = new PoseSolver();
            solver.Solve(PoseUtil.RestPose(), Vector3.zero, Vector3.zero);
            float standAnkle = solver.Positions[(int)Joint.AnkleL].y;
            float standToe = solver.Positions[(int)Joint.ToeL].y;
            var coverRoot = rt2.RootPos - new Vector3(0f, rt2.RootDrop, 0f);
            solver.Solve(rt2.Pose, coverRoot, rt2.RootEuler);
            float coverAnkle = solver.Positions[(int)Joint.AnkleL].y;
            float coverToe = solver.Positions[(int)Joint.ToeL].y;
            float coverHeel = solver.Positions[(int)Joint.HeelL].y;

            Check(Mathf.Abs(rt2.RootPos.x) + probe.BodyRadius <= probe.HalfWidth + 0.01f,
                $"贴墙时身体不应嵌进墙（外缘 {Mathf.Abs(rt2.RootPos.x) + probe.BodyRadius:F2} ≤ 墙面 {probe.HalfWidth:F2}）");
            Check(Mathf.Abs(coverAnkle - standAnkle) < 0.02f,
                $"贴墙时脚应落地（踝高 {coverAnkle:F3}，站立 {standAnkle:F3}，差 {Mathf.Abs(coverAnkle - standAnkle):F3}m）");
            Check(coverToe > -0.03f && coverHeel > -0.03f && coverToe < 0.05f && coverHeel < 0.05f,
                $"贴墙时脚尖与脚跟都该贴地（脚尖 {coverToe:F3} / 脚跟 {coverHeel:F3}，站立脚尖 {standToe:F3}）");
            Check(Mathf.Abs(coverToe - coverHeel) < 0.03f,
                $"贴墙时脚掌应水平（脚尖与脚跟差 {Mathf.Abs(coverToe - coverHeel):F3}m）");
        }

        /// <summary>用真实探针驱动若干秒（每帧查询掩体 + 边界钳制）。</summary>
        private static void Drive(MotionRuntime rt, CorridorProbe probe, MotionConfig cfg,
            float seconds, float moveX = 0f, float moveZ = 0f)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(seconds * 60f));
            for (int i = 0; i < steps; i++)
            {
                var c = probe.Query(rt.RootPos, rt.Facing);
                var input = new MotionInput
                {
                    MoveX = moveX,
                    MoveZ = moveZ,
                    CoverAvailable = c.Available,
                    CoverSide = c.Side,
                    CoverDistance = c.Distance,
                    CoverNormal = c.Normal,
                };
                MotionSystem.Step(rt, input, 1f / 60f, cfg);

                var clamped = probe.Clamp(rt.RootPos);
                if ((clamped - rt.RootPos).sqrMagnitude > 1e-8f)
                {
                    rt.RootPos = clamped;
                    rt.Speed *= 0.3f;
                }
            }
        }

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                _pass++;
                Debug.Log($"  PASS  {message}");
            }
            else
            {
                _fail++;
                Debug.LogError($"  FAIL  {message}");
            }
        }
    }
}
