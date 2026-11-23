// ============================================================
// ItemBarUI.cs — 道具栏 UI（3 槽位 HUD）
// 策划案 UI：右下角，3 个槽位，数字键 1/2/3 切换
// 包含提示特效（拾取/使用反馈）
// ============================================================
using UnityEngine;
using TMPro;
using System.Collections;

namespace ItemSystem
{
    /// <summary>
    /// 道具栏 UI — 管理 3 个 ItemSlotUI，处理选中/刷新/提示
    /// 在 Canvas 上挂载此组件，子物体为 3 个 ItemSlotUI
    /// </summary>
    public class ItemBarUI : MonoBehaviour
    {
        [Header("槽位引用")]
        [Tooltip("3 个槽位 UI（按顺序排列）")]
        [SerializeField] private ItemSlotUI[] slots;

        [Header("提示特效 — 策划案「提示特效」")]
        [Tooltip("道具名称提示文本")]
        [SerializeField] private TextMeshProUGUI itemNameText;

        [Tooltip("提示信息文本（拾取/使用反馈）")]
        [SerializeField] private TextMeshProUGUI messageText;

        [Tooltip("提示信息显示时长（秒）")]
        [SerializeField] private float messageDuration = 2f;

        [Header("动画")]
        [Tooltip("道具栏整体动画控制器（可选）")]
        [SerializeField] private Animator barAnimator;

        private Coroutine _messageCoroutine;

        // ==================== 生命周期 ====================

        private void Start()
        {
            InitializeSlots();

            if (messageText != null)
                messageText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (ItemManager.Instance != null)
            {
                ItemManager.Instance.OnSlotChanged += OnSlotChanged;
                ItemManager.Instance.OnSelectionChanged += OnSelectionChanged;
            }

            // 监听事件播报，显示提示
            ItemEventSystem.OnItemPickedUp += OnItemPickedUp;
            ItemEventSystem.OnItemUsed += OnItemUsed;
            ItemEventSystem.OnItemEffectApplied += OnItemEffectApplied;
        }

        private void OnDisable()
        {
            if (ItemManager.Instance != null)
            {
                ItemManager.Instance.OnSlotChanged -= OnSlotChanged;
                ItemManager.Instance.OnSelectionChanged -= OnSelectionChanged;
            }

            ItemEventSystem.OnItemPickedUp -= OnItemPickedUp;
            ItemEventSystem.OnItemUsed -= OnItemUsed;
            ItemEventSystem.OnItemEffectApplied -= OnItemEffectApplied;
        }

        // ==================== 初始化 ====================

        private void InitializeSlots()
        {
            if (slots == null || slots.Length == 0)
            {
                slots = GetComponentsInChildren<ItemSlotUI>();
            }

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Setup(i);
            }

            // 刷新所有槽位显示
            RefreshAllSlots();

            // 默认选中第一个
            OnSelectionChanged(0);
        }

        // ==================== 事件处理 ====================

        /// <summary>槽位数据变化 — 刷新显示</summary>
        private void OnSlotChanged(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length) return;

            var item = ItemManager.Instance?.GetSlot(slotIndex);
            slots[slotIndex].UpdateDisplay(item);
        }

        /// <summary>选中槽位变化 — 更新高亮</summary>
        private void OnSelectionChanged(int slotIndex)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].SetSelected(i == slotIndex);
            }

            // 显示选中道具名称
            var item = ItemManager.Instance?.GetSlot(slotIndex);
            if (itemNameText != null)
            {
                if (item != null && !item.IsEmpty)
                    itemNameText.text = item.data.itemName;
                else
                    itemNameText.text = "";
            }
        }

        // ==================== 提示特效 ====================

        /// <summary>拾取道具提示</summary>
        private void OnItemPickedUp(ItemEventArgs args)
        {
            ShowMessage($"获得 {args.Item?.data?.itemName} ×{args.Item?.quantity}");
            PlaySlotAnimation(args.SlotIndex, "Pickup");
        }

        /// <summary>使用道具提示</summary>
        private void OnItemUsed(ItemEventArgs args)
        {
            ShowMessage($"使用 {args.Item?.data?.itemName}");
            PlaySlotAnimation(args.SlotIndex, "Use");
        }

        /// <summary>效果生效提示 — 策划案怪谈风格</summary>
        private void OnItemEffectApplied(ItemEventArgs args)
        {
            string effectMsg = args.Item?.data?.effectType switch
            {
                ItemEffectType.Stun => "目标陷入停滞",
                ItemEffectType.Paralyze => "目标已被束缚",
                ItemEffectType.Lure => "声响已引出注意",
                ItemEffectType.Damage => $"造成 {args.EffectValue} 点伤害",
                ItemEffectType.Trap => "陷阱已触发",
                ItemEffectType.Stealth => "身影已隐入暗处",
                _ => "效果已生效"
            };
            ShowMessage(effectMsg);
        }

        /// <summary>显示提示信息（带淡出）</summary>
        private void ShowMessage(string text)
        {
            if (messageText == null) return;

            if (_messageCoroutine != null)
                StopCoroutine(_messageCoroutine);

            messageText.text = text;
            messageText.gameObject.SetActive(true);
            _messageCoroutine = StartCoroutine(FadeMessage());
        }

        private IEnumerator FadeMessage()
        {
            // 显示
            var color = messageText.color;
            color.a = 1f;
            messageText.color = color;

            yield return new WaitForSeconds(messageDuration * 0.7f);

            // 淡出
            float fadeTime = messageDuration * 0.3f;
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                color.a = 1f - (elapsed / fadeTime);
                messageText.color = color;
                yield return null;
            }

            messageText.gameObject.SetActive(false);
        }

        private void PlaySlotAnimation(int slotIndex, string trigger)
        {
            if (barAnimator != null)
                barAnimator.SetTrigger(trigger);
        }

        // ==================== 公开方法 ====================

        /// <summary>刷新所有槽位显示</summary>
        public void RefreshAllSlots()
        {
            if (ItemManager.Instance == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                var item = ItemManager.Instance.GetSlot(i);
                slots[i].UpdateDisplay(item);
            }
        }
    }
}
