// 动作参数 —— 从 TypeScript 版 MOTION_CONFIG 迁移。
// 全部集中在这里，方便在 Inspector 里调；改腿角就一定要同步重算 crouchDrop。

using UnityEngine;

namespace PlayerMotion
{
    [System.Serializable]
    public sealed class MotionConfig
    {
        [Header("速度")]
        public float walkSpeed = 1.7f;
        public float runSpeed = 4.3f;
        public float crouchSpeed = 0.85f;

        [Header("步频（每秒完整周期数）")]
        public float walkCycleHz = 0.95f;
        public float runCycleHz = 1.55f;
        public float crouchCycleHz = 0.6f;

        [Header("转身")]
        [Tooltip("度/秒")]
        public float turnSpeed = 420f;

        [Header("过渡时间（秒）")]
        public float crouchBlend = 0.28f;
        public float coverBlend = 0.34f;

        [Header("翻滚")]
        public float rollDuration = 0.58f;
        [Tooltip(
            "翻滚横向位移（米）。\n" +
            "必须 = 2 ×（走廊半宽 - 身体半径）= 2 ×（1.0 - 0.3）= 1.4，\n" +
            "这样从一侧贴墙出发，正好落在另一侧贴墙位置。改走廊宽度或身体半径就要同步改这里。")]
        public float rollDistance = 1.4f;
        [Tooltip("翻滚落地后，距墙多近算「落到对面掩体」")]
        public float rollCoverSnap = 0.6f;

        [Header("贴墙")]
        [Tooltip("附近有墙的判定距离")]
        public float coverEnterDistance = 1.15f;
        [Tooltip("多近才真正贴上去")]
        public float coverSnapDistance = 0.45f;
        [Tooltip("贴墙时速度超过它就脱离")]
        public float coverExitSpeed = 0.6f;

        [Header("蹲伏")]
        [Tooltip(
            "蹲下时骨盆下降量（米）。\n" +
            "与腿部角度（髋 -60 / 膝 +100）配套：这组角度让髋到踝的垂直距离缩短约 0.35m，\n" +
            "root 同步下降同样的量，脚踝才能留在原高度、脚掌贴地。改角度就要重算这个值。")]
        public float crouchDrop = 0.35f;

        [Tooltip(
            "贴墙姿态的根下沉量（米）。\n" +
            "贴墙时双腿微屈（骨盆 -6 / 髋 10 / 膝 18），髋到踝的垂直距离缩短 0.052m（实测），\n" +
            "根要同步下降同样的量，脚掌才会踩在地上；不补的话脚会悬空，看着像踮脚。\n" +
            "改 MotionSystem.PoseCover 的腿部角度就要重跑校验、按实测值重设这个数。")]
        public float coverLegDrop = 0.052f;

        /// <summary>做一个浅拷贝，避免运行时改到原始资产。</summary>
        public MotionConfig Clone() => (MotionConfig)MemberwiseClone();
    }
}
