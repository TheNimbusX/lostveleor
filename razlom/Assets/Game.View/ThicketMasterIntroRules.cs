using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — КАТ-СЦЕНА ВСТУПЛЕНИЯ: чистые правила вида (без UnityEngine, тесты —
    /// tools/Combat.Presentation.Tests/ThicketMasterIntroRulesTests.cs). Владелец 02.10: «мы идём снизу,
    /// заходим на арену и проигрывается скрипт, где босс выходит из спячки, это всё должно показываться
    /// как кат-сцена красивая, и босс сразу начинает файт, не дожидаясь нашего первого удара».
    ///
    /// Окно — из Sim (Simulation.TryGetThicketIntro: S — герой ступил на пол поляны, W — пробуждение,
    /// E — конец рёва, в этот тик герой снова слушается и босс бьёт). Рисует ThicketMasterIntroView;
    /// здесь — только время. Время — тики Sim с долей кадра (тик − 1 + Alpha драйвера): пауза, хит-стоп
    /// и съёмка держат кадр сами, вид ничего не считает по своим часам.
    ///
    /// По тикам (105 тиков окна + хвост):
    ///  • S … S+12 — чёрные полосы въезжают сверху и снизу, HUD гаснет за 9 тиков (холсты HUD с миникартой
    ///    выключаются целиком до E — <see cref="HudCanvasOn"/>);
    ///  • S … S+24 (<see cref="GlideTicks"/> = подлёт Sim) — камера летит от героя к боссу и
    ///    приближается (орто 6,2 → 4,8), к пробуждению она у босса;
    ///  • W … рёв — медленный наезд 4,8 → 4,6 (кадр живой, пока лапы рвут корни и босс набирает рёв);
    ///  • начало рёва (<see cref="TitleStart"/> = W + 30, Started(Roar) Sim, контракт «Титр — от
    ///    Started(Roar)»; владелец 02.10, вечер: «да» — с начала рёва, к началу боя ушёл) — титр
    ///    «ХОЗЯИН ЧАЩИ» проявляется (буквы сходятся) и стоит через кольцо;
    ///  • красное кольцо рёва вступления на земле не рисуется (владелец 02.10, вечер: «убери») — метки
    ///    босса, открытые внутри окна, общий вид меток пропускает (<see cref="HidesTelegraph"/>);
    ///  • общего сигнала угрозы в кат-сцене нет (владелец 02.10, вечер: «убери») — ни на вступлении, ни на
    ///    пробуждении, ни на рёве вступления (<see cref="SilencesWarning"/>);
    ///  • контакт рёва (<see cref="RoarContact"/> = E − 15, кольцо Sim) — небольшая тряска, титр гаснет
    ///    за 15 тиков и догорает ровно к E — первая атака босса идёт по чистому экрану;
    ///  • через 3 тика после рёва камера возвращается к герою (21 тик, к E+9);
    ///  • E — полосы уезжают, HUD возвращается, полоса босса встаёт своим появлением
    ///    (<see cref="HoldsBossBar"/>), босс в этот тик начинает первую атаку.
    /// Босс умер в окне (release &lt; E) — всё уходит от тика смерти, титр гаснет от него же и не
    /// встаёт, если рёв не начался.
    /// Часы сдвигают W и E — наезд, рёв и титр едут вместе с ними (вид читает окно каждый кадр).
    /// </summary>
    public static class ThicketMasterIntroRules
    {
        // ---------------------------------------------------------------- тексты

        /// <summary>Титр: имя босса (EnemyTexts.BossName) капителью.</summary>
        public const string Title = "ХОЗЯИН ЧАЩИ";

        /// <summary>Строка под титром.</summary>
        public const string Subtitle = "Древний страж леса";

        // ---------------------------------------------------------------- камера

        /// <summary>Подлёт камеры к боссу, тиков: ровно подлёт Sim — к пробуждению камера у босса.</summary>
        public const int GlideTicks = Simulation.ThicketIntroLeadTicks;

        /// <summary>Размер ортокамеры боя (CameraFollow.CombatSize) — от него считаются приближения.</summary>
        public const float CombatSize = 6.2f;

        /// <summary>Размер к концу подлёта и к контакту рёва (медленный наезд между ними).</summary>
        public const float ArriveSize = 4.8f, RoarSize = 4.6f;

        /// <summary>Те же размеры множителем к размеру камеры до кат-сцены (CameraFollow.SetCinematic).</summary>
        public const float ArriveZoom = ArriveSize / CombatSize, RoarZoom = RoarSize / CombatSize;

        /// <summary>Возврат к герою: начинается через столько тиков после контакта рёва и длится столько.</summary>
        public const int ReturnDelayTicks = 3, ReturnTicks = 21;

        /// <summary>
        /// Точка кадра у босса: над землёй на столько метров (середина тела 4,14 м чуть ниже — видны лапы
        /// и корни) и на столько к герою (лапы впереди тела).
        /// </summary>
        public const float FocusLift = 1.5f, FocusLead = .6f;

        // ---------------------------------------------------------------- полосы и HUD

        /// <summary>Высота каждой чёрной полосы, доля высоты экрана (кадр ~2,35 : 1 на 16 : 9).</summary>
        public const float BarHeight = .12f;

        /// <summary>Мягкая кромка полосы внутрь кадра, доля высоты экрана: край дымный, не линейка.</summary>
        public const float FeatherHeight = .04f;

        /// <summary>Полосы въезжают за столько тиков от S и уезжают за столько от E.</summary>
        public const int BarsInTicks = 12, BarsOutTicks = 12;

        /// <summary>HUD гаснет за столько тиков от S и возвращается за столько от E.</summary>
        public const int HudOutTicks = 9, HudInTicks = 12;

        /// <summary>
        /// Холст HUD выключается целиком (ревью 02.10, вечер: «в кат-сцене виден HUD и миникарта»). Холст со
        /// своей группой кат-сцены — когда погас (ниже <see cref="HudCanvasOffBelow"/>: пропадает и то, что
        /// рисуется мимо прозрачности группы); холст с чужой группой (боевой HUD с миникартой — группа
        /// PlayerHud, второй на объекте не бывает) — на середине затухания, <see cref="HudCutAt"/>.
        /// Включается обратно на тех же порогах.
        /// </summary>
        public const float HudCanvasOffBelow = .01f, HudCutAt = .5f;

        /// <summary>Холст HUD включён при прозрачности HUD hud (Look.Hud); fades — его ведёт своя группа кат-сцены.</summary>
        public static bool HudCanvasOn(float hud, bool fades) => hud > (fades ? HudCanvasOffBelow : HudCutAt);

        // ---------------------------------------------------------------- титр

        /// <summary>
        /// Титр: проявляется за TitleInTicks от начала рёва, стоит до контакта рёва (28 тиков, ~0,9 с) и
        /// гаснет за TitleOutTicks — к E (концу рёва). Не перекрывает первую атаку босса.
        /// </summary>
        public const int TitleInTicks = 8, TitleOutTicks = 15;

        /// <summary>За столько тиков от начала титра буквы сходятся и черта под именем дорастает.</summary>
        public const int TitleSettleTicks = 20;

        /// <summary>Межбуквенный интервал титра (TMP characterSpacing): с разлёта — к покою.</summary>
        public const float TitleSpacingFrom = 34f, TitleSpacingTo = 10f;

        /// <summary>Центр титра над низом экрана, доля высоты: над нижней полосой и панелью способностей.</summary>
        public const float TitleY = .23f;

        /// <summary>Титр поднимается на столько единиц Canvas, пока буквы сходятся.</summary>
        public const float TitleRise = 14f;

        /// <summary>Длина черты под именем в покое, единиц Canvas.</summary>
        public const float RuleWidth = 340f;

        // ---------------------------------------------------------------- тряска и полоса босса

        /// <summary>Тряска на контакте рёва (CombatCameraJuice.AddImpulse; сила — из настроек игрока).</summary>
        public const float ShakeTrauma = .42f, ShakeZoom = .35f;

        /// <summary>Полоса босса встаёт через столько тиков после E (0 — в тик первой атаки).</summary>
        public const int BossBarDelayTicks = 0;

        // ---------------------------------------------------------------- вид кадра

        /// <summary>Кадр кат-сцены: доли 0…1 и множитель размера камеры.</summary>
        public readonly struct Look
        {
            /// <summary>Камера: 0 — обычный кадр за героем, 1 — у босса.</summary>
            public readonly float Camera;
            /// <summary>Множитель размера ортокамеры (1 — как в бою).</summary>
            public readonly float Zoom;
            /// <summary>Чёрные полосы: 0 — за кадром, 1 — на месте.</summary>
            public readonly float Bars;
            /// <summary>Прозрачность HUD: 1 — как обычно, 0 — спрятан.</summary>
            public readonly float Hud;
            /// <summary>Прозрачность титра.</summary>
            public readonly float Title;
            /// <summary>Буквы титра сошлись и черта дорастает: 0 — разлёт, 1 — покой.</summary>
            public readonly float TitleSettle;
            /// <summary>Кат-сцена со всем хвостом кончилась: вид отпускает камеру и HUD.</summary>
            public readonly bool Done;

            public Look(float camera, float zoom, float bars, float hud, float title, float titleSettle, bool done)
            {
                Camera = camera; Zoom = zoom; Bars = bars; Hud = hud; Title = title; TitleSettle = titleSettle; Done = done;
            }

            public static Look Finished => new Look(0f, 1f, 0f, 1f, 0f, 1f, true);
        }

        /// <summary>Тик контакта рёва вступления (кольцо, Impact(ThicketRoar)) по концу окна E.</summary>
        public static int RoarContact(int end) => end - Simulation.ThicketRoarRecoveryTicks;

        /// <summary>Рёв успел ударить до того, как окно отпустило героя (иначе тряски нет).</summary>
        public static bool RoarShown(int end, int release) => RoarContact(end) < release;

        /// <summary>Титр проявляется с начала рёва вступления (Started(Roar) Sim) — по пробуждению W.</summary>
        public static int TitleStart(int wake) => wake + Simulation.ThicketWakeTicks;

        /// <summary>Рёв начался до того, как окно отпустило героя (иначе титра нет).</summary>
        public static bool TitleShown(int wake, int release) => TitleStart(wake) < release;

        /// <summary>Титр начинает гаснуть: за TitleOutTicks до E, а умер босс раньше — с тика смерти.</summary>
        public static int TitleFadeStart(int end, int release) => Math.Min(end - TitleOutTicks, release);

        /// <summary>Тик, когда титр догас: E (первая атака — по чистому экрану), раньше — если босс умер в окне.</summary>
        public static int TitleEnd(int end, int release) => TitleFadeStart(end, release) + TitleOutTicks;

        /// <summary>Камера начинает возврат к герою: после рёва или сразу, если окно оборвалось раньше.</summary>
        public static int ReturnStart(int end, int release) => Math.Min(RoarContact(end) + ReturnDelayTicks, release);

        /// <summary>
        /// Тик, с которого кат-сцене нечего показывать: камера дома, полосы ушли, HUD вернулся. Титр к этому
        /// тику всегда догас (TitleEnd ≤ E и ≤ смерть + 15, а полосы и HUD уходят от E или смерти дольше).
        /// </summary>
        public static int DoneTick(int end, int release)
            => Math.Max(ReturnStart(end, release) + ReturnTicks, Math.Max(release + BarsOutTicks, release + HudInTicks));

        /// <summary>
        /// Кадр кат-сцены в момент now (тик Sim с долей кадра). start/wake/end — окно Sim, release —
        /// тик, когда герой снова слушается: end, а умер босс в окне — тик смерти.
        /// </summary>
        public static Look LookAt(int start, int wake, int end, int release, float now)
        {
            if (float.IsNaN(now)) return Look.Finished;
            release = Math.Max(start, Math.Min(release, end));
            if (now >= DoneTick(end, release)) return Look.Finished;

            float glideIn = Smoother(Clamp01((now - start) / GlideTicks));
            float glideOut = Smooth(Clamp01((now - ReturnStart(end, release)) / ReturnTicks));
            float camera = glideIn * (1f - glideOut);

            int contact = RoarContact(end);
            float creep = Smooth(Clamp01((now - wake) / Math.Max(1, contact - wake)));
            float close = Lerp(ArriveZoom, RoarZoom, creep);
            float zoom = Lerp(1f, close, camera);

            float bars = Smooth(Clamp01((now - start) / BarsInTicks)) * (1f - Smooth(Clamp01((now - release) / BarsOutTicks)));
            float veil = Smooth(Clamp01((now - start) / HudOutTicks)) * (1f - Smooth(Clamp01((now - release) / HudInTicks)));

            float title = 0f, settle = 0f;
            int titleAt = TitleStart(wake);
            if (TitleShown(wake, release) && now >= titleAt)
            {
                float age = now - titleAt;
                float fadeIn = Smooth(Clamp01(age / TitleInTicks));
                float fadeOut = Smooth(Clamp01((now - TitleFadeStart(end, release)) / TitleOutTicks));
                title = fadeIn * (1f - fadeOut);
                settle = Smoother(Clamp01(age / TitleSettleTicks));
            }
            return new Look(camera, zoom, bars, 1f - veil, title, settle, false);
        }

        /// <summary>
        /// Между прошлым кадром (before) и этим (now) прошёл контакт рёва — тряхнуть камеру один раз.
        /// Пауза (now == before) и первый кадр вида (before — NaN) не трясут; оборванное окно — тоже.
        /// </summary>
        public static bool RoarShake(float before, float now, int end, int release)
        {
            if (float.IsNaN(before) || float.IsNaN(now) || !RoarShown(end, release)) return false;
            int contact = RoarContact(end);
            return before < contact && now >= contact;
        }

        /// <summary>Межбуквенный интервал титра при доле схода settle.</summary>
        public static float TitleSpacing(float settle) => Lerp(TitleSpacingFrom, TitleSpacingTo, Clamp01(settle));

        /// <summary>
        /// Полоса босса ждёт кат-сцену: Хозяин Чащи на поляне ещё спит (вступления не было) или окно
        /// ещё идёт. Встаёт в тик E (+<see cref="BossBarDelayTicks"/>) своим обычным появлением. Стенды
        /// без поляны и прочие боссы — false, как раньше.
        /// </summary>
        public static bool HoldsBossBar(Simulation sim, int boss)
        {
            if (sim == null || !sim.ThicketWakesOnClearing(boss)) return false;
            if (!sim.TryGetThicketIntro(boss, out _, out _, out int end)) return true;
            return sim.Tick - 1 < end + BossBarDelayTicks;
        }

        /// <summary>
        /// Метку на земле не рисовать (владелец 02.10, вечер: «убери» — красное кольцо рёва в кат-сцене):
        /// её открыл Хозяин Чащи внутри своего окна вступления [S, E). Это кольцо рёва вступления — героя оно
        /// не бьёт (окно его держит), а в кадре читалось как угроза. Решает тик открытия метки, а не текущий
        /// кадр: метка, открытая в окне, не всплывает и тогда, когда доживает вспышку или босс умер в окне.
        /// Первая атака (метка с тика E) и рёвы порогов 66/50/33 рисуются как раньше; Sim не тронут — метка
        /// и её удар в Sim те же. Стенды без поляны (окна нет) и прочие мобы — false.
        /// </summary>
        public static bool HidesTelegraph(Simulation sim, int source, int startTick) => InIntroWindow(sim, source, startTick);

        /// <summary>
        /// Общий сигнал угрозы на начало действия (CombatAudio.PlayEnemyActionStart) молчит (владелец 02.10,
        /// вечер: «убери» — звук предупреждения в кат-сцене): действие Хозяина Чащи началось на тике tick внутри
        /// его окна вступления [S, E) — само вступление (S), пробуждение (W) и рёв вступления (W + 30). Своего
        /// голоса у босса нет (звуки босса не трогаем), кат-сцена идёт без сигнала. Первая атака (тик E), рёвы
        /// порогов 66/50/33, стенды без поляны и прочие мобы — с сигналом, как раньше.
        /// </summary>
        public static bool SilencesWarning(Simulation sim, int source, int tick) => InIntroWindow(sim, source, tick);

        /// <summary>Тик tick — внутри окна вступления [S, E) Хозяина Чащи source. Нет окна (спит, стенд), не он — false.</summary>
        private static bool InIntroWindow(Simulation sim, int source, int tick)
        {
            if (sim == null || (uint)source >= (uint)sim.Entities.Count) return false;
            if (sim.Entities.Kind[source] != EnemyKind.ForestThicketMaster) return false;
            return sim.TryGetThicketIntro(source, out int start, out _, out int end) && tick >= start && tick < end;
        }

        // ---------------------------------------------------------------- кривые

        // Вход уже 0…1; выход прижат: у 1 полином во float даёт 1,0000001 (доли вида — строго 0…1).
        private static float Smooth(float x) => Clamp01(x * x * (3f - 2f * x));

        private static float Smoother(float x) => Clamp01(x * x * x * (x * (x * 6f - 15f) + 10f));

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : float.IsNaN(x) ? 0f : x;
    }
}
