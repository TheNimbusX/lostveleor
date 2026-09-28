using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [DisallowMultipleComponent]
    public sealed class EnemyTestArena : MonoBehaviour
    {
        [Min(0)] public int Seed = 42;
        public Transform PlayerSpawn;
        public Transform SpawnRoot;
        public Transform ObstacleRoot;
        public Bootstrap Bootstrap;
        public TickDriver Driver => Bootstrap != null ? Bootstrap.Driver : null;

        internal EnemySandboxSpawn[] CaptureSpawns()
        {
            var result = new List<EnemySandboxSpawn>();
            if (SpawnRoot != null)
                foreach (var point in SpawnRoot.GetComponentsInChildren<EnemyTestSpawnPoint>())
                    if (point.enabled && point.SpawnOnStart)
                        result.Add(new EnemySandboxSpawn(point.Kind, TickDriver.SandboxPosition(point.transform.position),
                            point.Count, point.HealthPercent));
            return result.ToArray();
        }

        internal LayoutObstacle[] CaptureObstacles()
        {
            var result = new List<LayoutObstacle>();
            if (ObstacleRoot != null)
                foreach (var obstacle in ObstacleRoot.GetComponentsInChildren<EnemyTestObstacle>())
                    if (obstacle.enabled)
                        result.Add(new LayoutObstacle(TickDriver.SandboxPosition(obstacle.transform.position),
                            Fix64.FromDouble(obstacle.WorldRadius), 0));
            return result.ToArray();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(.35f, .8f, .95f, .8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(Simulation.EnemySandboxSize, .1f, Simulation.EnemySandboxSize));
            if (PlayerSpawn == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(PlayerSpawn.position + Vector3.up * .5f, .5f);
        }
    }
}
