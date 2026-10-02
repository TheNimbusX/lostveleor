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
        CycloneHook,
        CycloneChain,
        CycloneWake,
        ChainStepDash,
        ChainStepHit,
        TargetFlash,
        DustSmall,
        DustHeavy,
        AnchorLeapLanding,
        AnchorLeapFlight,
        AutoAttackCriticalImpact,
        FootstepDust,
        CyclonePullImpact,
        ChainStepFinish,
        RollDash,
        Evade,
        CleaveHit,
        CleaveSlash,
        CleaveGround,
        BlazeIgnite,
        BlazeBlade,
        BlazeHit,
        AnchorSlamContact,
        // Серия сабли «морская пена» (01.10, PelagSabreComboVfxSetup): волна
        // лёгкого удара, стоячая волна добивающего, пенный накат по земле
        // добивающего и всплеск на теле цели.
        SabreWave,
        SabreCrash,
        SabreWash,
        SabreSplash,
        // Рывок «Пенный след» (02.10, PelagDashVfxSetup): след на земле от
        // старта до ног с заносом и каплями, корона брызг у передней ноги.
        DashWake,
        DashSplash,
        // Формы Вихря (02.10, PelagWhirlwindFoamVfxSetup.Forms): Буря — водяной
        // столб на удержание и всплеск в конце; Водоворот — шесть рукавов на
        // земле; Пенные волны — бегущее кольцо пены; корона брызг — удар
        // кольца по врагу и оглушение Водоворота.
        WhirlwindStormColumn,
        WhirlwindStormSplash,
        WhirlwindMaelstrom,
        WhirlwindFoamWave,
        WhirlwindCrownSplash,
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
        Rotation,
        Cleave,
        Blaze,
        Dash,
        Wreck,
        FireFlask,
        Skewer,
        Backblast
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
