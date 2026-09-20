using System;

namespace Game.Sim
{
    /// <summary>Начальный баланс отдельного вида; существующие враги его не читают.</summary>
    public sealed class ForestBudSettings
    {
        public static readonly ForestBudSettings Default = new ForestBudSettings();
        public readonly int ShotCount = 5, ShotIntervalTicks = 6, FlightTicks = 45;
        public readonly int Health, Damage, AttackCooldownTicks, WindupTicks, RecoveryTicks;
        public readonly Fix64 MoveSpeed, AttackRange, PreferredRange, RetreatRange, ImpactRadius, BodyRadius;
        public int ActionTicks => WindupTicks + (ShotCount - 1) * ShotIntervalTicks + RecoveryTicks;

        public ForestBudSettings(int health = 80, int damage = 20, int attackCooldownTicks = 135,
            int windupTicks = 24, int recoveryTicks = 18, Fix64? moveSpeed = null,
            Fix64? attackRange = null, Fix64? preferredRange = null, Fix64? retreatRange = null,
            Fix64? impactRadius = null, Fix64? bodyRadius = null)
        {
            Health = health; Damage = damage; AttackCooldownTicks = attackCooldownTicks;
            WindupTicks = windupTicks; RecoveryTicks = recoveryTicks;
            MoveSpeed = moveSpeed ?? Fix64.Ratio(12, 10);
            AttackRange = attackRange ?? Fix64.FromInt(10);
            PreferredRange = preferredRange ?? Fix64.Ratio(17, 2);
            RetreatRange = retreatRange ?? Fix64.FromInt(4);
            ImpactRadius = impactRadius ?? Fix64.Ratio(8, 10);
            BodyRadius = bodyRadius ?? Fix64.Ratio(65, 100);
            if (Health <= 0 || Damage < 0 || WindupTicks < 1 || RecoveryTicks < 1 ||
                AttackCooldownTicks < ActionTicks || AttackCooldownTicks > CombatStats.MaxAttackCooldown ||
                MoveSpeed < Fix64.Zero || RetreatRange <= BodyRadius || PreferredRange <= RetreatRange ||
                AttackRange < PreferredRange || ImpactRadius <= Fix64.Zero || BodyRadius <= Fix64.Zero ||
                BodyRadius > EntityStore.MaxBodyRadius)
                throw new ArgumentException("Invalid Forest Bud combat settings.");
        }

        internal void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Health); Hashing.Mix(ref hash, Damage);
            Hashing.Mix(ref hash, AttackCooldownTicks); Hashing.Mix(ref hash, WindupTicks);
            Hashing.Mix(ref hash, RecoveryTicks); Hashing.Mix(ref hash, MoveSpeed);
            Hashing.Mix(ref hash, AttackRange); Hashing.Mix(ref hash, PreferredRange);
            Hashing.Mix(ref hash, RetreatRange); Hashing.Mix(ref hash, ImpactRadius);
            Hashing.Mix(ref hash, BodyRadius);
        }
    }

    /// <summary>Цель записана при выстреле и остаётся неподвижной весь полёт.</summary>
    public readonly struct ForestFruitState
    {
        public readonly int Serial, Source, ShotIndex, LaunchTick, ImpactTick, Damage;
        public readonly FixVec2 Origin, Target;
        public readonly Fix64 Radius;
        internal ForestFruitState(int serial, int source, int shotIndex, int launchTick,
            int impactTick, FixVec2 origin, FixVec2 target, Fix64 radius, int damage)
        {
            Serial = serial; Source = source; ShotIndex = shotIndex; LaunchTick = launchTick;
            ImpactTick = impactTick; Origin = origin; Target = target; Radius = radius; Damage = damage;
        }
    }

    /// <summary>Часы позы принадлежат Sim; Animator лишь выбирает соответствующий кадр.</summary>
    public readonly struct ForestBudAttackState
    {
        public readonly int Serial, StartTick, FirstShotTick, EndTick, ShotsFired;
        internal ForestBudAttackState(int serial, int startTick, int firstShotTick, int endTick, int shotsFired)
        { Serial = serial; StartTick = startTick; FirstShotTick = firstShotTick; EndTick = endTick; ShotsFired = shotsFired; }
        internal ForestBudAttackState WithShot()
            => new ForestBudAttackState(Serial, StartTick, FirstShotTick, EndTick, ShotsFired + 1);
    }
}
