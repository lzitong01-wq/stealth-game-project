using System.Collections.Generic;
using System.IO;
using StealthPrototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace StealthPrototype.EditorTools
{
    public static class StealthTestSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/StealthTest.unity";
        const string NavPath = "Assets/NavMesh/StealthTestNavMesh.asset";
        const string MaterialFolder = "Assets/Materials/StealthPrototype";

        [MenuItem("Tools/Stealth Prototype/Create Test Scene")]
        public static void CreateTestScene()
        {
            if (File.Exists(ToFullPath(ScenePath)))
            {
                EditorUtility.DisplayDialog(
                    "潜行测试场景",
                    "Assets/Scenes/StealthTest.unity 已经存在。\n\n为了不覆盖你手动改过的内容，也避免生成重复物体，这次没有修改场景。\n\n如果确实要按工具重做，使用 Tools > Stealth Prototype > Rebuild Test Scene。",
                    "确定");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            BuildScene();
        }

        [MenuItem("Tools/Stealth Prototype/Rebuild Test Scene")]
        public static void RebuildTestScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "重新生成测试场景",
                    "这会删除并覆盖 Assets/Scenes/StealthTest.unity。你在这张场景里的手动修改会丢失。",
                    "覆盖生成",
                    "取消"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene active = EditorSceneManager.GetActiveScene();
            if (active.path == ScenePath)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (AssetDatabase.LoadAssetAtPath<Object>(ScenePath) != null)
                AssetDatabase.DeleteAsset(ScenePath);
            if (AssetDatabase.LoadAssetAtPath<Object>(NavPath) != null)
                AssetDatabase.DeleteAsset(NavPath);

            BuildScene();
        }

        static void BuildScene()
        {
            EnsureLayer(StealthLayers.Obstacle, StealthLayers.ObstacleIndex);
            EnsureLayer(StealthLayers.Player, StealthLayers.PlayerIndex);
            EnsureLayer(StealthLayers.Enemy, StealthLayers.EnemyIndex);
            EnsureTag("Player");
            EnsureFolder("Assets/Materials");
            EnsureFolder(MaterialFolder);
            EnsureFolder("Assets/NavMesh");
            EnsureFolder("Assets/Scenes");

            int obstacleLayer = StealthLayers.ObstacleIndex;
            int playerLayer = StealthLayers.PlayerIndex;
            int enemyLayer = StealthLayers.EnemyIndex;
            int obstacleMask = 1 << obstacleLayer;

            Material floorMat = GetMaterial("Floor", new Color(0.72f, 0.73f, 0.68f));
            Material boundaryMat = GetMaterial("Boundary", new Color(0.28f, 0.30f, 0.34f));
            Material wallMat = GetMaterial("HighWall", new Color(0.55f, 0.64f, 0.78f));
            Material counterMat = GetMaterial("Counter", new Color(0.58f, 0.36f, 0.18f));
            Material playerMat = GetMaterial("Player", new Color(0.18f, 0.42f, 0.95f));
            Material enemyMat = GetMaterial("Enemy", new Color(0.82f, 0.15f, 0.13f));
            Material noseMat = GetMaterial("EnemyNose", new Color(0.96f, 0.86f, 0.18f));
            Material pointMat = GetMaterial("PatrolPoint", new Color(0.95f, 0.78f, 0.18f));
            Material spotMat = GetMaterial("CrouchSpot", new Color(0.2f, 0.78f, 0.32f));
            Material markerMat = GetMaterial("VisionMarker", new Color(0.25f, 0.9f, 1f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("StealthTestRoot");
            var level = new GameObject("Level");
            var characters = new GameObject("Characters");
            var routes = new GameObject("PatrolRoute");
            var markers = new GameObject("Markers");
            var systems = new GameObject("Systems");
            level.transform.SetParent(root.transform, false);
            characters.transform.SetParent(root.transform, false);
            routes.transform.SetParent(root.transform, false);
            markers.transform.SetParent(root.transform, false);
            systems.transform.SetParent(root.transform, false);

            GameObject floor = CreatePrimitive("Floor", PrimitiveType.Cube, level.transform, new Vector3(0f, -0.1f, 0f), new Vector3(18f, 0.2f, 14f), floorMat, 0);
            GameObject north = CreatePrimitive("BoundaryNorth", PrimitiveType.Cube, level.transform, new Vector3(0f, 1.1f, 7.2f), new Vector3(18.8f, 2.2f, 0.4f), boundaryMat, obstacleLayer);
            GameObject south = CreatePrimitive("BoundarySouth", PrimitiveType.Cube, level.transform, new Vector3(0f, 1.1f, -7.2f), new Vector3(18.8f, 2.2f, 0.4f), boundaryMat, obstacleLayer);
            GameObject east = CreatePrimitive("BoundaryEast", PrimitiveType.Cube, level.transform, new Vector3(9.2f, 1.1f, 0f), new Vector3(0.4f, 2.2f, 14.8f), boundaryMat, obstacleLayer);
            GameObject west = CreatePrimitive("BoundaryWest", PrimitiveType.Cube, level.transform, new Vector3(-9.2f, 1.1f, 0f), new Vector3(0.4f, 2.2f, 14.8f), boundaryMat, obstacleLayer);
            GameObject highWall = CreatePrimitive("HighWall", PrimitiveType.Cube, level.transform, new Vector3(-2f, 1.5f, 0.5f), new Vector3(0.5f, 3f, 6f), wallMat, obstacleLayer);
            GameObject counter = CreatePrimitive("Counter", PrimitiveType.Cube, level.transform, new Vector3(4.2f, 0.55f, 0f), new Vector3(2.4f, 1.1f, 1f), counterMat, obstacleLayer);

            var patrolPositions = new[]
            {
                new Vector3(4.2f, 0.02f, -4.8f),
                new Vector3(4.2f, 0.02f, -2.35f),
                new Vector3(7.2f, 0.02f, -2.35f),
                new Vector3(7.2f, 0.02f, 5.5f),
                new Vector3(-6.3f, 0.02f, 5.6f),
                new Vector3(-6.5f, 0.02f, -4.6f),
                new Vector3(0.2f, 0.02f, -5.5f)
            };
            float[] pauses = { 1.1f, 4f, 1.1f, 1.1f, 1.1f, 1.1f, 1.1f };
            var waypointComponents = new PatrolWaypoint[patrolPositions.Length];
            for (int i = 0; i < patrolPositions.Length; i++)
            {
                GameObject point = CreatePrimitive(
                    "PatrolPoint_" + i,
                    PrimitiveType.Sphere,
                    routes.transform,
                    patrolPositions[i] + Vector3.up * 0.18f,
                    Vector3.one * 0.28f,
                    pointMat,
                    2,
                    false);
                PatrolWaypoint waypoint = point.AddComponent<PatrolWaypoint>();
                waypoint.pauseSeconds = pauses[i];
                waypointComponents[i] = waypoint;
            }

            GameObject crouchSpot = CreatePrimitive(
                "CrouchTestSpot",
                PrimitiveType.Cylinder,
                markers.transform,
                new Vector3(4.2f, 0.02f, 1.05f),
                new Vector3(0.7f, 0.01f, 0.7f),
                spotMat,
                2,
                false);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8.8f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 80f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.15f, 0.17f, 0.2f);
            Vector3 lookTarget = new Vector3(1.116f, 0f, 1.116f);
            cameraGo.transform.rotation = Quaternion.Euler(35f, 45f, 0f);
            cameraGo.transform.position = lookTarget - cameraGo.transform.forward * 20f;
            cameraGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.SetParent(root.transform, false);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.5f, 0.54f);
            RenderSettings.fog = false;

            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(characters.transform, false);
            playerGo.transform.position = new Vector3(-3.6f, 0f, -0.8f);
            playerGo.layer = playerLayer;
            try { playerGo.tag = "Player"; } catch (UnityException) { }
            CharacterController controller = playerGo.AddComponent<CharacterController>();
            controller.radius = 0.32f;
            controller.height = 1.8f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            PrototypePlayerController player = playerGo.AddComponent<PrototypePlayerController>();
            GameObject playerBody = CreatePrimitive("Body", PrimitiveType.Capsule, playerGo.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.64f, 0.9f, 0.64f), playerMat, playerLayer, false);
            GameObject visionMarker = CreatePrimitive("VisionMarker", PrimitiveType.Sphere, playerGo.transform, new Vector3(0f, 1.5f, 0f), Vector3.one * 0.12f, markerMat, 2, false);
            player.body = playerBody.transform;
            player.visionMarker = visionMarker.transform;
            player.viewCamera = camera;
            player.obstacleMask = obstacleMask;
            player.ApplyStance();

            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(characters.transform, false);
            enemyGo.transform.position = patrolPositions[0];
            enemyGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            enemyGo.layer = enemyLayer;
            CapsuleCollider enemyCollider = enemyGo.AddComponent<CapsuleCollider>();
            enemyCollider.center = new Vector3(0f, 1f, 0f);
            enemyCollider.height = 2f;
            enemyCollider.radius = 0.35f;
            NavMeshAgent agent = enemyGo.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.speed = 2.1f;
            agent.angularSpeed = 720f;
            agent.acceleration = 28f;
            agent.stoppingDistance = 0.15f;
            agent.autoBraking = true;
            agent.enabled = false;
            EnemyStealthController enemy = enemyGo.AddComponent<EnemyStealthController>();
            GameObject enemyBody = CreatePrimitive("Body", PrimitiveType.Capsule, enemyGo.transform, new Vector3(0f, 1f, 0f), new Vector3(0.7f, 1f, 0.7f), enemyMat, enemyLayer, false);
            GameObject nose = CreatePrimitive("Nose", PrimitiveType.Cube, enemyGo.transform, new Vector3(0f, 1.45f, 0.48f), new Vector3(0.18f, 0.14f, 0.36f), noseMat, enemyLayer, false);
            GameObject eye = new GameObject("Eye");
            eye.transform.SetParent(enemyGo.transform, false);
            eye.transform.localPosition = new Vector3(0f, 1.32f, 0.15f);
            GameObject eyeMarker = CreatePrimitive("EyeMarker", PrimitiveType.Sphere, eye.transform, Vector3.zero, Vector3.one * 0.1f, noseMat, 2, false);
            enemy.eyePoint = eye.transform;
            enemy.bodyRenderer = enemyBody.GetComponent<Renderer>();
            enemy.targetComponent = player;
            enemy.occlusionMask = obstacleMask;
            enemy.waypoints = waypointComponents;
            enemy.eyeHeight = 1.32f;

            Physics.SyncTransforms();
            NavMeshData navData = BakeNavMesh(level.transform, counter.transform, highWall.transform, north.transform, south.transform, east.transform, west.transform);
            var navAsset = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            if (navAsset != null)
                AssetDatabase.DeleteAsset(NavPath);
            AssetDatabase.CreateAsset(navData, NavPath);
            navData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);

            NavMeshDataHolder holder = systems.AddComponent<NavMeshDataHolder>();
            holder.data = navData;
            StealthTestSession session = systems.AddComponent<StealthTestSession>();
            session.player = player;
            session.enemy = enemy;
            StealthDebugHud hud = systems.AddComponent<StealthDebugHud>();
            hud.session = session;
            StealthGeneratedScene stamp = systems.AddComponent<StealthGeneratedScene>();
            stamp.toolVersion = "1.0";

            Physics.SyncTransforms();
            bool geometryOk = ValidateGeometry(enemy, player, crouchSpot.transform.position);
            bool navOk = ValidateNavMesh(holder, enemyGo.transform.position, patrolPositions, crouchSpot.transform.position);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool listed = false;
            for (int i = 0; i < buildScenes.Count; i++)
            {
                if (buildScenes[i].path == ScenePath)
                {
                    listed = true;
                    break;
                }
            }

            if (!listed)
            {
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = geometryOk && navOk
                ? "已生成 Assets/Scenes/StealthTest.unity。\n打开该场景后按 Play。\nWASD 移动，左 Ctrl 蹲伏，R 重置。\n柜台后的绿色圆点是蹲伏测试点。"
                : "场景已保存，但几何或导航自检没有全部通过。请看 Console 里以 [Stealth] 开头的日志。";
            Debug.Log("[Stealth] " + summary.Replace("\n", " ") + " 朝向标记=" + nose.name + " 眼部标记=" + eyeMarker.name + " 地面=" + floor.name);
            EditorUtility.DisplayDialog("潜行测试场景", summary, "确定");
        }

        static bool ValidateGeometry(EnemyStealthController enemy, PrototypePlayerController player, Vector3 crouchSpot)
        {
            bool ok = true;
            Vector3 counterFront = new Vector3(4.2f, 0f, -2.35f);
            Vector3 eye = counterFront + Vector3.up * 1.32f + Vector3.forward * 0.15f;
            Vector3 standPoint = new Vector3(crouchSpot.x, 1.5f, crouchSpot.z);
            Vector3 crouchPoint = new Vector3(crouchSpot.x, 0.7f, crouchSpot.z);
            int mask = 1 << StealthLayers.ObstacleIndex;

            ok &= Expect("柜台前站立应可见", Evaluate(eye, Vector3.forward, counterFront, crouchSpot, standPoint, mask), VisionBlockReason.Visible);
            ok &= Expect("柜台前下蹲应被柜台挡住", Evaluate(eye, Vector3.forward, counterFront, crouchSpot, crouchPoint, mask), VisionBlockReason.Occluded);

            Vector3 sideEye = new Vector3(7f, 1.32f, crouchSpot.z);
            ok &= Expect("绕到柜台侧面后下蹲仍应可见", Evaluate(sideEye, Vector3.left, new Vector3(7f, 0f, crouchSpot.z), crouchSpot, crouchPoint, mask), VisionBlockReason.Visible);

            Vector3 wallEye = new Vector3(0f, 1.32f, -3.6f);
            Vector3 hiddenFeet = new Vector3(-3.2f, 0f, 1.2f);
            ok &= Expect("正面无遮挡应可见", Evaluate(wallEye, Vector3.forward, new Vector3(0f, 0f, -3.6f), new Vector3(0f, 0f, 1.6f), new Vector3(0f, 1.5f, 1.6f), mask), VisionBlockReason.Visible);
            ok &= Expect("隔墙且仍在扇形内应被挡住", Evaluate(wallEye, Vector3.forward, new Vector3(0f, 0f, -3.6f), hiddenFeet, hiddenFeet + Vector3.up * 1.5f, mask), VisionBlockReason.Occluded);
            ok &= Expect("身后近距离不应发现", Evaluate(wallEye, Vector3.forward, new Vector3(0f, 0f, -3.6f), new Vector3(0f, 0f, -5f), new Vector3(0f, 1.5f, -5f), mask), VisionBlockReason.OutOfAngle);
            ok &= Expect("超出 8 米不应发现", Evaluate(wallEye, Vector3.forward, new Vector3(0f, 0f, -3.6f), new Vector3(0f, 0f, 6f), new Vector3(0f, 1.5f, 6f), mask), VisionBlockReason.OutOfRange);

            if (enemy.eyePoint == null || player == null)
                ok = false;
            return ok;
        }

        static VisionResult Evaluate(Vector3 eye, Vector3 forward, Vector3 selfFeet, Vector3 targetFeet, Vector3 targetPoint, int mask)
        {
            return StealthVision.Evaluate(new VisionQuery
            {
                eyePosition = eye,
                forward = forward,
                selfFlatPosition = selfFeet,
                targetFlatPosition = targetFeet,
                targetPoint = targetPoint,
                maxDistance = 8f,
                fullViewAngle = 90f,
                occlusionMask = mask
            });
        }

        static bool Expect(string label, VisionResult result, VisionBlockReason expected)
        {
            string blocker = result.blocker != null ? result.blocker.name : "无";
            bool pass = result.reason == expected;
            Debug.Log((pass ? "[Stealth] PASS " : "[Stealth] FAIL ") + label +
                      " 期望=" + expected + " 实际=" + result.reason +
                      " 距离=" + result.flatDistance.ToString("0.00") +
                      " 角度=" + result.angle.ToString("0.0") +
                      " 遮挡=" + blocker);
            return pass;
        }

        static bool ValidateNavMesh(NavMeshDataHolder holder, Vector3 enemyPos, Vector3[] points, Vector3 crouchSpot)
        {
            holder.EnsureLoaded();
            bool ok = true;
            ok &= ExpectOnMesh("敌人出生点", enemyPos);
            ok &= ExpectOnMesh("蹲伏测试点", crouchSpot);
            for (int i = 0; i < points.Length; i++)
                ok &= ExpectOnMesh("巡逻点 " + i, points[i]);

            NavMeshPath path = new NavMeshPath();
            Vector3 from = points[1];
            Vector3 to = points[4];
            bool found = NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path);
            bool complete = found && path.status == NavMeshPathStatus.PathComplete;
            Debug.Log((complete ? "[Stealth] PASS " : "[Stealth] FAIL ") + "柜台前到墙北侧的路径 status=" + (found ? path.status.ToString() : "none"));
            ok &= complete;
            return ok;
        }

        static bool ExpectOnMesh(string label, Vector3 point)
        {
            bool pass = NavMesh.SamplePosition(point, out NavMeshHit hit, 0.6f, NavMesh.AllAreas);
            Debug.Log((pass ? "[Stealth] PASS " : "[Stealth] FAIL ") + label + " 在导航网格上 " + (pass ? hit.position.ToString() : point.ToString()));
            return pass;
        }

        static NavMeshData BakeNavMesh(Transform level, params Transform[] notWalkable)
        {
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            for (int i = 0; i < notWalkable.Length; i++)
            {
                if (notWalkable[i] == null)
                    continue;
                markups.Add(new NavMeshBuildMarkup
                {
                    root = notWalkable[i],
                    overrideArea = true,
                    area = 1
                });
            }

            int mask = (1 << 0) | (1 << StealthLayers.ObstacleIndex);
            var bounds = new Bounds(Vector3.zero, new Vector3(40f, 16f, 40f));
            NavMeshBuilder.CollectSources(bounds, mask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;
            settings.agentSlope = 45f;
            settings.agentClimb = 0.3f;
            settings.minRegionArea = 0.4f;

            Debug.Log("[Stealth] 导航源数量 " + sources.Count);
            return NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        }

        static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, int layer, bool keepCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            // 用局部坐标。父物体不在原点时，写世界坐标会把模型挂到远离根节点的位置。
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            go.layer = layer;
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            if (!keepCollider)
            {
                Collider collider = go.GetComponent<Collider>();
                if (collider != null)
                    Object.DestroyImmediate(collider);
            }

            return go;
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
                material.SetFloat("_Glossiness", 0.08f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureLayer(string layerName, int index)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
                return;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            SerializedProperty slot = layers.GetArrayElementAtIndex(index);
            if (slot.stringValue == layerName)
                return;
            if (!string.IsNullOrEmpty(slot.stringValue))
            {
                Debug.LogError("[Stealth] 层 " + index + " 已被 " + slot.stringValue + " 占用，需要空层来放 " + layerName);
                return;
            }

            slot.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
        }

        static void EnsureTag(string tagName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
                return;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tagName)
                    return;
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
            tagManager.ApplyModifiedProperties();
        }

        static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string folderName = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        static string ToFullPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }
    }
}
