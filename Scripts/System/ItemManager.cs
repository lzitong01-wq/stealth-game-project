// ============================================================
// ItemManager.cs — 道具系统核心管理器
// 策划案功能：
//   1. 存储和修改道具数据
//   2. 从总控制系统读取修改道具使用状态/数量/调用使用界面
//   3. 播报道具使用情况至总控制系统
// UI 呈现：3 槽位（数字键 1/2/3）
// ============================================================
using System;
using UnityEngine;

namespace ItemSystem
{
    /// <summary>
    /// 道具系统核心管理器 — 单例模式
    /// 管理 3 个道具槽位，处理道具使用/拾取/状态变更
    /// </summary>
    public class ItemManager : MonoBehaviour
    {
        // ==================== 单例 ====================
        public static ItemManager Instance { get; private set; }

        // ==================== 配置 ====================
        [Header("配置")]
        [SerializeField] private ItemDatabase database;
        [SerializeField] private int slotCount = 3;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Vector3 throwOffset = new Vector3(0, 1.2f, 0);

        // ==================== 运行时数据 ====================
        public ItemInstance[] Slots { get; private set; }
        public int SelectedSlot { get; private set; } = 0;

        private bool _isOnCooldown = false;
        private float _cooldownTimer = 0f;
        private const float USE_COOLDOWN = 0.5f;

        // ==================== 事件（供 UI 监听） ====================
        public event Action<int> OnSlotChanged;
        public event Action<int> OnSelectionChanged;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Slots = new ItemInstance[slotCount];
            for (int i = 0; i < slotCount; i++)
                Slots[i] = new ItemInstance();

            if (database != null)
                database.Initialize();
        }

        private void OnEnable()
        {
            ItemEventSystem.OnRequestUseItem += HandleRequestUseItem;
            ItemEventSystem.OnRequestSelectSlot += HandleRequestSelectSlot;
        }

        private void OnDisable()
        {
            ItemEventSystem.OnRequestUseItem -= HandleRequestUseItem;
            ItemEventSystem.OnRequestSelectSlot -= HandleRequestSelectSlot;
        }

        private void Update()
        {
            HandleInput();
            UpdateCooldown();
        }

        // ==================== 输入处理 ====================

        private void HandleInput()
        {
            // 数字键 1/2/3 选择槽位
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);

            // 鼠标左键使用当前选中道具
            if (Input.GetMouseButtonDown(0))
                UseCurrentItem();
        }

        private void UpdateCooldown()
        {
            if (!_isOnCooldown) return;
            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer <= 0f)
                _isOnCooldown = false;
        }

        // ==================== 核心功能 ====================

        /// <summary>选择道具槽位</summary>
        public void SelectSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotCount) return;
            if (SelectedSlot == slotIndex) return;
            SelectedSlot = slotIndex;
            OnSelectionChanged?.Invoke(slotIndex);
        }

        /// <summary>使用当前选中槽位的道具</summary>
        public void UseCurrentItem()
        {
            UseItem(SelectedSlot);
        }

        /// <summary>使用指定槽位的道具</summary>
        public void UseItem(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotCount) return;
            if (_isOnCooldown) return;

            var item = Slots[slotIndex];
            if (item == null || !item.CanUse)
            {
                Debug.Log("[道具系统] 该槽位无可用道具");
                return;
            }

            switch (item.data.usageType)
            {
                case ItemUsageType.Throwable:
                    UseThrowableItem(item, slotIndex);
                    break;
                case ItemUsageType.Placeable:
                    UsePlaceableItem(item, slotIndex);
                    break;
                case ItemUsageType.Instant:
                    UseInstantItem(item, slotIndex);
                    break;
                case ItemUsageType.Consumable:
                    UseConsumableItem(item, slotIndex);
                    break;
            }

            _isOnCooldown = true;
            _cooldownTimer = USE_COOLDOWN;
        }

        /// <summary>
        /// 投掷类道具 — 状态：未使用 → 实体飞行中 → 实体碰撞后
        /// </summary>
        private void UseThrowableItem(ItemInstance item, int slotIndex)
        {
            if (playerTransform == null) return;

            item.Consume();

            Vector3 spawnPos = playerTransform.position + throwOffset;
            Vector3 throwDir = playerTransform.forward;

            item.state = ItemState.EntityInFlight;
            NotifyStateChanged(item, slotIndex);

            if (item.data.entityPrefab != null)
            {
                var entityObj = Instantiate(item.data.entityPrefab, spawnPos, Quaternion.identity);
                var entity = entityObj.GetComponent<ItemEntity>();
                if (entity != null)
                    entity.Launch(item.data, throwDir * item.data.throwSpeed, slotIndex);
            }

            ItemEventSystem.RaiseItemUsed(new ItemEventArgs(
                ItemEventType.ItemUsed, item, slotIndex, spawnPos));
            OnSlotChanged?.Invoke(slotIndex);
        }

        /// <summary>
        /// 布置类道具 — 状态：未使用 → 已布置未使用 → 已布置已使用
        /// </summary>
        private void UsePlaceableItem(ItemInstance item, int slotIndex)
        {
            if (playerTransform == null) return;

            item.Consume();

            Vector3 placePos = playerTransform.position + playerTransform.forward * 1.5f;
            item.position = placePos;
            item.state = ItemState.PlacedInactive;
            NotifyStateChanged(item, slotIndex);

            if (item.data.entityPrefab != null)
            {
                var entityObj = Instantiate(item.data.entityPrefab, placePos, Quaternion.identity);
                var entity = entityObj.GetComponent<ItemEntity>();
                if (entity != null)
                    entity.PlaceAsTrap(item.data, slotIndex);
            }

            ItemEventSystem.RaiseItemUsed(new ItemEventArgs(
                ItemEventType.ItemUsed, item, slotIndex, placePos));
            OnSlotChanged?.Invoke(slotIndex);
        }

        /// <summary>即时类道具 — 立即生效</summary>
        private void UseInstantItem(ItemInstance item, int slotIndex)
        {
            item.Consume();
            item.state = ItemState.Used;

            Vector3 pos = playerTransform != null ? playerTransform.position : Vector3.zero;

            if (item.data.effectVfxPrefab != null)
            {
                var vfx = Instantiate(item.data.effectVfxPrefab, pos, Quaternion.identity);
                Destroy(vfx, item.data.effectDuration);
            }

            ItemEventSystem.RaiseItemUsed(new ItemEventArgs(
                ItemEventType.ItemUsed, item, slotIndex, pos));
            ItemEventSystem.RaiseItemEffectApplied(new ItemEventArgs(
                ItemEventType.ItemEffectApplied, item, slotIndex, pos,
                effectValue: item.data.effectValue));
            OnSlotChanged?.Invoke(slotIndex);
        }

        /// <summary>消耗类道具</summary>
        private void UseConsumableItem(ItemInstance item, int slotIndex)
        {
            item.Consume();
            item.state = ItemState.Used;

            Vector3 pos = playerTransform != null ? playerTransform.position : Vector3.zero;
            ItemEventSystem.RaiseItemUsed(new ItemEventArgs(
                ItemEventType.ItemUsed, item, slotIndex, pos));
            OnSlotChanged?.Invoke(slotIndex);
        }

        // ==================== 拾取 & 添加 ====================

        /// <summary>拾取道具 — 添加到背包</summary>
        public bool PickupItem(ItemData itemData, int amount = 1)
        {
            if (itemData == null) return false;

            // 优先叠加到已有同类道具的槽位
            for (int i = 0; i < slotCount; i++)
            {
                if (Slots[i].data == itemData && Slots[i].quantity < itemData.maxStack)
                {
                    int added = Slots[i].Add(amount);
                    if (added > 0)
                    {
                        NotifyPickup(Slots[i], i);
                        OnSlotChanged?.Invoke(i);
                        return true;
                    }
                }
            }

            // 寻找空槽位
            for (int i = 0; i < slotCount; i++)
            {
                if (Slots[i].IsEmpty)
                {
                    Slots[i] = new ItemInstance(itemData, amount);
                    NotifyPickup(Slots[i], i);
                    OnSlotChanged?.Invoke(i);
                    return true;
                }
            }

            Debug.Log("[道具系统] 背包已满，无法拾取");
            return false;
        }

        /// <summary>通过 ID 拾取道具</summary>
        public bool PickupItemById(string itemId, int amount = 1)
        {
            if (database == null) return false;
            var data = database.GetItemById(itemId);
            if (data == null) return false;
            return PickupItem(data, amount);
        }

        /// <summary>获取指定槽位的道具实例</summary>
        public ItemInstance GetSlot(int index)
        {
            if (index < 0 || index >= slotCount) return null;
            return Slots[index];
        }

        /// <summary>清空指定槽位</summary>
        public void ClearSlot(int index)
        {
            if (index < 0 || index >= slotCount) return;
            Slots[index] = new ItemInstance();
            OnSlotChanged?.Invoke(index);
        }

        // ==================== 状态通知 ====================

        /// <summary>
        /// 外部调用 — 实体碰撞后更新状态（由 ItemEntity 回调）
        /// </summary>
        public void NotifyEntityCollided(int slotIndex, Vector3 hitPosition,
            GameObject hitTarget = null)
        {
            if (slotIndex < 0 || slotIndex >= slotCount) return;
            var item = Slots[slotIndex];

            if (item.state == ItemState.EntityInFlight)
                item.state = ItemState.EntityCollided;
            else if (item.state == ItemState.PlacedInactive)
                item.state = ItemState.PlacedActivated;

            NotifyStateChanged(item, slotIndex);

            ItemEventSystem.RaiseItemEffectApplied(new ItemEventArgs(
                ItemEventType.ItemEffectApplied, item, slotIndex, hitPosition,
                hitTarget, item.data != null ? item.data.effectValue : 0f));
        }

        // ==================== 内部辅助 ====================

        private void NotifyStateChanged(ItemInstance item, int slotIndex)
        {
            ItemEventSystem.RaiseItemStateChanged(new ItemEventArgs(
                ItemEventType.ItemStateChanged, item, slotIndex));
        }

        private void NotifyPickup(ItemInstance item, int slotIndex)
        {
            ItemEventSystem.RaiseItemPickedUp(new ItemEventArgs(
                ItemEventType.ItemPickedUp, item, slotIndex));
            ItemEventSystem.RaiseItemQuantityChanged(new ItemEventArgs(
                ItemEventType.ItemQuantityChanged, item, slotIndex));
        }

        private void HandleRequestUseItem(int slotIndex) => UseItem(slotIndex);
        private void HandleRequestSelectSlot(int slotIndex) => SelectSlot(slotIndex);
    }
}
