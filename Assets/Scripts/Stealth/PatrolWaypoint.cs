using UnityEngine;

namespace StealthPrototype
{
    /// <summary>
    /// 巡逻点。敌人走到该点后停留 pauseSeconds 秒。
    /// </summary>
    public class PatrolWaypoint : MonoBehaviour
    {
        public float pauseSeconds = 1.1f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.15f, 0.18f);
        }
    }
}
