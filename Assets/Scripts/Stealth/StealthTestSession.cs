using UnityEngine;

namespace StealthPrototype
{
    /// <summary>
    /// 整场测试的重置、受击计数和计时。R 键从这里统一重置玩家和敌人。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Stealth/Test Session")]
    public class StealthTestSession : MonoBehaviour
    {
        public PrototypePlayerController player;
        public EnemyStealthController enemy;

        public int HitCount { get; private set; }
        public float BannerUntil { get; private set; }
        public float Elapsed => Time.time - elapsedAnchor;

        float elapsedAnchor;

        void Awake()
        {
            if (player == null)
                player = FindObjectOfType<PrototypePlayerController>();
            if (enemy == null)
                enemy = FindObjectOfType<EnemyStealthController>();
        }

        void OnEnable()
        {
            StealthSignals.PlayerHit += HandleHit;
            elapsedAnchor = Time.time;
        }

        void OnDisable()
        {
            StealthSignals.PlayerHit -= HandleHit;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
                ResetTest();
        }

        void HandleHit(StealthHitInfo info)
        {
            HitCount++;
            BannerUntil = Time.time + 2.2f;
        }

        public void ResetTest()
        {
            HitCount = 0;
            BannerUntil = -1f;
            elapsedAnchor = Time.time;
            if (player != null)
                player.ResetState();
            if (enemy != null)
                enemy.ResetState();
        }
    }
}
