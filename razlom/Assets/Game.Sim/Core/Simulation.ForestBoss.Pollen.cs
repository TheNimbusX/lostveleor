using System;

namespace Game.Sim
{
    /// <summary>
    /// Облако пыльцы Хозяина Чащи. Изменяемая структура пула: каждое поле
    /// обязано попасть в HashThicketPollen.
    /// </summary>
    public struct ThicketPollenZone
    {
        /// <summary>Номер облака; 0 — места нет.</summary>
        public int Serial;

        /// <summary>Чья пыльца.</summary>
        public int Source;

        /// <summary>Стряхнута (падает), легла, последний тик на земле (включительно).</summary>
        public int StartTick, LandTick, EndTick;

        /// <summary>Тик следующего укуса пыльцы; раз в ThicketPollenPulseTicks от падения.</summary>
        public int NextPulseTick;

        /// <summary>Песочные Часы: до этого тика облако не замедляет и не жжёт.</summary>
        public int FrozenUntil;

        /// <summary>Урон укуса, снятый при касте.</summary>
        public int Damage;

        public FixVec2 Center;
        public Fix64 Radius;

        /// <summary>Лежит ли облако на земле в тик tick (после падения и до конца включительно).</summary>
        public bool LandedAt(int tick) => Serial != 0 && tick >= LandTick && tick <= EndTick;
    }

    /// <summary>
    /// ОБЛАКА ПЫЛЬЦЫ Хозяина Чащи (этап 2, фазы 2–3). Крона стряхивает 3
    /// пятна r1,8: одно — где герой стоит в тик каста, два — по разные стороны
    /// от него в 3,4 м (поворот — свой поток босса). Падают через 24 тика,
    /// лежат 120. Пока центр героя в лежащем облаке — замедление 30%
    /// (ApplyHeroSlow каждый тик на один шаг: вышел — следующий шаг уже
    /// свободен) и укус раз в 15 тиков от падения — доля 5/41 удара босса
    /// (5 на арене 9), не больше 8 укусов за жизнь облака (40) и не больше
    /// одного укуса на все облака за 15 тиков. Облаков на земле не больше 3:
    /// новое вытесняет самое старое. Своё состояние, как у луж, не метки;
    /// падение — крупная метка весом 1 в бюджете (ThicketHazardMarkWeight).
    /// Наслоение (темп 02.10): жест босса — ThicketCastGestureTicks, облака
    /// падают уже без него; пока облака босса падают или лежат, он начинает
    /// только серии лапы (ThicketHazardActive). Смерть босса уносит его
    /// облака. EnemyActionImpact ThicketPollen — на каждое облако в тик
    /// падения (из ThicketZonesTick): Amount — слот (TryGetThicketPollenZone),
    /// Position — центр, Flag — герой в нём.
    /// </summary>
    public sealed partial class Simulation
    {
        public const int ThicketPollenZones = 3;
        public const int ThicketPollenFallTicks = 24, ThicketPollenLifeTicks = 120, ThicketPollenPulseTicks = 15;
        public const int ThicketPollenSlowPercent = 30;
        public const int ThicketPollenCooldownTicks = 270;
        public static readonly Fix64 ThicketPollenRadius = Fix64.Ratio(9, 5);

        /// <summary>Два боковых облака — в 3,4 м от героя, по разные стороны (±30° от прямой).</summary>
        public static readonly Fix64 ThicketPollenSpread = Fix64.Ratio(17, 5);
        private static readonly Fix64 ThicketPollenSideJitter = Fix64.Pi / 6;

        private ThicketPollenZone[] _thicketPollen;
        private int _thicketPollenSerial, _thicketPollenPulseTick = int.MinValue / 2;

        private ThicketPollenZone[] ThicketPollen => _thicketPollen ??= new ThicketPollenZone[ThicketPollenZones];

        public bool TryGetThicketPollenZone(int slot, out ThicketPollenZone zone)
        {
            zone = _thicketPollen != null && (uint)slot < (uint)_thicketPollen.Length ? _thicketPollen[slot] : default;
            return zone.Serial != 0;
        }

        /// <summary>Урон укуса пыльцы босса id: доля 5/41 его удара, не меньше 1.</summary>
        public int ThicketPollenDamageOf(int id) => Math.Max(1, ThicketShareOf(id, ThicketPollenDamageA9));

        /// <summary>Стоит ли центр героя в лежащем (не замороженном) облаке.</summary>
        public bool ThicketHeroInPollen()
        {
            if (_thicketPollen == null || Entities.Count <= PlayerId) return false;
            FixVec2 hero = Entities.Position[PlayerId];
            for (int k = 0; k < _thicketPollen.Length; k++)
                if (ThicketPollenTouches(_thicketPollen[k], hero)) return true;
            return false;
        }

        private bool ThicketPollenTouches(in ThicketPollenZone z, FixVec2 point)
            => z.LandedAt(Tick) && Tick >= z.FrozenUntil
                && FixVec2.DistanceSq(point, z.Center) <= z.Radius * z.Radius;

        private int ThicketLivePollenZones()
        {
            if (_thicketPollen == null) return 0;
            int live = 0;
            for (int k = 0; k < _thicketPollen.Length; k++) if (_thicketPollen[k].Serial != 0) live++;
            return live;
        }

        /// <summary>
        /// Каст (наслоение): жест ThicketCastGestureTicks, облака встают сразу и
        /// падают через 24 — уже без босса; пока облака этого босса падают или
        /// лежат, он начинает только серии лапы (ThicketHazardActive).
        /// </summary>
        private bool StartThicketPollen(int id)
        {
            int land = Tick + ThicketPollenFallTicks;
            if (!BigMarkAllowed(id, 1, land)) return false;
            FixVec2 hero = Entities.Position[PlayerId];
            ref var a = ref BeginThicketCast(id, ThicketMasterAction.Pollen);
            ref var rng = ref ThicketMemory[id].Rng;
            Fix64 angle = rng.NextFix() * Fix64.TwoPi;
            Fix64 opposite = angle + Fix64.Pi + (rng.NextFix() * 2 - Fix64.One) * ThicketPollenSideJitter;
            int damage = ThicketPollenDamageOf(id);
            OpenThicketPollen(id, hero, land, damage);
            OpenThicketPollen(id, hero + FixVec2.FromAngle(angle) * ThicketPollenSpread, land, damage);
            OpenThicketPollen(id, hero + FixVec2.FromAngle(opposite) * ThicketPollenSpread, land, damage);
            a.Tag = ThicketPollenZones;
            SetThicketCooldown(id, ThicketMasterAction.Pollen, ThicketPollenCooldownTicks);
            return true;
        }

        /// <summary>Облако в свободный слот; места нет — вытесняется самое старое (меньший номер).</summary>
        private void OpenThicketPollen(int source, FixVec2 center, int land, int damage)
        {
            var zones = ThicketPollen;
            int slot = -1;
            for (int k = 0; k < zones.Length && slot < 0; k++) if (zones[k].Serial == 0) slot = k;
            if (slot < 0)
            {
                slot = 0;
                for (int k = 1; k < zones.Length; k++) if (zones[k].Serial < zones[slot].Serial) slot = k;
            }
            zones[slot] = new ThicketPollenZone
            {
                Serial = ++_thicketPollenSerial, Source = source, StartTick = Tick, LandTick = land,
                EndTick = land + ThicketPollenLifeTicks, NextPulseTick = land + ThicketPollenPulseTicks,
                Damage = damage, Center = center, Radius = ThicketPollenRadius,
            };
        }

        /// <summary>Есть ли облака босса id: падающие (fallingOnly) или любые (падают или лежат).</summary>
        private bool ThicketPollenOf(int id, bool fallingOnly)
        {
            if (_thicketPollen == null) return false;
            for (int k = 0; k < _thicketPollen.Length; k++)
            {
                var z = _thicketPollen[k];
                if (z.Serial != 0 && z.Source == id && (!fallingOnly || z.LandTick > Tick)) return true;
            }
            return false;
        }

        /// <summary>Снятый каст: ещё падающие облака этого босса не ложатся.</summary>
        private void DropFallingPollen(int id)
        {
            if (_thicketPollen == null) return;
            for (int k = 0; k < _thicketPollen.Length; k++)
                if (_thicketPollen[k].Serial != 0 && _thicketPollen[k].Source == id && _thicketPollen[k].LandTick > Tick)
                    _thicketPollen[k] = default;
        }

        /// <summary>
        /// Раз в тик, до действий боссов: облака мёртвого босса и отлежавшие
        /// уходят; герой в облаке замедлен на следующий шаг и раз в 15 тиков
        /// получает укус (один на все облака).
        /// </summary>
        partial void ThicketZonesTick()
        {
            if (_thicketPollen == null) return;
            var zones = _thicketPollen;
            for (int k = 0; k < zones.Length; k++)
            {
                var z = zones[k];
                if (z.Serial == 0) continue;
                if (Tick > z.EndTick || !Entities.Alive[z.Source] || Entities.Kind[z.Source] != EnemyKind.ForestThicketMaster)
                    zones[k] = default;
            }
            if (Entities.Count <= PlayerId || !Entities.Alive[PlayerId]) return;
            FixVec2 hero = Entities.Position[PlayerId];
            // Падение (наслоение: жест босса к этому тику уже кончился) — событие на каждое облако.
            for (int k = 0; k < zones.Length; k++)
            {
                var z = zones[k];
                if (z.Serial == 0 || z.LandTick != Tick) continue;
                _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionImpact, z.Source, PlayerId,
                    EnemyActionKind.ThicketPollen, z.Center, k, ThicketPollenTouches(z, hero)));
            }
            bool inside = false;
            int bite = -1;
            for (int k = 0; k < zones.Length; k++)
            {
                var z = zones[k];
                if (z.Serial == 0) continue;
                bool due = Tick >= z.NextPulseTick && Tick >= z.FrozenUntil;
                bool touches = ThicketPollenTouches(z, hero);
                if (touches) inside = true;
                if (due)
                {
                    // Часы укусов у облака свои: идут, даже если героя в нём нет, — 8 за жизнь.
                    zones[k].NextPulseTick = z.NextPulseTick + ThicketPollenPulseTicks;
                    if (touches && (bite < 0 || z.Damage > zones[bite].Damage)) bite = k;
                }
            }
            if (inside) ApplyHeroSlow(ThicketPollenSlowPercent, 1);
            if (bite < 0 || Tick - _thicketPollenPulseTick < ThicketPollenPulseTicks) return;
            _thicketPollenPulseTick = Tick;
            ApplyAbilityDamage(zones[bite].Source, PlayerId, zones[bite].Damage, -1, DamageType.Physical, overTime: true);
        }

        /// <summary>Часы: облака босса id ждут вместе с ним (не падают, не жгут и не уходят).</summary>
        private void DelayThicketPollen(int id, int ticks)
        {
            if (_thicketPollen == null) return;
            for (int k = 0; k < _thicketPollen.Length; k++)
            {
                ref var z = ref _thicketPollen[k];
                if (z.Serial == 0 || z.Source != id) continue;
                if (z.LandTick >= Tick) z.LandTick += ticks;
                z.NextPulseTick += ticks;
                z.EndTick += ticks;
                z.FrozenUntil = Math.Max(z.FrozenUntil, Tick) + ticks;
            }
        }

        private void ResetThicketPollen()
        {
            if (_thicketPollen != null) Array.Clear(_thicketPollen, 0, _thicketPollen.Length);
            _thicketPollenSerial = 0;
            _thicketPollenPulseTick = int.MinValue / 2;
        }

        private void HashThicketPollen(ref ulong hash)
        {
            if (_thicketPollenSerial == 0) return;
            Hashing.Mix(ref hash, 0x54485047); // "THPG"
            Hashing.Mix(ref hash, _thicketPollenSerial); Hashing.Mix(ref hash, _thicketPollenPulseTick);
            for (int k = 0; k < _thicketPollen.Length; k++)
            {
                var z = _thicketPollen[k];
                Hashing.Mix(ref hash, z.Serial);
                if (z.Serial == 0) continue;
                Hashing.Mix(ref hash, z.Source); Hashing.Mix(ref hash, z.StartTick); Hashing.Mix(ref hash, z.LandTick);
                Hashing.Mix(ref hash, z.EndTick); Hashing.Mix(ref hash, z.NextPulseTick); Hashing.Mix(ref hash, z.FrozenUntil);
                Hashing.Mix(ref hash, z.Damage); Hashing.Mix(ref hash, z.Center.X); Hashing.Mix(ref hash, z.Center.Y);
                Hashing.Mix(ref hash, z.Radius);
            }
        }
    }
}
