using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        public static bool TempoPlaytestRequested { get; } =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pelag-tempo-playtest") >= 0;
        private bool _tempoPlaytestStarted;
        private int _tempoCaptureStart = -1;
        private int _tempoCaptureChoice;

        private void StartTempoPlaytestIfRequested()
        {
            if (!TempoPlaytestRequested || _tempoPlaytestStarted) return;
            _tempoPlaytestStarted = true;
            StartTempoTest(new[] { 0, 1, 8, 9 }, 2);
        }

        private void PrepareTempoCapture(ref InputFrame frame)
        {
            frame = InputFrame.Empty;
            if (Sim == null || !Session.IsDeveloperRun || CaptureRig.GcWarmupActive)
            { _tempoCaptureStart = -1; _tempoCaptureChoice = 0; return; }
            if (_tempoCaptureStart < 0) _tempoCaptureStart = Sim.Tick;
            int elapsed = Sim.Tick - _tempoCaptureStart;
            var player = Sim.Entities.Position[0];
            int target = -1; Fix64 distance = Fix64.FromInt(10000);
            for(int i=1;i<Sim.Entities.Count;i++)
            {
                if(!Sim.Entities.Alive[i])continue;
                var d=(Sim.Entities.Position[i]-player).LengthSq;
                if(d<distance){distance=d;target=i;}
            }
            var aim = target >= 0 ? Sim.Entities.Position[target] : player + new FixVec2(Fix64.FromInt(4),Fix64.Zero);
            frame.Aim = aim; frame.AbilityTarget = target;
            frame.Flags = (byte)InputFlags.DirectMovement;
            if(distance>Fix64.FromInt(7))frame.MoveDirection=(aim-player).Normalized();
            // Тот же приоритет и те же ограничения, что при ручных нажатиях. Никаких возвратов ресурса стендом.
            if(elapsed>=18 && elapsed<318 && elapsed%3==0 && Sim.PlayerAction.CanChainAt(Sim.Tick))
            {
                for(int i=0;i<5;i++)
                {
                    int slot=(_tempoCaptureChoice+i)%5;var build=Sim.GetAbility(slot);
                    if(build==null || Sim.Tick<Sim.AbilityReadyTick(slot)
                        || Sim.Entities.Lavidium[0]<build.Get(AbilityStatType.LavidiumCost))continue;
                    frame.AbilityMask=(byte)(1<<slot);_tempoCaptureChoice=(slot+1)%5;break;
                }
            }
        }

        public void StartTempoTest(int[] pool, int preset)
        {
            if (pool == null || pool.Length != 4) throw new System.ArgumentException("Выберите четыре навыка.");
            for (int i = 0; i < 4; i++)
            {
                if ((uint)pool[i] >= PelagKit.PoolSize) throw new System.ArgumentException("Неизвестная способность.");
                for (int j = 0; j < i; j++) if (pool[i] == pool[j]) throw new System.ArgumentException("Навыки должны различаться.");
            }
            byte[] before = CampSaveCodec.Encode(Session.Camp);
            StartForestBudTest(GetComponent<LayoutView>().Profile, 20260829UL);
            var loadout = Run.Loadout;
            for (int i = 0; i < 4; i++) loadout.Put(i, RunLoadout.EmptySlot);
            for (int i = 0; i < 4; i++) loadout.Put(i, pool[i]);
            Run.ApplyLoadout();
            RefreshAbilityBuild();
            CombatTempoPreset.Apply(Sim, preset);
            Sim.AddTempoMeleeEnemies(4);
            if(CaptureRig.TempoPreset>=0)
                for(int i=1;i<Sim.Entities.Count;i++)
                {
                    // В записи десятисекундной связки цели живут дольше; ручной бой использует штатное здоровье.
                    Sim.Entities.Stats[i].SetBase(StatType.MaxHealth,Fix64.FromInt(5000));
                    Sim.Entities.RefreshStats(i);Sim.Entities.Health[i]=5000;
                }
            _tempoCaptureStart = -1; _tempoCaptureChoice = 0;
            byte[] after = CampSaveCodec.Encode(Session.Camp);
            bool same = before.Length == after.Length;
            for(int i=0;same && i<before.Length;i++)same=before[i]==after[i];
            if(!same)Debug.LogError("[pelag-tempo] Тест изменил сохранённое состояние лагеря.");
            Debug.Log("[pelag-tempo] Стенд запущен: сборка " + preset + ". Прогресс лагеря не меняется.");
        }
    }
}
