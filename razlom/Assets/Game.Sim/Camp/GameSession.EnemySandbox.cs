namespace Game.Sim
{
    public sealed partial class GameSession
    {
        public void StartEnemySandbox(ulong seed, FixVec2 hero, EnemySandboxSpawn[] spawns, LayoutObstacle[] obstacles = null)
        {
            bool immortal = DeveloperInvulnerable;
            var previousLoadout = ActiveLoadout;
            LeaveProvingGround();
            LastRunSeed = seed;
            RunNumber++;
            IsDeveloperRun = true;
            BeginRunStats();
            var sim = new Simulation(seed, _simCapacity);
            sim.ApplyHeroBaseline();
            Camp.Worn.Bind(sim.Entities.Stats[Simulation.PlayerId]);
            var run = new RiftRun(sim, Simulation.EnemySandboxModules(), Camp.Items, _itemBaseIds);
            run.PlayerEquipment = Camp.Worn;
            run.Loadout.CopyFrom(previousLoadout);
            run.StartEnemySandbox(hero, spawns, obstacles);
            sim.PlayerInvulnerable = immortal;
            Run = run;
            Mode = GameMode.Rift;
            _alchemyLevelWithoutPotion = false;
            Generation++;
        }
    }
}
