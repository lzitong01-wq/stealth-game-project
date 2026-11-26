// 动作状态机的类型定义 —— 从 TypeScript 版 motion.ts 的类型部分迁移。

using UnityEngine;

namespace PlayerMotion
{
    public enum MotionState
    {
        Idle,
        Walk,
        Run,
        Crouch,
        Cover,
        Roll,
    }

    /// <summary>每帧传入的外部输入。相机固定朝 +Z 时，输入即世界方向。</summary>
    public struct MotionInput
    {
        /// <summary>左右输入，-1..1（右为正）。</summary>
        public float MoveX;
        /// <summary>前后输入，-1..1（前为正）。</summary>
        public float MoveZ;
        public bool Crouch;
        public bool Sprint;
        /// <summary>翻滚键是否「刚按下」。</summary>
        public bool RollPressed;

        /// <summary>附近是否存在可躲藏的墙。</summary>
        public bool CoverAvailable;
        /// <summary>墙在角色哪一侧：-1 左、1 右。</summary>
        public sbyte CoverSide;
        /// <summary>到墙的水平距离（米）。</summary>
        public float CoverDistance;
        /// <summary>
        /// 墙的表面法线（由墙指向角色）。
        /// 只靠 CoverSide 在「正对墙」时会退化，判断不出该往哪脱离、该朝哪站。
        /// </summary>
        public Vector3 CoverNormal;

        public static MotionInput None => new MotionInput
        {
            CoverSide = 1,
            CoverDistance = 999f,
            CoverNormal = new Vector3(-1f, 0f, 0f),
        };
    }

    /// <summary>动作运行时状态。由 MotionSystem 原地更新。</summary>
    public sealed class MotionRuntime
    {
        public MotionState State;
        /// <summary>进入当前状态后经过的时间。</summary>
        public float StateTime;
        /// <summary>上一状态与切换瞬间的姿态，用于过渡。</summary>
        public MotionState PrevState;
        public Vector3[] PrevPose = PoseUtil.RestPose();
        public Vector3[] TargetPose = PoseUtil.RestPose();
        /// <summary>最终输出姿态（已混合）。</summary>
        public Vector3[] Pose = PoseUtil.RestPose();
        /// <summary>
        /// 暂存缓冲：生成「待机 + 半蹲」这类叠加姿态时用，
        /// 避免每帧 new 数组（哪怕只有几十字节，待机时每帧分配也会累积成 GC 峰值）。
        /// </summary>
        public Vector3[] ScratchPose = PoseUtil.RestPose();

        /// <summary>过渡进度 0..1。初始为 1（已经稳定在初始姿态，不需要过渡）。</summary>
        public float Blend = 1f;
        public float BlendDuration = 0.0001f;

        /// <summary>步态相位（弧度），保证停走切换时不跳变。</summary>
        public float GaitPhase;
        /// <summary>当前水平速度（m/s）。</summary>
        public float Speed;
        /// <summary>朝向（度，绕 Y）。</summary>
        public float Facing;
        /// <summary>根位置（脚底世界坐标）。</summary>
        public Vector3 RootPos;
        /// <summary>
        /// 每帧算出的根下沉量（米）。骨盆高度固定，腿一弯脚就会离地，
        /// 视图层要用 RootPos - (0, RootDrop, 0) 当作真正的根位置。
        /// 蹲下和贴墙共用这一个通道。
        /// </summary>
        public float RootDrop;
        /// <summary>根整体旋转（度）。翻滚时绕 Z 轴转。</summary>
        public Vector3 RootEuler;

        /// <summary>下蹲混合量 0..1（不是布尔，保证起身过渡连续）。</summary>
        public float CrouchAmount;
        /// <summary>贴墙混合量 0..1。</summary>
        public float CoverAmount;
        /// <summary>翻滚进度 0..1。</summary>
        public float RollT;
        /// <summary>翻滚位移方向（水平单位向量，指向「远离起手时那面墙」）。</summary>
        public Vector3 RollVec = Vector3.forward;
        /// <summary>翻滚方向：-1 左、1 右（相对角色，只影响翻转的转向）。</summary>
        public sbyte RollDir = 1;
        /// <summary>翻滚前的贴墙侧。</summary>
        public sbyte RollFromSide = 1;
        /// <summary>当前贴的是哪一侧的墙（单独记一份，避免淡出时姿态翻转）。</summary>
        public sbyte CoverSide = 1;
        /// <summary>上一次的翻滚键状态，用于检测「刚按下」。</summary>
        public bool WasRollPressed;

        public static MotionRuntime Create(Vector3 spawn)
        {
            var pose = PoseUtil.RestPose();
            return new MotionRuntime
            {
                State = MotionState.Idle,
                PrevState = MotionState.Idle,
                PrevPose = PoseUtil.Clone(pose),
                TargetPose = PoseUtil.Clone(pose),
                Pose = PoseUtil.Clone(pose),
                Facing = 0f,
                RootPos = spawn,
                RootEuler = Vector3.zero,
                CoverSide = 1,
            };
        }

        /// <summary>HUD 用的一行摘要。</summary>
        public string Describe()
        {
            return $"{MotionSystem.StateLabel(State)} · {Speed:F2} m/s · 朝向 {Facing:F0}° · 衔接 {Blend * 100f:F0}%";
        }
    }
}
