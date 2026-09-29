using System;

namespace Game.Sim
{
    public readonly struct CampDummyDefinition
    {
        public readonly FixVec2 Position;
        public readonly int Health;
        public readonly Fix64 Armor, FireResist;
        public CampDummyDefinition(FixVec2 position, int health, Fix64 armor, Fix64 fireResist)
        {
            if (health <= 0) throw new ArgumentOutOfRangeException(nameof(health));
            Position = position; Health = health; Armor = armor; FireResist = fireResist;
        }
    }

    // Мишени живут в общей симуляции лагеря: экипировка и способности те же, что в забеге.
    //
    // ЗОНА ПОЛИГОНА (владелец, 29 сентября): бить и колдовать в лагере можно только на
    // огороженном полигоне у манекенов, кувырок и зелья — везде. Зона — круг на весь
    // полигон; представление меряет его по забору и передаёт при настройке. По этому же
    // кругу HUD прячет и возвращает свою боевую часть — у запрета и у экрана одна граница.
    // Круг — неизменная настройка, а не состояние боя: в StateHash его нет, как нет и
    // счётчиков замера.
    public sealed class CampTraining
    {
        /// <summary>
        /// Запас зоны вокруг манекенов, когда полигон не измерен (старые сцены и тесты):
        /// прежняя дистанция панели тренировки, 2,75 м, вокруг рамки всех манекенов.
        /// </summary>
        public static readonly Fix64 FallbackZoneMargin = Fix64.Ratio(11, 4);

        /// <summary>Какие способности нажимаются вне полигона: только кувырок, пятый слот.</summary>
        public const byte AbilitiesOutsideZone = 1 << PelagKit.DashSlot;

        readonly CampDummyDefinition[] _definitions;
        readonly int[] _ids;

        /// <summary>Центр зоны полигона в координатах симуляции (x, z мира).</summary>
        public FixVec2 ZoneCenter { get; }

        /// <summary>Радиус зоны полигона; граница входит в зону.</summary>
        public Fix64 ZoneRadius { get; }

        public long DamageTotal { get; private set; }
        public long FireDamage { get; private set; }
        public long Hits { get; private set; }
        public long Crits { get; private set; }
        public int LastDamage { get; private set; }
        public int Ticks { get; private set; }
        public int Count => _ids.Length;
        public int DamagePerSecond => Ticks == 0 ? 0 : (int)Math.Min(int.MaxValue, DamageTotal * Simulation.TicksPerSecond / Ticks);
        public int EntityId(int index) => _ids[index];
        public bool Contains(int id) => Array.IndexOf(_ids, id) >= 0;

        public CampTraining(CampDummyDefinition[] definitions) : this(definitions, FixVec2.Zero, Fix64.Zero) { }

        /// <param name="zoneRadius">Радиус полигона; ноль и меньше — зона по манекенам с <see cref="FallbackZoneMargin"/>.</param>
        public CampTraining(CampDummyDefinition[] definitions, FixVec2 zoneCenter, Fix64 zoneRadius)
        {
            _definitions = (CampDummyDefinition[])definitions.Clone();
            _ids = new int[definitions.Length];
            Array.Fill(_ids, -1);
            if (zoneRadius > Fix64.Zero)
            {
                ZoneCenter = zoneCenter;
                ZoneRadius = zoneRadius;
            }
            else
            {
                FallbackZone(_definitions, out FixVec2 center, out Fix64 radius);
                ZoneCenter = center;
                ZoneRadius = radius;
            }
        }

        /// <summary>Круг, в который целиком входит рамка манекенов, плюс запас на подход к крайним.</summary>
        static void FallbackZone(CampDummyDefinition[] definitions, out FixVec2 center, out Fix64 radius)
        {
            center = FixVec2.Zero;
            radius = FallbackZoneMargin;
            if (definitions.Length == 0) return;
            Fix64 minX = definitions[0].Position.X, maxX = minX, minY = definitions[0].Position.Y, maxY = minY;
            for (int i = 1; i < definitions.Length; i++)
            {
                FixVec2 p = definitions[i].Position;
                minX = Fix64.Min(minX, p.X); maxX = Fix64.Max(maxX, p.X);
                minY = Fix64.Min(minY, p.Y); maxY = Fix64.Max(maxY, p.Y);
            }
            center = new FixVec2((minX + maxX) / 2, (minY + maxY) / 2);
            radius = FixVec2.Distance(center, new FixVec2(maxX, maxY)) + FallbackZoneMargin;
        }

        /// <summary>Стоит ли точка на полигоне. Граница — ещё полигон.</summary>
        public bool InZone(FixVec2 position) => FixVec2.DistanceSq(position, ZoneCenter) <= ZoneRadius * ZoneRadius;

        /// <summary>
        /// Урезает ввод героя, который стоит вне полигона: снимаются удар, цель удара и
        /// нажатия способностей, кроме кувырка. Зажатая ЛКМ при этом не пропадает, а
        /// становится приказом идти в точку курсора — левый клик вне полигона просто
        /// ведёт героя (при движении с клавиатуры идти и так есть чем, там он молчит).
        /// Зелья сюда не доходят: их разбирает GameSession.Step до шага лагеря.
        /// </summary>
        /// <returns>true — герой вне полигона и ввод урезан.</returns>
        public bool GateInput(ref InputFrame input, FixVec2 hero)
        {
            if (InZone(hero)) return false;
            input.AbilityMask &= AbilitiesOutsideZone;
            input.AbilityHoldMask &= AbilitiesOutsideZone;
            if (input.Has(InputFlags.Attack))
            {
                input.Flags = (byte)(input.Flags & ~(int)InputFlags.Attack);
                if (!input.Has(InputFlags.DirectMovement)) input.Flags |= (byte)InputFlags.MoveOrder;
            }
            input.AttackTarget = -1;
            return true;
        }
        public void Populate(Simulation sim)
        {
            for (int i = 0; i < Count; i++)
                _ids[i] = sim.SpawnCampDummy(_definitions[i]);
            ResetCounters();
        }
        public void ResetCounters()
        {
            DamageTotal = FireDamage = Hits = Crits = 0;
            Ticks = LastDamage = 0;
        }
        public void AfterStep(Simulation sim)
        {
            foreach (var e in sim.Events)
            {
                if ((e.Type != SimEventType.Damage && e.Type != SimEventType.DamageOverTime)
                    || e.Source != Simulation.PlayerId || !Contains(e.Target)) continue;
                DamageTotal += e.Amount;
                LastDamage = e.Amount;
                if (e.DamageKind == DamageType.Fire) FireDamage += e.Amount;
                if (e.Type == SimEventType.Damage) { Hits++; if (e.Flag) Crits++; }
            }
            if (DamageTotal > 0) Ticks++;
            for (int i = 0; i < Count; i++)
            {
                int id = _ids[i];
                if (!sim.Entities.Alive[id]) sim.ReviveDummy(id);
                sim.Entities.Position[id] = _definitions[i].Position;
                sim.Entities.Velocity[id] = FixVec2.Zero;
                ForcedMotion.Clear(sim.Entities, id);
            }
        }
    }
}
