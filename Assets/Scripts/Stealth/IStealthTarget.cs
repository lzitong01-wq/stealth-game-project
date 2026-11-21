using UnityEngine;

namespace StealthPrototype
{
    /// <summary>
    /// 敌人观察和攻击玩家时使用的契约。正式角色接入时实现本接口即可，不必继承临时玩家。
    /// WorldPosition 用脚底（追击落点），VisionPoint 用当前姿态的视线检测点。
    /// </summary>
    public interface IStealthTarget
    {
        Vector3 WorldPosition { get; }
        Vector3 VisionPoint { get; }
        bool IsTargetable { get; }
        void ReceiveHit(StealthHitInfo hit);
    }

    public struct StealthHitInfo
    {
        public Object source;
        public Vector3 sourcePosition;
        public float time;
    }

    public struct StealthConfirmInfo
    {
        public Object source;
        public IStealthTarget target;
        public Vector3 enemyPosition;
        public Vector3 targetPosition;
        public float time;
    }

    /// <summary>
    /// 场景里没有手动指定目标时，敌人使用最近一个启用的目标。
    /// </summary>
    public static class StealthTargetRegistry
    {
        public static IStealthTarget Current { get; private set; }

        public static void Register(IStealthTarget target)
        {
            if (target != null)
                Current = target;
        }

        public static void Unregister(IStealthTarget target)
        {
            if (Current == target)
                Current = null;
        }
    }
}
