namespace Game.Sim
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — вступление (кат-сцена, владелец 02.10): «мы идём снизу,
    /// заходим на арену, и проигрывается скрипт, где босс выходит из спячки…
    /// и босс сразу начинает файт, не дожидаясь нашего первого удара».
    ///
    /// На поляне босса (SetupThicketMasterArena отмечает её в
    /// ThicketMasterMemory.Clearing) босс спит укоренённым сколько угодно —
    /// пока герой идёт по тропе снизу, — и просыпается, когда центр героя
    /// ступил на пол поляны (GladeRegion.Field ≤ 1) или босса ранили издали.
    /// С этого тика (IntroStartTick, событие EnemyActionStarted ThicketIntro)
    /// идёт окно вступления длиной ThicketIntroTicks (105 тиков, 3,5 с):
    ///  • IntroStartTick … +24 (ThicketIntroLeadTicks) — камера летит к боссу,
    ///    он ещё спит (Sleep);
    ///  • IntroWakeTick — пробуждение (Wake, 30 тиков), за ним сразу рёв
    ///    (36 до кольца + 15 стойки);
    ///  • IntroEndTick — конец рёва: с этого тика ввод героя снова читается, и
    ///    в этот же тик босс начинает первую атаку (ThicketIntroOpener) — лапу,
    ///    если герой в её досягаемости, иначе нырок к нему. Без отдыха и без
    ///    ожидания удара героя.
    /// Пока окно идёт (ThicketIntroHoldsHero): ввод героя не читается (Step
    /// подаёт пустой кадр, приказ идти и отложенная способность сняты в тик
    /// начала — герой тормозит за AccelerationTicks и стоит), урон, отброс и
    /// контроль по нему не проходят (PlayerImmune); начатое действие героя снято
    /// в тик начала (CancelPlayerAction). Босс не ходит: сон, пробуждение и рёв —
    /// на месте (в пробуждении и рёве он доворачивается к герою, как было), и
    /// урон по нему тоже не проходит (ThicketIntroShields). Умер босс — окно
    /// гаснет сразу.
    ///
    /// Без поляны (стенды SetupKindTestArena, песочница, старая арена босса без
    /// областей) — прежнее пробуждение: 90 тиков сна и герой ближе 9 м или
    /// ранил; окна нет, ввод и урон не трогаются.
    ///
    /// «К боссу» (RiftRun.PlaceNearBoss) ставит героя на тропу входа в
    /// ThicketIntroTrailDistance до кромки поляны (TryGetThicketIntroTrailPoint)
    /// — вступление играет, когда он ступит на пол.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Подлёт камеры к спящему боссу: от входа героя на поляну до пробуждения.</summary>
        public const int ThicketIntroLeadTicks = 24;

        /// <summary>Всё вступление: подлёт 24 + пробуждение 30 + рёв 36 + 15 = 105 тиков (3,5 с).</summary>
        public const int ThicketIntroTicks = ThicketIntroLeadTicks + ThicketWakeTicks + ThicketRoarWindupTicks
            + ThicketRoarRecoveryTicks;

        /// <summary>«К боссу» ставит героя на тропу входа столько метров до кромки поляны.</summary>
        public static readonly Fix64 ThicketIntroTrailDistance = Fix64.FromInt(4);

        /// <summary>Шаг поиска кромки поляны по линии к входу.</summary>
        private static readonly Fix64 ThicketIntroEdgeProbe = Fix64.Ratio(1, 4);

        /// <summary>Чьё вступление держит героя и до какого тика (не включая). 0/0 — ничьё.</summary>
        private int _thicketIntroBoss, _thicketIntroUntil;

        // ---- чтение для вида и тестов ----

        /// <summary>
        /// Идёт окно вступления: ввод героя не читается, урон, отброс и контроль
        /// по нему не проходят. Гаснет в IntroEndTick или со смертью босса.
        /// </summary>
        public bool ThicketIntroHoldsHero
            => Tick < _thicketIntroUntil && (uint)_thicketIntroBoss < (uint)Entities.Count && Entities.Alive[_thicketIntroBoss];

        /// <summary>
        /// Босс в своём вступлении — кат-сцена для обеих сторон: урон по нему
        /// (ApplyAttack, ApplyAbilityDamage — горение, лужи, волны пены, след
        /// огня, начатые до окна) не проходит, горение гаснет (UpdateThicketMasters),
        /// как в нырке. Иначе выстрел с тропы жёг бы застывшего босса 3,5 с, а
        /// перейдённый в окне порог 66% съедал бы первую атаку (рёв 66 — раньше неё).
        /// </summary>
        private bool ThicketIntroShields(int id)
            => _thicketIntroBoss > 0 && id == _thicketIntroBoss && ThicketIntroHoldsHero;

        /// <summary>
        /// Окно вступления босса id: start — герой ступил на поляну (событие
        /// ThicketIntro), wake — пробуждение (Wake), end — конец рёва: герой
        /// снова слушается, босс в этот тик начинает первую атаку. false —
        /// вступления ещё не было (спит) или это стенд без поляны.
        /// </summary>
        public bool TryGetThicketIntro(int id, out int start, out int wake, out int end)
        {
            bool known = TryGetThicketMasterMemory(id, out var m) && m.IntroEndTick != 0;
            start = known ? m.IntroStartTick : 0;
            wake = known ? m.IntroWakeTick : 0;
            end = known ? m.IntroEndTick : 0;
            return known;
        }

        /// <summary>Босс стоит на поляне и проснётся вступлением (а не по-старому, по 9 м).</summary>
        public bool ThicketWakesOnClearing(int id) => TryGetThicketMasterMemory(id, out var m) && m.Clearing != 0;

        /// <summary>
        /// Где поставить героя для «К боссу» (RiftRun.PlaceNearBoss): на линии от
        /// босса к входу, в ThicketIntroTrailDistance за кромкой пола поляны, на
        /// ходимом месте вне поляны. false — босс без поляны, уже проснулся или
        /// места на тропе нет.
        /// </summary>
        public bool TryGetThicketIntroTrailPoint(int id, out FixVec2 point)
        {
            point = default;
            if (_layout == null || !TryGetThicketMasterMemory(id, out var m) || m.Clearing == 0
                || m.Clearing > _layout.GladeCount || m.Awake || m.IntroEndTick != 0) return false;
            var glade = _layout.GetGlade(m.Clearing - 1);
            FixVec2 toEntry = _layout.EntryPoint - m.Home;
            if (toEntry.LengthSq.Raw == 0) return false;
            Fix64 length = toEntry.Length;
            FixVec2 direction = toEntry / length;
            Fix64 edge = Fix64.Zero;
            while (edge < length && glade.Field(m.Home + direction * edge) <= Fix64.One) edge += ThicketIntroEdgeProbe;
            Fix64 at = Fix64.Min(edge + ThicketIntroTrailDistance, length);
            point = _layout.ClampToWalkable(m.Home + direction * at, Entities.BodyRadius[PlayerId]);
            return glade.Field(point) > Fix64.One;
        }

        // ---- расстановка ----

        /// <summary>Отмечает поляну, на которой встал босс (SetupThicketMasterArena): дальше он ждёт героя на её полу.</summary>
        private void MarkThicketClearing(int id, LayoutMap map)
        {
            if (map == null) return;
            FixVec2 home = ThicketMemory[id].Home;
            for (int k = 0; k < map.GladeCount; k++)
                if (map.GetGlade(k).Field(home) <= Fix64.One)
                {
                    ThicketMemory[id].Clearing = k + 1;
                    return;
                }
        }

        // ---- сон и вступление ----

        /// <summary>
        /// Сон на поляне (зовёт TryWakeThicketMaster): вступление начинается, когда
        /// герой ступил на пол поляны или босса ранили; true — пора пробуждения
        /// (IntroWakeTick, после подлёта камеры).
        /// </summary>
        private bool ThicketIntroWakeDue(int id)
        {
            ref var m = ref ThicketMemory[id];
            if (m.IntroEndTick == 0)
            {
                bool hurt = Entities.Health[id] < Entities.MaxHealth[id];
                if (!hurt && !ThicketHeroOnClearing(id)) return false;
                BeginThicketIntro(id);
            }
            return Tick >= m.IntroWakeTick;
        }

        private bool ThicketHeroOnClearing(int id)
        {
            int clearing = ThicketMemory[id].Clearing;
            return _layout != null && clearing > 0 && clearing <= _layout.GladeCount
                && _layout.GetGlade(clearing - 1).Field(Entities.Position[PlayerId]) <= Fix64.One;
        }

        /// <summary>
        /// Начало вступления: окно, событие для вида, герой — без приказа идти и без
        /// отложенной способности (тормозит сам, своим шагом). Ввод этого тика уже
        /// прочитан: держать героя начинает следующий тик. Начатое действие героя
        /// снимается, как оглушением (CancelPlayerAction): удар серии и обычный удар
        /// с их буферами нажатия (иначе клик за тик до пола начал бы новый взмах уже
        /// в окне), рывок, прыжок цепи, удар Вихря, выпад и прочее своё движение;
        /// чужой отброс доезжает.
        /// </summary>
        private void BeginThicketIntro(int id)
        {
            ref var m = ref ThicketMemory[id];
            m.IntroStartTick = Tick;
            m.IntroWakeTick = Tick + ThicketIntroLeadTicks;
            m.IntroEndTick = Tick + ThicketIntroTicks;
            _thicketIntroBoss = id;
            _thicketIntroUntil = m.IntroEndTick;
            _hasMoveOrder = false;
            _explicitMoveOrder = false;
            _navigationWaypoint = false;
            _navigationTransit = false;
            _bufferedUntil = -1;
            CancelPlayerAction(keepKnockback: true);
            _events.Add(SimEvent.EnemyAction(SimEventType.EnemyActionStarted, id, PlayerId,
                EnemyActionKind.ThicketIntro, Entities.Position[id]));
        }

        /// <summary>Первое решение после вступления — в тик конца рёва (IntroEndTick).</summary>
        private bool ThicketIntroOpenerDue(int id)
        {
            int end = ThicketMemory[id].IntroEndTick;
            return end != 0 && Tick == end;
        }

        /// <summary>
        /// Первая атака сразу после рёва: серия лапы, если герой в досягаемости, иначе
        /// нырок к нему, если он в дальней полосе (с кромки поляны до угла босса —
        /// ≈ 12,6 м: всегда так; бугор едет под героя, круг — под ним) — без двух секунд
        /// дальней полосы, но с перезарядкой нырка (стенд или тест мог его закрыть). Ни то
        /// ни другое — обычный выбор (ChooseThicketAction): ближе — подходит под лапу.
        /// </summary>
        private ThicketMasterAction ThicketIntroOpener(int id)
        {
            if (ThicketPawReady(id)) return ThicketMasterAction.Paw;
            if (Tick >= ThicketReadyAt(id, ThicketMasterAction.Dive) && ThicketHeroBand(id) == ThicketBand.Far)
                return ThicketMasterAction.Dive;
            return ChooseThicketAction(id);
        }

        /// <summary>Песочные Часы во вступлении: пробуждение и конец окна ждут вместе с боссом.</summary>
        private void ShiftThicketIntro(int id, int ticks)
        {
            ref var m = ref ThicketMemory[id];
            if (m.IntroEndTick <= Tick) return;
            if (m.IntroWakeTick > Tick) m.IntroWakeTick += ticks;
            m.IntroEndTick += ticks;
            if (_thicketIntroBoss == id) _thicketIntroUntil = m.IntroEndTick;
        }

        // ---- сброс и хеш ----

        private void ResetThicketIntro()
        {
            _thicketIntroBoss = 0;
            _thicketIntroUntil = 0;
        }

        private void HashThicketIntro(ref ulong hash)
        {
            Hashing.Mix(ref hash, 0x54484E54); // "THNT"
            Hashing.Mix(ref hash, _thicketIntroBoss); Hashing.Mix(ref hash, _thicketIntroUntil);
            for (int id = 1; id < Entities.Count; id++)
            {
                if (Entities.Kind[id] != EnemyKind.ForestThicketMaster) continue;
                var m = ThicketMemory[id];
                Hashing.Mix(ref hash, m.Clearing); Hashing.Mix(ref hash, m.IntroStartTick);
                Hashing.Mix(ref hash, m.IntroWakeTick); Hashing.Mix(ref hash, m.IntroEndTick);
            }
        }
    }
}
