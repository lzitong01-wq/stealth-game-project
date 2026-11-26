// 一键生成白模演示场景（编辑器工具）。
//
// 菜单：Tools/Player Motion/生成白模场景
//
// 会创建：地面、两面走廊墙、方向光、等距相机、角色（WhiteboxRig + PlayerMotionController），
// 并保存为 Assets/Scenes/PlayerMotion.unity。

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PlayerMotion.EditorTools
{
    public static class WhiteboxSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/PlayerMotion.unity";

        [MenuItem("Tools/Player Motion/生成白模场景")]
        public static void BuildScene()
        {
            // 1. 新场景
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. 地面
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.1f, 1f);
            ground.transform.localScale = new Vector3(12f, 0.2f, 16f);
            Tint(ground, new Color(0.16f, 0.2f, 0.22f));

            // 3. 两面走廊墙（与 CorridorProbe.HalfWidth = 1.0 对齐）
            BuildWall("Wall_L", -1.0f);
            BuildWall("Wall_R", 1.0f);

            // 4. 方向光
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 5. 相机
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.backgroundColor = new Color(0.04f, 0.06f, 0.08f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.AddComponent<AudioListener>();

            // 6. 角色
            var player = new GameObject("Player");
            var rig = player.AddComponent<WhiteboxRig>();
            var ctrl = player.AddComponent<PlayerMotionController>();
            ctrl.Rig = rig;
            ctrl.TargetCamera = cam;
            ctrl.Spawn = Vector3.zero;

            // 7. 保存
            System.IO.Directory.CreateDirectory(Application.dataPath + "/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Selection.activeGameObject = player;
            Debug.Log($"[PlayerMotion] 场景已生成：{ScenePath}\n" +
                      "按 Play 运行。操作：WASD 移动 / Shift 疾走 / Ctrl 下蹲 / 靠近墙自动贴墙 / Space 翻滚换位。");
        }

        private static void BuildWall(string name, float x)
        {
            const float height = 1.65f;
            const float thick = 0.26f;
            const float zFrom = -2.5f;
            const float zTo = 5f;
            float zCenter = (zFrom + zTo) * 0.5f;
            float zLen = zTo - zFrom;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = new Vector3(x, height * 0.5f, zCenter);
            wall.transform.localScale = new Vector3(thick, height, zLen);
            Tint(wall, new Color(0.45f, 0.18f, 0.2f));
        }

        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            var mat = new Material(shader) { name = go.name + "_Mat", color = c };
            r.sharedMaterial = mat;
        }
    }
}
