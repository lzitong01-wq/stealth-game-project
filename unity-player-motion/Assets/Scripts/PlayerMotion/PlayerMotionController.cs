// 把动作系统接进场景 —— 对应 TypeScript 版 install.ts。
//
// 用法：把本组件挂到一个空 GameObject 上，同物体或子物体上挂 WhiteboxRig。
// 场景生成器（Tools/Player Motion/生成白模场景）会自动搭好这一切。
//
// 每帧流程：读键盘 → 查询掩体 → 推进状态机 → 边界钳制 → 驱动白模 → 相机跟随

using UnityEngine;

namespace PlayerMotion
{
    [DisallowMultipleComponent]
    public sealed class PlayerMotionController : MonoBehaviour
    {
        [Header("动作参数")]
        public MotionConfig Config = new MotionConfig();

        [Header("演示掩体（走廊）")]
        public CorridorProbe Probe = new CorridorProbe();

        [Header("出生点")]
        public Vector3 Spawn = Vector3.zero;

        [Header("白模")]
        [Tooltip("留空则自动在子物体里找")]
        public WhiteboxRig Rig;

        [Header("相机跟随（等距 2.5D）")]
        public bool CameraFollow = true;
        public Camera TargetCamera;
        [Tooltip("环绕距离")]
        public float CameraDistance = 2.6f;
        [Tooltip(
            "相机高度。必须够高才能越过 1.65m 的侧墙看到角色全身：\n" +
            "俯角 = atan((高度 - 视线高度) / 距离) = atan(4.1/2.6) ≈ 58°。\n" +
            "俯角小于 45° 时近侧墙会把角色下半身完全挡死。")]
        public float CameraHeight = 5.0f;
        [Tooltip("水平角（度）。角度越大越偏侧，但太大会把相机甩到墙外被墙挡住")]
        public float CameraYawDeg = -35f;
        [Tooltip("视线对准的高度")]
        public float CameraLookHeight = 0.9f;

        /// <summary>动作运行时。HUD / 调试可读。</summary>
        public MotionRuntime Runtime { get; private set; }

        private void Awake()
        {
            Runtime = MotionRuntime.Create(Spawn);
            if (Rig == null) Rig = GetComponentInChildren<WhiteboxRig>();
            if (Rig == null)
            {
                Rig = gameObject.AddComponent<WhiteboxRig>();
                Debug.LogWarning("[PlayerMotion] 场景里没有 WhiteboxRig，已自动添加一个。");
            }
            if (TargetCamera == null) TargetCamera = Camera.main;
        }

        private void Update()
        {
            if (Runtime == null) return;

            // 1. 查询掩体（位置 + 朝向决定墙在哪侧、多远、法线朝哪）
            var cover = Probe.Query(Runtime.RootPos, Runtime.Facing);

            // 2. 读输入并推进状态机
            var input = MotionInputReader.Read(cover);
            MotionSystem.Step(Runtime, input, Time.deltaTime, Config);

            // 3. 边界钳制：撞墙时同步压速，避免 HUD 显示假速度
            var clamped = Probe.Clamp(Runtime.RootPos);
            if ((clamped - Runtime.RootPos).sqrMagnitude > 1e-8f)
            {
                Runtime.RootPos = clamped;
                Runtime.Speed *= 0.3f;
            }

            // 4. 驱动白模
            Rig.Sync(Runtime, Config);
        }

        private void LateUpdate()
        {
            if (!CameraFollow || TargetCamera == null || Runtime == null) return;

            float yaw = CameraYawDeg * Mathf.Deg2Rad;
            var camPos = new Vector3(
                Runtime.RootPos.x + Mathf.Sin(yaw) * CameraDistance,
                CameraHeight,
                Runtime.RootPos.z + Mathf.Cos(yaw) * CameraDistance);

            var lookAt = new Vector3(Runtime.RootPos.x, CameraLookHeight, Runtime.RootPos.z);
            TargetCamera.transform.position = camPos;
            TargetCamera.transform.LookAt(lookAt);
        }

        /// <summary>把角色放回出生点（调试用）。</summary>
        public void ResetToSpawn()
        {
            Runtime = MotionRuntime.Create(Spawn);
        }

        private void OnDrawGizmosSelected()
        {
            if (Probe == null) return;
            // 画出走廊两面墙，便于在 Scene 视图里对准
            Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.8f);
            var p = Application.isPlaying && Runtime != null ? Runtime.RootPos : Spawn;
            float z0 = p.z - 2.5f, z1 = p.z + 5f;
            Gizmos.DrawLine(new Vector3(-Probe.HalfWidth, 0f, z0), new Vector3(-Probe.HalfWidth, 0f, z1));
            Gizmos.DrawLine(new Vector3(Probe.HalfWidth, 0f, z0), new Vector3(Probe.HalfWidth, 0f, z1));
        }
    }
}
