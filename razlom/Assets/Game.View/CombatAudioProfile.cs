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
        WhooshHeavy, CycloneTurn, WhirlwindEnd, ChainStepHop, ChainStepEnd, Count
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
                case CombatSound.Kill: case CombatSound.RootSwarmKill: return 80;
                case CombatSound.HitBody: case CombatSound.RootSwarmHit: return 70;
                case CombatSound.HitMetal: return 65;
                case CombatSound.Footstep: case CombatSound.Dissolve:
                case CombatSound.RootSwarmDissolve: return 10;
                default: return 40;
            }
        }
    }
}
