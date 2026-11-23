// ============================================================
// ItemEntity.cs — 道具实体（投掷物/布置陷阱）
// 策划案行为：「检测与敌人/场景的碰撞，播放对应动画」
// 状态流转：
//   投掷类：实体飞行中 → 实体碰撞后
//   布置类：已布置未使用 → 已布置已使用
// ============================================================
using UnityEngine;
using System.Collections;

namespace ItemSystem
{
    [RequireComponent(typeof(Collider))]
    public class ItemEntity : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private float autoDestroyTime = 10f;
        [SerializeField] private float effectLingerTime = 3f;

        private ItemData _data;
        private int _slotIndex;
        private bool _hasCollided = false;
        private EntityMode _mode = EntityMode.None;

        private Rigidbody _rigidbody;
        private Collider _collider;
        private Animator _animator;

        private enum EntityMode { None, Thrown, Placed }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            _animator = GetComponent<Animator>();
        }

        // ==================== 发射（投掷类） ====================

        /// <summary>投掷发射</summary>
        public void Launch(ItemData data, Vector3 velocity, int slotIndex)
        {
            _data = data;
            _slotIndex = slotIndex;
            _mode = EntityMode.Thrown;
            _hasCollided = false;

            if (_rigidbody == null)
                _rigidbody = gameObject.AddComponent<Rigidbody>();

            _rigidbody.useGravity = true;
            _rigidbody.linearVelocity = velocity;

            if (_collider != null)
                _collider.isTrigger = false;

            Destroy(gameObject, autoDestroyTime);
        }

        // ==================== 放置（布置类） ====================

        /// <summary>放置为陷阱</summary>
        public void PlaceAsTrap(ItemData data, int slotIndex)
        {
            _data = data;
            _slotIndex = slotIndex;
            _mode = EntityMode.Placed;
            _hasCollided = false;

            if (_rigidbody != null)
                _rigidbody.isKinematic = true;

            if (_collider != null)
                _collider.isTrigger = true;

            // 策划案：作用范围/判定体积
            SetupEffectVolume();
        }

        // ==================== 碰撞检测 ====================

        /// <summary>
        /// 物理碰撞 — 投掷物命中
        /// 策划案：「检测与敌人/场景的碰撞」
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (_hasCollided || _mode != EntityMode.Thrown) return;
            _hasCollided = true;

            Vector3 hitPoint = collision.contacts.Length > 0
                ? collision.contacts[0].point : transform.position;

            PlayHitAnimation();

            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
            }

            ApplyAreaEffect(hitPoint, collision.gameObject);

            if (ItemManager.Instance != null)
                ItemManager.Instance.NotifyEntityCollided(_slotIndex, hitPoint, collision.gameObject);

            StartCoroutine(DestroyAfterEffect());
        }

        /// <summary>
        /// 触发器检测 — 陷阱触发（仅对敌人生效）
        /// 策划案：布置类 已布置未使用 → 已布置已使用
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Enemy")) return;
            if (_hasCollided || _mode != EntityMode.Placed) return;
            _hasCollided = true;

            Vector3 hitPoint = other.ClosestPoint(transform.position);

            PlayHitAnimation();
            ApplyAreaEffect(hitPoint, other.gameObject);

            if (ItemManager.Instance != null)
                ItemManager.Instance.NotifyEntityCollided(_slotIndex, hitPoint, other.gameObject);

            StartCoroutine(DestroyAfterEffect());
        }

        // ==================== 效果应用 ====================

        /// <summary>
        /// 范围效果 — 策划案「作用范围/判定体积」
        /// 对范围内敌人施加效果（眩晕/麻痹/诱敌等）
        /// </summary>
        private void ApplyAreaEffect(Vector3 center, GameObject directHit)
        {
            if (_data == null) return;

            Collider[] hits = Physics.OverlapSphere(center, _data.effectRadius);
            foreach (var hit in hits)
            {
                if (!hit.CompareTag("Enemy")) continue;

                var receiver = hit.GetComponent<IItemEffectReceiver>();
                if (receiver != null)
                    receiver.ReceiveEffect(_data.effectType, _data.effectValue,
                        _data.effectDuration, center);
            }

            if (_data.effectVfxPrefab != null)
            {
                var vfx = Instantiate(_data.effectVfxPrefab, center, Quaternion.identity);
                Destroy(vfx, _data.effectDuration);
            }

            // 声音吸引 — 策划案：敌人「被声音吸引」
            if (_data.noiseRadius > 0f)
                EmitNoise(center, _data.noiseRadius);
        }

        /// <summary>发出噪音 — 吸引听觉型敌人（策划案敌人类型1）</summary>
        private void EmitNoise(Vector3 center, float radius)
        {
            Collider[] detected = Physics.OverlapSphere(center, radius);
            foreach (var col in detected)
            {
                if (!col.CompareTag("Enemy")) continue;
                var listener = col.GetComponent<INoiseListener>();
                if (listener != null)
                    listener.OnNoiseHeard(center, radius);
            }
        }

        private void SetupEffectVolume()
        {
            if (_data == null) return;
            var sc = GetComponent<SphereCollider>();
            if (sc == null) sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = _data.effectRadius;
        }

        /// <summary>播放碰撞动画 — 策划案「播放对应动画」</summary>
        private void PlayHitAnimation()
        {
            if (_animator != null && _data != null
                && !string.IsNullOrEmpty(_data.hitAnimTrigger))
                _animator.SetTrigger(_data.hitAnimTrigger);
        }

        private IEnumerator DestroyAfterEffect()
        {
            yield return new WaitForSeconds(effectLingerTime);
            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            if (_data != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, _data.effectRadius);
            }
        }
    }

    // ==================== 接口定义 ====================

    /// <summary>道具效果接收者接口 — 敌人实现此接口</summary>
    public interface IItemEffectReceiver
    {
        void ReceiveEffect(ItemEffectType effectType, float value,
            float duration, Vector3 source);
    }

    /// <summary>
    /// 噪音监听者接口 — 听觉型敌人实现此接口
    /// 策划案敌人类型1：「对声音极其敏感」
    /// </summary>
    public interface INoiseListener
    {
        void OnNoiseHeard(Vector3 source, float radius);
    }
}
