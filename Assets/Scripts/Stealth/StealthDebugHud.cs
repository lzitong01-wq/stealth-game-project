using UnityEngine;

namespace StealthPrototype
{
    /// <summary>
    /// Game 视图里的调试信息。字体使用系统黑体，方便显示中文。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Stealth/Debug HUD")]
    public class StealthDebugHud : MonoBehaviour
    {
        public StealthTestSession session;

        GUIStyle panel;
        GUIStyle title;
        GUIStyle body;
        GUIStyle button;
        GUIStyle banner;
        Font font;
        bool stylesReady;
        bool showDetails;

        void Awake()
        {
            if (session == null)
                session = FindObjectOfType<StealthTestSession>();
        }

        void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            EnsureStyles();
            DrawPanel();
            DrawBanner();
        }

        void EnsureStyles()
        {
            if (stylesReady)
                return;

            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "Arial" },
                16);

            panel = new GUIStyle(GUI.skin.box);
            panel.font = font;
            panel.alignment = TextAnchor.UpperLeft;
            panel.normal.textColor = Color.white;
            panel.padding = new RectOffset(8, 8, 6, 6);

            title = new GUIStyle(GUI.skin.label);
            title.font = font;
            title.fontSize = 15;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(1f, 0.92f, 0.55f);

            body = new GUIStyle(GUI.skin.label);
            body.font = font;
            body.fontSize = 14;
            body.normal.textColor = Color.white;
            body.wordWrap = true;
            body.richText = true;

            button = new GUIStyle(GUI.skin.button);
            button.font = font;
            button.fontSize = 13;

            banner = new GUIStyle(GUI.skin.label);
            banner.font = font;
            banner.fontSize = 28;
            banner.fontStyle = FontStyle.Bold;
            banner.alignment = TextAnchor.MiddleCenter;
            banner.normal.textColor = new Color(1f, 0.35f, 0.3f);

            stylesReady = true;
        }

        void DrawPanel()
        {
            const float width = 300f;
            float height = showDetails ? 250f : 112f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, height), panel);

            if (session == null || session.enemy == null)
            {
                GUILayout.Label("没有找到敌人。", body);
                GUILayout.EndArea();
                return;
            }

            EnemyStealthController enemy = session.enemy;
            VisionResult vision = enemy.LastVision;
            string crouch = session.player != null && session.player.IsCrouching ? "下蹲" : "站立";
            StealthGroundDebug ground = enemy.GetComponent<StealthGroundDebug>();

            GUILayout.Label("敌人状态：" + enemy.StateLabel, title);
            GUILayout.Label("玩家姿态：" + crouch, body);
            GUILayout.Label("是否可见：" + StealthVision.Describe(vision.reason), body);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(showDetails ? "收起详情" : "详情", button, GUILayout.Height(22f)))
                showDetails = !showDetails;
            if (ground != null && GUILayout.Button(ground.showSightLine ? "视线：开" : "视线：关", button, GUILayout.Height(22f)))
                ground.showSightLine = !ground.showSightLine;
            GUILayout.EndHorizontal();

            if (showDetails)
            {
                string blocker = vision.blocker != null ? vision.blocker.name : "无";
                string head = session.player != null && session.player.HeadBlocked ? "\n头顶被挡住，暂时无法站起。" : "";
                GUILayout.Label(
                    "距离：" + vision.flatDistance.ToString("0.00") + " / " + enemy.viewDistance.ToString("0.00") + " 米" + Mark(vision.inRange) +
                    "\n角度：" + vision.angle.ToString("0") + "° / " + (enemy.viewAngle * 0.5f).ToString("0") + "°（半角）" + Mark(vision.inAngle) +
                    "\n遮挡：" + blocker +
                    "\n连续可见：" + enemy.VisibleTimer.ToString("0.00") + " / " + enemy.confirmDelay.ToString("0.0") + " 秒" +
                    "\n连续丢失：" + enemy.LostTimer.ToString("0.00") + " / " + enemy.loseSightDuration.ToString("0.0") + " 秒" +
                    "\n受击次数：" + session.HitCount +
                    head +
                    "\n黄扇形=视野  青扇形=立即发现" +
                    "\n绿线=视线到达  红线=被墙或柜台截断" +
                    "\nWASD 移动   左 Ctrl 蹲伏   R 重置   V 视线",
                    body);
            }

            GUILayout.EndArea();
        }

        static string Mark(bool passed)
        {
            return passed ? "  通过" : "  未通过";
        }

        void DrawBanner()
        {
            if (session == null || Time.time > session.BannerUntil)
                return;

            float width = 420f;
            Rect rect = new Rect((Screen.width - width) * 0.5f, 28f, width, 64f);
            GUI.Label(rect, "玩家被击中", banner);
        }
    }
}
