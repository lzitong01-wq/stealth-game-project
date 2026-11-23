// ============================================================
// ItemData.cs — 道具数据定义 (ScriptableObject)
// 策划案：道具的静态数据（效果、作用范围/判定体积等）
// 用法：在 Unity Editor 中右键 → Create → 道具系统 → 道具数据
// ============================================================
using UnityEngine;

namespace ItemSystem
{
    [CreateAssetMenu(fileName = "NewItem", menuName = "道具系统/道具数据")]
    public class ItemData : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("道具唯一 ID")]
        public string itemId;

        [Tooltip("道具名称")]
        public string itemName;

        [Tooltip("道具描述 — 可用「规则怪谈」风格文案")]
        [TextArea(3, 6)]
        public string description;

        [Tooltip("道具图标 — 显示在 UI 槽位中")]
        public Sprite icon;

        [Header("道具类型")]
        [Tooltip("使用方式：投掷/布置/即时/消耗")]
        public ItemUsageType usageType = ItemUsageType.Throwable;

        [Tooltip("效果类型：眩晕/麻痹/诱敌等")]
        public ItemEffectType effectType = ItemEffectType.None;

        [Header("数值参数")]
        [Tooltip("最大持有数量")]
        public int maxStack = 5;

        [Tooltip("效果持续时间（秒）")]
        public float effectDuration = 3f;

        [Tooltip("效果数值（伤害值/眩晕时长等）")]
        public float effectValue = 10f;

        [Header("物理参数 — 策划案「作用范围/判定体积」")]
        [Tooltip("作用范围半径（米）")]
        public float effectRadius = 5f;

        [Tooltip("投掷速度")]
        public float throwSpeed = 15f;

        [Tooltip("投掷距离上限")]
        public float maxThrowDistance = 20f;

        [Header("声音参数 — 与敌人「被声音吸引」机制联动")]
        [Tooltip("使用时产生的噪音半径（会吸引听觉型敌人）")]
        public float noiseRadius = 0f;

        [Header("预制体")]
        [Tooltip("道具在世界中的实体预制体")]
        public GameObject entityPrefab;

        [Tooltip("效果特效预制体")]
        public GameObject effectVfxPrefab;

        [Header("动画")]
        [Tooltip("使用时播放的动画 Trigger 名")]
        public string useAnimTrigger = "UseItem";

        [Tooltip("碰撞后播放的动画 Trigger 名")]
        public string hitAnimTrigger = "ItemHit";
    }
}
