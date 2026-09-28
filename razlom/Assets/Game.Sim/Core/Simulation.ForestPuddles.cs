using System;

namespace Game.Sim
{
    /// <summary>
    /// Кислая лужа гнилого плода. Изменяемая структура пула луж: каждое поле
    /// обязано попасть в HashForestPuddles.
    /// </summary>
    public struct ForestPuddleState
    {
        /// <summary>Номер лужи; 0 — места нет.</summary>
        public int Serial;

        /// <summary>Чей плод. Лужа переживает стрелка: он может быть уже мёртв.</summary>
        public int Source;

        /// <summary>Падение плода, начало урона, конец урона, уход с земли (после угасания).</summary>
        public int StartTick, ArmTick, EndTick, GoneTick;

        /// <summary>Урон тика, снятый при падении: смерть стрелка его не меняет.</summary>
        public int Damage;

        public FixVec2 Center;
        public Fix64 Radius;

        /// <summary>Жжёт ли лужа в тик tick: после созревания и до конца.</summary>
        public bool ArmedAt(int tick) => Serial != 0 && tick >= ArmTick && tick < EndTick;
    }

    /// <summary>
    /// КИСЛЫЕ ЛУЖИ ПЛЮЙ-ПЛОДА (выбранный владельцем вариант 2,
    /// 2-acid-puddle-flat: вязкая яркая лаймовая лужа с неровным краем,
    /// остатком объёмного плода, каплями и пузырями).
    ///
    /// Каждый третий залп четвёртый плод гнилой: диск шире (1,3 м против
    /// 0,8), тот же полёт и урон удара. Где он упал, через 9 тиков созревает
    /// лужа 1,2 м и живёт 105 тиков, потом 9 тиков угасает без урона. Раз в 15
    /// тиков после созревания жжёт героя, если его центр в 1,45 м от центра
    /// лужи: доля урона стрелка 5/11 (5 на первой арене). Сколько бы луж ни
    /// перекрылось, герой получает не больше одного тика кислоты за 15 тиков:
    /// простоять в луже всю её жизнь — не больше 30 здоровья, 11% эталонного
    /// героя. Луж не больше четырёх — новая вытесняет самую старую; гнилой
    /// плод откладывается до следующего залпа, если луж уже три или цель в
    /// 2,5 м от живой лужи. Мобы кислоты не боятся, но в лужах не стоят
    /// (Simulation.EnemySurround — InAllyDanger).
    /// </summary>
    public sealed partial class Simulation
    {
        public const int ForestPuddleCapacity = 4;
        public const int PuddleArmTicks = 9, PuddleLifeTicks = 105, PuddleFadeTicks = 9, PuddlePulseTicks = 15;
        public const int PuddlePulseDamage = 5;
        public static readonly Fix64 PuddleRadius = Fix64.Ratio(6, 5);
        public static readonly Fix64 PuddleHitRadius = Fix64.Ratio(29, 20);
        public static readonly Fix64 RottenFruitRadius = Fix64.Ratio(13, 10);

        /// <summary>Каждый какой залп несёт гнилой плод и какой по счёту плод в нём гнилой.</summary>
        public const int RottenVolleyEvery = 3, RottenShotIndex = 3;

        /// <summary>Гнилой откладывается, если живых луж столько или больше.</summary>
        public const int RottenMaxLivePuddles = 3;
        public static readonly Fix64 RottenPuddleSpacing = Fix64.Ratio(5, 2);

        private readonly ForestPuddleState[] _puddles = new ForestPuddleState[ForestPuddleCapacity];
        private int _puddleSerial, _puddleHeroPulseTick = int.MinValue / 2;
        private int[] _budVolleys;
        private bool[] _budRotPending;

        public bool TryGetForestPuddle(int slot, out ForestPuddleState puddle)
        {
            puddle = (uint)slot < (uint)_puddles.Length ? _puddles[slot] : default;
            return puddle.Serial != 0;
        }

        /// <summary>Урон тика кислоты от урона стрелка: доля 5/11, как в таблице видов.</summary>
        public static int PuddleDamageOf(int budDamage)
            => Math.Max(1, EnemyArchetypes.Share(budDamage, PuddlePulseDamage, EnemyArchetypes.ForestBudDamage));

        private void AllocateForestPuddles(int capacity)
        {
            _budVolleys = new int[capacity];
            _budRotPending = new bool[capacity];
        }

        private void ResetForestPuddles()
        {
            Array.Clear(_puddles, 0, _puddles.Length);
            Array.Clear(_budVolleys, 0, _budVolleys.Length);
            Array.Clear(_budRotPending, 0, _budRotPending.Length);
            _puddleSerial = 0;
            // Половина MinValue: Tick − это значение не переполняется.
            _puddleHeroPulseTick = int.MinValue / 2;
        }

        private int LivePuddles()
        {
            int live = 0;
            for (int k = 0; k < _puddles.Length; k++) if (_puddles[k].Serial != 0 && Tick < _puddles[k].EndTick) live++;
            return live;
        }

        /// <summary>Можно ли гнилому упасть в target: луж меньше трёх и ни одной ближе 2,5 м.</summary>
        private bool RottenFruitAllowed(FixVec2 target)
        {
            if (LivePuddles() >= RottenMaxLivePuddles) return false;
            Fix64 spacing = RottenPuddleSpacing * RottenPuddleSpacing;
            for (int k = 0; k < _puddles.Length; k++)
                if (_puddles[k].Serial != 0 && Tick < _puddles[k].EndTick
                    && FixVec2.DistanceSq(_puddles[k].Center, target) < spacing) return false;
            return true;
        }

        /// <summary>
        /// Лужа на месте падения гнилого плода. Места нет — вытесняется самая
        /// старая (меньший номер), и её угасание вид показывает по PuddleClosed.
        /// </summary>
        private void OpenForestPuddle(int source, FixVec2 center, int budDamage)
        {
            int slot = -1;
            for (int k = 0; k < _puddles.Length && slot < 0; k++) if (_puddles[k].Serial == 0) slot = k;
            if (slot < 0)
            {
                slot = 0;
                for (int k = 1; k < _puddles.Length; k++) if (_puddles[k].Serial < _puddles[slot].Serial) slot = k;
                _events.Add(new SimEvent(SimEventType.PuddleClosed, _puddles[slot].Source, -1, slot, true,
                    _puddles[slot].Center, actionVariant: _puddles[slot].Serial));
            }
            int arm = Tick + PuddleArmTicks;
            _puddles[slot] = new ForestPuddleState
            {
                Serial = ++_puddleSerial, Source = source, StartTick = Tick, ArmTick = arm,
                EndTick = arm + PuddleLifeTicks, GoneTick = arm + PuddleLifeTicks + PuddleFadeTicks,
                Damage = PuddleDamageOf(budDamage), Center = center, Radius = PuddleRadius,
            };
            _events.Add(new SimEvent(SimEventType.PuddleOpened, source, -1, slot, false, center,
                actionVariant: _puddleSerial));
        }

        /// <summary>
        /// Лужи за тик: кислота по герою (один тик на все лужи раз в 15), уход
        /// угасших. Зовётся в Step сразу после UpdateForestBud — лужа от плода,
        /// упавшего в этот тик, уже открыта.
        /// </summary>
        private void UpdateForestPuddles()
        {
            for (int k = 0; k < _puddles.Length; k++)
            {
                var p = _puddles[k];
                if (p.Serial == 0 || Tick < p.GoneTick) continue;
                _puddles[k] = default;
                _events.Add(new SimEvent(SimEventType.PuddleClosed, p.Source, -1, k, false, p.Center,
                    actionVariant: p.Serial));
            }
            if (!Entities.Alive[PlayerId] || Tick - _puddleHeroPulseTick < PuddlePulseTicks) return;
            FixVec2 hero = Entities.Position[PlayerId];
            Fix64 reach = PuddleHitRadius * PuddleHitRadius;
            int burning = -1;
            for (int k = 0; k < _puddles.Length; k++)
            {
                var p = _puddles[k];
                if (!p.ArmedAt(Tick) || (Tick - p.ArmTick) % PuddlePulseTicks != 0 || Tick == p.ArmTick) continue;
                if (FixVec2.DistanceSq(hero, p.Center) > reach) continue;
                if (burning < 0 || p.Damage > _puddles[burning].Damage) burning = k;
            }
            if (burning < 0) return;
            _puddleHeroPulseTick = Tick;
            ApplyAbilityDamage(_puddles[burning].Source, PlayerId, _puddles[burning].Damage, -1,
                DamageType.Physical, overTime: true);
        }

        /// <summary>Стоит ли тело в жгущей (или созревающей) луже — для мобов, которые из неё выходят.</summary>
        private bool InPuddleDanger(FixVec2 point, Fix64 body, out FixVec2 escape)
        {
            escape = FixVec2.Zero;
            for (int k = 0; k < _puddles.Length; k++)
            {
                var p = _puddles[k];
                if (p.Serial == 0 || Tick >= p.EndTick) continue;
                Fix64 reach = p.Radius + body;
                FixVec2 offset = point - p.Center;
                if (offset.LengthSq > reach * reach) continue;
                escape = offset.LengthSq.Raw == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : offset.Normalized();
                return true;
            }
            return false;
        }

        /// <summary>Сколько живых луж в 8 м от героя — для бюджета крупных меток (все вместе весят 1).</summary>
        private bool PuddleNearHero()
        {
            FixVec2 hero = Entities.Position[PlayerId];
            Fix64 near = Fix64.FromInt(8) + PuddleRadius;
            for (int k = 0; k < _puddles.Length; k++)
                if (_puddles[k].Serial != 0 && Tick < _puddles[k].EndTick
                    && FixVec2.DistanceSq(hero, _puddles[k].Center) <= near * near) return true;
            return false;
        }

        /// <summary>Песочные Часы: лужи не зреют и не сохнут, пока стоит время.</summary>
        private void DelayForestPuddles(int ticks)
        {
            for (int k = 0; k < _puddles.Length; k++)
            {
                if (_puddles[k].Serial == 0) continue;
                _puddles[k].ArmTick += ticks; _puddles[k].EndTick += ticks; _puddles[k].GoneTick += ticks;
            }
        }

        private void HashForestPuddles(ref ulong hash)
        {
            if (_puddleSerial == 0) return;
            Hashing.Mix(ref hash, 0x50554444); Hashing.Mix(ref hash, _puddleSerial);
            Hashing.Mix(ref hash, _puddleHeroPulseTick);
            for (int k = 0; k < _puddles.Length; k++)
            {
                var p = _puddles[k];
                Hashing.Mix(ref hash, p.Serial);
                if (p.Serial == 0) continue;
                Hashing.Mix(ref hash, p.Source); Hashing.Mix(ref hash, p.StartTick); Hashing.Mix(ref hash, p.ArmTick);
                Hashing.Mix(ref hash, p.EndTick); Hashing.Mix(ref hash, p.GoneTick); Hashing.Mix(ref hash, p.Damage);
                Hashing.Mix(ref hash, p.Center.X); Hashing.Mix(ref hash, p.Center.Y); Hashing.Mix(ref hash, p.Radius);
            }
        }

        private void HashBudVolleys(ref ulong hash)
        {
            for (int id = 1; id < Entities.Count; id++)
            {
                if (_budVolleys[id] == 0 && !_budRotPending[id]) continue;
                Hashing.Mix(ref hash, id); Hashing.Mix(ref hash, _budVolleys[id]); Hashing.Mix(ref hash, _budRotPending[id] ? 1 : 0);
            }
        }
    }
}
