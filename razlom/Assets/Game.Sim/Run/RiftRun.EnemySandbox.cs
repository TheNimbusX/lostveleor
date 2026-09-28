namespace Game.Sim
{
    public sealed partial class RiftRun
    {
        public bool IsEnemySandbox { get; private set; }

        internal void StartEnemySandbox(FixVec2 hero, EnemySandboxSpawn[] spawns, LayoutObstacle[] obstacles)
        {
            IsEnemySandbox = true;
            Depth = 1;
            LayoutSeed = SpawnSeed = _sim.Rng.MasterSeed;
            LevelSettings = RiftLevelSettings.Prototype(1);
            _map.Clear();
            _map.TryPlace(0, 0, -Simulation.EnemySandboxSize / 4, -Simulation.EnemySandboxSize / 4);
            if (obstacles != null) foreach (var obstacle in obstacles) _map.AddTestObstacle(obstacle);
            _map.BuildRoutes();
            _sim.SetupEnemySandbox(_map, hero, spawns);
            PlayerEquipment?.Reapply();
            for (int id = 0; id < _enemyBranch.Length; id++) _enemyBranch[id] = -1;
            ApplyLoadout();
            Phase = RunPhase.Clearing;
        }
    }
}
