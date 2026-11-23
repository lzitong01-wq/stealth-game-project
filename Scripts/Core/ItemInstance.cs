// ============================================================
// ItemInstance.cs — 道具运行时实例
// 策划案：数量 / 效果 / 状态 / 位置 等运行时数据
// ============================================================
using System;
using UnityEngine;

namespace ItemSystem
{
    /// <summary>
    /// 道具运行时实例 — 每个背包槽位持有一个
    /// 包含策划案定义的全部数据字段
    /// </summary>
    [Serializable]
    public class ItemInstance
    {
        [Header("引用")]
        [Tooltip("对应的静态道具数据")]
        public ItemData data;

        [Header("运行时数据 — 对应策划案字段")]
        [Tooltip("数量")]
        public int quantity;

        [Tooltip("当前状态")]
        public ItemState state = ItemState.Unused;

        [Tooltip("当前位置（布置类道具放置后的世界坐标）")]
        public Vector3 position;

        /// <summary>
        /// 道具是否可用（有数量且处于未使用状态）
        /// </summary>
        public bool CanUse => quantity > 0 && state == ItemState.Unused;

        /// <summary>
        /// 道具是否为空
        /// </summary>
        public bool IsEmpty => data == null || quantity <= 0;

        /// <summary>
        /// 创建道具实例
        /// </summary>
        public ItemInstance(ItemData itemData, int count = 1)
        {
            data = itemData;
            quantity = Mathf.Min(count, itemData != null ? itemData.maxStack : count);
            state = ItemState.Unused;
            position = Vector3.zero;
        }

        /// <summary>
        /// 空实例
        /// </summary>
        public ItemInstance()
        {
            data = null;
            quantity = 0;
            state = ItemState.Unused;
        }

        /// <summary>
        /// 消耗一个道具
        /// </summary>
        /// <returns>是否成功消耗</returns>
        public bool Consume()
        {
            if (quantity <= 0) return false;
            quantity--;
            if (quantity <= 0)
            {
                state = ItemState.Used;
            }
            return true;
        }

        /// <summary>
        /// 增加道具数量
        /// </summary>
        /// <param name="amount">增加量</param>
        /// <returns>实际增加的数量（受最大堆叠限制）</returns>
        public int Add(int amount)
        {
            if (data == null) return 0;
            int canAdd = Mathf.Max(0, data.maxStack - quantity);
            int actualAdd = Mathf.Min(amount, canAdd);
            quantity += actualAdd;
            if (quantity > 0) state = ItemState.Unused;
            return actualAdd;
        }
    }
}
