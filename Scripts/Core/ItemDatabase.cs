// ============================================================
// ItemDatabase.cs — 道具数据库 (ScriptableObject)
// 集中管理所有道具数据，供 ItemManager 查询
// 用法：在 Unity Editor 中右键 → Create → 道具系统 → 道具数据库
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace ItemSystem
{
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "道具系统/道具数据库")]
    public class ItemDatabase : ScriptableObject
    {
        [Tooltip("所有道具数据列表")]
        public List<ItemData> allItems = new List<ItemData>();

        // 运行时用的快速查找字典
        private Dictionary<string, ItemData> _lookup;

        /// <summary>
        /// 初始化查找字典（在游戏启动时调用）
        /// </summary>
        public void Initialize()
        {
            _lookup = new Dictionary<string, ItemData>();
            foreach (var item in allItems)
            {
                if (item != null && !string.IsNullOrEmpty(item.itemId))
                {
                    _lookup[item.itemId] = item;
                }
            }
            Debug.Log($"[道具数据库] 已加载 {_lookup.Count} 种道具");
        }

        /// <summary>
        /// 根据 ID 查找道具数据
        /// </summary>
        public ItemData GetItemById(string id)
        {
            if (_lookup == null) Initialize();
            _lookup.TryGetValue(id, out var data);
            return data;
        }

        /// <summary>
        /// 获取指定效果类型的所有道具
        /// </summary>
        public List<ItemData> GetItemsByEffect(ItemEffectType effectType)
        {
            var result = new List<ItemData>();
            foreach (var item in allItems)
            {
                if (item != null && item.effectType == effectType)
                    result.Add(item);
            }
            return result;
        }
    }
}
