// 场景探针 —— 从 TypeScript 版 input.ts 的 createCorridorProbe 迁移。
//
// 这里刻意把「场景」抽象成最小接口：给一个世界坐标，回答
// 「附近有没有可躲藏的墙、在哪一侧、多远、法线指向哪」。
// 这样动作系统完全不依赖具体关卡；接真实场景时，
// 只要换掉本类的 Query/Clamp 实现（射线或触发器），动作代码不用动。

using UnityEngine;

namespace PlayerMotion
{
    /// <summary>一次掩体查询的结果。</summary>
    public struct CoverInfo
    {
        public bool Available;
        /// <summary>墙在角色哪一侧：-1 左、1 右。</summary>
        public sbyte Side;
        /// <summary>到墙的水平距离（米）。</summary>
        public float Distance;
        /// <summary>墙的表面法线（由墙指向角色）。正对墙时 Side 会退化，靠它判断方向。</summary>
        public Vector3 Normal;
    }

    /// <summary>
    /// 演示用掩体：两条沿 Z 轴延伸的平行墙，位于 x = ±HalfWidth。
    ///
    /// 尺寸是算过的：
    ///   贴墙位置 = HalfWidth - BodyRadius = 1.0 - 0.25 = 0.75m
    ///   距墙 = 0.25m，落在「可贴墙」阈值 0.45m 内
    ///   翻滚位移 1.5m = 2 × 0.75，正好从一侧贴墙滚到另一侧贴墙
    /// </summary>
    [System.Serializable]
    public sealed class CorridorProbe
    {
        [Tooltip("走廊半宽（米）")]
        public float HalfWidth = 1.0f;
        [Tooltip("「附近有墙」的判定距离")]
        public float Trigger = 1.15f;
        [Tooltip("身体半径，决定能贴多近")]
        public float BodyRadius = 0.3f;

        public CoverInfo Query(Vector3 position, float facingDeg)
        {
            float px = position.x;
            float distLeft = px + HalfWidth;   // 到 x = -HalfWidth
            float distRight = HalfWidth - px;  // 到 x = +HalfWidth
            bool useLeft = distLeft <= distRight;
            float distance = Mathf.Max(0f, useLeft ? distLeft : distRight);

            // 墙在世界 -X 或 +X 方向；判断它在角色左侧还是右侧
            float yaw = facingDeg * Mathf.Deg2Rad;
            float rightX = Mathf.Cos(yaw);
            float rightZ = -Mathf.Sin(yaw);
            float toWallX = useLeft ? -1f : 1f;
            float dotRight = rightX * toWallX + rightZ * 0f;
            sbyte side = (sbyte)(dotRight > 0f ? 1 : -1);

            // 法线由墙指向角色：靠左墙（-X）时是 +X，靠右墙（+X）时是 -X
            var normal = useLeft ? Vector3.right : Vector3.left;

            return new CoverInfo
            {
                Available = distance <= Trigger,
                Side = side,
                Distance = distance,
                Normal = normal,
            };
        }

        /// <summary>把角色限制在走廊内，模拟墙体阻挡。</summary>
        public Vector3 Clamp(Vector3 position, float? radius = null)
        {
            float r = radius ?? BodyRadius;
            float limit = Mathf.Max(0f, HalfWidth - r);
            float px = Mathf.Clamp(position.x, -limit, limit);
            return Mathf.Approximately(px, position.x)
                ? position
                : new Vector3(px, position.y, position.z);
        }

        /// <summary>两面墙的世界坐标线段（沿 Z 延伸），供视图按真实位置绘制。</summary>
        public (Vector3 a, Vector3 b)[] Walls(Vector3 position, float span = 6f)
        {
            float z0 = position.z - span;
            float z1 = position.z + span;
            return new[]
            {
                (new Vector3(-HalfWidth, 0f, z0), new Vector3(-HalfWidth, 0f, z1)),
                (new Vector3(HalfWidth, 0f, z0), new Vector3(HalfWidth, 0f, z1)),
            };
        }
    }
}
