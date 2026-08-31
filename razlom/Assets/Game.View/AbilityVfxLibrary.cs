using UnityEngine;

namespace Game.View
{
    public enum PelagVfxId : byte
    {
        AutoAttackSlash,
        AutoAttackImpact,
        WhirlwindRing,
        WhirlwindHit,
        AnchorLeapThrow,
        AnchorLeapChain,
        AnchorLeapLand,
        AnchorSweepThrow,
        AnchorSweepPull,
        AnchorSweepEnemyPull,
        ChainStepDash,
        ChainStepHit,
        TargetFlash,
        DustSmall,
        DustHeavy,
        Count
    }

    public enum PelagVfxShowcase : byte
    {
        None,
        Autoattack,
        Whirlwind,
        AnchorLeap,
        AnchorSweep,
        ChainStep,
        Rotation
    }

    /// <summary>
    /// Единственная таблица prefab -> размер пула. Она живёт в Game.View и не
    /// является gameplay-данными: Sim по-прежнему знает только о способности,
    /// цели, позиции и подтверждённом попадании.
    /// </summary>
    [CreateAssetMenu(menuName = "Разлом/Pelag VFX Library", fileName = "AbilityVfxLibrary")]
    public sealed class AbilityVfxLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public PelagVfxId Id;
            public GameObject Prefab;
            [Min(1)] public int Prewarm;
        }

        [HideInInspector] public int BuildVersion;
        public Entry[] Entries;
    }
}
