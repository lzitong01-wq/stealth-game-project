using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StealthPrototype.EditorTools
{
    /// <summary>
    /// 复制原始测试场景，并只在副本里挂上独立的低模视觉 Prefab。
    /// </summary>
    public static class LowPolySceneSetup
    {
        const string SourceScene = "Assets/Scenes/StealthTest.unity";
        const string CopyScene = "Assets/Scenes/StealthTest_LowPoly.unity";
        const string PrefabFolder = "Assets/Prefabs/LowPoly";
        const string MaterialFolder = "Assets/Materials/LowPoly";

        [MenuItem("Tools/Stealth Prototype/Create Low Poly Scene")]
        public static void CreateFromMenu()
        {
            if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, SourceScene)))
            {
                EditorUtility.DisplayDialog("低模场景", "找不到原始场景 Assets/Scenes/StealthTest.unity。", "确定");
                return;
            }

            EnsureFolders();
            Material playerBody = GetMaterial("PlayerBody", new Color(0.18f, 0.42f, 0.92f));
            Material playerLimb = GetMaterial("PlayerLimb", new Color(0.12f, 0.28f, 0.72f));
            Material enemyBody = GetMaterial("EnemyBody", new Color(0.78f, 0.14f, 0.12f));
            Material enemyLimb = GetMaterial("EnemyLimb", new Color(0.52f, 0.1f, 0.1f));
            Material skin = GetMaterial("Skin", new Color(0.93f, 0.76f, 0.62f));
            Material helmet = GetMaterial("Helmet", new Color(0.22f, 0.24f, 0.28f));
            Material face = GetMaterial("Face", new Color(0.98f, 0.9f, 0.72f));

            GameObject playerPrefab = SaveVisual("PlayerVisual", RoleVisual(false, playerBody, playerLimb, skin, face, helmet));
            GameObject enemyPrefab = SaveVisual("EnemyVisual", RoleVisual(true, enemyBody, enemyLimb, skin, face, helmet));

            bool created = false;
            if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, CopyScene)))
            {
                if (!AssetDatabase.CopyAsset(SourceScene, CopyScene))
                {
                    EditorUtility.DisplayDialog("低模场景", "复制场景失败。", "确定");
                    return;
                }

                created = true;
            }

            Scene scene = EditorSceneManager.OpenScene(CopyScene, OpenSceneMode.Single);
            PrototypePlayerController player = Object.FindObjectOfType<PrototypePlayerController>();
            Attach(player, playerPrefab);
            Attach(Object.FindObjectOfType<EnemyStealthController>(), enemyPrefab);
            if (player != null)
            {
                player.hitsToDie = 2;
                EditorUtility.SetDirty(player);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, CopyScene);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();

            string sourceGuid = AssetDatabase.AssetPathToGUID(SourceScene);
            string copyGuid = AssetDatabase.AssetPathToGUID(CopyScene);
            Debug.Log("[LowPoly] 原场景 GUID " + sourceGuid + " 副本 GUID " + copyGuid + (created ? "（新建）" : "（已存在，只更新了视觉）"));
            EditorUtility.DisplayDialog(
                "低模场景",
                "副本已保存到 Assets/Scenes/StealthTest_LowPoly.unity。\n原场景 StealthTest 没有被修改。\n两个场景的 GUID 不同。",
                "确定");
        }

        static void Attach(Component character, GameObject prefab)
        {
            if (character == null || prefab == null)
                return;

            Transform old = character.transform.Find("Visual");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, character.transform);
            instance.name = "Visual";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Renderer[] renderers = character.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].transform.IsChildOf(instance.transform))
                    continue;
                string name = renderers[i].gameObject.name;
                if (name == "Body" || name == "Nose" || name == "EyeMarker" || name == "VisionMarker")
                    renderers[i].enabled = false;
            }
        }

        static GameObject SaveVisual(string assetName, GameObject temporary)
        {
            string path = PrefabFolder + "/" + assetName + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, path);
            Object.DestroyImmediate(temporary);
            return prefab;
        }

        static GameObject RoleVisual(bool enemy, Material body, Material limb, Material skin, Material face, Material helmet)
        {
            var root = new GameObject(enemy ? "EnemyVisual" : "PlayerVisual");
            var driver = root.AddComponent<LowPolyPoseDriver>();
            driver.role = enemy ? LowPolyPoseDriver.Role.Enemy : LowPolyPoseDriver.Role.Player;

            Transform pose = Joint("PoseRoot", root.transform, new Vector3(0f, 0.9f, 0f));
            Transform torso = Joint("TorsoPivot", pose, new Vector3(0f, 0.06f, 0f));
            Part("Torso", torso, PrimitiveType.Cube, new Vector3(0f, 0.24f, 0f), new Vector3(0.42f, 0.46f, 0.22f), body);

            Transform head = Joint("HeadPivot", torso, new Vector3(0f, 0.5f, 0f));
            Part("Head", head, PrimitiveType.Sphere, new Vector3(0f, 0.13f, 0f), Vector3.one * 0.28f, skin);
            Part("Face", head, PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.13f), new Vector3(0.12f, 0.07f, 0.04f), face);
            if (enemy)
            {
                Part("Helmet", head, PrimitiveType.Sphere, new Vector3(0f, 0.18f, -0.01f), new Vector3(0.34f, 0.24f, 0.34f), helmet);
                Part("HelmetBrow", head, PrimitiveType.Cube, new Vector3(0f, 0.16f, 0.1f), new Vector3(0.3f, 0.08f, 0.12f), helmet);
            }

            BuildArm("L", torso, -1f, limb);
            BuildArm("R", torso, 1f, limb);
            BuildLeg("L", pose, -0.12f, limb, body);
            BuildLeg("R", pose, 0.12f, limb, body);
            return root;
        }

        static void BuildArm(string side, Transform torso, float sign, Material limb)
        {
            Transform shoulder = Joint("Shoulder" + side, torso, new Vector3(0.26f * sign, 0.4f, 0f));
            Part("UpperArm" + side, shoulder, PrimitiveType.Cube, new Vector3(0f, -0.15f, 0f), new Vector3(0.1f, 0.3f, 0.1f), limb);
            Transform elbow = Joint("Elbow" + side, shoulder, new Vector3(0f, -0.3f, 0f));
            Part("Forearm" + side, elbow, PrimitiveType.Cube, new Vector3(0f, -0.13f, 0f), new Vector3(0.09f, 0.26f, 0.09f), limb);
        }

        static void BuildLeg(string side, Transform pose, float x, Material limb, Material footMaterial)
        {
            Transform hip = Joint("Hip" + side, pose, new Vector3(x, 0f, 0f));
            Part("Thigh" + side, hip, PrimitiveType.Cube, new Vector3(0f, -0.22f, 0f), new Vector3(0.16f, 0.44f, 0.16f), limb);
            Transform knee = Joint("Knee" + side, hip, new Vector3(0f, -0.44f, 0f));
            Part("Shin" + side, knee, PrimitiveType.Cube, new Vector3(0f, -0.18f, 0f), new Vector3(0.14f, 0.36f, 0.14f), limb);
            Transform ankle = Joint("Ankle" + side, knee, new Vector3(0f, -0.36f, 0f));
            Part("Foot" + side, ankle, PrimitiveType.Cube, new Vector3(0f, -0.035f, 0.07f), new Vector3(0.15f, 0.06f, 0.28f), footMaterial);
        }

        static Transform Joint(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        static void Part(string name, Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        static Material GetMaterial(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Diffuse");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.12f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/Prefabs", "LowPoly");
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "LowPoly");
        }

        static void AddToBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == CopyScene)
                    return;
            }

            var list = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(list, 0);
            list[scenes.Length] = new EditorBuildSettingsScene(CopyScene, true);
            EditorBuildSettings.scenes = list;
        }
    }
}
