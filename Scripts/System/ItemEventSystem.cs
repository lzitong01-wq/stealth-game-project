// ============================================================
// ItemEventSystem.cs — 道具事件系统
// 策划案功能：「播报道具使用情况（伤害/效果等）至总控制系统」
// 使用 C# 事件实现系统间解耦通信
// ============================================================
using System;
using UnityEngine;

namespace ItemSystem
{
    /// <summary>
    /// 道具事件参数 — 携带播报数据
    /// </summary>
    public class ItemEventArgs : EventArgs
    {
        public ItemEventType EventType { get; private set; }
        public ItemInstance Item { get; private set; }
        public int SlotIndex { get; private set; }
        public Vector3 Position { get; private set; }
        public GameObject Target { get; private set; }
        public float EffectValue { get; private set; }

        public ItemEventArgs(ItemEventType eventType, ItemInstance item, int slotIndex,
            Vector3 position = default, GameObject target = null, float effectValue = 0f)
        {
            EventType = eventType;
            Item = item;
            SlotIndex = slotIndex;
            Position = position;
            Target = target;
            EffectValue = effectValue;
        }
    }

    /// <summary>
    /// 道具事件系统 — 总控制系统与道具系统之间的通信桥梁
    /// 其他系统（敌人系统、剧情系统等）订阅这些事件即可收到播报
    /// </summary>
    public static class ItemEventSystem
    {
        // ===== 道具系统 → 总控制系统（播报） =====

        /// <summary>道具被使用时触发</summary>
        public static event Action<ItemEventArgs> OnItemUsed;

        /// <summary>道具效果生效时触发（伤害/眩晕等命中目标）</summary>
        public static event Action<ItemEventArgs> OnItemEffectApplied;

        /// <summary>道具被拾取时触发</summary>
        public static event Action<ItemEventArgs> OnItemPickedUp;

        /// <summary>道具状态变化时触发</summary>
        public static event Action<ItemEventArgs> OnItemStateChanged;

        /// <summary>道具数量变化时触发</summary>
        public static event Action<ItemEventArgs> OnItemQuantityChanged;

        // ===== 总控制系统 → 道具系统（指令） =====

        /// <summary>总控制系统请求使用道具</summary>
        public static event Action<int> OnRequestUseItem;

        /// <summary>总控制系统请求切换选中槽位</summary>
        public static event Action<int> OnRequestSelectSlot;

        // ===== 触发方法 =====

        public static void RaiseItemUsed(ItemEventArgs args)
        {
            OnItemUsed?.Invoke(args);
            Debug.Log($"[道具播报] 使用道具: {args.Item?.data?.itemName} " +
                      $"效果: {args.Item?.data?.effectType} 位置: {args.Position}");
        }

        public static void RaiseItemEffectApplied(ItemEventArgs args)
        {
            OnItemEffectApplied?.Invoke(args);
            Debug.Log($"[道具播报] 效果生效: {args.Item?.data?.effectType} " +
                      $"目标: {args.Target?.name} 数值: {args.EffectValue}");
        }

        public static void RaiseItemPickedUp(ItemEventArgs args)
        {
            OnItemPickedUp?.Invoke(args);
            Debug.Log($"[道具播报] 拾取道具: {args.Item?.data?.itemName} " +
                      $"数量: {args.Item?.quantity}");
        }

        public static void RaiseItemStateChanged(ItemEventArgs args)
        {
            OnItemStateChanged?.Invoke(args);
        }

        public static void RaiseItemQuantityChanged(ItemEventArgs args)
        {
            OnItemQuantityChanged?.Invoke(args);
        }

        public static void RequestUseItem(int slotIndex)
        {
            OnRequestUseItem?.Invoke(slotIndex);
        }

        public static void RequestSelectSlot(int slotIndex)
        {
            OnRequestSelectSlot?.Invoke(slotIndex);
        }

        /// <summary>
        /// 清理所有事件订阅（场景切换时调用，防止内存泄漏）
        /// </summary>
        public static void ClearAll()
        {
            OnItemUsed = null;
            OnItemEffectApplied = null;
            OnItemPickedUp = null;
            OnItemStateChanged = null;
            OnItemQuantityChanged = null;
            OnRequestUseItem = null;
            OnRequestSelectSlot = null;
        }
    }
}
