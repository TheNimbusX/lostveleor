using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Последнее событие Крушения, после которого пришёл Damage того же тика (PelagWreckVfxRules.ClassifyHit).</summary>
    public enum WreckVfxCue : byte { None = 0, Swing = 1, Slam = 2, Catch = 3, Crash = 4, Burst = 5, Fourth = 6 }

    /// <summary>Чей удар нарисовать по Damage слота Крушения.</summary>
    public enum WreckVfxHit : byte
    {
        /// <summary>Не опознан (старый каст, витрина) — простой всплеск на теле.</summary>
        Other = 0,
        /// <summary>Мах или обратный мах — всплеск по касательной и короткая отдача тела.</summary>
        Swing = 1,
        /// <summary>Круг удара оземь — тяжёлый всплеск, стоп-кадр.</summary>
        Circle = 2,
        /// <summary>Вал (база, Девятый вал, Панцирь) дошёл до тела — сбит с ног: корона у ног.</summary>
        Wave = 3,
        /// <summary>Стена Волнореза подхватила или ударила.</summary>
        Wall = 4,
        /// <summary>Обрушение стены у её конца.</summary>
        Crash = 5,
        /// <summary>Взрыв Водяного панциря.</summary>
        Burst = 6,
        /// <summary>«Четвёртый удар» (талант) — земля вокруг героя.</summary>
        Fourth = 7,
    }

    /// <summary>
    /// Числа вида Крушения v2 без Unity — проверяются тестами представления
    /// (tools/Combat.Presentation.Tests/WreckVfxRulesTests.cs). Время — тик показа
    /// sim.Tick − 2 + Alpha (тело героя рисуется на нём; как Абордаж и Шквал).
    /// Правило владельца 02.10: видимый край воды = край урона Sim. Гребень вала стоит
    /// ровно на краю шага Sim в тик этого шага (шаг k бьёт полосу до start + k·step в тик
    /// WaveTick + k − 1) и не уходит за конец стены; круг удара доходит до ImpactRadius
    /// за полтора тика и дотекает не дальше <see cref="FrontDrift"/>; капли взрыва
    /// Панциря падают внутри его радиуса. Размеры — по числам Sim, не по кадру (кадры «как ульты»).
    /// </summary>
    public static class PelagWreckVfxRules
    {
        public static float ShownTick(int simTick, float alpha) => simTick - 2 + alpha;

        public static float Seconds(float ticks) => ticks / Simulation.TicksPerSecond;

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        public static float Clamp(float x, float min, float max) => x < min ? min : x > max ? max : x;

        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // ---------------------------------------------------------------- кто бит

        /// <summary>
        /// Чей удар у Damage слота Крушения. Sim пишет: WreckStage (мах, удар оземь, четвёртый) →
        /// Damage махов; WreckSlam → Damage круга → первый шаг фронта (вал — без своего события,
        /// стена — WreckBreakwaterCatch перед каждым Damage) → WreckShellBurst → Damage взрыва;
        /// следующие шаги фронта — в своих тиках, обрушение — WreckBreakwaterCrash → Damage.
        /// Последнее событие того же тика решает; после WreckSlam круг от вала отличает геометрия
        /// (<paramref name="inCircle"/>); иначе — окно хода фронта; иначе — не опознан.
        /// </summary>
        public static WreckVfxHit ClassifyHit(WreckVfxCue lastCue, int lastCueTick, int cueTarget, int hitTick, int hitTarget,
            bool inCircle, int waveTick, int waveTravelTicks, bool wall)
        {
            if (lastCue != WreckVfxCue.None && hitTick == lastCueTick)
            {
                switch (lastCue)
                {
                    case WreckVfxCue.Swing: return WreckVfxHit.Swing;
                    case WreckVfxCue.Fourth: return WreckVfxHit.Fourth;
                    case WreckVfxCue.Burst: return WreckVfxHit.Burst;
                    case WreckVfxCue.Crash: return WreckVfxHit.Crash;
                    case WreckVfxCue.Catch: return hitTarget == cueTarget || wall ? WreckVfxHit.Wall : WreckVfxHit.Other;
                    case WreckVfxCue.Slam: return inCircle ? WreckVfxHit.Circle : wall ? WreckVfxHit.Wall : WreckVfxHit.Wave;
                }
            }
            if (waveTick >= 0 && hitTick >= waveTick && hitTick < waveTick + Math.Max(1, waveTravelTicks))
                return wall ? WreckVfxHit.Wall : WreckVfxHit.Wave;
            return WreckVfxHit.Other;
        }

        /// <summary>Тело касается круга удара оземь (как Sim: радиус круга + тело; запас 2 см на округление).</summary>
        public static bool InSlamCircle(float distance, float impactRadius, float bodyRadius)
            => distance <= impactRadius + Math.Max(0f, bodyRadius) + .02f;

        /// <summary>
        /// Босс с корпусом (Sim: радиус круга + ThicketBodyFrom = расстояние до центра − зазор до ближнего круга
        /// корпуса): задет, когда зазор от точки удара до корпуса не больше радиуса круга.
        /// </summary>
        public static bool InSlamCircleHull(float hullGap, float impactRadius) => hullGap <= impactRadius + .02f;

        /// <summary>Очередь шагов до тика показа растёт до стольких записей; дальше старейший шаг показывается сразу.</summary>
        public const int CueQueueStart = 64, CueQueueMax = 512;

        /// <summary>Всплеск на теле, доля авторского размера.</summary>
        public static float HitSplashScale(WreckVfxHit hit)
        {
            switch (hit)
            {
                case WreckVfxHit.Swing: return 1f;
                case WreckVfxHit.Circle: return 1.25f;
                case WreckVfxHit.Wave: return .8f;
                case WreckVfxHit.Wall: return .9f;
                case WreckVfxHit.Crash: return 1f;
                case WreckVfxHit.Burst: return .8f;
                case WreckVfxHit.Fourth: return 1.1f;
                default: return .8f;
            }
        }

        /// <summary>Стоп-кадр цели, с: тяжёлые удары держат дольше, несомого стеной не держим (едет).</summary>
        public static float HitHoldSeconds(WreckVfxHit hit)
        {
            switch (hit)
            {
                case WreckVfxHit.Swing: return .05f;
                case WreckVfxHit.Circle: return .07f;
                case WreckVfxHit.Wave: return .04f;
                case WreckVfxHit.Crash: return .05f;
                case WreckVfxHit.Burst: return .04f;
                case WreckVfxHit.Fourth: return .07f;
                default: return 0f;
            }
        }

        /// <summary>Сбит с ног (корона у ног): вал, обрушение, четвёртый удар.</summary>
        public static bool Knocks(WreckVfxHit hit)
            => hit == WreckVfxHit.Wave || hit == WreckVfxHit.Crash || hit == WreckVfxHit.Fourth || hit == WreckVfxHit.Circle;

        // ---------------------------------------------------------------- отдача тела (вес маха)

        /// <summary>Отдача тела цели от маха: столько метров за столько секунд — рывок и возврат.</summary>
        public const float RecoilMeters = .12f, RecoilSeconds = .16f;

        /// <summary>Смещение отдачи, м: быстрый выход за первую четверть, плавный возврат.</summary>
        public static float Recoil(float seconds)
        {
            float u = seconds / RecoilSeconds;
            if (u <= 0f || u >= 1f) return 0f;
            return RecoilMeters * (u < .25f ? Smooth01(u / .25f) : 1f - Smooth01((u - .25f) / .75f));
        }

        /// <summary>
        /// Касательная маха у цели (на плоскости x, z — оси мира Unity): <paramref name="side"/> +1 —
        /// мах справа налево (влево от луча герой → цель), −1 — слева направо. Вид берёт скорость
        /// настоящей головы якоря, когда она есть; это правило — запас.
        /// </summary>
        public static void SwingTangent(float radialX, float radialZ, int side, out float x, out float z)
        {
            float length = (float)Math.Sqrt(radialX * radialX + radialZ * radialZ);
            if (length < 1e-5f) { x = 0f; z = 0f; return; }
            float s = side < 0 ? -1f : 1f;
            x = -radialZ / length * s;
            z = radialX / length * s;
        }

        // ---------------------------------------------------------------- пенная дуга за головой якоря

        /// <summary>Точки пути головы пишутся до удара + 1; дуга живёт до удара + 3 (спека 5) и рвётся на капли.</summary>
        public const float ArcRecordAfterTicks = 1f, ArcAfterTicks = 3f;

        /// <summary>Хвост дуги — столько секунд пути головы (на 24 м/с ≈ 2 м дуги).</summary>
        public const float ArcTailSeconds = .09f;

        /// <summary>Распад дуги после удара: возраст для шейдера идёт быстрее времени (.30 с шейдера ≈ .19 с).</summary>
        public const float ArcBreakRate = 1.6f;

        /// <summary>Пишется ли путь головы на тике показа (этап начался и удар + 1 ещё не прошёл).</summary>
        public static bool ArcRecording(float shown, int stageStart, int contact)
            => shown >= stageStart && shown <= contact + ArcRecordAfterTicks;

        /// <summary>Рост толщины к удару: 0 в начале этапа, 1 в тик удара.</summary>
        public static float ArcGrow(float shown, int stageStart, int contact)
            => Smooth01((shown - stageStart) / Math.Max(1, contact - stageStart));

        /// <summary>
        /// Полуширина ленты у головы, м: 3 → 8 см к удару. У махов это тонкий след когтя (тело маха — серп пака,
        /// он толще), у удара оземь — вертикальная дуга; <paramref name="stage"/> оставлен для правки по этапам.
        /// </summary>
        public static float ArcHeadHalfWidth(int stage, float grow) => .03f + .05f * Clamp01(grow);

        // ---------------------------------------------------------------- серп маха (меш пака)

        /// <summary>
        /// Серп маха — полумесяц пака CFXR «sword_trail 180 thick» (принятая волна серии сабли, свой пул): его
        /// голова выбегает за ~0,06 с, поэтому он рождается за столько тиков показа до удара — приходит к удару
        /// вместе с головой якоря, а не после неё.
        /// </summary>
        public const float SweepLeadTicks = 2f;

        /// <summary>Центр серпа чуть позади героя, м (острые концы обнимают бока) — как у волны серии сабли.</summary>
        public const float SweepBack = .15f;

        /// <summary>Внешний край серпа — радиус головы якоря по земле (спека 4: 2,2–2,7 м), с пределами.</summary>
        public static float SweepRadius(float headDistance) => Clamp(headDistance, 1.8f, 3f);

        /// <summary>Высота серпа — высота головы над землёй (мах на высоте пояса), с пределами.</summary>
        public static float SweepHeight(float headHeight) => Clamp(headHeight, .45f, 1.5f);

        /// <summary>
        /// Куда уходит голова серпа: +1 — вправо от удара, −1 — влево. По скорости настоящей головы (проекция на
        /// «вправо», м/с); медленная — по стороне маха Sim (Side +1 — справа налево, голова слева).
        /// </summary>
        public static int SweepHeadSide(float velocityRight, int simSide)
        {
            if (Math.Abs(velocityRight) > 1.5f) return velocityRight > 0f ? 1 : -1;
            return simSide < 0 ? 1 : -1;
        }

        // ---------------------------------------------------------------- удар оземь: трещина

        /// <summary>Короткая трещина (кадр A): от точки удара назад на долю радиуса круга, длина — в радиусах круга.</summary>
        public const float CrackBackOfRadius = .35f, CrackLengthOfRadius = 1.6f;

        /// <summary>Возраст распада дуги: 0 до удара, дальше — с ускорением.</summary>
        public static float ArcBreakAge(float shown, int contact)
            => shown <= contact ? 0f : Seconds(shown - contact) * ArcBreakRate;

        public static bool ArcDone(float shown, int contact) => shown > contact + ArcAfterTicks + 4f;

        /// <summary>Капли с цепи в окне между нажатиями, штук в секунду; след тает.</summary>
        public const float WindowDropRate = 16f;

        // ---------------------------------------------------------------- удар оземь: круг

        /// <summary>Сколько гребень дотекает за край урона после хода, м (как у Абордажа).</summary>
        public const float FrontDrift = .12f;

        /// <summary>Круг удара раскрывается до радиуса Sim за столько тиков (урон круга — в тик удара).</summary>
        public const float CraterOpenTicks = 1.5f;

        /// <summary>Разгон кольца круга: быстрый выход и замедление к краю.</summary>
        public const float CraterEase = .7f;

        /// <summary>
        /// Гребень кольца круга, м: в тик удара уже треть радиуса, к удару + 1 — радиус Sim, потом
        /// дотекает экспонентой не дальше <see cref="FrontDrift"/>.
        /// </summary>
        public static float CraterCrest(float shown, int slamTick, float radius)
        {
            float x = (shown - slamTick + .5f) / CraterOpenTicks;
            if (x <= 0f) return 0f;
            if (x <= 1f) return radius * (x + CraterEase * x * (1f - x));
            float after = Seconds((x - 1f) * CraterOpenTicks);
            return radius + FrontDrift * (1f - (float)Math.Exp(-after / .08f));
        }

        /// <summary>Кольцо круга держится столько тиков и рвётся (возраст распада для шейдера).</summary>
        public const float CraterHoldTicks = 4f;

        public static float CraterBreakAge(float shown, int slamTick)
            => Math.Max(0f, Seconds(shown - slamTick - CraterHoldTicks)) * 1.3f;

        public static float CraterLifeSeconds => Seconds(CraterHoldTicks + 2f) + .45f;

        /// <summary>Комья земли падают внутри круга: доля радиуса Sim.</summary>
        public const float ClodReachMin = .25f, ClodReachMax = .95f;

        /// <summary>
        /// Бросок частицы с высоты <paramref name="height"/> так, чтобы за <paramref name="flight"/> с
        /// она пролетела <paramref name="reach"/> м и упала на землю: g — ускорение слоя (м/с²).
        /// </summary>
        public static void Launch(float reach, float flight, float height, float g, out float horizontal, out float vertical)
        {
            flight = Math.Max(.05f, flight);
            horizontal = Math.Max(0f, reach) / flight;
            vertical = (.5f * g * flight * flight - height) / flight;
        }

        // ---------------------------------------------------------------- вал и стена по полосе

        /// <summary>
        /// Губа гребня вдоль полосы от LaneOrigin, м, на тике показа: шаг k (тик WaveTick + k − 1)
        /// бьёт полосу до start + k·step — гребень стоит ровно там в тот же тик, между тиками идёт
        /// линейно, не дальше конца стены (WallEnd, преграда).
        /// </summary>
        public static float WaveFront(float shown, int waveTick, float start, float step, int travelTicks, float end)
        {
            float k = Clamp(shown - waveTick + 1f, 0f, Math.Max(0, travelTicks));
            float front = start + step * k;
            return front < end ? front : end;
        }

        /// <summary>Тик последнего шага фронта (у стены обрушение — тиком позже).</summary>
        public static int WaveEndTick(int waveTick, int travelTicks) => waveTick + Math.Max(1, travelTicks) - 1;

        /// <summary>Высота гребня, м: вал 0,7 (спека 0,6–0,8), Девятый вал ×1 → ×2 по доле урона, стена Волнореза 1,1.</summary>
        public const float CrestBaseHeight = .7f, BreakwaterHeight = 1.1f;

        public static float CrestHeight(PelagForm waveForm, int damagePercent)
        {
            if (waveForm == PelagForm.WreckBreakwater) return BreakwaterHeight;
            return CrestBaseHeight * Clamp(damagePercent, 100, 200) / 100f;
        }

        /// <summary>Загиб губы вперёд: вал — низкий гребень, стена — загибается (кадр B), горб — тяжёлый.</summary>
        public static float CrestCurl(PelagForm waveForm) => waveForm == PelagForm.WreckBreakwater ? .9f : waveForm == PelagForm.WreckNinthWave ? .6f : .35f;

        /// <summary>Задний склон гребня, м (от губы назад до земли).</summary>
        public static float CrestBack(float height) => .45f + .9f * Math.Max(0f, height);

        /// <summary>Гребень встаёт из удара за столько тиков (с трети высоты, не из ничего).</summary>
        public const float CrestRiseTicks = 1.5f;

        /// <summary>После последнего шага гребень оседает и рвётся за столько тиков.</summary>
        public const float CrestCollapseTicks = 7f;

        public static float CrestRise(float shown, int waveTick)
            => .35f + .65f * Smooth01((shown - waveTick + 1f) / CrestRiseTicks);

        /// <summary>Доля высоты после хода: 1 до последнего шага, дальше оседает к нулю.</summary>
        public static float CrestCollapse(float shown, int waveTick, int travelTicks)
            => 1f - Smooth01((shown - WaveEndTick(waveTick, travelTicks)) / CrestCollapseTicks);

        /// <summary>Возраст распада гребня: шейдер рвёт на .30 с — к середине оседания.</summary>
        public static float CrestBreakAge(float shown, int waveTick, int travelTicks)
            => Math.Max(0f, Seconds(shown - WaveEndTick(waveTick, travelTicks))) * 1.6f;

        /// <summary>Гребень и мокрый след живут столько секунд после последнего шага (с запасом капель).</summary>
        public static float WaveLifeSeconds(int travelTicks) => Seconds(Math.Max(1, travelTicks) + CrestCollapseTicks) + .7f;

        /// <summary>Мокрый след за гребнем живёт ~0,4 с (спека 5): шейдер рвёт на .30 — возраст ×0,75.</summary>
        public const float TrailAgeScale = .75f;

        /// <summary>Возраст следа в точке <paramref name="along"/>: сколько прошло с тех пор, как там была губа.</summary>
        public static float TrailAge(float shown, int waveTick, float start, float step, float along)
        {
            float passTick = waveTick - 1f + (along - start) / Math.Max(1e-3f, step);
            return Math.Max(0f, Seconds(shown - passTick)) * TrailAgeScale;
        }

        // ---------------------------------------------------------------- Волнорез

        /// <summary>Несомого стена приподнимает (вид, без Sim): высота, м; набор и сброс — тиков.</summary>
        public const float CarriedLift = .32f, CarriedLiftTicks = 2f, CarriedDropTicks = 3f;

        /// <summary>Подъём несомого на тике показа: подхват в <paramref name="catchTick"/>, обрушение в <paramref name="crashTick"/>.</summary>
        public static float CarriedLiftAt(float shown, int catchTick, int crashTick)
        {
            if (shown < catchTick) return 0f;
            float up = Smooth01((shown - catchTick) / CarriedLiftTicks);
            float down = shown <= crashTick ? 1f : 1f - Smooth01((shown - crashTick) / CarriedDropTicks);
            return CarriedLift * up * down;
        }

        /// <summary>Несомый откинут назад от хода стены (кадр B), град: полный наклон — на полном подъёме.</summary>
        public const float CarriedTiltDegrees = 14f;

        public static float CarriedTiltAt(float lift) => CarriedTiltDegrees * Clamp01(lift / CarriedLift);

        /// <summary>Всплеск обрушения, доля авторского: о преграду — выше и шире.</summary>
        public static float CrashSplashScale(bool stopped) => stopped ? 1.7f : 1.15f;

        // ---------------------------------------------------------------- Девятый вал

        /// <summary>Заряд на тике показа: тики удержания с начала заряда до отпускания, не больше 30.</summary>
        public static float ChargeShown(float shown, int chargeStartTick, int releaseTick, int releasedCharge)
        {
            if (chargeStartTick < 0) return 0f;
            float c = releaseTick >= 0 && shown >= releaseTick ? releasedCharge : shown - chargeStartTick;
            return Clamp(c, 0f, Simulation.WreckChargeMaxTicks);
        }

        public static float Charge01(float charge) => Clamp01(charge / Simulation.WreckChargeMaxTicks);

        /// <summary>Индиговая лента за головой над героем: шире и длиннее (больше круга) с зарядом.</summary>
        public static float VortexHalfWidth(float k) => .045f + .10f * Clamp01(k);

        public static float VortexTailSeconds(float k) => .16f + .20f * Clamp01(k);

        /// <summary>Горб воды в точке удара: видимый рост = заряд (12 → 60 см).</summary>
        public static float HumpHeight(float k) => .12f + .48f * Clamp01(k);

        /// <summary>Полуширина горба = будущая полоса Sim: Width/2 × (1 + c/30).</summary>
        public static float HumpHalfWidth(float baseHalfWidth, float charge) => baseHalfWidth * (1f + Charge01(charge));

        // ---------------------------------------------------------------- Водяной панцирь

        /// <summary>Оболочка: радиус у пояса, лент пены, вращение, рождение и таяние.</summary>
        public const float ShellRadius = .72f, ShellSpin = 2.4f, ShellGrowSeconds = .15f, ShellMeltSeconds = .35f;
        public const int ShellBands = 3;

        /// <summary>Рябь от удара по оболочке: выпуклость, м, и её срок.</summary>
        public const float ShellRippleBulge = .14f, ShellRippleSeconds = .28f;

        public static float ShellRipple(float seconds)
        {
            float u = seconds / ShellRippleSeconds;
            if (u <= 0f || u >= 1f) return 0f;
            return ShellRippleBulge * (float)Math.Sin(Math.PI * Math.Min(1f, u * 2f)) * (1f - u);
        }

        /// <summary>Лопается: оболочка раздаётся до столько метров и рвётся, дальше летят пласты и капли.</summary>
        public const float ShellBurstExpand = 1.25f, ShellBurstExpandSeconds = .12f;

        public static float ShellBurstRadius(float seconds)
            => ShellRadius + (ShellBurstExpand - ShellRadius) * Smooth01(seconds / ShellBurstExpandSeconds);

        /// <summary>Пласты и капли падают внутри радиуса взрыва Sim: доля радиуса.</summary>
        public const float BurstReachMin = .45f, BurstReachMax = .98f;
    }
}
