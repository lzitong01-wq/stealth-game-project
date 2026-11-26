// 火柴人/白模骨架定义 —— 从 TypeScript 版 skeleton.ts 迁移。
//
// 设计约束（与原版一致）：
//   1. 纯数据 + 纯静态函数，不依赖场景对象，方便单元测试与复用。
//   2. 关节角度用「欧拉角（度）」表达。
//   3. BoneOffset 是「从父关节到本关节」的局部偏移（米），在父关节局部空间里。
//
// FK 约定：
//   worldRot[j] = worldRot[parent] * localRot[j]
//   worldPos[j] = worldPos[parent] + worldRot[parent] * BoneOffset[j]
// 即：本关节的旋转只影响子关节，自己的位置由父关节决定。
//
// 坐标约定：X 右、Y 上、Z 前（与 Unity 一致）。

using System;
using UnityEngine;

namespace PlayerMotion
{
    /// <summary>骨架关节。排列顺序即 FK 求解顺序（父在子前）。</summary>
    public enum Joint
    {
        Pelvis = 0,
        Spine,
        Chest,
        Neck,
        Head,
        ShoulderL,
        ElbowL,
        HandL,
        ShoulderR,
        ElbowR,
        HandR,
        HipL,
        KneeL,
        AnkleL,
        FootL,
        ToeL,
        HeelL,
        HipR,
        KneeR,
        AnkleR,
        FootR,
        ToeR,
        HeelR,
    }

    public static class Skeleton
    {
        public const int JointCount = 23;

        /// <summary>父关节；null 表示骨架根节点。</summary>
        public static readonly Joint?[] Parent =
        {
            null,               // Pelvis
            Joint.Pelvis,       // Spine
            Joint.Spine,        // Chest
            Joint.Chest,        // Neck
            Joint.Neck,         // Head
            Joint.Chest,        // ShoulderL
            Joint.ShoulderL,    // ElbowL
            Joint.ElbowL,       // HandL
            Joint.Chest,        // ShoulderR
            Joint.ShoulderR,    // ElbowR
            Joint.ElbowR,       // HandR
            Joint.Pelvis,       // HipL
            Joint.HipL,         // KneeL
            Joint.KneeL,        // AnkleL
            Joint.AnkleL,       // FootL
            Joint.FootL,        // ToeL
            Joint.AnkleL,       // HeelL
            Joint.Pelvis,       // HipR
            Joint.HipR,         // KneeR
            Joint.KneeR,        // AnkleR
            Joint.AnkleR,       // FootR
            Joint.FootR,        // ToeR
            Joint.AnkleR,       // HeelR
        };

        /// <summary>
        /// 父关节局部空间里的偏移（米）。按 1.75m 身高比例取整。
        /// 脚拆成三段：踝→跖趾（脚掌前段）、跖趾→脚尖、踝→脚跟。
        /// 只有一段的话蹲下时脚掌就是一块斜板，看不出「脚掌贴地」。
        /// 脚底高度：踝 0.06 - 0.055 ≈ 0.005，正好落在地面上。
        /// </summary>
        public static readonly Vector3[] BoneOffset =
        {
            new Vector3(0f, 0.95f, 0f),      // Pelvis：骨盆离地高度
            new Vector3(0f, 0.18f, 0f),      // Spine
            new Vector3(0f, 0.26f, 0f),      // Chest
            new Vector3(0f, 0.09f, 0f),      // Neck
            new Vector3(0f, 0.22f, 0f),      // Head
            new Vector3(0.17f, 0.16f, 0f),   // ShoulderL
            new Vector3(0.28f, 0f, 0f),      // ElbowL
            new Vector3(0.25f, 0f, 0f),      // HandL
            new Vector3(-0.17f, 0.16f, 0f),  // ShoulderR
            new Vector3(-0.28f, 0f, 0f),     // ElbowR
            new Vector3(-0.25f, 0f, 0f),     // HandR
            new Vector3(0.1f, -0.06f, 0f),   // HipL
            new Vector3(0f, -0.44f, 0f),     // KneeL
            new Vector3(0f, -0.39f, 0f),     // AnkleL
            new Vector3(0f, -0.055f, 0.105f),// FootL（跖趾）
            new Vector3(0f, -0.005f, 0.085f),// ToeL
            new Vector3(0f, -0.055f, -0.062f),// HeelL
            new Vector3(-0.1f, -0.06f, 0f),  // HipR
            new Vector3(0f, -0.44f, 0f),     // KneeR
            new Vector3(0f, -0.39f, 0f),     // AnkleR
            new Vector3(0f, -0.055f, 0.105f),// FootR（跖趾）
            new Vector3(0f, -0.005f, 0.085f),// ToeR
            new Vector3(0f, -0.055f, -0.062f),// HeelR
        };

        /// <summary>绘制/建白模时用的骨段（父 → 子），不含根。</summary>
        public static readonly (Joint from, Joint to)[] BoneSegments = BuildSegments();

        private static (Joint, Joint)[] BuildSegments()
        {
            var list = new System.Collections.Generic.List<(Joint, Joint)>(JointCount - 1);
            for (int i = 1; i < JointCount; i++)
            {
                var p = Parent[i];
                if (p.HasValue) list.Add((p.Value, (Joint)i));
            }
            return list.ToArray();
        }

        /// <summary>关节名的中文标签，HUD 用。</summary>
        public static string Label(Joint j) => j switch
        {
            Joint.Pelvis => "骨盆",
            Joint.Spine => "脊柱",
            Joint.Chest => "胸腔",
            Joint.Neck => "颈",
            Joint.Head => "头",
            Joint.ShoulderL => "左肩",
            Joint.ElbowL => "左肘",
            Joint.HandL => "左手",
            Joint.ShoulderR => "右肩",
            Joint.ElbowR => "右肘",
            Joint.HandR => "右手",
            Joint.HipL => "左髋",
            Joint.KneeL => "左膝",
            Joint.AnkleL => "左踝",
            Joint.FootL => "左脚掌",
            Joint.ToeL => "左脚尖",
            Joint.HeelL => "左脚跟",
            Joint.HipR => "右髋",
            Joint.KneeR => "右膝",
            Joint.AnkleR => "右踝",
            Joint.FootR => "右脚掌",
            Joint.ToeR => "右脚尖",
            Joint.HeelR => "右脚跟",
            _ => j.ToString(),
        };
    }
}
