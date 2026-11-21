using System.IO;
using System.Text;
using StealthPrototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StealthPrototype.EditorTools
{
    /// <summary>
    /// 在 Play 模式下按验收用例驱动临时玩家和敌人，结果写到项目根目录 stealth-check-result.txt。
    /// 失败时只写警告，避免 Console 的 Error Pause 把检查卡住。
    /// </summary>
    [InitializeOnLoad]
    public static class StealthPlaymodeValidator
    {
        const string ScenePath = "Assets/Scenes/StealthTest.unity";
        const string KeyRun = "StealthPrototype.RunChecks";
        const string KeyShow = "StealthPrototype.ShowResult";
        const string KeyResult = "StealthPrototype.Result";

        static int phase;
        static float phaseStart;
        static float realStart;
        static bool started;
        static int failures;
        static int confirmEvents;
        static int hitBaseline;
        static Vector3 expectedLastSeen;
        static bool dodged;
        static float dodgeStart;
        static StringBuilder log;

        static StealthPlaymodeValidator()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        [MenuItem("Tools/Stealth Prototype/Run Automated Checks")]
        public static void RunFromMenu()
        {
            if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScenePath)))
            {
                EditorUtility.DisplayDialog("潜行测试", "请先执行 Tools > Stealth Prototype > Create Test Scene。", "确定");
                return;
            }

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
                WriteResult(text);
                EditorUtility.DisplayDialog(text.Contains("RESULT FAIL") ? "自动检查未通过" : "自动检查通过", TrimForDialog(text), "确定");
                return;
            }

            if (!SessionState.GetBool(KeyRun, false))
                return;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                return;

            if (!started)
            {
                started = true;
                phase = 0;
                failures = 0;
                confirmEvents = 0;
                dodged = false;
                log = new StringBuilder();
                realStart = Time.realtimeSinceStartup;
                phaseStart = Time.time;
                Time.timeScale = 4f;
                Time.maximumDeltaTime = 0.05f;
                StealthSignals.PlayerConfirmed += OnConfirmed;
            }

            try
            {
                if (Time.realtimeSinceStartup - realStart > 90f)
                {
                    Fail("总超时");
                    Finish();
                    return;
                }

                Tick();
            }
            catch (System.Exception exception)
            {
                Fail("异常 " + exception);
                Finish();
            }
        }

        static void OnConfirmed(StealthConfirmInfo info)
        {
            confirmEvents++;
        }

        static void Tick()
        {
            StealthTestSession session = Object.FindObjectOfType<StealthTestSession>();
            if (session == null || session.player == null || session.enemy == null || !session.enemy.IsReady)
            {
                if (Time.time - phaseStart > 5f)
                {
                    Fail("场景里的玩家或敌人没有就绪");
                    Finish();
                }
                return;
            }

            PrototypePlayerController player = session.player;
            EnemyStealthController enemy = session.enemy;
            float elapsed = Time.time - phaseStart;

            switch (phase)
            {
                case 0:
                    if (elapsed < 0.4f)
                        return;
                    Check(enemy.State == EnemyAwareness.Patrol, "开局应处于巡逻，实际 " + enemy.State);
                    Check(enemy.LastVision.reason != VisionBlockReason.Visible, "开局玩家不应被看见，实际 " + enemy.LastVision.reason);
                    Check(enemy.ConfirmCount == 0, "开局不应已经确认发现");
                    Begin(1);
                    break;

                case 1:
                    enemy.FreezeMotion = true;
                    enemy.WarpTo(new Vector3(0f, 0f, -3.6f), Vector3.forward);
                    player.Teleport(new Vector3(0f, 0f, -5.2f), false);
                    Begin(2);
                    break;

                case 2:
                    if (elapsed < 0.35f)
                        return;
                    Check(enemy.LastVision.reason == VisionBlockReason.OutOfAngle, "身后近距离应为超出视角，实际 " + Describe(enemy));
                    Check(enemy.State == EnemyAwareness.Patrol, "身后近距离不应离开巡逻");
                    Begin(3);
                    break;

                case 3:
                    enemy.WarpTo(new Vector3(0f, 0f, -3.6f), Vector3.forward);
                    player.Teleport(new Vector3(-3.2f, 0f, 1.2f), false);
                    Begin(4);
                    break;

                case 4:
                    if (elapsed < 0.35f)
                        return;
                    Check(enemy.LastVision.reason == VisionBlockReason.Occluded, "隔墙近距离应为遮挡，实际 " + Describe(enemy));
                    Check(enemy.State == EnemyAwareness.Patrol, "隔墙时不应确认发现");
                    Begin(5);
                    break;

                case 5:
                {
                    Vector3 spot = SpotFeet();
                    enemy.WarpTo(new Vector3(4.2f, 0f, -2.35f), Vector3.forward);
                    player.Teleport(spot, false);
                    Begin(6);
                    break;
                }

                case 6:
                    if (elapsed < 0.35f)
                        return;
                    Check(enemy.LastVision.reason == VisionBlockReason.Visible, "柜台后站立应可见，实际 " + Describe(enemy));
                    player.Teleport(SpotFeet(), true);
                    Begin(7);
                    break;

                case 7:
                    if (elapsed < 0.35f)
                        return;
                    Check(player.IsCrouching, "玩家应处于下蹲");
                    Check(enemy.LastVision.reason == VisionBlockReason.Occluded, "柜台后下蹲应被挡住，实际 " + Describe(enemy));
                    Check(enemy.LastVision.blocker != null && enemy.LastVision.blocker.name == "Counter", "挡住下蹲玩家的应是柜台，实际 " + (enemy.LastVision.blocker != null ? enemy.LastVision.blocker.name : "无"));
                    Begin(8);
                    break;

                case 8:
                    enemy.WarpTo(new Vector3(7f, 0f, SpotFeet().z), Vector3.left);
                    player.Teleport(SpotFeet(), true);
                    Begin(9);
                    break;

                case 9:
                    if (elapsed < 0.45f)
                        return;
                    Check(player.IsCrouching, "绕到侧面时玩家仍应下蹲");
                    Check(enemy.LastVision.reason == VisionBlockReason.Visible, "绕过柜台后下蹲玩家应可见，实际 " + Describe(enemy));
                    Begin(10);
                    break;

                case 10:
                    session.ResetTest();
                    enemy.FreezeMotion = true;
                    enemy.WarpTo(new Vector3(0f, 0f, -3.6f), Vector3.forward);
                    player.Teleport(new Vector3(0f, 0f, 1.6f), false);
                    confirmEvents = 0;
                    Begin(11);
                    break;

                case 11:
                    if (elapsed < 2.4f)
                        return;
                    Check(enemy.State == EnemyAwareness.Suspicious, "远处可见 2.4 秒时应仍是怀疑，实际 " + enemy.State);
                    Check(enemy.ConfirmCount == 0, "未满 3 秒不应确认发现");
                    Begin(12);
                    break;

                case 12:
                    if (elapsed < 1.3f)
                        return;
                    Check(enemy.State == EnemyAwareness.Chase, "持续可见超过 3 秒后应追击，实际 " + enemy.State + " 可见计时=" + enemy.VisibleTimer.ToString("0.00"));
                    Check(enemy.ConfirmCount == 1, "确认发现应只发生一次，实际 " + enemy.ConfirmCount);
                    Check(confirmEvents == 1, "确认发现事件应只发出一次，实际 " + confirmEvents);
                    expectedLastSeen = player.WorldPosition;
                    player.Teleport(new Vector3(-4.2f, 0f, -0.6f), false);
                    enemy.FreezeMotion = false;
                    Begin(13);
                    break;

                case 13:
                    if (elapsed < 0.8f)
                        return;
                    Check(enemy.LastVision.reason != VisionBlockReason.Visible, "躲到墙后不应继续可见，实际 " + Describe(enemy));
                    Check(enemy.State == EnemyAwareness.Chase, "丢失视线后应保持追击并前往最后位置，实际 " + enemy.StateLabel);
                    float destinationGap = Flat(enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().destination, expectedLastSeen);
                    Check(destinationGap < 1.2f, "追击目标应是最后可见位置，偏差 " + destinationGap.ToString("0.00"));
                    Check(Flat(enemy.transform.position, player.WorldPosition) > 3f, "敌人不应直接穿到玩家的躲藏点");
                    Begin(14);
                    break;

                case 14:
                    if (enemy.State == EnemyAwareness.Patrol)
                    {
                        Check(enemy.LostTimer <= enemy.loseSightDuration + 0.4f, "恢复巡逻时丢失计时异常 " + enemy.LostTimer.ToString("0.00"));
                        Begin(15);
                        return;
                    }

                    if (elapsed > 13f)
                    {
                        Fail("丢失视线超过 10 秒后没有恢复巡逻，状态=" + enemy.StateLabel + " 丢失=" + enemy.LostTimer.ToString("0.00"));
                        Begin(15);
                    }
                    break;

                case 15:
                    session.ResetTest();
                    enemy.FreezeMotion = true;
                    enemy.WarpTo(new Vector3(0f, 0f, -3.6f), Vector3.forward);
                    player.Teleport(new Vector3(0f, 0f, 1.6f), false);
                    Begin(16);
                    break;

                case 16:
                    if (elapsed < 1.2f)
                        return;
                    Check(enemy.State == EnemyAwareness.Suspicious, "中断测试开始时应在怀疑，实际 " + enemy.State);
                    Check(enemy.VisibleTimer >= 0.8f && enemy.VisibleTimer < 2.2f, "连续可见时间应大约 1.2 秒，实际 " + enemy.VisibleTimer.ToString("0.00"));
                    player.Teleport(new Vector3(-3.2f, 0f, 1.2f), false);
                    Begin(17);
                    break;

                case 17:
                    if (elapsed < 0.35f)
                        return;
                    Check(enemy.LastVision.reason == VisionBlockReason.Occluded, "躲到墙后应是遮挡而不是单纯超距，实际 " + Describe(enemy));
                    Check(enemy.VisibleTimer <= 0.05f, "遮挡后连续可见时间应清零，实际 " + enemy.VisibleTimer.ToString("0.00"));
                    player.Teleport(new Vector3(0f, 0f, 1.6f), false);
                    Begin(18);
                    break;

                case 18:
                    if (elapsed < 2.5f)
                        return;
                    Check(enemy.State == EnemyAwareness.Suspicious, "重新出现后 2.5 秒内不应确认，说明计时已重来。实际 " + enemy.State + " 可见=" + enemy.VisibleTimer.ToString("0.00"));
                    Begin(19);
                    break;

                case 19:
                    if (elapsed < 1.3f)
                        return;
                    Check(enemy.ConfirmCount >= 1 && enemy.State == EnemyAwareness.Chase, "重新看见并再持续 3 秒后应确认并追击，状态=" + enemy.State + " 次数=" + enemy.ConfirmCount);
                    Begin(20);
                    break;

                case 20:
                    session.ResetTest();
                    enemy.attackWindup = 0.6f;
                    enemy.FreezeMotion = true;
                    enemy.WarpTo(new Vector3(2f, 0f, -5f), Vector3.right);
                    player.Teleport(new Vector3(3.4f, 0f, -5f), false);
                    hitBaseline = session.HitCount;
                    Begin(21);
                    break;

                case 21:
                    if (elapsed < 1.1f)
                        return;
                    Check(session.HitCount == hitBaseline + 1, "正面攻击前摇结束后应命中一次，受击次数=" + session.HitCount);
                    Check(enemy.LastAttackRecheck == VisionBlockReason.Visible, "命中前的复查应为可见，实际 " + enemy.LastAttackRecheck);
                    Begin(22);
                    break;

                case 22:
                    session.ResetTest();
                    enemy.attackWindup = 1.2f;
                    enemy.attackRange = 12f;
                    enemy.viewDistance = 12f;
                    enemy.FreezeMotion = true;
                    enemy.WarpTo(new Vector3(2f, 0f, -5f), Vector3.right);
                    player.Teleport(new Vector3(3.4f, 0f, -5f), false);
                    hitBaseline = session.HitCount;
                    dodged = false;
                    Begin(23);
                    break;

                case 23:
                    if (!dodged)
                    {
                        if (enemy.State != EnemyAwareness.Attack)
                        {
                            if (elapsed > 1.5f)
                            {
                                Fail("攻击前摇测试没有进入攻击，状态=" + enemy.StateLabel + " " + Describe(enemy));
                                Begin(24);
                            }
                            return;
                        }

                        player.Teleport(new Vector3(-3.2f, 0f, 1.2f), false);
                        dodged = true;
                        dodgeStart = Time.time;
                        return;
                    }

                    if (session.HitCount > hitBaseline)
                        Fail("前摇期间躲到墙后仍然被命中");

                    // 前摇结束并复查失败后，敌人会离开攻击状态。判定要等复查写完，不能要求一直停在攻击。
                    if (Time.time - dodgeStart < enemy.attackWindup + 0.45f)
                        return;

                    Check(session.HitCount == hitBaseline, "隔墙复查后不应命中");
                    Check(enemy.LastAttackRecheck == VisionBlockReason.Occluded, "攻击复查应判定为遮挡，实际 " + enemy.LastAttackRecheck + " " + Describe(enemy));
                    Begin(24);
                    break;

                case 24:
                    Vector3 playerSpawn = player.transform.position;
                    session.ResetTest();
                    Begin(25);
                    log.AppendLine("重置前位置仅作分隔，当前=" + playerSpawn);
                    break;

                case 25:
                    if (elapsed < 0.4f)
                        return;
                    Check(session.HitCount == 0, "重置后受击次数应为 0");
                    Check(!player.IsCrouching, "重置后玩家应站立");
                    Check(Flat(player.transform.position, new Vector3(-3.6f, 0f, -0.8f)) < 0.25f, "重置后玩家应回到出生点，实际 " + player.transform.position);
                    Check(enemy.State == EnemyAwareness.Patrol, "重置后敌人应巡逻，实际 " + enemy.State);
                    Check(enemy.VisibleTimer <= 0.05f && enemy.LostTimer <= 0.05f, "重置后计时应清零");
                    Check(enemy.ConfirmCount == 0, "重置后确认次数应清零");
                    Check(Flat(enemy.transform.position, new Vector3(4.2f, 0f, -4.8f)) < 0.8f, "重置后敌人应回到出生点，实际 " + enemy.transform.position);
                    Finish();
                    break;
            }
        }

        static Vector3 SpotFeet()
        {
            GameObject spot = GameObject.Find("CrouchTestSpot");
            if (spot == null)
                return new Vector3(4.2f, 0f, 1.05f);
            return new Vector3(spot.transform.position.x, 0f, spot.transform.position.z);
        }

        static void Begin(int next)
        {
            phase = next;
            phaseStart = Time.time;
        }

        static void Check(bool condition, string message)
        {
            if (condition)
            {
                log.AppendLine("PASS " + message);
                Debug.Log("[StealthCheck] PASS " + message);
            }
            else
            {
                failures++;
                log.AppendLine("FAIL " + message);
                Debug.LogWarning("[StealthCheck] FAIL " + message);
            }
        }

        static void Fail(string message)
        {
            Check(false, message);
        }

        static string Describe(EnemyStealthController enemy)
        {
            VisionResult vision = enemy.LastVision;
            string blocker = vision.blocker != null ? vision.blocker.name : "无";
            return vision.reason + " 距离=" + vision.flatDistance.ToString("0.00") + " 角度=" + vision.angle.ToString("0") + " 遮挡=" + blocker;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static void Finish()
        {
            if (!SessionState.GetBool(KeyRun, false) && started == false)
                return;

            StealthSignals.PlayerConfirmed -= OnConfirmed;
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 0.333f;
            log.AppendLine(failures == 0 ? "RESULT PASS" : "RESULT FAIL " + failures);
            string text = log.ToString();
            WriteResult(text);
            SessionState.SetString(KeyResult, text);
            SessionState.SetBool(KeyShow, true);
            SessionState.SetBool(KeyRun, false);
            started = false;
            EditorApplication.isPlaying = false;
        }

        static void WriteResult(string text)
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "stealth-check-result.txt");
            File.WriteAllText(path, text);
        }

        static string TrimForDialog(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "没有结果。";
            const int max = 1400;
            if (text.Length <= max)
                return text;
            return text.Substring(0, max) + "\n...";
        }
    }
}
