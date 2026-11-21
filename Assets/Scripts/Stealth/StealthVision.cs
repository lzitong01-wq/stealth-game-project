using UnityEngine;

namespace StealthPrototype
{
    public enum VisionBlockReason
    {
        NoTarget,
        NotTargetable,
        OutOfRange,
        OutOfAngle,
        Occluded,
        Visible
    }

    public struct VisionQuery
    {
        public Vector3 eyePosition;
        public Vector3 forward;
        public Vector3 selfFlatPosition;
        public Vector3 targetFlatPosition;
        public Vector3 targetPoint;
        public float maxDistance;
        public float fullViewAngle;
        public LayerMask occlusionMask;
    }

    public struct VisionResult
    {
        public VisionBlockReason reason;
        public bool inRange;
        public bool inAngle;
        public bool blocked;
        public float flatDistance;
        public float angle;
        public Collider blocker;
        public Vector3 rayEnd;
        public bool rayHitObstacle;

        public static VisionResult None(VisionBlockReason reason)
        {
            return new VisionResult { reason = reason };
        }
    }

    /// <summary>
    /// 距离、扇形角度、遮挡三项判定。敌人和调试绘制都走这里，避免两套参数。
    /// </summary>
    public static class StealthVision
    {
        public static VisionResult Evaluate(VisionQuery query)
        {
            Vector3 forward = query.forward;
            forward.y = 0f;

            Vector3 toTarget = query.targetFlatPosition - query.selfFlatPosition;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            float angle = 0f;
            if (forward.sqrMagnitude > 0.0001f && distance > 0.001f)
                angle = Vector3.Angle(forward.normalized, toTarget / distance);

            bool inRange = distance <= query.maxDistance;
            bool inAngle = distance <= 0.001f || angle <= query.fullViewAngle * 0.5f;

            bool blocked = false;
            Collider blocker = null;
            Vector3 rayEnd = query.targetPoint;
            Vector3 toPoint = query.targetPoint - query.eyePosition;
            float pointDistance = toPoint.magnitude;
            if (pointDistance > 0.05f && query.occlusionMask.value != 0)
            {
                if (Physics.Raycast(
                        query.eyePosition,
                        toPoint / pointDistance,
                        out RaycastHit hit,
                        pointDistance,
                        query.occlusionMask,
                        QueryTriggerInteraction.Ignore))
                {
                    blocked = true;
                    blocker = hit.collider;
                    rayEnd = hit.point;
                }
            }

            VisionBlockReason reason;
            if (!inRange)
                reason = VisionBlockReason.OutOfRange;
            else if (!inAngle)
                reason = VisionBlockReason.OutOfAngle;
            else if (blocked)
                reason = VisionBlockReason.Occluded;
            else
                reason = VisionBlockReason.Visible;

            return new VisionResult
            {
                reason = reason,
                inRange = inRange,
                inAngle = inAngle,
                blocked = blocked,
                flatDistance = distance,
                angle = angle,
                blocker = blocker,
                rayEnd = rayEnd,
                rayHitObstacle = blocked
            };
        }

        public static string Describe(VisionBlockReason reason)
        {
            switch (reason)
            {
                case VisionBlockReason.NoTarget: return "无目标";
                case VisionBlockReason.NotTargetable: return "目标不可侦测";
                case VisionBlockReason.OutOfRange: return "不可见：超出视距";
                case VisionBlockReason.OutOfAngle: return "不可见：超出视角";
                case VisionBlockReason.Occluded: return "不可见：被遮挡";
                case VisionBlockReason.Visible: return "可见";
                default: return reason.ToString();
            }
        }
    }
}
