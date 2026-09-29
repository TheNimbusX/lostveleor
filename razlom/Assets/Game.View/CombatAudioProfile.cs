using System;
using UnityEngine;

namespace Game.View
{
    public enum CombatSound
    {
        Whoosh, HitMetal, HitBody, Kill, Whirlwind, Cast, Reward,
        AnchorSweep, ChainStep, Footstep, Dissolve,
        GuardianFall, RootSwarmHit, RootSwarmKill, RootSwarmFall, RootSwarmDissolve,
        EnemyWarning, PlayerHurt, CycloneRelease,
        WhooshHeavy, CycloneTurn, WhirlwindEnd, ChainStepHop, ChainStepEnd,
        PelagAttack, Cleave, Dash, BlazePrepare, BlazeFire, Finisher,
        WhirlwindPulse, WhirlwindHit,
        // Плюй-плод и тихий замах хранителя (26.09). Номер звука хранится в профиле —
        // новые значения только в конец, перед Count.
        BudVolley, BudPop, BudFruitImpact, BudHurt, BudDeath, GuardianSwing,
        // Мобы леса (поток K, выбор владельца 29.09): клипы Resources/Audio/Combat/Mobs,
        // таблица слотов — MobSoundBank. Выстрел, шлепок и смерть Плюй-плода идут в прежние
        // BudPop, BudFruitImpact и BudDeath; BudGurgle — его голос боли, BudHurt — прежний «тук».
        GuardianClawSwing, GuardianClawImpact, GuardianHurt, GuardianDeath, GuardianStep,
        RootSwarmBite, RootSwarmScuttle, RootSwarmHurt, RootSwarmDeath,
        BudPuddle, BudGurgle,
        StonehoofSnort, StonehoofCharge, StonehoofCollision, StonehoofTusk, StonehoofHurt, StonehoofDeath,
        WendigoClaw, WendigoLeap, WendigoLand, WendigoHowl, WendigoSweep, WendigoHurt, WendigoDeath,
        ThornSpike, ThornBurst, ThornShot, ThorncasterHurt, ThorncasterDeath,
        SnarerSlam, SnarerRoots, SnarerMend, SnarerHurt, SnarerDeath,
        SplitterBite, SplitterRoll, SplitterCrack, SplitlingPop, SplitterHurt, SplitterDeath,
        KillImpact, HeroStunned, HeroRooted,
        Count
    }

    [Serializable]
    public sealed class CombatSoundEntry
    {
        public CombatSound Sound;
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        [Range(0f, 2f)] public float Gain = 1f;
        [Range(0.5f, 2f)] public float Pitch = 1f;
        [Range(0f, 0.2f)] public float PitchVariation = 0.025f;
        [Range(0, 100)] public int Priority = 50;
        [Range(1, 8)] public int MaxPerFrame = 1;
    }

    [CreateAssetMenu(menuName = "Разлом/Профиль боевого звука")]
    public sealed class CombatAudioProfile : ScriptableObject
    {
        [Range(0f, 1f)] public float Gain = 0.8f;
        public CombatSoundEntry[] Sounds = Array.Empty<CombatSoundEntry>();
        [HideInInspector] public int PelagAudioRevision;

        public CombatSoundEntry Find(CombatSound sound)
        {
            for (int i = 0; i < Sounds.Length; i++)
                if (Sounds[i] != null && Sounds[i].Sound == sound) return Sounds[i];
            return null;
        }

        public static int DefaultPriority(CombatSound sound)
        {
            switch (sound)
            {
                case CombatSound.EnemyWarning: case CombatSound.PlayerHurt: return 100;
                case CombatSound.Kill: case CombatSound.RootSwarmKill: case CombatSound.BudDeath: return 80;
                case CombatSound.HitBody: case CombatSound.RootSwarmHit: case CombatSound.BudHurt: return 70;
                case CombatSound.HitMetal: return 65;
                case CombatSound.BudFruitImpact: return 50;
                case CombatSound.BudVolley: case CombatSound.BudPop: return 45;
                // Замах хранителя — фон: его первым вытесняют удары и сигналы.
                case CombatSound.GuardianSwing: return 20;
                case CombatSound.Footstep: case CombatSound.Dissolve:
                case CombatSound.RootSwarmDissolve: return 10;

                // Мобы леса. Что случилось с героем — выше всего после сигналов: удар по
                // нему, оглушение и корни. Потом убийство, крупные атаки и смерти видов,
                // взмахи, мелочь; голос боли под саблей и фон — ниже всех. Топот роя (5)
                // никого не вытесняет: берёт только свободный голос.
                case CombatSound.HeroStunned: case CombatSound.HeroRooted: return 90;
                case CombatSound.GuardianClawImpact: case CombatSound.StonehoofCollision: return 88;
                case CombatSound.KillImpact: return 84;
                case CombatSound.WendigoHowl: return 75;
                case CombatSound.WendigoSweep: case CombatSound.WendigoLand: return 72;
                case CombatSound.SnarerSlam: case CombatSound.ThornBurst: return 70;
                case CombatSound.SnarerRoots: return 68;
                case CombatSound.StonehoofSnort: return 66;
                case CombatSound.GuardianDeath: case CombatSound.StonehoofDeath: case CombatSound.WendigoDeath:
                case CombatSound.ThorncasterDeath: case CombatSound.SnarerDeath: case CombatSound.SplitterDeath:
                case CombatSound.SplitterCrack: return 64;
                case CombatSound.StonehoofCharge: return 62;
                case CombatSound.WendigoClaw: case CombatSound.WendigoLeap: return 60;
                case CombatSound.GuardianClawSwing: case CombatSound.StonehoofTusk: return 58;
                case CombatSound.ThornSpike: return 55;
                case CombatSound.SplitterRoll: return 52;
                case CombatSound.RootSwarmDeath: return 50;
                case CombatSound.ThornShot: return 48;
                case CombatSound.SnarerMend: return 46;
                case CombatSound.SplitterBite: return 45;
                case CombatSound.SplitlingPop: return 42;
                case CombatSound.RootSwarmBite: return 35;
                case CombatSound.GuardianHurt: case CombatSound.StonehoofHurt: case CombatSound.WendigoHurt:
                case CombatSound.ThorncasterHurt: case CombatSound.SnarerHurt: case CombatSound.SplitterHurt:
                case CombatSound.BudGurgle: case CombatSound.BudPuddle: return 30;
                case CombatSound.RootSwarmHurt: return 25;
                case CombatSound.GuardianStep: return 8;
                case CombatSound.RootSwarmScuttle: return 5;
                default: return 40;
            }
        }
    }
}
