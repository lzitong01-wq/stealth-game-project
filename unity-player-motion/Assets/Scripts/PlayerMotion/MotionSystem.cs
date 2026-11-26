// 玩家动作状态机 —— 从 TypeScript 版 motion.ts 迁移。
//
// 覆盖的动作：待机 / 步行 / 疾走 / 下蹲 / 贴墙躲藏 / 翻滚换位。
// 全部为纯逻辑：不读输入设备、不碰渲染、不依赖任何 GameObject。
// 输入由调用方每帧传入，输出是姿态 + 根位移 + 朝向。
//
// 与原版的对应关系：
//   poseGait / poseIdle / poseCover / poseRoll -> 同名私有方法
//   MOTION_CONFIG -> MotionConfig（Inspector 可调）
//   MotionRuntime -> MotionTypes.cs

using UnityEngine;

namespace PlayerMotion
{
    public static class MotionSystem
    {
        public static string StateLabel(MotionState s) => s switch
        {
            MotionState.Idle => "待机",
            MotionState.Walk => "步行",
            MotionState.Run => "疾走",
            MotionState.Crouch => "下蹲",
            MotionState.Cover => "贴墙躲藏",
            MotionState.Roll => "翻滚换位",
            _ => s.ToString(),
        };

        private static void SetState(MotionRuntime rt, MotionState next, float blendDuration)
        {
            if (rt.State == next) return;
            rt.PrevState = rt.State;
            PoseUtil.CopyTo(rt.Pose, rt.PrevPose);
            rt.State = next;
            rt.StateTime = 0f;
            rt.Blend = 0f;
            rt.BlendDuration = Mathf.Max(0.0001f, blendDuration);
        }

        // ============================================================
        // 各状态的目标姿态
        // ============================================================

        /// <summary>
        /// 设置一条手臂的姿态。
        ///
        /// 几何要点（原版踩过两次坑）：手臂骨段沿 ±X 伸展，
        /// 「绕 X 轴旋转」只会让手臂自转、完全不改变指向。
        /// 所以：垂下用 Z 旋转、前后摆用 X 旋转（要在垂下之后，欧拉顺序 Ry*Rx*Rz）、
        /// 肘弯曲必须用 Y 旋转（把 ±X 转到 ±Z，即前臂向前折）。
        /// </summary>
        /// <param name="side">-1 = 沿 +X 那条（ShoulderL），1 = 沿 -X 那条（ShoulderR）</param>
        /// <param name="down">垂下角度：0 = 平举，90 = 完全垂在体侧</param>
        /// <param name="swing">前后摆（度，正 = 向后）</param>
        /// <param name="bend">肘弯曲（度，正 = 前臂向前）</param>
        /// <param name="tuck">肘外展（度，正 = 前臂向外张开）</param>
        private static void PoseArm(Vector3[] p, sbyte side, float down, float swing, float bend, float tuck = 0f)
        {
            var sh = side < 0 ? Joint.ShoulderL : Joint.ShoulderR;
            var el = side < 0 ? Joint.ElbowL : Joint.ElbowR;
            p[(int)sh] = new Vector3(swing, 0f, side * down);
            p[(int)el] = new Vector3(0f, side * bend, side * tuck);
        }

        /// <summary>站立待机：手臂自然下垂，极轻微呼吸。</summary>
        private static void PoseIdle(Vector3[] p, float t)
        {
            float breathe = Mathf.Sin(t * 1.6f) * 1.2f;
            p[(int)Joint.Chest] = new Vector3(breathe, 0f, 0f);
            p[(int)Joint.Head] = new Vector3(-breathe * 0.5f, 0f, 0f);
            PoseArm(p, -1, 74f, breathe * 0.8f, 14f, 7f);
            PoseArm(p, 1, 74f, breathe * 0.8f, 14f, 7f);
        }

        /// <summary>
        /// 步态公共部分：走路 / 跑步 / 蹲行。
        /// </summary>
        /// <param name="phase">当前步态相位（弧度）</param>
        /// <param name="amp">摆动幅度（度）</param>
        /// <param name="kneeAmp">膝盖弯曲幅度</param>
        /// <param name="lean">躯干前倾（度）</param>
        /// <param name="crouch">蹲下混合量 0..1</param>
        private static void PoseGait(Vector3[] p, float phase, float amp, float kneeAmp, float lean, float crouch)
        {
            float a = phase;
            float swingL = Mathf.Sin(a) * amp;
            float swingR = Mathf.Sin(a + Mathf.PI) * amp;
            float kneeL = Mathf.Max(0f, -Mathf.Sin(a)) * kneeAmp;
            float kneeR = Mathf.Max(0f, -Mathf.Sin(a + Mathf.PI)) * kneeAmp;

            // 腿：步态摆动 + 蹲下屈曲。
            //   hip  取负值 → 大腿向前上方抬，膝盖朝前
            //   knee 取正值 → 小腿相对大腿向后折（生理方向）
            //   ankle 取负值 → 踝背屈，把脚掌压回水平贴地
            // 蹲下时双腿向外微开，支撑面更宽，也更像潜行的低伏姿态。
            p[(int)Joint.HipL] = new Vector3(swingL - crouch * 60f, 0f, -crouch * 7f);
            p[(int)Joint.HipR] = new Vector3(swingR - crouch * 60f, 0f, crouch * 7f);
            p[(int)Joint.KneeL] = new Vector3(kneeL + crouch * 100f, 0f, 0f);
            p[(int)Joint.KneeR] = new Vector3(kneeR + crouch * 100f, 0f, 0f);
            // 蹲下时的背屈量取「骨盆后倾 12 + 髋 60 + 膝 100」的相反数，让脚掌净旋转归零
            p[(int)Joint.AnkleL] = new Vector3(-kneeL * 0.3f - crouch * 28f, 0f, 0f);
            p[(int)Joint.AnkleR] = new Vector3(-kneeR * 0.3f - crouch * 28f, 0f, 0f);

            // 手臂反向摆动：左腿前摆时右臂前摆（对侧摆臂）
            // 蹲伏时要「前伸扶膝」：上臂几乎垂下（不能只降到 52°，那看着像张开双臂），
            // 再大幅前摆、肘弯加大，手才会落到膝盖附近。
            float armSwingL = -swingL * 0.85f;
            float armSwingR = -swingR * 0.85f;
            float armDown = 74f - crouch * 4f;
            float armBend = 14f + crouch * 56f;
            float armCrouchSwing = -crouch * 55f;
            PoseArm(p, -1, armDown, armSwingL + armCrouchSwing, armBend, 7f);
            PoseArm(p, 1, armDown, armSwingR + armCrouchSwing, armBend, 7f);

            // 躯干：跑步前倾 + 蹲伏时明显压低上身。
            // 蹲伏不是单纯把腿弯起来——上身要前倾压低，颈部反向抬起保持视线。
            p[(int)Joint.Spine] = new Vector3(lean * 0.45f + crouch * 25f, 0f, 0f);
            p[(int)Joint.Chest] = new Vector3(lean * 0.35f + crouch * 14f, 0f, Mathf.Sin(a) * 2.5f);
            p[(int)Joint.Neck] = new Vector3(-lean * 0.5f - crouch * 22f, 0f, 0f);
            p[(int)Joint.Head] = new Vector3(-lean * 0.35f - crouch * 12f, 0f, 0f);

            // 蹲下时重心后移一点，别显得要往前栽
            if (crouch > 0.001f)
                p[(int)Joint.Pelvis] = new Vector3(-crouch * 12f, 0f, 0f);
        }

        /// <summary>
        /// 贴墙躲藏：侧身靠墙，靠墙那侧手臂上举扶墙，外侧手臂收紧，身体略侧倾。
        /// 注意骨架命名与实际左右相反：ShoulderL 的骨段沿 +X，而角色朝向 0° 时「右方」也是 +X。
        /// </summary>
        /// <param name="side">-1 左墙、1 右墙</param>
        /// <param name="t">状态时间，用于轻微呼吸</param>
        private static void PoseCover(Vector3[] p, sbyte side, float t)
        {
            float breathe = Mathf.Sin(t * 1.4f) * 0.8f;

            // 身体姿态。注意这里不要加大幅侧倾（绕 Z）：
            // 侧身感来自「朝向已经转到沿墙」（facing），不是靠身体歪。
            // 之前 pelvis -8° / spine -6° 会把肩膀从 x=0.87 推到 1.0，
            // 手臂再怎么收都会顶出墙面。保留 2° 的轻微倾向即可。
            p[(int)Joint.Pelvis] = new Vector3(CoverPelvisPitch, 0f, side * -2f);
            p[(int)Joint.Spine] = new Vector3(4f, 0f, side * -2f);
            p[(int)Joint.Chest] = new Vector3(2f + breathe, 0f, side * -2f);
            p[(int)Joint.Neck] = new Vector3(-4f, 0f, side * 1f);
            p[(int)Joint.Head] = new Vector3(-2f, 0f, side * 2f);

            // 内侧（靠墙那侧）手臂扶墙。
            //
            // 手的世界 x = 肩 x + 上臂水平分量 + 前臂水平分量，两个都必须压住：
            //   1) 上臂：水平外伸 = 0.28 * cos(down)。肩在 x≈0.87、墙面 1.0，
            //      所以 down 要到 -75° 才算「竖直向上」（-65° 实测还差 6cm）。
            //   2) 前臂：bend 是绕 Y 把 ±X 转向 ±Z（沿墙方向）。只折 38° 时仍有一大半
            //      指向 +X，实测手会顶到 x=1.37、穿墙 0.37m。必须折到 90° 才真正沿墙。
            // 两者一起，手落在墙面上（x≈0.99）、高度≈墙顶。
            PoseArm(p, (sbyte)(side * -1), -75f, -6f, 90f, 0f);

            // 外侧手臂收得更贴身：垂得更多、前伸更少、肘弯更大，避免看成「平举」
            PoseArm(p, side, 70f, -16f, 98f, 30f);

            // 双腿微屈。左右角度必须一致：骨盆高度是固定的，两腿弯曲不同会让一侧脚踝
            // 比另一侧高 4cm，怎么调都有一条腿悬空。
            // ankle 取「骨盆 -6 + 髋 10 + 膝 18」的相反数，让脚掌净旋转归零、真正贴平。
            var ankleFix = -(CoverPelvisPitch + CoverHip + CoverKnee);
            p[(int)Joint.HipL] = new Vector3(CoverHip, 0f, 0f);
            p[(int)Joint.HipR] = new Vector3(CoverHip, 0f, 0f);
            p[(int)Joint.KneeL] = new Vector3(CoverKnee, 0f, 0f);
            p[(int)Joint.KneeR] = new Vector3(CoverKnee, 0f, 0f);
            p[(int)Joint.AnkleL] = new Vector3(ankleFix, 0f, 0f);
            p[(int)Joint.AnkleR] = new Vector3(ankleFix, 0f, 0f);
        }

        // 贴墙姿态的腿部角度。改这里必须同步改 MotionConfig.coverLegDrop，
        // 否则脚会悬空或穿地（和蹲下的 crouchDrop 是同一个道理）。
        private const float CoverPelvisPitch = -6f;
        private const float CoverHip = 10f;
        private const float CoverKnee = 18f;

        /// <summary>翻滚：团身抱膝。整体旋转由 RootEuler 负责，这里只管四肢。</summary>
        private static void PoseRoll(Vector3[] p, float t)
        {
            // 起手 0.12s 内收身，结束前 0.15s 展开
            float tuck = t < 0.12f
                ? PoseUtil.EaseOutCubic(t / 0.12f)
                : t > 0.43f
                    ? 1f - PoseUtil.EaseInOut((t - 0.43f) / 0.15f)
                    : 1f;

            p[(int)Joint.Pelvis] = new Vector3(-20f * tuck, 0f, 0f);
            p[(int)Joint.Spine] = new Vector3(28f * tuck, 0f, 0f);
            p[(int)Joint.Chest] = new Vector3(22f * tuck, 0f, 0f);
            p[(int)Joint.Neck] = new Vector3(26f * tuck, 0f, 0f);
            p[(int)Joint.Head] = new Vector3(18f * tuck, 0f, 0f);
            p[(int)Joint.HipL] = new Vector3(96f * tuck, 0f, 0f);
            p[(int)Joint.HipR] = new Vector3(92f * tuck, 0f, 0f);
            p[(int)Joint.KneeL] = new Vector3(128f * tuck, 0f, 0f);
            p[(int)Joint.KneeR] = new Vector3(124f * tuck, 0f, 0f);
            p[(int)Joint.AnkleL] = new Vector3(-34f * tuck, 0f, 0f);
            p[(int)Joint.AnkleR] = new Vector3(-32f * tuck, 0f, 0f);

            // 抱膝：手臂前伸环抱小腿
            float armDown = 74f - 40f * tuck;
            float armSwing = -52f * tuck;
            float armBend = 20f + 100f * tuck;
            float armTuck = 7f + 16f * tuck;
            PoseArm(p, -1, armDown, armSwing, armBend, armTuck);
            PoseArm(p, 1, armDown, armSwing, armBend, armTuck);
        }

        // ============================================================
        // 主循环
        // ============================================================

        /// <summary>推进一帧。原地更新 rt。</summary>
        public static void Step(MotionRuntime rt, in MotionInput input, float dt, MotionConfig cfg)
        {
            float step = Mathf.Clamp(dt, 0f, 0.1f);
            rt.StateTime += step;

            // ---- 1. 输入归一化 ----
            float ix = Mathf.Clamp(input.MoveX, -1f, 1f);
            float iz = Mathf.Clamp(input.MoveZ, -1f, 1f);
            float mag = Mathf.Sqrt(ix * ix + iz * iz);
            if (mag > 1f) { ix /= mag; iz /= mag; }
            bool wantsMove = Mathf.Sqrt(ix * ix + iz * iz) > 0.12f;

            // ---- 2. 目标朝向：朝输入方向转 ----
            // 贴墙时朝向交给「沿墙方向」处理，翻滚时交给整体旋转，
            // 两者都不能被输入方向覆盖，否则会跟第 6.5 步打架。
            if (wantsMove && rt.State != MotionState.Cover && rt.State != MotionState.Roll)
            {
                float targetFacing = Mathf.Atan2(ix, iz) * Mathf.Rad2Deg;
                float diff = Mathf.DeltaAngle(rt.Facing, targetFacing);
                float maxTurn = cfg.turnSpeed * step;
                rt.Facing += Mathf.Clamp(diff, -maxTurn, maxTurn);
            }

            // ---- 3. 下蹲混合 ----
            float crouchTarget = input.Crouch ? 1f : 0f;
            if (rt.CrouchAmount < crouchTarget)
                rt.CrouchAmount = Mathf.Min(crouchTarget, rt.CrouchAmount + step / cfg.crouchBlend);
            else if (rt.CrouchAmount > crouchTarget)
                rt.CrouchAmount = Mathf.Max(crouchTarget, rt.CrouchAmount - step / cfg.crouchBlend);

            // ---- 4. 贴墙判定 ----
            // 贴近墙且没在下蹲 → 可贴墙。贴墙时角色站定，脱离方式：
            //   1. 按 Ctrl 下蹲    2. 朝背离墙/沿墙方向推方向键    3. 按 Space 翻滚到对面
            // 第 2 条是必需的：否则贴墙后速度被锁为 0，玩家会被永久粘在墙上。
            // 用「墙的法线」而不是「左右侧」判断方向：正对墙时左右会退化。
            Vector3 coverNormal = input.CoverNormal.sqrMagnitude > 1e-6f
                ? input.CoverNormal
                : new Vector3(input.CoverSide > 0 ? -1f : 1f, 0f, 0f);
            // 输入与「墙→角色」同向 = 在往墙里推（正）；反向 = 想离开（负）
            float pushIntoWall = -(ix * coverNormal.x + iz * coverNormal.z);
            bool wantsLeaveCover = wantsMove && pushIntoWall < 0.4f;
            bool nearCover = input.CoverAvailable
                             && input.CoverDistance <= cfg.coverSnapDistance
                             && !input.Crouch
                             && !wantsLeaveCover;

            // ---- 5. 翻滚：优先级最高 ----
            bool rollEdge = input.RollPressed && !rt.WasRollPressed;
            rt.WasRollPressed = input.RollPressed;

            if (rt.State == MotionState.Roll)
            {
                rt.RollT += step / cfg.rollDuration;
                if (rt.RollT >= 1f)
                {
                    rt.RollT = 1f;
                    // 翻滚结束：落到对面墙附近就继续贴墙，否则起身
                    if (input.CoverAvailable && input.CoverDistance <= cfg.rollCoverSnap)
                    {
                        rt.CoverSide = input.CoverSide;
                        SetState(rt, MotionState.Cover, cfg.coverBlend * 0.5f);
                        rt.CoverAmount = 1f;
                    }
                    else
                    {
                        SetState(rt, MotionState.Idle, 0.22f);
                        rt.CoverAmount = 0f;
                    }
                }
            }
            else if (rollEdge && (rt.State == MotionState.Cover || (nearCover && rt.Speed < cfg.coverExitSpeed)))
            {
                SetState(rt, MotionState.Roll, 0.1f);
                rt.RollT = 0f;
                rt.RollFromSide = input.CoverSide;
                rt.CoverSide = input.CoverSide;
                // 位移方向 = 墙的法线方向（法线本身就是「由墙指向角色」= 远离墙），锁存下来
                rt.RollVec = new Vector3(coverNormal.x, 0f, coverNormal.z);
                // 翻转方向跟随位移方向相对角色的左右
                float yawR = rt.Facing * Mathf.Deg2Rad;
                float rightX = Mathf.Cos(yawR);
                float rightZ = -Mathf.Sin(yawR);
                rt.RollDir = (sbyte)(rt.RollVec.x * rightX + rt.RollVec.z * rightZ >= 0f ? 1 : -1);
                rt.CoverAmount = 1f;
                rt.Speed = 0f;
            }

            // ---- 6. 状态迁移（翻滚期间锁定） ----
            if (rt.State != MotionState.Roll)
            {
                bool wantCrouch = rt.CrouchAmount > 0.5f;
                if (nearCover)
                {
                    if (rt.State != MotionState.Cover)
                        SetState(rt, MotionState.Cover, cfg.coverBlend);
                    rt.CoverSide = input.CoverSide;
                    rt.CoverAmount = Mathf.Min(1f, rt.CoverAmount + step / cfg.coverBlend);
                }
                else
                {
                    if (rt.State == MotionState.Cover)
                    {
                        var next = wantCrouch ? MotionState.Crouch
                            : wantsMove ? (input.Sprint ? MotionState.Run : MotionState.Walk)
                            : MotionState.Idle;
                        SetState(rt, next, cfg.coverBlend);
                    }
                    rt.CoverAmount = Mathf.Max(0f, rt.CoverAmount - step / cfg.coverBlend);

                    if (wantCrouch)
                    {
                        if (rt.State != MotionState.Crouch) SetState(rt, MotionState.Crouch, cfg.crouchBlend);
                    }
                    else if (wantsMove)
                    {
                        var next = input.Sprint ? MotionState.Run : MotionState.Walk;
                        if (rt.State != next) SetState(rt, next, 0.2f);
                    }
                    else if (rt.State != MotionState.Idle)
                    {
                        SetState(rt, MotionState.Idle, 0.24f);
                    }
                }
            }

            // ---- 6.5 贴墙转身：转成「沿墙侧身」，而不是正面贴着墙 ----
            // 只把朝向拧十几度是假侧身，看起来还是正面撞墙。
            if (rt.State == MotionState.Cover)
            {
                // 沿墙方向 = normal × up
                float tx = -coverNormal.z;
                float tz = coverNormal.x;
                float fwdX = Mathf.Sin(rt.Facing * Mathf.Deg2Rad);
                float fwdZ = Mathf.Cos(rt.Facing * Mathf.Deg2Rad);
                // 两个沿墙方向里选离当前朝向更近的，避免 180° 猛转
                float sgn = fwdX * tx + fwdZ * tz >= 0f ? 1f : -1f;
                float targetFacing = Mathf.Atan2(tx * sgn, tz * sgn) * Mathf.Rad2Deg;
                float d = Mathf.DeltaAngle(rt.Facing, targetFacing);
                float maxTurn = cfg.turnSpeed * step;
                rt.Facing += Mathf.Clamp(d, -maxTurn, maxTurn);

                // normal 由墙指向角色：与「角色右方」同向 → 说明墙在角色左侧
                float yawR = rt.Facing * Mathf.Deg2Rad;
                float dotRight = coverNormal.x * Mathf.Cos(yawR) + coverNormal.z * -Mathf.Sin(yawR);
                rt.CoverSide = (sbyte)(dotRight > 0f ? -1 : 1);
            }

            // ---- 7. 速度 ----
            float targetSpeed = 0f;
            if (rt.State == MotionState.Walk) targetSpeed = cfg.walkSpeed;
            else if (rt.State == MotionState.Run) targetSpeed = cfg.runSpeed;
            else if (rt.State == MotionState.Crouch) targetSpeed = wantsMove ? cfg.crouchSpeed : 0f;
            else if (rt.State == MotionState.Cover) targetSpeed = 0f;
            else if (rt.State == MotionState.Roll) targetSpeed = cfg.rollDistance / cfg.rollDuration;

            float accel = targetSpeed > rt.Speed ? 14f : 18f;
            rt.Speed += Mathf.Clamp(targetSpeed - rt.Speed, -accel * step, accel * step);

            // ---- 8. 位移 ----
            float yawRad = rt.Facing * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));

            if (rt.State == MotionState.Roll)
            {
                // 翻滚：沿「远离起手墙」的方向横移，起手快、收尾缓
                float prevT = Mathf.Clamp01(rt.RollT - step / cfg.rollDuration);
                float delta = PoseUtil.EaseInOut(rt.RollT) - PoseUtil.EaseInOut(prevT);
                float move = cfg.rollDistance * delta;
                rt.RootPos += new Vector3(rt.RollVec.x * move, 0f, rt.RollVec.z * move);
            }
            else if (rt.Speed > 0.001f)
            {
                float moveMag = rt.Speed;
                rt.RootPos += new Vector3(fwd.x * moveMag * step, 0f, fwd.z * moveMag * step);
            }

            // ---- 9. 步态相位 ----
            if (rt.State == MotionState.Walk || rt.State == MotionState.Run || rt.State == MotionState.Crouch)
            {
                float hz = rt.State == MotionState.Run ? cfg.runCycleHz
                    : rt.State == MotionState.Crouch ? cfg.crouchCycleHz
                    : cfg.walkCycleHz;
                rt.GaitPhase += hz * step * 2f * Mathf.PI;
                if (rt.GaitPhase > Mathf.PI * 2f) rt.GaitPhase -= Mathf.PI * 2f;
            }

            // ---- 10. 生成目标姿态 ----
            // 注意：这里每次都从 RestPose 重新写目标姿态，所以要先归零
            var target = rt.TargetPose;
            for (int i = 0; i < target.Length; i++) target[i] = Vector3.zero;

            switch (rt.State)
            {
                case MotionState.Roll:
                    PoseRoll(target, Mathf.Clamp01(rt.RollT * cfg.rollDuration));
                    break;
                case MotionState.Cover:
                    PoseCover(target, rt.CoverSide, rt.StateTime);
                    break;
                case MotionState.Crouch:
                {
                    // 静止蹲着不该还在小幅摆腿：摆幅按实际速度缩放，停住就完全定住
                    float k = Mathf.Clamp01(rt.Speed / Mathf.Max(0.0001f, cfg.crouchSpeed));
                    PoseGait(target, rt.GaitPhase, 14f * k, 26f * k, 6f, rt.CrouchAmount);
                    break;
                }
                case MotionState.Run:
                    PoseGait(target, rt.GaitPhase, 46f, 74f, 14f, rt.CrouchAmount);
                    break;
                case MotionState.Walk:
                    PoseGait(target, rt.GaitPhase, 30f, 44f, 5f, rt.CrouchAmount);
                    break;
                default:
                    PoseIdle(target, rt.StateTime);
                    // 待机也可能处于半蹲（刚松手），叠加蹲姿。
                    // 用运行时自带的暂存缓冲，避免每帧 new 一个数组（待机半蹲时每帧都在分配）。
                    if (rt.CrouchAmount > 0.001f)
                    {
                        var c = rt.ScratchPose;
                        for (int i = 0; i < c.Length; i++) c[i] = Vector3.zero;
                        PoseGait(c, rt.GaitPhase, 0f, 0f, 0f, rt.CrouchAmount);
                        PoseUtil.LerpTo(target, c, rt.CrouchAmount, target);
                    }
                    break;
            }

            // ---- 11. 过渡混合 ----
            if (rt.Blend < 1f)
                rt.Blend = Mathf.Min(1f, rt.Blend + step / rt.BlendDuration);
            float kb = PoseUtil.EaseInOut(rt.Blend);
            if (kb >= 1f) PoseUtil.CopyTo(target, rt.Pose);
            else PoseUtil.LerpTo(rt.PrevPose, target, kb, rt.Pose);

            // ---- 12. 根整体旋转（翻滚） ----
            if (rt.State == MotionState.Roll)
            {
                float t = Mathf.Clamp01(rt.RollT);
                // 侧翻 360°，与位移同步，收尾正好回正。
                // Y 必须带上 Facing：欧拉顺序是 Ry*Rx*Rz，先绕局部 Z 翻转再整体转朝向。
                // 只写 Z 的话，贴墙时若朝向是 180°（沿 -Z 那面墙），翻滚瞬间会猛地转回世界 +Z。
                rt.RootEuler = new Vector3(0f, rt.Facing, -360f * PoseUtil.EaseInOut(t) * rt.RollDir);
            }
            else if (rt.State == MotionState.Cover)
            {
                // 已经真的转成沿墙侧身了，这里不再额外拧朝向
                rt.RootEuler = new Vector3(0f, rt.Facing, 0f);
            }
            else
            {
                rt.RootEuler = new Vector3(0f, rt.Facing, 0f);
            }

            // ---- 13. 根下沉量 ----
            // 骨盆高度是固定的（BoneOffset.Pelvis = 0.95），所以腿一弯，脚就必然离地。
            //   蹲下：腿弯得多 → crouchDrop
            //   贴墙：腿微屈 → coverLegDrop（很小，但不补的话脚会悬空 3cm，看着像踮脚）
            // 视图层直接读这个值整体压低，两个状态用同一套约定。
            float drop = cfg.crouchDrop * rt.CrouchAmount;
            if (rt.State == MotionState.Cover)
                drop += cfg.coverLegDrop * rt.CoverAmount;
            rt.RootDrop = drop;
        }

        /// <summary>根节点（脚底）相对 RootPos 的下沉量，视图层用它压低整体。</summary>
        public static float RootHeightOffset(MotionRuntime rt, MotionConfig cfg)
            => -cfg.crouchDrop * rt.CrouchAmount;
    }
}
