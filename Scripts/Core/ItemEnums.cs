// ============================================================
// ItemEnums.cs — 道具系统枚举定义
// 对应策划案：道具数据字段中的「效果」和「状态」
// ============================================================
namespace ItemSystem
{
    /// <summary>
    /// 道具效果类型
    /// 策划案参考：眩晕/麻痹/诱导敌人等
    /// 可对应敌人机制：被声音吸引、强光致停、触电等
    /// </summary>
    public enum ItemEffectType
    {
        None,           // 无效果
        Stun,           // 眩晕 — 对应「强光照射，停止行动一段时间」
        Paralyze,       // 麻痹 — 类似触电死亡机制
        Lure,           // 诱敌 — 对应「被声音吸引」
        Damage,         // 直接伤害
        Trap,           // 陷阱 — 布置类道具
        Stealth         // 隐匿 — 增强潜行
    }

    /// <summary>
    /// 道具状态 — 完整映射策划案定义的 6 种状态
    /// </summary>
    public enum ItemState
    {
        Unused,             // 未使用 — 在背包中待用
        Used,               // 已使用 — 消耗完毕
        EntityInFlight,     // 实体飞行中 — 投掷类道具在空中飞行
        EntityCollided,     // 实体碰撞后 — 投掷物命中目标/场景
        PlacedInactive,     // 已布置未使用 — 陷阱类道具已放置但未触发
        PlacedActivated     // 已布置已使用 — 陷阱类道具已触发
    }

    /// <summary>
    /// 道具使用方式
    /// </summary>
    public enum ItemUsageType
    {
        Throwable,      // 投掷类 — 飞行 → 碰撞
        Placeable,      // 布置类 — 放置 → 触发（两段式）
        Instant,        // 即时类 — 立即生效
        Consumable      // 消耗类 — 使用后消失
    }

    /// <summary>
    /// 道具系统事件类型 — 用于向总控制系统播报
    /// </summary>
    public enum ItemEventType
    {
        ItemUsed,           // 道具被使用
        ItemEffectApplied,  // 道具效果生效（伤害/眩晕等）
        ItemPickedUp,       // 道具被拾取
        ItemDropped,        // 道具被丢弃
        ItemStateChanged,   // 道具状态变化
        ItemQuantityChanged // 道具数量变化
    }
}
