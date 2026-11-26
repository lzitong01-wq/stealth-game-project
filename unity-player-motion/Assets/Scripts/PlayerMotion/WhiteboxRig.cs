// 3D 白模 —— 从 TypeScript 版 mesh3d.ts 迁移，几何做法参考 Tomcasso 的 LowPolySceneSetup.cs。
//
// 为什么用「世界坐标 + 四元数」而不是父子层级：
//   动作层已经直接给出每个关节的世界坐标，如果在 Unity 里再建一遍父子层级，
//   等于把同一件事算两次，还容易和 FK 的约定不一致。
//   这里每个骨段一个 Cube，每帧只写 position / rotation / scale。
//
// Cube 的局部 +Y 方向即长度方向，所以用 Quaternion.FromToRotation(Vector3.up, dir)
// 把 +Y 转到骨段方向，localScale.y 直接就是骨段长度。

using UnityEngine;

namespace PlayerMotion
{
    public sealed class WhiteboxRig : MonoBehaviour
    {
        [Header("尺寸")]
        [Tooltip("头半径（米），1.75m 身高用 0.125")]
        public float HeadRadius = 0.125f;

        [Header("配色")]
        public Color BodyColor = new Color(0.12f, 0.36f, 0.46f);
        public Color LimbColor = new Color(0.09f, 0.27f, 0.36f);
        public Color FootColor = new Color(0.07f, 0.2f, 0.27f);
        public Color HeadColor = new Color(0.81f, 0.66f, 0.39f);

        private struct Seg
        {
            public Joint From, To;
            public float W, H;
            public byte Mat; // 0=躯干 1=四肢 2=脚
            public Seg(Joint f, Joint t, float w, float h, byte m) { From = f; To = t; W = w; H = h; Mat = m; }
        }

        // 比例参考 Tomcasso 的低模人形：躯干最粗、四肢渐细、脚最扁
        private static readonly Seg[] Parts =
        {
            new Seg(Joint.Pelvis,    Joint.Chest,    0.34f, 0.21f,  0),
            new Seg(Joint.Chest,     Joint.Neck,     0.12f, 0.12f,  0),
            new Seg(Joint.ShoulderL, Joint.ElbowL,   0.095f, 0.095f, 1),
            new Seg(Joint.ElbowL,    Joint.HandL,    0.082f, 0.082f, 1),
            new Seg(Joint.ShoulderR, Joint.ElbowR,   0.095f, 0.095f, 1),
            new Seg(Joint.ElbowR,    Joint.HandR,    0.082f, 0.082f, 1),
            new Seg(Joint.HipL,      Joint.KneeL,    0.145f, 0.145f, 1),
            new Seg(Joint.KneeL,     Joint.AnkleL,   0.125f, 0.125f, 1),
            new Seg(Joint.HipR,      Joint.KneeR,    0.145f, 0.145f, 1),
            new Seg(Joint.KneeR,     Joint.AnkleR,   0.125f, 0.125f, 1),
            // 脚拆三段：脚掌 / 脚趾 / 脚跟
            new Seg(Joint.AnkleL,    Joint.FootL,    0.1f,   0.072f, 2),
            new Seg(Joint.FootL,     Joint.ToeL,     0.095f, 0.048f, 2),
            new Seg(Joint.AnkleL,    Joint.HeelL,    0.088f, 0.062f, 2),
            new Seg(Joint.AnkleR,    Joint.FootR,    0.1f,   0.072f, 2),
            new Seg(Joint.FootR,     Joint.ToeR,     0.095f, 0.048f, 2),
            new Seg(Joint.AnkleR,    Joint.HeelR,    0.088f, 0.062f, 2),
        };

        private GameObject[] _segObjects;
        private GameObject _headObject;
        private Material[] _materials;
        private readonly PoseSolver _solver = new PoseSolver();
        private bool _built;

        private void Awake()
        {
            Build();
        }

        private void OnDestroy()
        {
            if (_materials == null) return;
            foreach (var m in _materials)
                if (m != null) Destroy(m);
        }

        /// <summary>生成白模对象。重复调用无副作用。</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            _materials = new[]
            {
                MakeMaterial(BodyColor, "PM_Body"),
                MakeMaterial(LimbColor, "PM_Limb"),
                MakeMaterial(FootColor, "PM_Foot"),
                MakeMaterial(HeadColor, "PM_Head"),
            };

            _segObjects = new GameObject[Parts.Length];
            for (int i = 0; i < Parts.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"seg_{Parts[i].From}_{Parts[i].To}";
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col); // 白模不参与物理
                go.GetComponent<MeshRenderer>().sharedMaterial = _materials[Parts[i].Mat];
                go.transform.SetParent(transform, true);
                _segObjects[i] = go;
            }

            _headObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _headObject.name = "head";
            var hcol = _headObject.GetComponent<Collider>();
            if (hcol != null) Destroy(hcol);
            _headObject.GetComponent<MeshRenderer>().sharedMaterial = _materials[3];
            _headObject.transform.SetParent(transform, true);
        }

        /// <summary>用最新解算结果驱动所有白模对象。</summary>
        public void Sync(MotionRuntime rt, MotionConfig cfg)
        {
            if (!_built) Build();

            // 蹲下时整体压低（与动作层同一套约定）
            var root = new Vector3(rt.RootPos.x, rt.RootPos.y - cfg.crouchDrop * rt.CrouchAmount, rt.RootPos.z);
            _solver.Solve(rt.Pose, root, rt.RootEuler);

            for (int i = 0; i < Parts.Length; i++)
            {
                var a = _solver.Positions[(int)Parts[i].From];
                var b = _solver.Positions[(int)Parts[i].To];
                var delta = b - a;
                float len = delta.magnitude;
                var go = _segObjects[i];

                if (len < 1e-4f)
                {
                    if (go.activeSelf) go.SetActive(false);
                    continue;
                }
                if (!go.activeSelf) go.SetActive(true);

                var t = go.transform;
                t.position = (a + b) * 0.5f;
                t.rotation = Quaternion.FromToRotation(Vector3.up, delta / len);
                t.localScale = new Vector3(Parts[i].W, len, Parts[i].H);
            }

            var head = _headObject.transform;
            head.position = _solver.Positions[(int)Joint.Head];
            head.localScale = Vector3.one * (HeadRadius * 2f);
        }

        private static Material MakeMaterial(Color c, string name)
        {
            // 内置渲染管线优先用 Standard；找不到时退回 Diffuse，避免材质变粉
            var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            var m = new Material(shader) { name = name, color = c };
            return m;
        }
    }
}
