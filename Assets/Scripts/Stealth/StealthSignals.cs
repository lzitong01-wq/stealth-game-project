using System;

namespace StealthPrototype
{
    /// <summary>
    /// 敌人与玩家之间的全局事件。正式版的慢动作 QTE、扣血都可以直接订阅。
    /// 订阅方要在 OnDisable 里取消订阅。
    /// </summary>
    public static class StealthSignals
    {
        public static event Action<StealthConfirmInfo> PlayerConfirmed;
        public static event Action<StealthHitInfo> PlayerHit;

        public static void RaisePlayerConfirmed(StealthConfirmInfo info)
        {
            PlayerConfirmed?.Invoke(info);
        }

        public static void RaisePlayerHit(StealthHitInfo info)
        {
            PlayerHit?.Invoke(info);
        }
    }
}
