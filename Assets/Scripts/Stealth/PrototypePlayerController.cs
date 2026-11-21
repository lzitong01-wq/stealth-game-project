using UnityEngine;
using UnityEngine.Events;

namespace StealthPrototype
{
    /// <summary>
    /// 临时玩家。只负责在测试场景里移动、蹲伏和接收受击。
    /// 敌人只通过 IStealthTarget 读取位置和检测点，不依赖本类。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Stealth/Prototype Player")]
    public class PrototypePlayerController : MonoBehaviour, IStealthTarget
    {
        [Header("移动")]
        public float standSpeed = 5f;
        public float crouchSpeed = 2.5f;
        public float gravity = -24f;
        public Camera viewCamera;

        [Header("体型")]
        public float standHeight = 1.8f;
        public float crouchHeight = 0.95f;
        public float bodyRadius = 0.32f;

        [Header("视线检测点（离地高度）")]
        public float standVisionHeight = 1.5f;
        public float crouchVisionHeight = 0.7f;

        [Header("站起时头顶检测")]
        public LayerMask obstacleMask;

        [Header("受击")]
        [Tooltip("累计受击达到这个次数后死亡。0 表示只播放受击，不进入死亡。")]
        public int hitsToDie;
        public UnityEvent onHit;

        public Transform body;
        public Transform visionMarker;

        public bool IsCrouching => crouching;
        public bool IsDead => dead;
        public bool HeadBlocked => Time.time < headBlockedUntil;
        public Vector3 WorldPosition => transform.position;
        public Vector3 VisionPoint => transform.position + Vector3.up * (crouching ? crouchVisionHeight : standVisionHeight);
        public bool IsTargetable => !dead && isActiveAndEnabled && gameObject.activeInHierarchy;

        CharacterController controller;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        float verticalVelocity;
        float headBlockedUntil;
        bool crouching;
        bool spawnSaved;
        bool dead;
        int hitsTaken;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (obstacleMask.value == 0)
                obstacleMask = LayerMask.GetMask(StealthLayers.Obstacle);
        }

        void OnEnable()
        {
            StealthTargetRegistry.Register(this);
        }

        void OnDisable()
        {
            StealthTargetRegistry.Unregister(this);
        }

        void Start()
        {
            if (viewCamera == null)
                viewCamera = Camera.main;
            CaptureSpawn();
            ApplyStance();
            StealthNameplate.Ensure(transform, "玩家", new Vector3(0f, 2.15f, 0f), new Color(0.45f, 0.75f, 1f));
        }

        void Update()
        {
            if (dead || controller == null || !controller.enabled)
                return;

            ReadCrouch();
            Vector3 planar = ReadPlanarMove();

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            controller.Move((planar * CurrentSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (planar.sqrMagnitude > 0.0025f)
            {
                Quaternion look = Quaternion.LookRotation(planar, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 14f * Time.deltaTime);
            }

            if (visionMarker != null)
                visionMarker.position = VisionPoint;
        }

        float CurrentSpeed => crouching ? crouchSpeed : standSpeed;

        void ReadCrouch()
        {
            if (!Input.GetKeyDown(KeyCode.LeftControl))
                return;

            if (crouching)
            {
                if (!HasHeadroom())
                {
                    headBlockedUntil = Time.time + 1.2f;
                    return;
                }

                crouching = false;
            }
            else
            {
                crouching = true;
            }

            ApplyStance();
        }

        Vector3 ReadPlanarMove()
        {
            float x = 0f;
            float y = 0f;
            if (Input.GetKey(KeyCode.D)) x += 1f;
            if (Input.GetKey(KeyCode.A)) x -= 1f;
            if (Input.GetKey(KeyCode.W)) y += 1f;
            if (Input.GetKey(KeyCode.S)) y -= 1f;

            Vector2 input = new Vector2(x, y);
            if (input.sqrMagnitude > 1f)
                input.Normalize();
            if (input.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            Vector3 screenRight = Vector3.right;
            Vector3 screenUp = Vector3.forward;
            if (viewCamera != null)
            {
                screenRight = viewCamera.transform.right;
                screenRight.y = 0f;
                screenUp = viewCamera.transform.forward;
                screenUp.y = 0f;
            }

            if (screenRight.sqrMagnitude < 0.0001f)
                screenRight = Vector3.right;
            if (screenUp.sqrMagnitude < 0.0001f)
                screenUp = Vector3.forward;

            screenRight.Normalize();
            screenUp.Normalize();
            return screenRight * input.x + screenUp * input.y;
        }

        public void ApplyStance()
        {
            if (controller == null)
                controller = GetComponent<CharacterController>();

            float height = crouching ? crouchHeight : standHeight;
            float minHeight = bodyRadius * 2f + 0.02f;
            if (height < minHeight)
                height = minHeight;

            controller.radius = bodyRadius;
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
            controller.stepOffset = 0.2f;
            controller.slopeLimit = 50f;

            if (body != null)
            {
                float meshRadius = 0.5f;
                float meshHeight = 2f;
                body.localScale = new Vector3(bodyRadius / meshRadius, height / meshHeight, bodyRadius / meshRadius);
                body.localPosition = new Vector3(0f, height * 0.5f, 0f);
            }

            if (visionMarker != null)
                visionMarker.position = VisionPoint;
        }

        public bool HasHeadroom()
        {
            float radius = bodyRadius * 0.9f;
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (standHeight - radius);
            if (top.y <= bottom.y + 0.01f)
                return true;

            return !Physics.CheckCapsule(bottom, top, radius, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        public void ReceiveHit(StealthHitInfo hit)
        {
            if (dead)
                return;

            hitsTaken++;
            if (hitsToDie > 0 && hitsTaken >= hitsToDie)
            {
                Die(hit);
                return;
            }

            onHit?.Invoke();
            StealthSignals.RaisePlayerHit(hit);
        }

        void Die(StealthHitInfo hit)
        {
            dead = true;
            crouching = false;
            verticalVelocity = 0f;
            if (controller != null)
                controller.enabled = false;

            StealthSignals.RaisePlayerHit(hit);
            if (hit.source is EnemyStealthController enemy)
                enemy.ReleaseDeadTarget();
        }

        public void CaptureSpawn()
        {
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            spawnSaved = true;
        }

        public void ResetState()
        {
            if (!spawnSaved)
                CaptureSpawn();

            dead = false;
            hitsTaken = 0;
            crouching = false;
            verticalVelocity = 0f;
            headBlockedUntil = 0f;
            ApplyStance();
            controller.enabled = false;
            transform.position = spawnPosition;
            transform.rotation = spawnRotation;
            controller.enabled = true;
            if (visionMarker != null)
                visionMarker.position = VisionPoint;
        }

        public void Teleport(Vector3 feetPosition, bool crouch)
        {
            crouching = crouch;
            verticalVelocity = 0f;
            ApplyStance();
            controller.enabled = false;
            transform.position = feetPosition;
            controller.enabled = true;
            if (visionMarker != null)
                visionMarker.position = VisionPoint;
        }

        void OnDrawGizmos()
        {
            Vector3 point = Application.isPlaying
                ? VisionPoint
                : transform.position + Vector3.up * standVisionHeight;
            Gizmos.color = crouching ? new Color(0.2f, 0.9f, 0.85f, 0.95f) : new Color(0.35f, 0.75f, 1f, 0.95f);
            Gizmos.DrawWireSphere(point, 0.12f);
            Gizmos.DrawLine(transform.position, point);
        }
    }
}
