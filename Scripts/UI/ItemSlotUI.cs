// ============================================================
// ItemSlotUI.cs — 单个道具槽位 UI
// 策划案 UI：道具图标 + 数量角标 + 选中态
// ============================================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ItemSystem
{
    /// <summary>
    /// 单个道具槽位 UI 组件
    /// 显示：槽位编号、道具图标、数量角标、选中高亮
    /// </summary>
    public class ItemSlotUI : MonoBehaviour
    {
        [Header("UI 元素")]
        [Tooltip("槽位编号文本 (1/2/3)")]
        [SerializeField] private TextMeshProUGUI slotNumberText;

        [Tooltip("道具图标")]
        [SerializeField] private Image iconImage;

        [Tooltip("数量角标文本")]
        [SerializeField] private TextMeshProUGUI quantityText;

        [Tooltip("选中高亮边框")]
        [SerializeField] private GameObject selectedHighlight;

        [Tooltip("空槽位提示")]
        [SerializeField] private GameObject emptyOverlay;

        [Tooltip("冷却遮罩（可选）")]
        [SerializeField] private Image cooldownMask;

        [Header("视觉配置")]
        [SerializeField] private Color normalColor = new Color(0.2f, 0.2f, 0.3f, 0.8f);
        [SerializeField] private Color selectedColor = new Color(0.9f, 0.75f, 0.3f, 1f);
        [SerializeField] private Color emptyIconColor = new Color(1f, 1f, 1f, 0.2f);

        private int _slotIndex;
        private Image _backgroundImage;

        /// <summary>
        /// 初始化槽位
        /// </summary>
        public void Setup(int slotIndex)
        {
            _slotIndex = slotIndex;
            _backgroundImage = GetComponent<Image>();

            if (slotNumberText != null)
                slotNumberText.text = (slotIndex + 1).ToString();

            SetSelected(false);
            UpdateDisplay(null);
        }

        /// <summary>
        /// 更新道具显示
        /// </summary>
        public void UpdateDisplay(ItemInstance item)
        {
            bool hasItem = item != null && !item.IsEmpty;

            // 图标
            if (iconImage != null)
            {
                if (hasItem && item.data.icon != null)
                {
                    iconImage.sprite = item.data.icon;
                    iconImage.color = Color.white;
                    iconImage.enabled = true;
                }
                else
                {
                    iconImage.color = emptyIconColor;
                    iconImage.enabled = hasItem;
                }
            }

            // 数量角标
            if (quantityText != null)
            {
                if (hasItem && item.quantity > 0)
                {
                    quantityText.text = item.quantity.ToString();
                    quantityText.gameObject.SetActive(true);
                }
                else
                {
                    quantityText.gameObject.SetActive(false);
                }
            }

            // 空槽位提示
            if (emptyOverlay != null)
                emptyOverlay.SetActive(!hasItem);
        }

        /// <summary>
        /// 设置选中状态 — 策划案「道具选中态」
        /// </summary>
        public void SetSelected(bool selected)
        {
            if (selectedHighlight != null)
                selectedHighlight.SetActive(selected);

            if (_backgroundImage != null)
                _backgroundImage.color = selected ? selectedColor : normalColor;
        }

        /// <summary>
        /// 设置冷却进度 (0~1)
        /// </summary>
        public void SetCooldown(float progress)
        {
            if (cooldownMask != null)
            {
                cooldownMask.fillAmount = progress;
                cooldownMask.gameObject.SetActive(progress > 0f);
            }
        }
    }
}
