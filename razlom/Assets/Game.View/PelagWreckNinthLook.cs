using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Крушение · ДЕВЯТЫЙ ВАЛ (форма 12; 07.10; целевой кадр
    /// ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-form-ninthwave.png; ассеты —
    /// Editor/PelagWreckComboVfxSetup.Ninth, рождение — PelagVfxController.WreckNinth / .WreckNinthLunge). Чистые числа вида
    /// без Unity. Sim (Simulation.Wreck.Forms): мах, задевший хоть одного врага, — заряд выпада (один за мах, до двух);
    /// 0 — выпад базы, 1 — урон и ширина полосы ×1,5, 2 — ×2 и полоса на 2 м длиннее. Точка и круг выпада не растут.
    ///
    /// Вид: на махе, давшем заряд, — индиговая вспышка и короткое кольцо у головы якоря, голова дальше светится индиго
    /// (с двумя зарядами — сильнее). Выпад с зарядами — стек выпада базы в индиго, но гребень свой: стоячий веер из трёх
    /// гребней с шипами (центр + два по краям полосы Sim, края наклонены наружу), шире у точки удара и сужается вперёд,
    /// как на целевом кадре; с двумя зарядами — второе эхо позади; по земле — шипы-звёзды по ширине полосы (свет — только
    /// в вале и трещинах, земля не заливается); крупные освещённые камни. Без зарядов — ровно выпад базы.
    /// Чёрного обвода нет (владелец 07.10 «обводка херовая»): контур — тёмное индиго, тонкий.
    /// </summary>
    public static class PelagWreckNinthLook
    {
        // ---------------------------------------------------------------- заряды

        /// <summary>Заряды выпада по доле урона Sim (WreckState.DamagePercent: 100 / 150 / 200).</summary>
        public static int ChargesFromPercent(int percent)
        {
            if (percent >= (Simulation.WreckNinthPercentOne + Simulation.WreckNinthPercentTwo) / 2) return 2;
            if (percent >= (100 + Simulation.WreckNinthPercentOne) / 2) return 1;
            return 0;
        }

        /// <summary>
        /// Задетый махом этапа <paramref name="stage"/> даёт заряд, если этот мах ещё не давал (маска махов серии, бит —
        /// этап) — как WreckNinthSwingLanded в Sim: только махи 1 и 2, один заряд за мах.
        /// </summary>
        public static bool GainsCharge(int stage, int landedMask)
            => stage >= 0 && stage < Simulation.WreckNinthMaxCharges && (landedMask & (1 << stage)) == 0;

        // ---------------------------------------------------------------- палитра (sRGB)

        /// <summary>Индиго формы #4B3FD0 (тот же, что PelagWreckComboLook.MidHex), глубина, светлый, бледный, контур.</summary>
        public const int IndigoHex = 0x4B3FD0, DeepHex = 0x1A1062, LightHex = 0x8A6BFF, PaleHex = 0xDCD6FF, ContourHex = 0x14093F;

        // ---------------------------------------------------------------- гребни выпада

        /// <summary>Высота центрального гребня у фронта, м: база 0,95, заряд +38 % (1,31 / 1,67).</summary>
        public static float CenterHeight(int charges) => .95f * (1f + .38f * Clamp(charges, 0, 2));

        /// <summary>Гребень ниже к концу полосы (доля 0…1 пути от точки удара до конца): к концу — 0,55.</summary>
        public static float HeightTaper(float t) => 1f - .45f * Clamp01(t);

        /// <summary>
        /// Ширина вала поперёк полосы — доля полуширины Sim: у точки удара ровно край полосы, вперёд сужается (на конце 0,42),
        /// как на целевом кадре (вал шире у якоря и острый вперёд).
        /// </summary>
        public static float LateralTaper(float t) => 1f - .58f * (float)Math.Pow(Clamp01(t), 1.2);

        /// <summary>
        /// Крайние гребни встают ВНУТРИ вала по земле (доля полуширины Sim), ниже центрального, клонятся наружу: дальний —
        /// от камеры сильнее, ближний — чуть (камера 48°: лента, наклонённая к камере на ~45°, видна ребром — пробы 1–2,
        /// ближний край у кромки полосы выходил оторванной полоской).
        /// </summary>
        public const float FlankHeight = .40f, FlankInset = .62f, FlankTiltFar = 34f, FlankTiltNear = 12f;

        /// <summary>
        /// Вал по земле (второй слой): край меша — полуширина Sim × сужение × разлёт (шипы выходят за край полосы), чуть над
        /// землёй, м.
        /// </summary>
        public const float GroundSpread = 1.45f, GroundLift = .035f;

        /// <summary>Эхо центрального: на тик позже, выше, дальше от камеры (как у базы). Второе эхо (2 заряда) — ещё позже и выше.</summary>
        public const float EchoDelayTicks = 1f, EchoHeight = 1.08f, EchoBack = .24f;
        public const float Echo2DelayTicks = 2.2f, Echo2Height = 1.32f, Echo2Back = .55f;

        /// <summary>Крайние гребни встают на полтика позже центрального (веер раскрывается от оси).</summary>
        public const float FlankDelayTicks = .5f;

        /// <summary>
        /// Острый конец: последние <see cref="EndTaperMeters"/> м до конца полосы гребни и вал по земле сходят на нет до доли
        /// <see cref="EndTaperFloor"/> (раунд 1 в игре: конец полосы — тупой срез, вал читался брусом).
        /// </summary>
        public const float EndTaperMeters = 1.6f, EndTaperFloor = .22f;

        public static float EndTaper(float s, float end)
        {
            float x = Clamp01((end - s) / EndTaperMeters);
            return EndTaperFloor + (1f - EndTaperFloor) * x * x * (3f - 2f * x);
        }

        /// <summary>
        /// Высота точки гребня по её возрасту: подъём за <see cref="RiseSeconds"/> (у базы 0,035 с) — фронт клином, а не
        /// стенкой (раунд 1: три ленты с отвесными передними краями складывались в коробку), перелёт, потом спад, как у базы.
        /// </summary>
        public const float RiseSeconds = .10f, Overshoot = 1.08f, SettleSeconds = .14f, CollapseTo = .44f, CollapseFloor = .14f;

        public static float Profile(float age)
        {
            if (age <= 0f) return 0f;
            if (age < RiseSeconds) return Overshoot * (float)Math.Sin(Math.PI * .5 * age / RiseSeconds);
            if (age < SettleSeconds) return Overshoot + (1f - Overshoot) * Smooth((age - RiseSeconds) / (SettleSeconds - RiseSeconds));
            if (age >= CollapseTo) return CollapseFloor;
            float t = (age - SettleSeconds) / (CollapseTo - SettleSeconds);
            return 1f - (1f - CollapseFloor) * (float)Math.Pow(t, 1.25);
        }

        private static float Smooth(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>Доля пути вала (0 — точка удара, 1 — конец полосы).</summary>
        public static float PathFraction(float s, float start, float end) => end - start > 1e-3f ? Clamp01((s - start) / (end - start)) : 0f;

        // ---------------------------------------------------------------- куски по фронту

        /// <summary>
        /// Крупных камней с шага фронта: 1 заряд — один, 2 — один и через шаг второй (раунд 1: по два на шаг — завал из
        /// двух дюжин глыб, на целевом кадре их около десятка).
        /// </summary>
        public static int BigRocksAt(int charges, int step) => charges >= 2 ? 1 + (step & 1) : charges == 1 ? 1 : 0;

        /// <summary>Размер крупного камня, м (по наибольшей оси): с двумя зарядами крупнее.</summary>
        public static float RockMin(int charges) => charges >= 2 ? .58f : .46f;
        public static float RockMax(int charges) => charges >= 2 ? .95f : .74f;

        /// <summary>Шипы-звезда по земле: вдоль полосы — шагов фронта, поперёк — доля полной ширины вала.</summary>
        public const float GroundStarAlong = 3.4f, GroundStarAcross = 1.25f;

        /// <summary>Искр с крайних гребней на шаг фронта.</summary>
        public static int FlankSparks(int charges) => charges >= 2 ? 4 : 3;

        /// <summary>Веер у точки удара (стоячая полузвезда шире круга — по ширине полосы Sim): ширина, м.</summary>
        public static float FanWidth(float halfWidth) => 2f * Clamp(halfWidth, .5f, 2f) * 1.15f;

        // ---------------------------------------------------------------- заряд на махе и свечение головы

        /// <summary>Вспышка на голове якоря при заряде, м (диаметр): второй заряд крупнее.</summary>
        public static float FlareSize(int chargesAfter) => chargesAfter >= 2 ? 1.25f : .95f;

        /// <summary>Свечение головы якоря между зарядом и выпадом: масштаб ореола и сколько искорок в секунду.</summary>
        public static float AuraScale(int charges) => charges >= 2 ? 1.18f : charges == 1 ? .82f : 0f;
        public static float MotesPerSecond(int charges) => charges >= 2 ? 34f : charges == 1 ? 14f : 0f;

        /// <summary>Ореол гаснет за столько секунд после выпада или конца серии (короткий хлопок, не мягкое таяние).</summary>
        public const float AuraOutSeconds = .10f;

        /// <summary>Ореол без выпада живёт не дольше (серию бросили после махов): окно серии 0,8 с + запас.</summary>
        public const float AuraMaxSeconds = 1.6f;

        public static int Clamp(int x, int a, int b) => x < a ? a : x > b ? b : x;
        public static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        public static float Clamp01(float x) => Clamp(x, 0f, 1f);
    }
}
