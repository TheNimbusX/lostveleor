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
        // Водоворот 02.10, вечер: пенный след за каждым притянутым врагом —
        // копия следа рывка в цветах Водоворота (PelagDashWake ведёт его по
        // настоящему пути тела), с заносом брызг у ног.
        WhirlwindMaelstromDrag,
        // Пенные волны v4 (02.10): удар кольца по врагу — большой веер брызг выше
        // пояса (waves-2); корона у ног осталась оглушению Водоворота.
        WhirlwindWaveSplash,
        // Шквал v2 «морская пена» (02.10, PelagSquallFoamVfxSetup): живая вода
        // прыжка (струя, полоса Пенного следа, дуга возврата), кольцо пены у ног
        // цели и последний удар, всплеск на теле (префаб всплеска серии сабли),
        // корона у ноги (префаб короны рывка), волна-толчок Охоты (стоячая волна
        // добивающего серии), метка добычи, знаки у ног (замедление пеной, вуаль
        // неуязвимости) и пенный двойник Неуловимого.
        SquallWater,
        SquallStrike,
        SquallFinish,
        SquallSplash,
        SquallCrown,
        SquallSurge,
        SquallMark,
        SquallCue,
        SquallGhost,
        // Абордаж v2 «морская пена» (02.10, PelagAbordageFoamVfxSetup): лента воды вдоль
        // цепи и росчерк якоря, всплеск на теле (префаб всплеска серии сабли), корона у ноги
        // (префаб короны рывка), корона сбитого (корона Вихря), веер падения воды (веер
        // Пенных волн), след тяги (след рывка), след волока (след Водоворота), кольцо Обвала,
        // струя Пробоины, столб Гейзера; голова якоря и цепь — префабы прежнего броска
        // в своих пулах (прежний путь и Шквал их не трогают).
        AbordageRibbon,
        AbordageSplash,
        AbordageCrown,
        AbordageKnock,
        AbordageBurst,
        AbordageWake,
        AbordageDrag,
        AbordageQuake,
        AbordageBreach,
        AbordageGeyser,
        AbordageAnchor,
        AbordageChain,
        // Крушение v2 «морская пена» (03.10, PelagWreckFoamVfxSetup): дуга за головой якоря и вихрь
        // Девятого вала, стоячий гребень вала/стены/горба с мокрым следом, круг удара оземь и
        // обрушения, ленты Водяного панциря; всплеск на теле (префаб всплеска серии сабли), корона
        // (префаб короны рывка), корона сбитого (корона Вихря), веер (веер Пенных волн), серп маха
        // (волна серии сабли — полумесяц пака), короткая трещина (тёмная копия линии раскола) — в своих пулах.
        WreckArc,
        WreckCrest,
        WreckSlam,
        WreckShell,
        WreckSplash,
        WreckCrown,
        WreckKnock,
        WreckBurst,
        WreckSwing,
        WreckCrack,
        // Бросок якоря «морская пена» (03.10, PelagAnchorThrowFoamVfxSetup): лента воды на цепи, росчерк,
        // тень-линия и струйки к цепи (лента Абордажа v2), всплеск на теле (всплеск серии сабли), корона у ноги
        // (корона рывка), корона петли, узла и приземления (корона Вихря), веер укуса Гарпуна (веер Пенных волн),
        // вспаханная пена за головой (след рывка), борозда волока (след Водоворота), сеть Невода и призрак Веера
        // (свои), голова якоря и цепь временного пути (префабы прежнего броска в своих пулах).
        AnchorThrowRibbon,
        AnchorThrowSplash,
        AnchorThrowCrown,
        AnchorThrowKnock,
        AnchorThrowBurst,
        AnchorThrowWake,
        AnchorThrowDrag,
        AnchorThrowNet,
        AnchorThrowGhost,
        AnchorThrowAnchor,
        AnchorThrowChain,
        // Крушение «холодное железо» (база 06.10, PelagWreckIronVfxSetup): удар оземь (разбитая земля круга и
        // полосы со светом из трещин, камни, железные осколки, крупные звенья, пыль, искры, холодный свет),
        // росчерк маха у головы якоря, искра железа на теле, камешки и пыль у ног сбитого.
        WreckIronSlam,
        WreckIronStreak,
        WreckIronHit,
        WreckIronKnock,
        // Крушение «холодное железо» — махи (06.10, PelagWreckSwingVfxSetup): дуга за головой якоря (контур,
        // звенья-призраки, цвет формы), знак на задетом (сколы, вспышка, пыль, вмятина тяжёлого маха).
        WreckSwingArc,
        WreckSwingHit,
        // Махи Крушения v4 на рисованной технике серии сабли (06.10, PelagWreckSwingIronVfxSetup): полумесяц пака
        // «sword_trail 180 thick» на шейдере Sabre Foam Wave в цветах «холодного железа», звенья-призраки, искры;
        // мах 2 — тяжёлый полумесяц; знак на задетом — сколы, искры, пыль (Sabre Foam Blob).
        WreckIronSwing,
        WreckIronSwingHeavy,
        WreckIronSwingHit,
        // Выпад Крушения v4 «просто» (06.10, PelagWreckLungeVfxSetup, кадр v4-lunge-simple.png): одна круглая вспышка в
        // точке удара и одна прямая яркая линия по полосе Sim с несколькими крупными комьями земли.
        WreckLunge,
        // Крушение v4 на РИСОВАННЫХ текстурах (06.10, PelagWreckPaintedVfxSetup; лист vfx-textures/sheet-base-v1.png):
        // полумесяц маха со звеньями (полумесяц пака «sword_trail 180 thick», рисунок вдоль дуги), мах 2 — свой,
        // знак на задетом (звезда и сколы-спрайты), выпад (звезда на земле, рисованная полоса по фронту Sim, комья).
        // Прежние WreckIronSwing*/WreckLunge от библиотеки отвязаны (файлы на месте).
        WreckPaintedSwing,
        WreckPaintedSwingHeavy,
        WreckPaintedHit,
        WreckPaintedLunge,
        // Махи Крушения на стеке серии сабли (06.10 вечер, PelagWreckComboVfxSetup): полумесяц «холодного железа»
        // (волна + эхо, хлопок, доворот, грани и блик в шейдере, искры и сколы с края), мах 2 — зеркало крупнее;
        // знак на задетом — вспышка-звезда, искры и сколы с комьями. Рисованные махи и знак от библиотеки отвязаны.
        WreckComboSwing,
        WreckComboSwingHeavy,
        WreckComboHit,
        // Выпад Крушения на стеке серии сабли (06.10 поздно, PelagWreckComboVfxSetup.Lunge): стоячий всплеск удара
        // (полузвезда от земли, звезда и эхо), кольцо и трещины, камни и искры; по полосе — стоячий гребень кобальта
        // (лента к камере, эхо), искры, сколы, камни, пыль и тонкий след. Рисованный выпад от библиотеки отвязан.
        WreckComboLunge,
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
