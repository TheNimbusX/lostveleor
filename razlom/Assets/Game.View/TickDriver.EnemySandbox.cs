using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        // Assigned on the inactive runtime root, before Awake can load a save.
        public EnemyTestArena EnemySandbox { get; set; }
        private bool _enemySandboxResetPending;

        public void QueueEnemySandboxReset()
        {
            if (EnemySandbox != null) _enemySandboxResetPending = true;
        }

        public void QueueEnemySandboxSpawn(EnemyKind kind, Vector3 position, int count = 1, int healthPercent = 100)
        {
            if (EnemySandbox == null || Sim == null) return;
            Sim.QueueEnemySandbox(EnemySandboxAction.Spawn,
                new EnemySandboxSpawn(kind, SandboxPosition(position), count, healthPercent));
        }

        public void QueueEnemySandboxAction(EnemySandboxAction action, int entity = -1)
        {
            if (EnemySandbox != null && Sim != null) Sim.QueueEnemySandbox(action, entity: entity);
        }

        internal void StartEnemySandbox()
        {
            if (EnemySandbox == null || Session == null) return;
            _enemySandboxResetPending = false;
            Session.StartEnemySandbox((ulong)Mathf.Max(0, EnemySandbox.Seed),
                SandboxPosition(EnemySandbox.PlayerSpawn != null ? EnemySandbox.PlayerSpawn.position : Vector3.zero),
                EnemySandbox.CaptureSpawns(), EnemySandbox.CaptureObstacles());
            ClearCapturedInput();
            SyncGeneration();
        }

        internal static FixVec2 SandboxPosition(Vector3 at)
            => new FixVec2(QuantizePosition(at.x), QuantizePosition(at.z));
    }
}
