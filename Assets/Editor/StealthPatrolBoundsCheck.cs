using System.IO;
using System.Text;
using StealthPrototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace StealthPrototype.EditorTools
{
    /// <summary>
    /// 让敌人按真实巡逻走完两圈，再追击、丢失并回到巡逻，记录身体是否离开房间。
    /// </summary>
    [InitializeOnLoad]
    public static class StealthPatrolBoundsCheck
    {
        const string ScenePath = "Assets/Scenes/StealthTest.unity";
        const string KeyRun = "StealthPrototype.RunBounds";
        const string KeyShow = "StealthPrototype.ShowBounds";
        const string KeyResult = "StealthPrototype.BoundsResult";

        static readonly Vector3[] Points =
        {
            new Vector3(4.2f, 0f, -4.8f),
            new Vector3(4.2f, 0f, -2.35f),
            new Vector3(7.2f, 0f, -2.35f),
            new Vector3(7.2f, 0f, 5.5f),
            new Vector3(-6.3f, 0f, 5.6f),
            new Vector3(-6.5f, 0f, -4.6f),
            new Vector3(0.2f, 0f, -5.5f)
        };

        static int phase;
        static float phaseStart;
        static float realStart;
        static bool started;
        static bool reportedOutside;
        static int failures;
        static int cursor;
        static int[] visits;
        static float worstX;
        static float worstZ;
        static StringBuilder log;

        static StealthPatrolBoundsCheck()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        [MenuItem("Tools/Stealth Prototype/Check Patrol Bounds")]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(KeyRun, true);
            SessionState.SetBool(KeyShow, false);
            started = false;
            EditorApplication.isPlaying = true;
        }

        static void OnEditorUpdate()
        {
            if (SessionState.GetBool(KeyShow, false) && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(KeyShow, false);
                string text = SessionState.GetString(KeyResult, "");
                string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "patrol-bounds-result.txt");
                File.WriteAllText(path, text);
                EditorUtility.DisplayDialog(text.Contains("RESULT FAIL") ? "巡逻出界检查未通过" : "巡逻出界检查通过", text.Length > 1400 ? text.Substring(0, 1400) : text, "确定");
                return;
            }

            if (!SessionState.GetBool(KeyRun, false) || !EditorApplication.isPlaying || EditorApplication.isPaused)
                return;

            if (!started)
            {
                started = true;
                phase = 0;
                failures = 0;
                reportedOutside = false;
                cursor = 0;
                visits = new int[Points.Length];
                worstX = 0f;
                worstZ = 0f;
                log = new StringBuilder();
                realStart = Time.realtimeSinceStartup;
                phaseStart = Time.time;
                Time.timeScale = 4f;
                Time.maximumDeltaTime = 0.08f;
            }

            try
            {
                if (Time.realtimeSinceStartup - realStart > 150f)
                {
                    Fail("总超时");
                    Finish();
                    return;
                }

                Tick();
            }
            catch (System.Exception exception)
            {
                Fail("异常 " + exception.Message);
                Finish();
            }
        }

        static void Tick()
        {
            EnemyStealthController enemy = Object.FindObjectOfType<EnemyStealthController>();
            PrototypePlayerController player = Object.FindObjectOfType<PrototypePlayerController>();
            StealthTestSession session = Object.FindObjectOfType<StealthTestSession>();
            if (enemy == null || player == null || session == null || !enemy.IsReady)
            {
                if (Time.time - phaseStart > 5f)
                {
                    Fail("角色没有就绪");
                    Finish();
                }
                return;
            }

            SampleBody(enemy);

            switch (phase)
            {
                case 0:
                    player.enabled = false;
                    ProbeNavMesh();
                    phaseStart = Time.time;
                    phase = 1;
                    break;
                case 1:
                    TrackRoute(enemy.transform.position);
                    if (AllVisitedTwice())
                    {
                        log.AppendLine("PASS 两圈巡逻完成，各点次数 " + string.Join(",", visits));
                        player.enabled = true;
                        session.ResetTest();
                        player.Teleport(new Vector3(4.2f, 0f, -2.0f), false);
                        phaseStart = Time.time;
                        phase = 2;
                    }
                    else if (Time.time - phaseStart > 90f)
                    {
                        Fail("两圈巡逻超时，次数 " + string.Join(",", visits));
                        Finish();
                    }
                    break;
                case 2:
                    if (Time.time - phaseStart < 0.4f)
                        return;
                    if (enemy.State == EnemyAwareness.Chase || enemy.State == EnemyAwareness.Attack)
                    {
                        log.AppendLine("PASS 进入追击或攻击，状态 " + enemy.StateLabel);
                        player.Teleport(new Vector3(-3.2f, 0f, 1.2f), false);
                        phaseStart = Time.time;
                        phase = 3;
                    }
                    else if (Time.time - phaseStart > 8f)
                    {
                        Fail("没有进入追击，状态 " + enemy.StateLabel);
                        Finish();
                    }
                    break;
                case 3:
                    if (Time.time - phaseStart < 0.6f)
                        return;
                    Check(enemy.State == EnemyAwareness.Chase, "丢失后应前往最后可见位置，状态 " + enemy.StateLabel);
                    Check(enemy.HasLastSeen, "应记录最后可见位置");
                    if (enemy.HasLastSeen && enemy.GetComponent<NavMeshAgent>() != null)
                    {
                        Vector3 destination = enemy.GetComponent<NavMeshAgent>().destination;
                        float off = Flat(destination, enemy.LastSeenPosition);
                        Check(off < 1.5f, "追击目标应靠近最后可见位置，偏差 " + off.ToString("0.00"));
                    }
                    phaseStart = Time.time;
                    phase = 4;
                    break;
                case 4:
                    if (enemy.State == EnemyAwareness.Patrol)
                    {
                        log.AppendLine("PASS 丢失超过时限后回到巡逻");
                        Check(worstX <= 9.02f && worstZ <= 7.02f, "身体应留在房间内，最远 |x|=" + worstX.ToString("0.00") + " |z|=" + worstZ.ToString("0.00"));
                        Finish();
                    }
                    else if (Time.time - phaseStart > 16f)
                    {
                        Fail("没有回到巡逻，状态 " + enemy.StateLabel);
                        Finish();
                    }
                    break;
            }
        }

        static void TrackRoute(Vector3 position)
        {
            if (Flat(position, Points[cursor]) > 0.8f)
                return;
            visits[cursor]++;
            cursor = (cursor + 1) % Points.Length;
        }

        static bool AllVisitedTwice()
        {
            if (visits[0] < 3)
                return false;
            for (int i = 1; i < visits.Length; i++)
            {
                if (visits[i] < 2)
                    return false;
            }

            return true;
        }

        static void SampleBody(EnemyStealthController enemy)
        {
            Transform body = enemy.transform.Find("Body");
            if (body == null)
                return;
            Renderer renderer = body.GetComponent<Renderer>();
            if (renderer == null)
                return;
            Bounds bounds = renderer.bounds;
            worstX = Mathf.Max(worstX, Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x));
            worstZ = Mathf.Max(worstZ, Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z));
            if (!reportedOutside && (bounds.max.x > 9.05f || bounds.min.x < -9.05f || bounds.max.z > 7.05f || bounds.min.z < -7.05f))
            {
                reportedOutside = true;
                Fail("身体出界 " + bounds.min + " .. " + bounds.max + " 根节点 " + enemy.transform.position);
            }
        }

        static void ProbeNavMesh()
        {
            Probe(new Vector3(0f, 0.2f, 7.8f), "北墙外");
            Probe(new Vector3(0f, 0.2f, -7.8f), "南墙外");
            Probe(new Vector3(9.8f, 0.2f, 0f), "东墙外");
            Probe(new Vector3(-9.8f, 0.2f, 0f), "西墙外");
            Probe(new Vector3(9.5f, 0.2f, 7.5f), "东北墙角外");
        }

        static void Probe(Vector3 point, string label)
        {
            bool hit = NavMesh.SamplePosition(point, out NavMeshHit navHit, 0.35f, NavMesh.AllAreas);
            if (!hit)
            {
                log.AppendLine("PASS 导航没有延伸到" + label);
                return;
            }

            bool outside = Mathf.Abs(navHit.position.x) > 9.02f || Mathf.Abs(navHit.position.z) > 7.02f;
            Check(!outside, "导航采样落到" + label + " " + navHit.position);
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static void Check(bool condition, string message)
        {
            if (condition)
            {
                log.AppendLine("PASS " + message);
            }
            else
            {
                failures++;
                log.AppendLine("FAIL " + message);
                Debug.LogWarning("[PatrolBounds] FAIL " + message);
            }
        }

        static void Fail(string message)
        {
            Check(false, message);
        }

        static void Finish()
        {
            if (log == null)
                log = new StringBuilder();
            log.AppendLine("身体最远边界 |x|=" + worstX.ToString("0.00") + " |z|=" + worstZ.ToString("0.00") + "（房间内沿 x=±9 z=±7）");
            log.AppendLine(failures == 0 ? "RESULT PASS" : "RESULT FAIL " + failures);
            SessionState.SetString(KeyResult, log.ToString());
            SessionState.SetBool(KeyRun, false);
            SessionState.SetBool(KeyShow, true);
            Time.timeScale = 1f;
            EditorApplication.isPlaying = false;
        }
    }
}
