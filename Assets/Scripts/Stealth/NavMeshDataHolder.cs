using UnityEngine;
using UnityEngine.AI;

namespace StealthPrototype
{
    /// <summary>
    /// 把烘焙好的导航网格注册进场景。执行顺序早于敌人，避免代理启用时还没有可行走区域。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class NavMeshDataHolder : MonoBehaviour
    {
        public NavMeshData data;

        NavMeshDataInstance instance;

        void OnEnable()
        {
            EnsureLoaded();
        }

        void OnDisable()
        {
            RemoveInstance();
        }

        void OnDestroy()
        {
            RemoveInstance();
        }

        void RemoveInstance()
        {
            if (!instance.valid)
                return;
            NavMesh.RemoveNavMeshData(instance);
            instance = default;
        }

        public void EnsureLoaded()
        {
            if (instance.valid || data == null)
                return;
            instance = NavMesh.AddNavMeshData(data);
        }
    }
}
