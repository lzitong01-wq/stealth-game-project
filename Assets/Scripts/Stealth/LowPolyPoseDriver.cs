using UnityEngine;
using UnityEngine.AI;

namespace StealthPrototype
{
    /// <summary>
    /// 只转动 Visual 下面的关节。根节点、碰撞体、导航和视线点保持不动，
    /// 因此脸、地面箭头和视野扇形继续共用角色根节点的朝向。
    /// </summary>
    [DisallowMultipleComponent]
    public class LowPolyPoseDriver : MonoBehaviour
    {
        public enum Role
        {
            Player,
            Enemy
        }

        public Role role;

        Transform poseRoot;
        Transform torso;
        Transform head;
        Transform shoulderL;
        Transform elbowL;
        Transform shoulderR;
        Transform elbowR;
        Transform hipL;
        Transform kneeL;
        Transform ankleL;
        Transform hipR;
        Transform kneeR;
        Transform ankleR;

        PrototypePlayerController player;
        EnemyStealthController enemy;
        CharacterController body;
        NavMeshAgent agent;
        Renderer[] visualRenderers;
        Renderer nameplate;
        TextMesh question;
        Transform questionTransform;

        float hitUntil;
        bool wasWindingUp;
        float swingUntil;
        float cycle;
        float crouchBlend;
        float armLift;
        float brow;
        float suspicionStart = -10f;
        bool wasSuspicious;
        bool wasDead;
        float fallStart;
        bool modelHidden;

        const float StandHipHeight = 0.88f;
        const float CrouchHipHeight = 0.46f;
        const float CrouchHipBend = -78f;
        const float CrouchKneeBend = 122f;

        void Awake()
        {
            poseRoot = transform.Find("PoseRoot");
            torso = FindJoint("TorsoPivot");
            head = FindJoint("HeadPivot");
            shoulderL = FindJoint("ShoulderL");
            elbowL = FindJoint("ElbowL");
            shoulderR = FindJoint("ShoulderR");
            elbowR = FindJoint("ElbowR");
            hipL = FindJoint("HipL");
            kneeL = FindJoint("KneeL");
            ankleL = FindJoint("AnkleL");
            hipR = FindJoint("HipR");
            kneeR = FindJoint("KneeR");
            ankleR = FindJoint("AnkleR");
            if (role == Role.Player)
                ThickenPlayerLegs();

            player = GetComponentInParent<PrototypePlayerController>();
            enemy = GetComponentInParent<EnemyStealthController>();
            body = GetComponentInParent<CharacterController>();
            agent = GetComponentInParent<NavMeshAgent>();
            visualRenderers = GetComponentsInChildren<Renderer>(true);
            if (player != null)
            {
                player.onHit.AddListener(OnPlayerHit);
                Transform plate = player.transform.Find("Nameplate");
                if (plate != null)
                    nameplate = plate.GetComponent<Renderer>();
            }

            if (role == Role.Enemy)
                CreateQuestionMark();
        }

        void OnDestroy()
        {
            if (player != null)
                player.onHit.RemoveListener(OnPlayerHit);
        }

        void OnPlayerHit()
        {
            if (player != null && player.IsDead)
                return;
            hitUntil = Time.time + 0.45f;
        }

        void CreateQuestionMark()
        {
            var mark = new GameObject("QuestionMark");
            mark.transform.SetParent(transform, false);
            mark.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            mark.layer = 2;
            question = mark.AddComponent<TextMesh>();
            question.text = "？";
            question.font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Arial" },
                72);
            question.fontSize = 72;
            question.characterSize = 0.11f;
            question.anchor = TextAnchor.MiddleCenter;
            question.alignment = TextAlignment.Center;
            question.color = new Color(1f, 0.86f, 0.12f);
            question.font.RequestCharactersInTexture(question.text);
            MeshRenderer renderer = mark.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = question.font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            questionTransform = mark.transform;
            mark.SetActive(false);
        }

        Transform FindJoint(string jointName)
        {
            Transform[] joints = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i].name == jointName)
                    return joints[i];
            }

            return null;
        }

        void LateUpdate()
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            if (role == Role.Player)
                PosePlayer();
            else
                PoseEnemy();
        }

        void PosePlayer()
        {
            bool dead = player != null && player.IsDead;
            if (dead && !wasDead)
                fallStart = Time.time;
            wasDead = dead;

            if (dead)
            {
                PoseFall();
                return;
            }

            if (modelHidden)
                SetModelVisible(true);

            bool crouching = player != null && player.IsCrouching;
            float crouchSpeed = crouching ? 2.4f : 3.2f;
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouching ? 1f : 0f, Time.deltaTime * crouchSpeed);

            bool moving = body != null && FlatSpeed(body.velocity) > 0.2f;
            bool hit = Time.time < hitUntil;
            float stepRate = Mathf.Lerp(9f, 6.5f, crouchBlend);
            float strideAmp = moving ? Mathf.Lerp(30f, 12f, crouchBlend) : 0f;
            float armAmp = moving ? Mathf.Lerp(24f, 10f, crouchBlend) : 0f;
            if (moving)
                cycle += Time.deltaTime * stepRate;

            float stride = Mathf.Sin(cycle);
            float hip = Mathf.Lerp(0f, CrouchHipBend, crouchBlend);
            float knee = Mathf.Lerp(0f, CrouchKneeBend, crouchBlend);
            float hipY = Mathf.Lerp(StandHipHeight, CrouchHipHeight, crouchBlend);
            float splay = Mathf.Lerp(0f, 28f, crouchBlend);

            if (poseRoot != null)
                poseRoot.localPosition = new Vector3(0f, hipY, 0f);
            SetEuler(poseRoot, Vector3.zero);
            SetHipSpan(Mathf.Lerp(0.12f, 0.22f, crouchBlend));

            PoseLeg(hipL, kneeL, ankleL, hip + stride * strideAmp, knee + Mathf.Max(0f, -stride) * strideAmp * 0.85f, -splay);
            PoseLeg(hipR, kneeR, ankleR, hip - stride * strideAmp, knee + Mathf.Max(0f, stride) * strideAmp * 0.85f, splay);

            SetEuler(shoulderL, new Vector3(-stride * armAmp, 0f, Mathf.Lerp(0f, 16f, crouchBlend)));
            SetEuler(shoulderR, new Vector3(stride * armAmp, 0f, Mathf.Lerp(0f, -16f, crouchBlend)));
            SetEuler(elbowL, new Vector3(Mathf.Lerp(0f, -30f, crouchBlend), 0f, 0f));
            SetEuler(elbowR, new Vector3(Mathf.Lerp(0f, -30f, crouchBlend), 0f, 0f));
            SetEuler(torso, new Vector3(hit ? 20f : Mathf.Lerp(0f, 26f, crouchBlend), 0f, 0f));
            SetEuler(head, hit ? new Vector3(18f, 0f, 0f) : new Vector3(Mathf.Lerp(0f, 8f, crouchBlend), 0f, 0f));
        }

        void PoseFall()
        {
            float duration = 1.15f;
            float t = Mathf.Clamp01((Time.time - fallStart) / duration);
            float eased = 1f - (1f - t) * (1f - t);
            if (poseRoot != null)
                poseRoot.localPosition = new Vector3(0f, Mathf.Lerp(StandHipHeight, 0.2f, eased), Mathf.Lerp(0f, 0.35f, eased));

            SetEuler(poseRoot, new Vector3(Mathf.Lerp(0f, 82f, eased), 0f, Mathf.Lerp(0f, 48f, eased)));
            PoseLeg(hipL, kneeL, ankleL, Mathf.Lerp(0f, -18f, eased), Mathf.Lerp(0f, 24f, eased));
            PoseLeg(hipR, kneeR, ankleR, Mathf.Lerp(0f, 10f, eased), Mathf.Lerp(0f, 16f, eased));
            SetEuler(shoulderL, new Vector3(Mathf.Lerp(0f, -30f, eased), 0f, Mathf.Lerp(0f, 40f, eased)));
            SetEuler(shoulderR, new Vector3(Mathf.Lerp(0f, 20f, eased), 0f, Mathf.Lerp(0f, -55f, eased)));
            SetEuler(elbowL, new Vector3(Mathf.Lerp(0f, -20f, eased), 0f, 0f));
            SetEuler(elbowR, new Vector3(Mathf.Lerp(0f, -25f, eased), 0f, 0f));
            SetEuler(torso, new Vector3(Mathf.Lerp(0f, 12f, eased), 0f, 0f));
            SetEuler(head, new Vector3(Mathf.Lerp(0f, 24f, eased), 0f, 0f));

            if (t >= 1f)
                SetModelVisible(false);
        }

        void PoseEnemy()
        {
            bool winding = enemy != null && enemy.IsAttackWindup;
            bool attacking = enemy != null && enemy.State == EnemyAwareness.Attack;
            if (wasWindingUp && !winding && attacking)
                swingUntil = Time.time + 0.32f;
            wasWindingUp = winding;

            bool swinging = Time.time < swingUntil;
            bool chasing = enemy != null && enemy.State == EnemyAwareness.Chase;
            bool suspicious = enemy != null && enemy.State == EnemyAwareness.Suspicious;
            if (suspicious && !wasSuspicious)
                suspicionStart = Time.time;
            wasSuspicious = suspicious;

            float age = Time.time - suspicionStart;
            float speed = agent != null ? FlatSpeed(agent.velocity) : 0f;
            bool moving = speed > 0.15f && !suspicious && !winding && !swinging;
            if (moving)
                cycle += Time.deltaTime * (chasing ? 13f : 8f);

            float stride = Mathf.Sin(cycle);
            float leg = moving ? (chasing ? 40f : 28f) : 0f;
            float arm = moving ? (chasing ? 32f : 20f) : 0f;
            float lean = chasing && moving ? 12f : 0f;

            if (poseRoot != null)
                poseRoot.localPosition = new Vector3(0f, StandHipHeight, 0f);
            SetEuler(poseRoot, new Vector3(lean, 0f, 0f));
            PoseLeg(hipL, kneeL, ankleL, stride * leg, Mathf.Max(0f, -stride) * (chasing ? 36f : 24f));
            PoseLeg(hipR, kneeR, ankleR, -stride * leg, Mathf.Max(0f, stride) * (chasing ? 36f : 24f));

            float raise = winding && enemy != null ? Mathf.Lerp(0f, -105f, enemy.AttackWindup01) : 0f;
            if (swinging)
            {
                float swingT = 1f - (swingUntil - Time.time) / 0.32f;
                raise = Mathf.Lerp(-105f, 48f, Mathf.Clamp01(swingT));
            }

            if (winding || swinging)
                armLift = raise;
            else
                armLift = Mathf.MoveTowards(armLift, 0f, Time.deltaTime * 240f);

            SetEuler(shoulderR, new Vector3(stride * arm + armLift, 0f, (swinging || winding) ? -18f : 0f));
            SetEuler(elbowR, new Vector3(winding ? -28f : 0f, 0f, 0f));

            float handTarget = 0f;
            float yaw = 0f;
            float pitch = 0f;
            if (suspicious)
            {
                bool intro = age < 1.15f;
                float scan = intro ? 1f : 0.45f;
                yaw = Mathf.Sin(age * (intro ? 7.5f : 2.2f)) * (intro ? 58f : 22f);
                pitch = Mathf.Lerp(-42f, -14f, Mathf.Clamp01(age / 1.15f));
                float handIn = Mathf.Clamp01((age - 0.38f) / 0.32f);
                handTarget = handIn * scan;
            }

            brow = Mathf.MoveTowards(brow, handTarget, Time.deltaTime * 4.2f);
            SetEuler(shoulderL, new Vector3(-stride * arm - 48f * brow, 0f, -128f * brow));
            SetEuler(elbowL, new Vector3(-108f * brow, 0f, 18f * brow));
            SetEuler(torso, new Vector3(lean + (swinging ? 10f : 0f), suspicious ? yaw * 0.15f : 0f, 0f));
            SetEuler(head, new Vector3(pitch, yaw, 0f));
            UpdateQuestion(suspicious, age);
        }

        void UpdateQuestion(bool suspicious, float age)
        {
            if (questionTransform == null)
                return;

            bool show = suspicious && age < 1.5f;
            if (questionTransform.gameObject.activeSelf != show)
                questionTransform.gameObject.SetActive(show);
            if (!show)
                return;

            questionTransform.localPosition = new Vector3(0f, 2.45f, 0f);
            if (Camera.main != null)
                questionTransform.rotation = Camera.main.transform.rotation;
        }

        void SetHipSpan(float halfWidth)
        {
            if (hipL != null)
                hipL.localPosition = new Vector3(-halfWidth, 0f, 0f);
            if (hipR != null)
                hipR.localPosition = new Vector3(halfWidth, 0f, 0f);
        }

        void ThickenPlayerLegs()
        {
            SetPartScale(hipL, "ThighL", new Vector3(0.22f, 0.44f, 0.22f));
            SetPartScale(hipR, "ThighR", new Vector3(0.22f, 0.44f, 0.22f));
            SetPartScale(kneeL, "ShinL", new Vector3(0.20f, 0.36f, 0.20f));
            SetPartScale(kneeR, "ShinR", new Vector3(0.20f, 0.36f, 0.20f));
            SetPartScale(ankleL, "FootL", new Vector3(0.18f, 0.08f, 0.34f));
            SetPartScale(ankleR, "FootR", new Vector3(0.18f, 0.08f, 0.34f));
        }

        static void SetPartScale(Transform joint, string childName, Vector3 scale)
        {
            if (joint == null)
                return;
            Transform part = joint.Find(childName);
            if (part != null)
                part.localScale = scale;
        }

        static void PoseLeg(Transform hip, Transform knee, Transform ankle, float hipX, float kneeX, float yaw = 0f)
        {
            if (hip != null)
                hip.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(hipX, 0f, 0f);
            SetEuler(knee, new Vector3(kneeX, 0f, 0f));
            if (ankle != null)
                ankle.localRotation = Quaternion.Euler(-(hipX + kneeX), 0f, 0f) * Quaternion.Euler(0f, -yaw, 0f);
        }

        void SetModelVisible(bool visible)
        {
            if (modelHidden == !visible)
                return;

            modelHidden = !visible;
            if (nameplate == null && player != null)
            {
                Transform plate = player.transform.Find("Nameplate");
                if (plate != null)
                    nameplate = plate.GetComponent<Renderer>();
            }

            if (visualRenderers != null)
            {
                for (int i = 0; i < visualRenderers.Length; i++)
                {
                    if (visualRenderers[i] != null)
                        visualRenderers[i].enabled = visible;
                }
            }

            if (nameplate != null)
                nameplate.enabled = visible;
        }

        static void SetEuler(Transform joint, Vector3 euler)
        {
            if (joint != null)
                joint.localRotation = Quaternion.Euler(euler);
        }

        static float FlatSpeed(Vector3 velocity)
        {
            velocity.y = 0f;
            return velocity.magnitude;
        }
    }
}
