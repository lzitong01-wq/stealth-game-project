using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace StealthPrototype
{
    public enum EnemyAwareness
    {
        Patrol,
        Suspicious,
        Chase,
        Attack
    }

    /// <summary>
    /// 巡逻、视野、怀疑、追击和攻击。发现与受击都通过契约和事件发出，不直接操作玩家血量。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    [AddComponentMenu("Stealth/Enemy Controller")]
    public class EnemyStealthController : MonoBehaviour
    {
        [Header("目标")]
        [Tooltip("拖入实现了 IStealthTarget 的组件。留空则使用当前注册的目标。")]
        public MonoBehaviour targetComponent;

        [Header("视野")]
        public float viewDistance = 8f;
        public float viewAngle = 90f;
        public float immediateDistance = 2f;
        public float eyeHeight = 1.32f;
        public LayerMask occlusionMask;
        public Transform eyePoint;
        public Renderer bodyRenderer;

        [Header("时间")]
        public float confirmDelay = 3f;
        public float loseSightDuration = 10f;
        public float waypointPause = 1.1f;

        [Header("移动")]
        public float patrolSpeed = 2.1f;
        public float chaseSpeed = 3.5f;
        public float arriveThreshold = 0.35f;
        public float chaseStopDistance = 1.15f;
        public float turnSpeed = 720f;

        [Header("攻击")]
        public float attackRange = 1.8f;
        public float attackWindup = 0.6f;
        public float attackCooldown = 1.25f;

        [Header("巡逻")]
        public PatrolWaypoint[] waypoints;

        [Header("事件")]
        public UnityEvent onPlayerConfirmed;
        public event System.Action<StealthConfirmInfo> PlayerConfirmed;

        public EnemyAwareness State { get; private set; }
        public VisionResult LastVision { get; private set; }
        public VisionBlockReason LastAttackRecheck { get; private set; }
        public float VisibleTimer { get; private set; }
        public float LostTimer { get; private set; }
        public int ConfirmCount { get; private set; }
        public bool HasLastSeen { get; private set; }
        public Vector3 LastSeenPosition { get; private set; }
        public bool IsReady { get; private set; }
        public bool FreezeMotion { get; set; }
        public bool IsAttackWindup => attackPhase == AttackPhase.Windup;
        public float AttackWindup01 => attackPhase == AttackPhase.Windup
            ? Mathf.Clamp01(attackTimer / Mathf.Max(0.05f, attackWindup))
            : 0f;

        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case EnemyAwareness.Patrol: return "巡逻";
                    case EnemyAwareness.Suspicious: return "怀疑";
                    case EnemyAwareness.Chase:
                        return LastVision.reason == VisionBlockReason.Visible
                            ? "追击"
                            : "追击（前往最后可见位置）";
                    case EnemyAwareness.Attack:
                        return attackPhase == AttackPhase.Windup ? "攻击（前摇）" : "攻击（冷却）";
                    default: return State.ToString();
                }
            }
        }

        public Vector3 EyePosition
        {
            get
            {
                if (eyePoint != null)
                    return eyePoint.position;
                return transform.position + Vector3.up * eyeHeight + transform.forward * 0.15f;
            }
        }

        enum AttackPhase
        {
            None,
            Windup,
            Cooldown
        }

        NavMeshAgent agent;
        IStealthTarget target;
        MaterialPropertyBlock propertyBlock;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        int waypointIndex;
        float holdTimer;
        bool holding;
        bool spawnSaved;
        AttackPhase attackPhase;
        float attackTimer;

        static readonly Color PatrolColor = new Color(0.85f, 0.16f, 0.14f);
        static readonly Color SuspiciousColor = new Color(0.95f, 0.82f, 0.2f);
        static readonly Color ChaseColor = new Color(1f, 0.45f, 0.12f);
        static readonly Color AttackColor = new Color(1f, 0.92f, 0.92f);

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (bodyRenderer == null)
            {
                Transform body = transform.Find("Body");
                if (body != null)
                    bodyRenderer = body.GetComponent<Renderer>();
            }
            propertyBlock = new MaterialPropertyBlock();
            if (occlusionMask.value == 0)
                occlusionMask = LayerMask.GetMask(StealthLayers.Obstacle);
            FixVisualAnchors();
        }

        /// <summary>
        /// 生成场景时子物体曾被写成世界坐标，胶囊体会离导航根节点大约 6 米并随转向甩出围墙。
        /// 这里把身体、朝向标记和眼睛拉回根节点上的局部位置。
        /// </summary>
        public void FixVisualAnchors()
        {
            Transform bodyTransform = transform.Find("Body");
            if (bodyTransform != null)
                bodyTransform.localPosition = new Vector3(0f, 1f, 0f);

            Transform nose = transform.Find("Nose");
            if (nose != null)
                nose.localPosition = new Vector3(0f, 1.45f, 0.48f);

            Transform eye = transform.Find("Eye");
            if (eye != null)
            {
                eye.localPosition = new Vector3(0f, eyeHeight, 0.15f);
                Transform marker = eye.Find("EyeMarker");
                if (marker != null)
                    marker.localPosition = Vector3.zero;
            }

            Transform visual = transform.Find("Visual");
            if (visual != null)
            {
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
                visual.localScale = Vector3.one;
            }
        }

        void Start()
        {
            CaptureSpawn();
            ConfigureAgent();

            NavMeshDataHolder holder = FindObjectOfType<NavMeshDataHolder>();
            if (holder != null)
                holder.EnsureLoaded();

            agent.enabled = true;
            if (!agent.Warp(spawnPosition))
                Debug.LogError("敌人出生点不在导航网格上：" + spawnPosition, this);

            ResolveTarget();
            EnterPatrol();
            IsReady = true;
            StealthNameplate.Ensure(transform, "敌人", new Vector3(0f, 2.25f, 0f), new Color(1f, 0.45f, 0.4f));
            if (GetComponent<StealthGroundDebug>() == null)
                gameObject.AddComponent<StealthGroundDebug>();
        }

        void Update()
        {
            if (!IsReady)
                return;

            if (target == null)
                ResolveTarget();

            if (FreezeMotion && agent.enabled)
                agent.updateRotation = false;

            switch (State)
            {
                case EnemyAwareness.Patrol:
                    UpdatePatrol();
                    break;
                case EnemyAwareness.Suspicious:
                    UpdateSuspicious();
                    break;
                case EnemyAwareness.Chase:
                    UpdateChase();
                    break;
                case EnemyAwareness.Attack:
                    UpdateAttack();
                    break;
            }

            if (FreezeMotion && agent.enabled)
            {
                agent.updateRotation = false;
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                agent.nextPosition = transform.position;
                agent.ResetPath();
            }

            TintBody();
        }

        public void ConfigureAgent()
        {
            agent.radius = 0.35f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.acceleration = 28f;
            agent.angularSpeed = turnSpeed;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.stoppingDistance = 0.2f;
        }

        void ResolveTarget()
        {
            if (targetComponent is IStealthTarget assigned)
            {
                target = assigned;
                return;
            }

            target = StealthTargetRegistry.Current;
        }

        void UpdatePatrol()
        {
            if (!FreezeMotion)
                TickPatrolMovement();

            SampleVision();
            if (LastVision.reason != VisionBlockReason.Visible)
                return;

            if (LastVision.flatDistance <= immediateDistance)
                EnterConfirmed();
            else
                EnterSuspicious();
        }

        void TickPatrolMovement()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                agent.isStopped = true;
                return;
            }

            agent.updateRotation = true;
            agent.speed = patrolSpeed;
            agent.stoppingDistance = 0.15f;

            if (holding)
            {
                holdTimer += Time.deltaTime;
                agent.isStopped = true;
                if (holdTimer >= CurrentPause())
                {
                    holding = false;
                    waypointIndex = (waypointIndex + 1) % waypoints.Length;
                    GoToCurrent();
                }

                return;
            }

            if (HasArrived())
            {
                holding = true;
                holdTimer = 0f;
                agent.isStopped = true;
                return;
            }

            agent.isStopped = false;
        }

        void UpdateSuspicious()
        {
            FaceTarget();
            SampleVision();
            StopAgent();

            if (LastVision.reason == VisionBlockReason.Visible)
            {
                LostTimer = 0f;
                VisibleTimer += Time.deltaTime;
                RememberSeen();
                if (LastVision.flatDistance <= immediateDistance || VisibleTimer >= confirmDelay)
                    EnterConfirmed();
            }
            else
            {
                VisibleTimer = 0f;
                LostTimer += Time.deltaTime;
                if (LostTimer >= loseSightDuration)
                    EnterPatrol();
            }
        }

        void UpdateChase()
        {
            SampleVision();
            if (!FreezeMotion)
            {
                agent.updateRotation = true;
                agent.speed = chaseSpeed;
                agent.stoppingDistance = chaseStopDistance;
            }

            if (LastVision.reason == VisionBlockReason.Visible)
            {
                LostTimer = 0f;
                RememberSeen();
                if (!FreezeMotion)
                {
                    agent.isStopped = false;
                    agent.SetDestination(SnapToNavMesh(LastSeenPosition));
                }

                if (LastVision.flatDistance <= attackRange)
                    EnterAttack();
            }
            else
            {
                LostTimer += Time.deltaTime;
                if (LostTimer >= loseSightDuration)
                {
                    EnterPatrol();
                    return;
                }

                if (!HasLastSeen || FreezeMotion)
                    return;

                Vector3 destination = SnapToNavMesh(LastSeenPosition);
                if (FlatDistance(transform.position, destination) <= arriveThreshold)
                {
                    agent.isStopped = true;
                }
                else
                {
                    agent.isStopped = false;
                    agent.SetDestination(destination);
                }
            }
        }

        void UpdateAttack()
        {
            FaceTarget();
            SampleVision();
            StopAgent();

            if (LastVision.reason == VisionBlockReason.Visible)
            {
                LostTimer = 0f;
                RememberSeen();
            }
            else
            {
                LostTimer += Time.deltaTime;
            }

            if (attackPhase == AttackPhase.Windup)
            {
                attackTimer += Time.deltaTime;
                if (attackTimer < attackWindup)
                    return;

                LastAttackRecheck = LastVision.reason;
                bool canHit = LastVision.reason == VisionBlockReason.Visible && LastVision.flatDistance <= attackRange;
                attackPhase = AttackPhase.Cooldown;
                attackTimer = attackCooldown;
                if (canHit && target != null)
                {
                    IStealthTarget struck = target;
                    struck.ReceiveHit(new StealthHitInfo
                    {
                        source = this,
                        sourcePosition = EyePosition,
                        time = Time.time
                    });
                    if (target == null || !struck.IsTargetable)
                        return;
                }
            }
            else if (attackPhase == AttackPhase.Cooldown)
            {
                attackTimer -= Time.deltaTime;
            }

            if (LostTimer >= loseSightDuration)
            {
                EnterPatrol();
                return;
            }

            if (LastVision.reason != VisionBlockReason.Visible)
            {
                EnterChase();
                return;
            }

            if (LastVision.flatDistance > attackRange)
            {
                EnterChase();
                return;
            }

            if (attackPhase == AttackPhase.Cooldown && attackTimer <= 0f)
                EnterAttack();
        }

        void EnterSuspicious()
        {
            State = EnemyAwareness.Suspicious;
            VisibleTimer = 0f;
            LostTimer = 0f;
            RememberSeen();
            StopAgent();
        }

        void EnterConfirmed()
        {
            ConfirmCount++;
            RememberSeen();
            LostTimer = 0f;

            var info = new StealthConfirmInfo
            {
                source = this,
                target = target,
                enemyPosition = transform.position,
                targetPosition = target != null ? target.WorldPosition : LastSeenPosition,
                time = Time.time
            };

            onPlayerConfirmed?.Invoke();
            PlayerConfirmed?.Invoke(info);
            StealthSignals.RaisePlayerConfirmed(info);

            if (LastVision.flatDistance <= attackRange)
                EnterAttack();
            else
                EnterChase();
        }

        void EnterChase()
        {
            State = EnemyAwareness.Chase;
            attackPhase = AttackPhase.None;
            if (FreezeMotion)
                return;

            agent.isStopped = false;
            agent.updateRotation = true;
            agent.speed = chaseSpeed;
            agent.stoppingDistance = chaseStopDistance;
        }

        void EnterAttack()
        {
            State = EnemyAwareness.Attack;
            attackPhase = AttackPhase.Windup;
            attackTimer = 0f;
            StopAgent();
        }

        void EnterPatrol()
        {
            State = EnemyAwareness.Patrol;
            VisibleTimer = 0f;
            LostTimer = 0f;
            holding = false;
            holdTimer = 0f;
            attackPhase = AttackPhase.None;
            if (FreezeMotion)
                return;

            agent.updateRotation = true;
            agent.speed = patrolSpeed;
            agent.stoppingDistance = 0.15f;
            agent.ResetPath();
            GoToCurrent();
        }

        void GoToCurrent()
        {
            if (waypoints == null || waypoints.Length == 0)
                return;

            agent.isStopped = false;
            agent.SetDestination(SnapToNavMesh(waypoints[waypointIndex].transform.position));
        }

        float CurrentPause()
        {
            if (waypoints == null || waypointIndex < 0 || waypointIndex >= waypoints.Length || waypoints[waypointIndex] == null)
                return waypointPause;
            return Mathf.Max(0f, waypoints[waypointIndex].pauseSeconds);
        }

        bool HasArrived()
        {
            if (!agent.enabled || agent.pathPending)
                return false;
            if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
                return false;
            if (float.IsInfinity(agent.remainingDistance))
                return false;
            return agent.remainingDistance <= Mathf.Max(arriveThreshold, agent.stoppingDistance + 0.05f);
        }

        void SampleVision()
        {
            if (target == null)
            {
                LastVision = VisionResult.None(VisionBlockReason.NoTarget);
                return;
            }

            if (!target.IsTargetable)
            {
                LastVision = VisionResult.None(VisionBlockReason.NotTargetable);
                return;
            }

            LastVision = StealthVision.Evaluate(new VisionQuery
            {
                eyePosition = EyePosition,
                forward = transform.forward,
                selfFlatPosition = transform.position,
                targetFlatPosition = target.WorldPosition,
                targetPoint = target.VisionPoint,
                maxDistance = viewDistance,
                fullViewAngle = viewAngle,
                occlusionMask = occlusionMask
            });
        }

        void RememberSeen()
        {
            if (target == null)
                return;
            LastSeenPosition = target.WorldPosition;
            HasLastSeen = true;
        }

        void FaceTarget()
        {
            if (target == null)
                return;

            Vector3 look = target.WorldPosition - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f)
                return;

            Quaternion desired = Quaternion.LookRotation(look.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * Time.deltaTime);
            if (agent.enabled)
                agent.updateRotation = false;
        }

        void StopAgent()
        {
            if (!agent.enabled)
                return;
            agent.isStopped = true;
            agent.updateRotation = false;
            agent.velocity = Vector3.zero;
        }

        Vector3 SnapToNavMesh(Vector3 world)
        {
            if (NavMesh.SamplePosition(world, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                return hit.position;
            return world;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void TintBody()
        {
            if (bodyRenderer == null)
                return;
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            Color color = PatrolColor;
            switch (State)
            {
                case EnemyAwareness.Suspicious: color = SuspiciousColor; break;
                case EnemyAwareness.Chase: color = ChaseColor; break;
                case EnemyAwareness.Attack: color = AttackColor; break;
            }

            propertyBlock.SetColor("_Color", color);
            bodyRenderer.SetPropertyBlock(propertyBlock);
        }

        public void CaptureSpawn()
        {
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            spawnSaved = true;
        }

        /// <summary>
        /// 玩家死亡时取消还没打出去的一下，并立刻走回巡逻，不再追这个目标。
        /// </summary>
        public void ReleaseDeadTarget()
        {
            target = null;
            HasLastSeen = false;
            attackPhase = AttackPhase.None;
            attackTimer = 0f;
            EnterPatrol();
        }

        public void ResetState()
        {
            if (!spawnSaved)
                CaptureSpawn();

            FreezeMotion = false;
            VisibleTimer = 0f;
            LostTimer = 0f;
            ConfirmCount = 0;
            HasLastSeen = false;
            waypointIndex = 0;
            holding = false;
            holdTimer = 0f;
            attackPhase = AttackPhase.None;
            attackTimer = 0f;
            LastAttackRecheck = VisionBlockReason.NoTarget;
            WarpTo(spawnPosition, spawnRotation * Vector3.forward);
            EnterPatrol();
        }

        public void WarpTo(Vector3 feetPosition, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            if (!agent.enabled)
                agent.enabled = true;

            Vector3 snapped = SnapToNavMesh(feetPosition);
            if (!agent.Warp(snapped))
            {
                agent.enabled = false;
                transform.position = snapped;
                agent.enabled = true;
                agent.Warp(snapped);
            }

            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            agent.ResetPath();
            agent.updateRotation = !FreezeMotion && State == EnemyAwareness.Patrol;
        }

        void OnDrawGizmos()
        {
            Vector3 origin = transform.position + Vector3.up * 0.08f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            DrawSector(origin, forward, viewAngle, viewDistance, new Color(1f, 0.85f, 0.15f, 0.9f));
            DrawSector(origin, forward, viewAngle, immediateDistance, new Color(0.2f, 0.95f, 1f, 0.95f));
            Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.35f);
            Gizmos.DrawLine(origin, origin + forward * 0.9f);

            if (waypoints != null)
            {
                Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.85f);
                for (int i = 0; i < waypoints.Length; i++)
                {
                    if (waypoints[i] == null)
                        continue;
                    Vector3 point = waypoints[i].transform.position + Vector3.up * 0.12f;
                    Gizmos.DrawWireSphere(point, 0.22f);
                    PatrolWaypoint next = waypoints[(i + 1) % waypoints.Length];
                    if (next != null)
                        Gizmos.DrawLine(point, next.transform.position + Vector3.up * 0.12f);
                }
            }

            if (HasLastSeen)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.9f);
                Vector3 seen = LastSeenPosition + Vector3.up * 0.1f;
                Gizmos.DrawLine(seen + Vector3.left * 0.25f, seen + Vector3.right * 0.25f);
                Gizmos.DrawLine(seen + Vector3.forward * 0.25f, seen + Vector3.back * 0.25f);
            }

            IStealthTarget drawnTarget = target;
            if (drawnTarget == null && targetComponent is IStealthTarget assigned)
                drawnTarget = assigned;
            if (drawnTarget == null)
                return;

            VisionResult vision = Application.isPlaying
                ? LastVision
                : StealthVision.Evaluate(new VisionQuery
                {
                    eyePosition = EyePosition,
                    forward = transform.forward,
                    selfFlatPosition = transform.position,
                    targetFlatPosition = drawnTarget.WorldPosition,
                    targetPoint = drawnTarget.VisionPoint,
                    maxDistance = viewDistance,
                    fullViewAngle = viewAngle,
                    occlusionMask = occlusionMask.value != 0 ? occlusionMask : LayerMask.GetMask(StealthLayers.Obstacle)
                });

            Gizmos.color = ColorFor(vision.reason);
            Vector3 rayEnd = vision.rayEnd.sqrMagnitude > 0.0001f ? vision.rayEnd : drawnTarget.VisionPoint;
            Gizmos.DrawLine(EyePosition, rayEnd);
            Gizmos.DrawWireSphere(EyePosition, 0.08f);
        }

        static void DrawSector(Vector3 origin, Vector3 forward, float fullAngle, float radius, Color color)
        {
            Gizmos.color = color;
            const int steps = 24;
            Vector3 previous = origin + Quaternion.AngleAxis(-fullAngle * 0.5f, Vector3.up) * forward * radius;
            Gizmos.DrawLine(origin, previous);
            for (int i = 1; i <= steps; i++)
            {
                float yaw = Mathf.Lerp(-fullAngle * 0.5f, fullAngle * 0.5f, i / (float)steps);
                Vector3 next = origin + Quaternion.AngleAxis(yaw, Vector3.up) * forward * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            Gizmos.DrawLine(origin, previous);
        }

        static Color ColorFor(VisionBlockReason reason)
        {
            switch (reason)
            {
                case VisionBlockReason.Visible: return new Color(0.2f, 1f, 0.35f, 0.95f);
                case VisionBlockReason.Occluded: return new Color(1f, 0.2f, 0.2f, 0.95f);
                case VisionBlockReason.OutOfAngle: return new Color(1f, 0.9f, 0.2f, 0.95f);
                default: return new Color(0.65f, 0.65f, 0.65f, 0.8f);
            }
        }
    }
}
