using System;
using System.Collections.Generic;

namespace Game.Sim
{
    public readonly struct EnemySandboxSpawn
    {
        public readonly EnemyKind Kind;
        public readonly FixVec2 Position;
        public readonly int Count, HealthPercent;
        public EnemySandboxSpawn(EnemyKind kind, FixVec2 position, int count = 1, int healthPercent = 100)
        { Kind = kind; Position = position; Count = count; HealthPercent = healthPercent; }
    }

    public enum EnemySandboxAction : byte { Spawn, Remove, Kill, Clear }

    public sealed partial class Simulation
    {
        private readonly struct SandboxCommand
        {
            public readonly EnemySandboxAction Action;
            public readonly EnemySandboxSpawn Spawn;
            public readonly int Entity;
            public SandboxCommand(EnemySandboxAction action, EnemySandboxSpawn spawn, int entity)
            { Action = action; Spawn = spawn; Entity = entity; }
        }

        private readonly List<SandboxCommand> _sandboxCommands = new List<SandboxCommand>(16);
        public bool IsEnemySandbox { get; private set; }
        public string EnemySandboxError { get; private set; }
        public const int EnemySandboxSize = 40;

        public static ModuleSet EnemySandboxModules() => new ModuleSet(new[] {
            new ModuleDefinition("enemy.sandbox", EnemySandboxSize / 2, EnemySandboxSize / 2,
                Array.Empty<ModuleConnector>(), isEntrance: true) });

        internal void SetupEnemySandbox(LayoutMap map, FixVec2 hero, EnemySandboxSpawn[] spawns)
        {
            SetupRift(map, Rng.MasterSeed, 0, 0, 1);
            IsEnemySandbox = true;
            _campWalkMap = null;
            _sandboxCommands.Clear();
            _events.Clear();
            _eliteMask = new bool[Entities.Capacity];
            Entities.Position[PlayerId] = map.ClampToWalkable(hero, Entities.BodyRadius[PlayerId]);
            if (spawns != null) foreach (var spawn in spawns) SpawnSandboxGroup(spawn);
            Grid.Rebuild(Entities);
        }

        public void QueueEnemySandbox(EnemySandboxAction action, EnemySandboxSpawn spawn = default, int entity = -1)
        {
            if (!IsEnemySandbox) throw new InvalidOperationException("Открой тестовый стенд мобов.");
            if (_sandboxCommands.Count >= 128) { EnemySandboxError = "Очередь заполнена. Сними паузу."; return; }
            _sandboxCommands.Add(new SandboxCommand(action, spawn, entity));
        }

        // Called immediately after Step clears its event buffer: commands and their visual
        // events belong to this tick, including a real kill's pending splitter children.
        private void ApplyEnemySandboxCommands()
        {
            if (!IsEnemySandbox || _sandboxCommands.Count == 0) return;
            EnemySandboxError = null;
            foreach (var command in _sandboxCommands)
            {
                if (command.Action == EnemySandboxAction.Spawn) SpawnSandboxGroup(command.Spawn);
                else if (command.Action == EnemySandboxAction.Clear)
                {
                    for (int id = 1; id < Entities.Count; id++) RemoveSandboxEnemy(id);
                    ResetForestBud();
                    ResetForestMobs();
                }
                else if (ValidSandboxEnemy(command.Entity))
                {
                    if (command.Action == EnemySandboxAction.Kill) Kill(command.Entity, PlayerId, -1);
                    else RemoveSandboxEnemy(command.Entity);
                }
            }
            _sandboxCommands.Clear();
            Grid.Rebuild(Entities);
        }

        private bool ValidSandboxEnemy(int id) => id > PlayerId && id < Entities.Count
            && Entities.Alive[id] && Entities.Side[id] != Faction.Wole;

        private void SpawnSandboxGroup(EnemySandboxSpawn spawn)
        {
            if (!EnemyArchetypes.IsDefined(spawn.Kind) || spawn.Count < 1 || spawn.Count > 40
                || spawn.HealthPercent < 1 || spawn.HealthPercent > 100)
            { EnemySandboxError = "Проверь вид, число (1–40) и здоровье (1–100%)."; return; }
            int reserved = PendingSplitCount * SplitChildren;
            for (int id = 1; id < Entities.Count; id++)
                if (Entities.Alive[id] && Entities.Kind[id] == EnemyKind.ForestSplitter) reserved += SplitChildren;
            int required = spawn.Count * EnemyArchetypes.BodiesPerSpawn(spawn.Kind);
            if (Entities.Count + reserved + required > Entities.Capacity)
            { EnemySandboxError = "Лимит созданных мобов. Сбрось бой, чтобы освободить слоты."; return; }
            Fix64 radius = ArchetypeBodyRadius(spawn.Kind);
            Fix64 spacing = radius * 2 + Fix64.Ratio(1, 2);
            for (int n = 0; n < spawn.Count; n++)
            {
                int rank = (n + 1) / 2 * ((n & 1) == 0 ? -1 : 1);
                var at = spawn.Position + new FixVec2(Fix64.Zero, spacing * rank);
                at = _layout.ClampToWalkable(at, radius);
                int id = SpawnScaledEnemy(at, spawn.Kind, 100, 100, 100);
                Entities.Health[id] = Math.Max(1, Entities.MaxHealth[id] * spawn.HealthPercent / 100);
                Entities.Facing[id] = (Entities.Position[PlayerId] - at).Normalized();
                Entities.Aggro[id] = true;
                _eliteMask[id] = spawn.Kind == EnemyKind.ForestWendigo || spawn.Kind == EnemyKind.ForestThorncaster;
                _events.Add(SimEvent.Spawn(id, at));
            }
        }

        private void RemoveSandboxEnemy(int id)
        {
            if (!ValidSandboxEnemy(id)) return;
            Entities.Alive[id] = false;
            Entities.Velocity[id] = FixVec2.Zero;
            Entities.ForcedTicksLeft[id] = 0;
            CancelEnemySwing(id);
            CancelTelegraphsOf(id);
            _thornShots[id] = default;
            Statuses.ClearBurn(id);
            Statuses.StunUntilTick[id] = 0;
            if (_eliteMask != null) _eliteMask[id] = false;
            // Explicit removal also removes this enemy's lingering fruit/puddles.
            // A real kill deliberately leaves them alive under the ordinary rules.
            for (int slot = 0; slot < _forestFruitHighWater; slot++)
                if (_forestFruits[slot].Serial != 0 && _forestFruits[slot].Source == id)
                { _forestFruits[slot] = default; _forestFruitActiveCount--; }
            for (int slot = 0; slot < _puddles.Length; slot++)
                if (_puddles[slot].Source == id) _puddles[slot] = default;
            _events.Add(SimEvent.Burrow(id, Entities.Position[id]));
        }
    }
}
