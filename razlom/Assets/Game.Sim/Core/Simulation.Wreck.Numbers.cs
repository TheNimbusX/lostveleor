namespace Game.Sim
{
    /// <summary>
    /// КРУШЕНИЕ v2 — переделка 03.10 по спеке artifacts/wreck/plan/SPEC.md (решения
    /// владельца: AGENTS/DESIGN.md «Пелаг — новая структура набора»). Удар якорем влит
    /// сюда третьим ударом. Якорь на цепи и физичный — дело вида (разбор anchor-tech):
    /// Sim отдаёт сроки этапа, сторону маха, точку удара, полосу, фронт и заряд (WreckState).
    ///
    /// * СРОКИ v4 (06.10, сабельный ритм; владелец: «сам Пелаг норм») — клипы
    ///   ART/characters/pelag/wreck-2026-10-03/animation-v4/timing.json: мах влево (контакт 5),
    ///   мах вправо (5), выпад (8) — самые быстрые удары 5 / 11 / 20 (нажатия 0 / 6 / 12).
    ///   Было (v3, длинная цепь и вращение): 7 / 22 / 36.
    /// * МАХ 1 (замах 5, справа налево) и МАХ 2 (замах 5, слева направо — WreckSwingSide):
    ///   сектор Radius (+ тело) ±ArcCosine, Damage — каждому в тик, когда голова проходит его
    ///   угол, с контакт − 3 по контакт + 4 (Simulation.Wreck.Sweep). После удара 2 тика
    ///   проводки (герой стоит), окно ComboWindowTicks от удара — ходьба полной скоростью.
    /// * ВЫПАД (замах 8: якорь бросают вперёд на выдаваемой цепи): круг WreckSlamRadius (+ тело)
    ///   в WreckSlamReach перед героем (не дальше преграды) — 2 × Damage и оглушение StunTicks;
    ///   вал по полосе от точки удара до LaneLength шириной Width с тика удара, фронт
    ///   WreckWaveStep за тик — Damage и сбивание WreckWaveStunTicks, лёгких отбрасывает по
    ///   полосе. Удержание 3, выход 8 (2 первых тика без ходьбы). Кулдаун — от выпада (цикл 116).
    /// * Направление этапа — курсор в тик нажатия (из буфера — курсор того нажатия), корпус
    ///   встаёт по нему сразу, как в серии сабли; лишнего тика замаха на разворот нет.
    ///   Нажатие до удара — в буфер Tempo, выходит тиком после удара.
    /// * Срывают: рывок, оглушение, смерть, другой навык; отброс и урон — нет; корни — нет.
    ///
    /// ФОРМЫ (Simulation.Wreck.Forms; владелец 06.10 вечером — анимации у всех базовые, формы различает
    /// механика и VFX): Волнорез, Девятый вал (заряды — задевшие махи), Призрачный якорь.
    /// Все числа — именованные ЗАГЛУШКИ под приёмку.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- сроки (тики до темпа AbilityExecutionTicks) ----

        /// <summary>Замах маха 1 (справа налево, влево): контакт — кадр 5 Pelag_AN_Wreck4_Swing1. Было 7 (v3).</summary>
        public const int WreckSwingWindupTicks = 5;

        /// <summary>Замах маха 2 (слева направо, вправо): контакт — кадр 5 Pelag_AN_Wreck4_Swing2. Было 14 (v3, вращение).</summary>
        public const int WreckBackswingWindupTicks = 5;

        /// <summary>
        /// Выпад: якорь бросают вперёд на выдаваемой цепи, удар в точку — кадр 8
        /// Pelag_AN_Wreck4_Lunge. Было 7 вверх + 6 вниз (v3, удар оземь через голову).
        /// </summary>
        public const int WreckLungeWindupTicks = WreckSlamUpTicks + WreckSlamDownTicks;

        /// <summary>
        /// Выпад: 4 тика якорь ещё в руках (бросок клипа — кадр 4,5) + 4 полёта до удара. Удержания Девятого
        /// вала с 06.10 вечером нет — деление осталось виду (лента клипов). Было 7 + 6 (якорь над головой).
        /// </summary>
        public const int WreckSlamUpTicks = 4, WreckSlamDownTicks = 4;

        /// <summary>«Четвёртый удар» (талант): замах.</summary>
        public const int WreckFourthWindupTicks = 8;

        /// <summary>
        /// Сторона маха этапа (WreckState.Side): мах 1 — +1 (справа налево), мах 2 — −1 (слева
        /// направо), выпад и «Четвёртый удар» — 0. Сабельный ритм v4 06.10.
        /// </summary>
        public static int WreckSwingSide(int stage) => stage == 0 ? 1 : stage == 1 ? -1 : 0;

        /// <summary>Проводка маха: столько тиков после удара герой стоит.</summary>
        public const int WreckFollowTicks = 2;

        /// <summary>Выпад (прежний удар оземь): удержание, выход, первые тики выхода без ходьбы.</summary>
        public const int WreckHoldTicks = 3, WreckExitTicks = 8, WreckExitLockedTicks = 2;

        // ---- выпад (геометрия прежнего удара оземь) и вал ----

        /// <summary>Точка удара: столько метров перед героем.</summary>
        public static readonly Fix64 WreckSlamReach = Fix64.Ratio(22, 10);

        /// <summary>Круг удара (+ тело цели).</summary>
        public static readonly Fix64 WreckSlamRadius = Fix64.Ratio(12, 10);

        /// <summary>Фронт вала за тик: (6 − 2,2) / 0,5 → 8 шагов.</summary>
        public static readonly Fix64 WreckWaveStep = Fix64.Ratio(1, 2);

        /// <summary>Сбивание валом — оглушение 0,3 с.</summary>
        public const int WreckWaveStunTicks = 9;

        /// <summary>Лёгких вал отбрасывает по полосе на 0,8 м за 4 тика.</summary>
        public static readonly Fix64 WreckWaveKnockback = Fix64.Ratio(8, 10);
        public const int WreckWaveKnockbackTicks = 4;

        /// <summary>Вал гаснет на первой непроходимой точке оси; проба оси через столько метров.</summary>
        public static readonly Fix64 WreckWallProbeStep = Fix64.Ratio(1, 10);

        /// <summary>«Четвёртый удар»: урон × 3 (кругом Radius вокруг героя), оглушение 1 с.</summary>
        public const int WreckFourthDamageFactor = 3, WreckFourthStunTicks = 30;

        /// <summary>«Серия окупается»: возврат за последний удар серии.</summary>
        public const int WreckRefundAmount = 15;

        /// <summary>«По крупным»: урон по элитам и боссу, %.</summary>
        public const int WreckBigGamePercent = 140;

        // ---- формы 06.10 вечером: Девятый вал (заряды махами) и Призрачный якорь ----

        /// <summary>Девятый вал: зарядов не больше двух — по одному за мах, задевший хоть одного врага.</summary>
        public const int WreckNinthMaxCharges = 2;

        /// <summary>Девятый вал: урон выпада (круг и вал) и ширина полосы, % — 1 заряд ×1,5, 2 заряда ×2.</summary>
        public const int WreckNinthPercentOne = 150, WreckNinthPercentTwo = 200;

        /// <summary>Девятый вал: с двумя зарядами полоса длиннее на 2 м (6 → 8).</summary>
        public static readonly Fix64 WreckNinthLengthGain = Fix64.FromInt(2);

        /// <summary>Призрачный якорь падает через 8 тиков (≈0,25 с) после удара выпада.</summary>
        public const int WreckGhostDelayTicks = 8;

        /// <summary>Призрачный якорь: круг вокруг точки выпада (+ тело цели).</summary>
        public static readonly Fix64 WreckGhostRadius = Fix64.FromInt(3);

        /// <summary>Призрачный якорь: урон — ×1,5 урона круга выпада, %; оглушение 0,6 с (босс не оглушается).</summary>
        public const int WreckGhostDamagePercent = 150, WreckGhostStunTicks = 18;

        private WreckState _wreck = new WreckState { Slot = -1, OverheadTick = -1, ChargeStartTick = -1, WaveTick = -1, ShellBurstTick = -1, GhostTick = -1 };

        /// <summary>Крушение: фаза, этап, сроки, полоса, фронт, заряды Девятого вала, Призрачный якорь.</summary>
        public WreckState Wreck => _wreck;

        /// <summary>Серия идёт (от первого нажатия до конца выхода или окна).</summary>
        public bool WreckActive => _wreck.Phase != WreckPhase.None;

        /// <summary>Серия держит героя: своим шагом он не идёт. Кроме окна и хвоста выхода.</summary>
        public bool WreckHoldsHero
        {
            get
            {
                switch (_wreck.Phase)
                {
                    case WreckPhase.Windup: case WreckPhase.Follow: case WreckPhase.Charge: case WreckPhase.Hold: return true;
                    case WreckPhase.Exit: return Tick < _wreck.ExitWalkTick;
                    default: return false;
                }
            }
        }

        /// <summary>Слот серии для блокировок сабли и серии ЛКМ (их файлы спрашивают «≥ 0»): до хвоста выхода.</summary>
        private int _wreckSlot => _wreck.Phase == WreckPhase.None
            || (_wreck.Phase == WreckPhase.Exit && Tick >= _wreck.ExitWalkTick) ? -1 : _wreck.Slot;

        /// <summary>Прежний вид: сколько ударов серии уже было (0 — серии нет).</summary>
        public int WreckStage => _wreck.Phase == WreckPhase.None ? 0 : _wreck.Strikes;

        /// <summary>Прежний вид и HUD: направление этапа; вне серии — ноль.</summary>
        public FixVec2 WreckDirection => _wreck.Phase == WreckPhase.None ? FixVec2.Zero : _wreck.Direction;

        /// <summary>Открыто ли окно следующего нажатия. Показу — подсветить кнопку.</summary>
        public bool WreckComboOpen
            => _wreck.Phase != WreckPhase.None && _wreck.Phase != WreckPhase.Windup && _wreck.Phase != WreckPhase.Charge
               && _wreck.Strikes > 0 && _wreck.Strikes < WreckStageCount(WreckBuild) && Tick <= _wreck.WindowEndTick;

        /// <summary>Сборка серии, пока в слоте Крушение.</summary>
        private AbilityBuild WreckBuild
        {
            get
            {
                AbilityBuild build = (uint)_wreck.Slot < (uint)AbilitySlots ? _abilityBuilds[_wreck.Slot] : null;
                return build != null && build.DefinitionId == AbilityDefinition.WreckId ? build : null;
            }
        }

        /// <summary>С талантом «Четвёртый удар» серия длиннее на один удар.</summary>
        private static int WreckStageCount(AbilityBuild build)
            => build == null ? 0 : build.Has(AbilityFlag.WreckFourthStrike) ? WreckStages + 1 : WreckStages;

        /// <summary>Девятый вал, заряды 0…2 (задевшие махи): доля урона и ширины, % — 100, 150, 200.</summary>
        public static int WreckNinthPercent(int charges)
            => charges <= 0 ? 100 : charges == 1 ? WreckNinthPercentOne : WreckNinthPercentTwo;

        private static int WreckClamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        /// <summary>Попадание по элите и боссу с «По крупным».</summary>
        private int WreckBigGame(AbilityBuild build, int target, int damage)
            => build.Has(AbilityFlag.WreckBigGame) && IsElite(target) ? damage * WreckBigGamePercent / 100 : damage;

        private bool WreckEnemy(int id)
            => Entities.Alive[id] && Entities.Side[id] != Entities.Side[PlayerId] && !ThicketShielded(id);
    }
}
