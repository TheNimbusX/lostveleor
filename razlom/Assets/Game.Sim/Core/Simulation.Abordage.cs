namespace Game.Sim
{
    /// <summary>
    /// Фаза Абордажа (Simulation.Abordage). Значения навсегда, только дописывать:
    /// идут в хеш и в снимок для вида.
    /// </summary>
    public enum AbordagePhase : byte
    {
        None = 0,

        /// <summary>Замах: герой стоит AbordageState.WindupTicks (3, цель за спиной — 4), взгляд на цель.</summary>
        Windup = 1,

        /// <summary>Якорь летит к цели (точка Sim AnchorAt, 1,5 м/тик), герой стоит.</summary>
        Hook = 2,

        /// <summary>Зацеп был: тяга героя к точке посадки (ForcedMotion Lunge) с самонаведением.</summary>
        Pull = 3,

        /// <summary>Удар кулаком в тик прибытия и удержание AbordageHoldTicks.</summary>
        Strike = 4,

        /// <summary>Выход в стойку сабли; с ExitWalkTick ходьба срывает выход.</summary>
        Exit = 5,

        /// <summary>Цели нет (умерла, нырнула до зацепа): якорь назад AbordageRecallTicks.</summary>
        Recall = 6,
    }

    /// <summary>Как кончился Абордаж — Amount события AbordageEnded. Только дописывать.</summary>
    public enum AbordageEnd : byte
    {
        /// <summary>Выход доигран.</summary>
        Done = 0,

        /// <summary>Хвост выхода сорван ходьбой.</summary>
        WalkedOut = 1,

        /// <summary>Сняли: рывок, другая способность после удара, оглушение, смерть, чужой отброс.</summary>
        Interrupted = 2,

        /// <summary>Цель пропала до зацепа и рядом другой нет — якорь вернулся.</summary>
        NoTarget = 3,
    }

    /// <summary>
    /// Снимок Абордажа. Пишет только симуляция; вид читает отсюда, где якорь,
    /// откуда и куда тяга, когда удар и где идёт фронт воды формы. Входит в
    /// хеш, пока за расстановку был хоть один Абордаж.
    /// </summary>
    public struct AbordageState
    {
        /// <summary>Номер каста с начала расстановки; 0 — Абордажа ещё не было.</summary>
        public int Serial;

        public int Slot;
        public AbordagePhase Phase;
        public int CastTick;

        /// <summary>Цель (после перевыбора — новая; после удара — кого ударил кулак).</summary>
        public int Target;

        /// <summary>Тяга: где герой стоял в тик зацепа и точка посадки (с самонаведением). До зацепа To — прогноз.</summary>
        public FixVec2 From, To;

        /// <summary>Якорь: откуда выпущен (у правой руки) и где он сейчас (в цели — точка укуса).</summary>
        public FixVec2 AnchorFrom, AnchorAt;

        /// <summary>Выпуск якоря, зацеп, прибытие (= удар), конец стоячей фазы, с какого тика ходьба срывает выход.</summary>
        public int ReleaseTick, BiteTick, ArriveTick, PhaseEndTick, ExitWalkTick;

        /// <summary>Фронт воды формы (Обвал, Пробоина): тик удара, центр (вершина конуса), направление струи. −1 — фронта нет.</summary>
        public int WaveTick;
        public FixVec2 WaveCenter, WaveDir;

        /// <summary>Последний Гейзер: цель столба и тик падения воды (−1 — нет).</summary>
        public int GeyserTarget, GeyserFallTick;

        /// <summary>Где стояла цель в тик зацепа: ушла дальше AbordageHomingLimit — тяга замерзает.</summary>
        public FixVec2 HookCenter;

        /// <summary>Чей фронт: PelagForm.AbordageQuake или AbordageBreach.</summary>
        public PelagForm WaveForm;

        /// <summary>Урон фронта по каждому задетому (доля кулака без «Разгона»).</summary>
        public int WaveDamage;

        /// <summary>Каст запасным зарядом «Два заряда»: кнопка вернётся тиком после удара.</summary>
        public bool Spare;

        /// <summary>Цель — корпус Хозяина Чащи (посадка у груди, не по радиусу тела).</summary>
        public bool Hull;

        /// <summary>Самонаведение тяги снято: цель ушла рывком или умерла — точка посадки стоит.</summary>
        public bool Frozen;

        /// <summary>Кулак дошёл (после удара).</summary>
        public bool Landed;

        /// <summary>
        /// Замах этого каста, тиков: AbordageWindupTicks, цель за спиной (больше 90° от
        /// взгляда в каст) — на AbordageTurnWindupTicks дольше. Выпуск = CastTick + WindupTicks.
        /// </summary>
        public int WindupTicks;

        public bool Moving => Phase == AbordagePhase.Pull;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Slot); Hashing.Mix(ref hash, (int)Phase);
            Hashing.Mix(ref hash, CastTick); Hashing.Mix(ref hash, Target);
            Hashing.Mix(ref hash, From.X); Hashing.Mix(ref hash, From.Y);
            Hashing.Mix(ref hash, To.X); Hashing.Mix(ref hash, To.Y);
            Hashing.Mix(ref hash, AnchorFrom.X); Hashing.Mix(ref hash, AnchorFrom.Y);
            Hashing.Mix(ref hash, AnchorAt.X); Hashing.Mix(ref hash, AnchorAt.Y);
            Hashing.Mix(ref hash, ReleaseTick); Hashing.Mix(ref hash, BiteTick); Hashing.Mix(ref hash, ArriveTick);
            Hashing.Mix(ref hash, PhaseEndTick); Hashing.Mix(ref hash, ExitWalkTick);
            Hashing.Mix(ref hash, WaveTick); Hashing.Mix(ref hash, WaveCenter.X); Hashing.Mix(ref hash, WaveCenter.Y);
            Hashing.Mix(ref hash, WaveDir.X); Hashing.Mix(ref hash, WaveDir.Y);
            Hashing.Mix(ref hash, GeyserTarget); Hashing.Mix(ref hash, GeyserFallTick);
            Hashing.Mix(ref hash, HookCenter.X); Hashing.Mix(ref hash, HookCenter.Y);
            Hashing.Mix(ref hash, (int)WaveForm); Hashing.Mix(ref hash, WaveDamage);
            Hashing.Mix(ref hash, (Spare ? 1 : 0) | (Hull ? 2 : 0) | (Frozen ? 4 : 0) | (Landed ? 8 : 0));
            Hashing.Mix(ref hash, WindupTicks);
        }
    }

    /// <summary>
    /// АБОРДАЖ v2 — переделка 02.10 по спеке artifacts/abordage/plan/SPEC.md
    /// («супер медленно» → «резкое быстрое»). Цель выбирается как у Шквала:
    /// враг под курсором (InputFrame.AbilityTarget), иначе каста нет — ни цены,
    /// ни кулдауна, ни срыва текущего действия.
    ///
    /// * ЗАМАХ AbordageWindupTicks стоя, взгляд Sim сразу на цель; цель за спиной
    ///   (больше 90° от взгляда в каст) — на тик дольше, всё дальше сдвигается на тик.
    /// * ЯКОРЬ — точка Sim: выпуск правой рукой, 1,5 м/тик к ТЕКУЩЕЙ цели,
    ///   A = clamp(⌈(до точки укуса − 0,5) / 1,5⌉, 1, 6) тиков.
    /// * ЗАЦЕП (тик натяга, тело стоит), затем ТЯГА по длине:
    ///   P = clamp(round(L / 0,66), 2, 12) — ≈20 м/с, как рывок и Шквал; L &lt; 0,3 м —
    ///   тяги нет, удар тиком позже. Самонаведение: точка посадки каждый тик по
    ///   текущему центру цели; ушла дальше 1,5 м от места зацепа — замерзает.
    /// * ПОСАДКА вплотную (радиусы тел + 0,1, как Шквал); босс — у груди по корпусу.
    /// * УДАР в тик прибытия, удержание 3, выход 6 (первые 2 — без ходьбы).
    /// * Цель пропала до зацепа — перевыбор в 1,5 м от её места, иначе якорь
    ///   назад: AbordageRecallTicks, кулдаун AbordageRecallCooldownTicks.
    ///
    /// ФОРМЫ (владелец 02.10 ~22:35) меняют только миг удара — Simulation.Abordage.Forms:
    /// Обвал (кольцо), Гейзер (столб), Пробоина (струя-конус за целью).
    /// Числа форм — именованные ЗАГЛУШКИ под приёмку.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- сроки ----

        /// <summary>
        /// Замах, тиков до темпа (AbilityExecutionTicks): выпуск якоря — в тик каста + 3.
        /// Было 2; владелец 03.10: замах через плечо (якорь на цепи уходит за правое плечо,
        /// потом бросок) — на тик дольше, цель за спиной 3 → 4.
        /// </summary>
        public const int AbordageWindupTicks = 3;

        /// <summary>
        /// Цель за спиной — замах на столько тиков дольше (выбор владельца 03.10: «да удлини,
        /// ничего страшного»): за 2 тика разворот на 180° шёл 28°/62°/62°/28° за кадр 60 к/с,
        /// цепь гнулась; за 3 — та же S-кривая вида, шаг в полтора раза меньше.
        /// </summary>
        public const int AbordageTurnWindupTicks = 1;

        /// <summary>Цель за спиной: угол между взглядом и направлением на цель больше 90° (скалярное &lt; 0).</summary>
        public static bool AbordageTargetBehind(FixVec2 facing, FixVec2 toTarget)
            => FixVec2.Dot(facing, toTarget).Raw < 0;

        /// <summary>Полёт якоря за тик: 1,5 м (45 м/с).</summary>
        public static readonly Fix64 AbordageAnchorStep = Fix64.Ratio(3, 2);

        /// <summary>Якорь выходит из правой руки — в 0,5 м от центра героя к цели.</summary>
        public static readonly Fix64 AbordageHandReach = Fix64.Ratio(1, 2);

        public const int AbordageMinHookTicks = 1, AbordageMaxHookTicks = 6;
        public const int AbordageMinPullTicks = 2, AbordageMaxPullTicks = 12;

        /// <summary>Короче этого тяги нет: удар тиком после зацепа.</summary>
        public static readonly Fix64 AbordageNoPullDistance = Fix64.Ratio(3, 10);

        /// <summary>Удержание удара, выход, первые тики выхода без ходьбы — числа Шквала.</summary>
        public const int AbordageHoldTicks = SquallFinalHoldTicks;
        public const int AbordageExitTicks = SquallExitTicks;
        public const int AbordageExitLockedTicks = SquallExitLockedTicks;

        /// <summary>Цель ушла от места зацепа дальше этого — точка посадки замерзает (за рывком не гоняемся).</summary>
        public static readonly Fix64 AbordageHomingLimit = Fix64.Ratio(3, 2);

        /// <summary>Якорь назад без цели: столько тиков, потом конец NoTarget.</summary>
        public const int AbordageRecallTicks = 4;

        /// <summary>Перезарядка после промаха без цели (вместо полной): 0,5 с.</summary>
        public const int AbordageRecallCooldownTicks = 15;

        // ---- выбор цели (PickAbordageTarget, будущий геймпад) ----

        /// <summary>Правило 2: враг не дальше этого от точки прицела по краю тела.</summary>
        public static readonly Fix64 AbordageCursorReach = Fix64.Ratio(3, 2);

        /// <summary>Правило 3: конус от героя к прицелу, ±30°.</summary>
        public static readonly Fix64 AbordageConeCos = Fix64.Ratio(866, 1000);

        /// <summary>Правило 3: вес «вдоль» в счёте (поперёк + 0,25 × вдоль).</summary>
        private static readonly Fix64 AbordageConeAlongWeight = Fix64.Ratio(1, 4);

        private AbordageState _abordage = new AbordageState { Target = -1, WaveTick = -1, GeyserTarget = -1, GeyserFallTick = -1 };

        /// <summary>Абордаж: фаза, якорь, тяга, удар, фронт формы.</summary>
        public AbordageState Abordage => _abordage;

        /// <summary>Абордаж идёт (от каста до конца выхода или возврата якоря).</summary>
        public bool AbordageActive => _abordage.Phase != AbordagePhase.None;

        /// <summary>Абордаж держит героя: своим шагом он не идёт, сабля не бьёт. Кроме хвоста выхода.</summary>
        public bool AbordageHoldsHero => _abordage.Phase != AbordagePhase.None
            && !(_abordage.Phase == AbordagePhase.Exit && Tick >= _abordage.ExitWalkTick);

        /// <summary>
        /// Прежняя точка броска для нынешнего вида (PelagVfxController до переделки):
        /// точка посадки, до зацепа — прогноз. Уйдёт вместе с прежним показом.
        /// </summary>
        public FixVec2 LeapAim => _abordage.To;

        /// <summary>Полёт якоря до точки укуса: clamp(⌈(toBite − 0,5) / 1,5⌉, 1, 6) тиков.</summary>
        public static int AbordageHookTicks(Fix64 toBite)
        {
            Fix64 path = (toBite - AbordageHandReach) / AbordageAnchorStep;
            int ticks = path.Raw <= 0 ? 0 : -((-path).ToInt());
            return ticks < AbordageMinHookTicks ? AbordageMinHookTicks : ticks > AbordageMaxHookTicks ? AbordageMaxHookTicks : ticks;
        }

        /// <summary>Тяга длиной length: round(length / 0,66), 2…12 тиков; короче 0,3 м — 0 (тяги нет).</summary>
        public static int AbordagePullTicks(Fix64 length)
        {
            if (length < AbordageNoPullDistance) return 0;
            int ticks = (length / SquallFlightStep + Fix64.Half).ToInt();
            return ticks < AbordageMinPullTicks ? AbordageMinPullTicks : ticks > AbordageMaxPullTicks ? AbordageMaxPullTicks : ticks;
        }

        private AbilityBuild AbordageBuild
        {
            get
            {
                AbilityBuild build = (uint)_abordage.Slot < (uint)AbilitySlots ? _abilityBuilds[_abordage.Slot] : null;
                return build != null && build.DefinitionId == AbilityDefinition.AnchorLeapId ? build : null;
            }
        }

        /// <summary>Способность бьёт выбранного врага: без цели под курсором каста нет (Шквал, Абордаж).</summary>
        private static bool NeedsEnemyTarget(int definitionId)
            => definitionId == AbilityDefinition.ChainStepId || definitionId == AbilityDefinition.AnchorLeapId;

        private void AbordageFace(FixVec2 direction)
        {
            if (direction.LengthSq.Raw != 0) Entities.Facing[PlayerId] = direction.Normalized();
        }

        /// <summary>Направление тяги этого каста: от места зацепа к посадке, иначе к цели, иначе взгляд.</summary>
        private FixVec2 AbordagePullDirection()
        {
            FixVec2 path = _abordage.To - _abordage.From;
            if (path.LengthSq.Raw == 0 && (uint)_abordage.Target < (uint)Entities.Count)
                path = Entities.Position[_abordage.Target] - Entities.Position[PlayerId];
            if (path.LengthSq.Raw == 0) path = Entities.Facing[PlayerId];
            return path.LengthSq.Raw == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : path.Normalized();
        }
    }
}
