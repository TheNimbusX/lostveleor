namespace Game.Sim
{
    public sealed partial class GameSession
    {
        public void StartEnemySandbox(ulong seed, FixVec2 hero, EnemySandboxSpawn[] spawns, LayoutObstacle[] obstacles = null)
        {
            bool immortal = DeveloperInvulnerable;
            var previousLoadout = ActiveLoadout;
            LeaveProvingGround();
            CancelRiftEntryRequest();
            ResetRunProgressTracking();
            LastRunSeed = seed;
            RunNumber++;
            IsDeveloperRun = true;
            BeginRunStats();
            var sim = new Simulation(seed, _simCapacity);
            // Стенд мобов — тестовый забег: эталонный герой 270/54 при любом профиле.
            sim.ApplyHeroBaseline(Camp.HeroBaselineFor(true));
            Camp.Worn.Bind(sim.Entities.Stats[Simulation.PlayerId]);
            var run = new RiftRun(sim, Simulation.EnemySandboxModules(), Camp.Items, _itemBaseIds);
            run.PlayerEquipment = Camp.Worn;
            run.Loadout.CopyFrom(previousLoadout);
            run.StartEnemySandbox(hero, spawns, obstacles);
            sim.PlayerInvulnerable = immortal;
            Run = run;
            Mode = GameMode.Rift;
            Generation++;
        }
    }
}
