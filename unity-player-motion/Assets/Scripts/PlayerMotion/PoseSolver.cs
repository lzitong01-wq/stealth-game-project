// 姿态表达与正向运动学（FK）—— 从 TypeScript 版 pose.ts 迁移。
//
// 这一层是纯数学：输入一组关节欧拉角，输出每个关节的世界坐标与旋转矩阵。
// 不依赖任何场景对象，因此可以在 EditMode 测试里直接验证。
//
// 旋转顺序说明：Unity 的 Quaternion.Euler(x,y,z) 按 Z→X→Y 应用，
// 等价于 Ry * Rx * Rz —— 与原 TypeScript 实现的顺序完全一致，
// 所以两边的角度数值可以一一对应，不需要换算。

using UnityEngine;

namespace PlayerMotion
{
    /// <summary>一套姿态：每个关节一个局部欧拉角（度）。</summary>
    public static class PoseUtil
    {
        public static Vector3[] RestPose()
        {
            var p = new Vector3[Skeleton.JointCount];
            // Vector3 默认 (0,0,0)，即直立站姿
            return p;
        }

        public static Vector3[] Clone(Vector3[] src)
        {
            var dst = new Vector3[src.Length];
            System.Array.Copy(src, dst, src.Length);
            return dst;
        }

        public static void CopyTo(Vector3[] src, Vector3[] dst)
        {
            System.Array.Copy(src, dst, src.Length);
        }

        /// <summary>平滑过渡：用于动作之间的衔接，避免关节角突变。</summary>
        public static float EaseInOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
        }

        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        /// <summary>逐关节线性插值两套姿态。</summary>
        public static void LerpTo(Vector3[] a, Vector3[] b, float t, Vector3[] outPose)
        {
            float k = Mathf.Clamp01(t);
            for (int i = 0; i < outPose.Length; i++)
                outPose[i] = Vector3.LerpUnclamped(a[i], b[i], k);
        }
    }

    /// <summary>
    /// FK 求解器。持有一份结果缓冲区，每帧原地更新，避免 GC。
    /// </summary>
    public sealed class PoseSolver
    {
        public readonly Vector3[] Positions = new Vector3[Skeleton.JointCount];
        public readonly Matrix4x4[] Rotations = new Matrix4x4[Skeleton.JointCount];

        /// <summary>
        /// 整体翻转的枢轴高度（米，相对 RootPos）。
        /// 翻滚如果绕脚底转，转到 180° 时整个身体会沉到地面以下（骨盆跑到 rootPos.y - 0.95），
        /// 画面上就是人凭空消失。绕重心转才是真实的前滚翻。
        /// 纯 Y 轴的朝向旋转绕任意 (0, h, 0) 结果相同，所以非翻滚状态不受影响。
        /// </summary>
        private const float FlipPivotHeight = 0.9f;

        /// <summary>
        /// 正向运动学求解。
        /// </summary>
        /// <param name="pose">每个关节的局部欧拉角（度）</param>
        /// <param name="rootPos">根节点（脚底）世界位置</param>
        /// <param name="rootEuler">根节点整体旋转（度）。翻滚时靠它让整个人转起来。</param>
        public void Solve(Vector3[] pose, Vector3 rootPos, Vector3 rootEuler)
        {
            var rootRot = Matrix4x4.Rotate(Quaternion.Euler(rootEuler));

            for (int i = 0; i < Skeleton.JointCount; i++)
            {
                var local = Matrix4x4.Rotate(Quaternion.Euler(pose[i]));
                var parent = Skeleton.Parent[i];

                if (!parent.HasValue)
                {
                    // 根：绕重心枢轴翻转，而不是绕脚底
                    var pivot = rootPos + new Vector3(0f, FlipPivotHeight, 0f);
                    var rest = rootPos + Skeleton.BoneOffset[i];
                    Positions[i] = pivot + rootRot.MultiplyVector(rest - pivot);
                    Rotations[i] = rootRot * local;
                }
                else
                {
                    int pi = (int)parent.Value;
                    Positions[i] = Positions[pi] + Rotations[pi].MultiplyVector(Skeleton.BoneOffset[i]);
                    Rotations[i] = Rotations[pi] * local;
                }
            }
        }
    }
}
