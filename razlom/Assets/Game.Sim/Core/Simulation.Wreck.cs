namespace Game.Sim
{
    /// <summary>Фаза серии Крушения (Simulation.Wreck). Значения навсегда, только дописывать: хеш и снимок вида.</summary>
    public enum WreckPhase : byte
    {
        None = 0,

        /// <summary>Замах этапа: герой стоит (v4 06.10: мах 5, второй мах 5, выпад 8, четвёртый 8).</summary>
        Windup = 1,

        /// <summary>Проводка маха: 2 тика после удара герой стоит; окно уже открыто.</summary>
        Follow = 2,

        /// <summary>Окно следующего нажатия: ходьба полной скоростью.</summary>
        Window = 3,

        /// <summary>
        /// НЕ НАСТУПАЕТ с 06.10 вечером: Девятый вал больше не держат (заряды — махи). Номер занят навсегда;
        /// вид и HUD ещё спрашивают о нём — пусть спрашивают.
        /// </summary>
        Charge = 4,

        /// <summary>Удержание после удара оземь (3 тика): герой стоит.</summary>
        Hold = 5,

        /// <summary>Выход в стойку; с ExitWalkTick ходьба срывает выход.</summary>
        Exit = 6,
    }

    /// <summary>Как кончилась серия — Amount события WreckEnded. Только дописывать.</summary>
    public enum WreckEnd : byte
    {
        /// <summary>Выход доигран.</summary>
        Done = 0,

        /// <summary>Хвост выхода сорван ходьбой.</summary>
        WalkedOut = 1,

        /// <summary>Не нажал в окне: якорь на спину, кулдаун от конца окна.</summary>
        WindowExpired = 2,

        /// <summary>Сняли: рывок, оглушение, смерть, другой навык.</summary>
        Interrupted = 3,
    }

    /// <summary>
    /// Снимок Крушения. Пишет только симуляция; вид (разбор якоря anchor-tech, клипы,
    /// VFX, HUD) читает отсюда сроки этапа, сторону маха, точку удара, полосу,
    /// фронт вала и заряд. Входит в хеш, пока за расстановку была хоть одна серия.
    /// </summary>
    public struct WreckState
    {
        /// <summary>Номер серии с начала расстановки; 0 — Крушения ещё не было.</summary>
        public int Serial;

        public int Slot;
        public WreckPhase Phase;

        /// <summary>Этап: 0 — мах влево, 1 — мах вправо, 2 — выпад (удар в точку и вал), 3 — «Четвёртый удар».</summary>
        public int Stage;

        /// <summary>Сколько ударов серии уже было (0…4).</summary>
        public int Strikes;

        /// <summary>
        /// Куда идёт мах (v4 06.10, как серия сабли; Simulation.WreckSwingSide): +1 — справа налево
        /// (мах 1, влево), −1 — слева направо (мах 2, вправо), 0 — выпад и «Четвёртый удар».
        /// </summary>
        public int Side;

        /// <summary>Первое нажатие серии, нажатие этого этапа, удар этапа (прогноз до удара).</summary>
        public int CastTick, StageStartTick, ContactTick;

        /// <summary>
        /// Прежний тик «держат заряд» Девятого вала. С 06.10 вечером удержания нет — всегда −1; поле
        /// держится для вида (разбор якоря, лента клипов), пока он его читает.
        /// </summary>
        public int OverheadTick;

        /// <summary>Прежний заряд удержания Девятого вала: с 06.10 вечером всегда −1 и 0 (поля для вида).</summary>
        public int ChargeStartTick, Charge;

        /// <summary>
        /// Девятый вал (06.10 вечером): какие махи этой серии задели хоть одного врага (бит 0 — мах 1,
        /// бит 1 — мах 2) и сколько их — заряды выпада 0…2. У базы и других форм — 0.
        /// </summary>
        public int NinthLanded, NinthCharges;

        /// <summary>Окно следующего нажатия (до него включительно), конец удержания, ходьба срывает выход с, конец выхода.</summary>
        public int WindowEndTick, HoldEndTick, ExitWalkTick, ExitEndTick;

        /// <summary>Направление этапа (взгляд Sim).</summary>
        public FixVec2 Direction;

        /// <summary>
        /// Полоса удара оземь: от LaneOrigin (герой) по LaneDir до LaneLength, полуширина. С нажатия
        /// удара оземь, пока не бежит фронт прошлого удара; в тик удара — всегда.
        /// </summary>
        public FixVec2 LaneOrigin, LaneDir;
        public Fix64 LaneLength, LaneHalfWidth;

        /// <summary>Точка удара оземь (не дальше преграды оси) и радиус круга (без тела цели): с нажатия удара оземь.</summary>
        public FixVec2 ImpactPoint;
        public Fix64 ImpactRadius;

        /// <summary>Доля урона и ширины удара оземь, %: 100, у Девятого вала 150 / 200 по зарядам (WreckNinthPercent).</summary>
        public int DamagePercent;

        /// <summary>Вал (стена Волнореза): тик удара (первый шаг), начало вдоль полосы, шаг фронта, шагов, урон. −1 — фронта нет.</summary>
        public int WaveTick;
        public Fix64 WaveStart, WaveStep;
        public int WaveTravelTicks, WaveDamage;

        /// <summary>Чей фронт: None — вал базы, WreckNinthWave, WreckBreakwater — стена (у Призрачного якоря вала нет).</summary>
        public PelagForm WaveForm;

        /// <summary>Докуда вал доходит после преград (вдоль от LaneOrigin) и упёрся ли раньше полной длины.</summary>
        public Fix64 WallEnd;
        public bool WallStopped;

        /// <summary>Волнорез: сколько врагов сейчас несёт стена.</summary>
        public int CarriedCount;

        /// <summary>
        /// Прежняя «Якорная броня» (убрана 06.10 вечером, номер 13 — Призрачный якорь): всегда false и −1.
        /// Поля держатся для вида, пока он их читает.
        /// </summary>
        public bool Shell;
        public int ShellBurstTick;

        /// <summary>
        /// Призрачный якорь: тик падения (−1 — не падает), точка (точка выпада в тик удара) и урон по каждому
        /// (×1,5 урона круга выпада, до «По крупным»). Живёт и после конца серии, как вал.
        /// </summary>
        public int GhostTick;
        public FixVec2 GhostPoint;
        public int GhostDamage;

        public void HashInto(ref ulong hash)
        {
            Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Slot); Hashing.Mix(ref hash, (int)Phase);
            Hashing.Mix(ref hash, Stage); Hashing.Mix(ref hash, Strikes); Hashing.Mix(ref hash, Side);
            Hashing.Mix(ref hash, CastTick); Hashing.Mix(ref hash, StageStartTick); Hashing.Mix(ref hash, ContactTick);
            Hashing.Mix(ref hash, OverheadTick); Hashing.Mix(ref hash, ChargeStartTick); Hashing.Mix(ref hash, Charge);
            Hashing.Mix(ref hash, WindowEndTick); Hashing.Mix(ref hash, HoldEndTick);
            Hashing.Mix(ref hash, ExitWalkTick); Hashing.Mix(ref hash, ExitEndTick);
            Hashing.Mix(ref hash, Direction.X); Hashing.Mix(ref hash, Direction.Y);
            Hashing.Mix(ref hash, LaneOrigin.X); Hashing.Mix(ref hash, LaneOrigin.Y);
            Hashing.Mix(ref hash, LaneDir.X); Hashing.Mix(ref hash, LaneDir.Y);
            Hashing.Mix(ref hash, LaneLength); Hashing.Mix(ref hash, LaneHalfWidth);
            Hashing.Mix(ref hash, ImpactPoint.X); Hashing.Mix(ref hash, ImpactPoint.Y);
            Hashing.Mix(ref hash, ImpactRadius); Hashing.Mix(ref hash, DamagePercent);
            Hashing.Mix(ref hash, WaveTick); Hashing.Mix(ref hash, WaveStart); Hashing.Mix(ref hash, WaveStep);
            Hashing.Mix(ref hash, WaveTravelTicks); Hashing.Mix(ref hash, WaveDamage); Hashing.Mix(ref hash, (int)WaveForm);
            Hashing.Mix(ref hash, WallEnd); Hashing.Mix(ref hash, CarriedCount);
            Hashing.Mix(ref hash, (WallStopped ? 1 : 0) | (Shell ? 2 : 0));
            Hashing.Mix(ref hash, ShellBurstTick);
            Hashing.Mix(ref hash, NinthLanded); Hashing.Mix(ref hash, NinthCharges);
            Hashing.Mix(ref hash, GhostTick); Hashing.Mix(ref hash, GhostPoint.X); Hashing.Mix(ref hash, GhostPoint.Y);
            Hashing.Mix(ref hash, GhostDamage);
        }
    }
}
