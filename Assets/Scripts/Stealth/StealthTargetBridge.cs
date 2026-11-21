using UnityEngine;
using UnityEngine.Events;

namespace StealthPrototype
{
    /// <summary>
    /// 挂到正式玩家预制体上的适配器。不改移动代码，只把脚底、检测点和受击转给敌人。
    /// 把本组件拖到敌人的 Target Component，或保持启用让敌人自动找到它。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Stealth/Target Bridge")]
    public class StealthTargetBridge : MonoBehaviour, IStealthTarget
    {
        [Tooltip("留空则使用本物体的位置作为脚底。")]
        public Transform feet;

        [Tooltip("当前姿态的视线检测点。正式角色下蹲时，把这个点降到大约 0.7 米。")]
        public Transform visionPoint;

        public float fallbackVisionHeight = 1.5f;
        public bool isTargetable = true;
        public UnityEvent onHit;

        public Vector3 WorldPosition => feet != null ? feet.position : transform.position;

        public Vector3 VisionPoint => visionPoint != null
            ? visionPoint.position
            : WorldPosition + Vector3.up * fallbackVisionHeight;

        public bool IsTargetable => isTargetable && isActiveAndEnabled;

        void OnEnable()
        {
            StealthTargetRegistry.Register(this);
        }

        void OnDisable()
        {
            StealthTargetRegistry.Unregister(this);
        }

        public void ReceiveHit(StealthHitInfo hit)
        {
            onHit?.Invoke();
            StealthSignals.RaisePlayerHit(hit);
        }
    }
}
