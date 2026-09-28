using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DisallowMultipleComponent]
    public sealed class EnemyTestSpawnPoint : MonoBehaviour
    {
        public EnemyKind Kind = EnemyKind.ForestGuardian;
        public bool SpawnOnStart = true;
        [Range(1, 40)] public int Count = 1;
        [Range(1, 100)] public int HealthPercent = 100;

        private void OnDrawGizmos()
        {
            Gizmos.color = SpawnOnStart ? new Color(1f, .5f, .15f) : Color.gray;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * .5f, .5f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
        }
    }
}
