// ============================================================
// ItemPickup.cs — 世界中可拾取的道具
// 玩家靠近后按 E 键拾取到背包
// ============================================================
using UnityEngine;

namespace ItemSystem
{
    public class ItemPickup : MonoBehaviour
    {
        [Header("道具配置")]
        [SerializeField] private ItemData itemData;
        [SerializeField] private int quantity = 1;

        [Header("交互")]
        [SerializeField] private float interactDistance = 2f;
        [SerializeField] private KeyCode pickupKey = KeyCode.E;
        [SerializeField] private bool autoPickup = false;

        [Header("视觉")]
        [SerializeField] private GameObject promptUI;
        [SerializeField] private float rotateSpeed = 50f;
        [SerializeField] private float bobAmplitude = 0.15f;
        [SerializeField] private float bobFrequency = 1.5f;

        private Transform _playerTransform;
        private Vector3 _startPos;
        private bool _isInRange = false;
        private bool _isPickedUp = false;

        private void Start()
        {
            _startPos = transform.position;
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                _playerTransform = player.transform;
            if (promptUI != null)
                promptUI.SetActive(false);
        }

        private void Update()
        {
            if (_isPickedUp) return;

            // 悬浮动画
            transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime);
            float yOffset = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            transform.position = _startPos + new Vector3(0, yOffset, 0);

            if (_playerTransform == null) return;

            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            bool wasInRange = _isInRange;
            _isInRange = dist <= interactDistance;

            if (_isInRange && !wasInRange && promptUI != null)
                promptUI.SetActive(true);
            else if (!_isInRange && wasInRange && promptUI != null)
                promptUI.SetActive(false);

            if (_isInRange && (Input.GetKeyDown(pickupKey) || autoPickup))
                TryPickup();
        }

        private void TryPickup()
        {
            if (ItemManager.Instance == null) return;
            if (ItemManager.Instance.PickupItem(itemData, quantity))
            {
                _isPickedUp = true;
                if (promptUI != null) promptUI.SetActive(false);
                Destroy(gameObject, 0.1f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, interactDistance);
        }
    }
}
