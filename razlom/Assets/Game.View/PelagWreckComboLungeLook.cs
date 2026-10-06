using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Выпад Крушения на стеке серии сабли (06.10 поздно; ассеты — Editor/PelagWreckComboVfxSetup.Lunge, рождение и
    /// лента — PelagVfxController.WreckComboLunge). Чистые числа вида без Unity. Как добивающий сабли
    /// (стоячая волна к камере), но якорная семья: всплеск в точке удара СТОИТ к камере (полузвезда от земли, звезда у
    /// головы якоря и её эхо), по полосе Sim бежит СТОЯЧИЙ гребень кобальта (лента поднята к экрану), за фронтом он
    /// оседает и рвётся гранями; лежат на земле только кольцо удара, трещины и тонкий след. Палитра — та же, что у
    /// махов (PelagWreckComboLook), формы — другой краской того же стека.
    /// </summary>
    public static class PelagWreckComboLungeLook
    {
        // ---------------------------------------------------------------- гребень по полосе

        /// <summary>Высота гребня у фронта, м: база 0,95 (спека 0,6–0,9 + перелёт хлопка), стена Волнореза 1,1, Девятый вал — по доле урона.</summary>
        public static float CrestHeight(PelagForm form, int damagePercent)
        {
            if (form == PelagForm.WreckBreakwater) return 1.1f;
            float percent = Clamp(damagePercent, 100, 200) / 100f;
            return Math.Min(1.6f, .95f * (form == PelagForm.WreckNinthWave ? .8f + .45f * percent : 1f));
        }

        /// <summary>Гребень встаёт за фронтом за столько секунд (выпуклый передний склон ~0,5 м при шаге фронта 15 м/с).</summary>
        public const float RiseSeconds = .035f;

        /// <summary>Перелёт высоты сразу за фронтом (хлопок, как у волны сабли .86 → 1.03 → 1), отдача к 1 к этому возрасту.</summary>
        public const float Overshoot = 1.10f, SettleSeconds = .065f;

        /// <summary>Спад позади фронта (как у полумесяца сабли: толсто у головы, острый хвост): к этому возрасту — до доли пола.</summary>
        public const float CollapseTo = .40f, CollapseFloor = .14f;

        /// <summary>
        /// Доля высоты гребня в точке ленты по её возрасту (с тех пор, как там прошёл фронт Sim): 0 на самом фронте, выпуклый
        /// подъём с перелётом за ~0,5 м, потом выпуклый спад назад (верх — дуга, как спина полумесяца сабли; L2 с почти
        /// прямым спадом читался наклонной «рампой», L3 со степенью 1,6 — плитой): высокий у фронта, острый хвост к точке удара.
        /// Шейдер параллельно рвёт гранями и гасит старое (_ErodeFrom/_FadeFrom).
        /// </summary>
        public static float Profile(float localAge)
        {
            if (localAge <= 0f) return 0f;
            if (localAge < RiseSeconds) return Overshoot * (float)Math.Sin(Math.PI * .5 * localAge / RiseSeconds);
            if (localAge < SettleSeconds) return Lerp(Overshoot, 1f, Smooth01((localAge - RiseSeconds) / (SettleSeconds - RiseSeconds)));
            if (localAge >= CollapseTo) return CollapseFloor;
            float t = (localAge - SettleSeconds) / (CollapseTo - SettleSeconds);
            return 1f - (1f - CollapseFloor) * (float)Math.Pow(t, 1.25);
        }

        /// <summary>Эхо гребня: на тик позже, выше, отодвинуто от камеры, темнее и рвётся первым.</summary>
        public const float EchoDelaySeconds = 1f / Simulation.TicksPerSecond, EchoHeight = 1.25f, EchoBack = .22f;

        /// <summary>Наклон верха гребня вперёд по ходу (доля высоты): гребень «валится» вперёд, как вал.</summary>
        public const float Lean = .40f;

        /// <summary>Лента начинается до точки удара (выходит из всплеска), м.</summary>
        public const float TailLead = .35f;

        /// <summary>Шаг ленты вдоль полосы, м.</summary>
        public const float Segment = .10f;

        /// <summary>Рисунок шейдера: u = метры вдоль полосы / столько (грани и зубцы не тянутся с длиной полосы).</summary>
        public const float MetersPerU = 4f;

        /// <summary>Низ ленты под землёй, м (обвод низа прячет земля — гребень растёт из неё).</summary>
        public const float Sink = .10f;

        /// <summary>Лента живёт после последнего шага фронта, с (оседание + распад шейдера).</summary>
        public const float AfterSeconds = .55f;

        // ---------------------------------------------------------------- удар оземь

        /// <summary>Диаметр звезды у головы якоря, м, по кругу удара Sim (1,2 м → 1,75 м).</summary>
        public static float StarDiameter(float impactRadius) => Clamp(impactRadius, .6f, 2.2f) * 1.45f;

        /// <summary>Полузвезда от земли (стоит к камере): ширина, м; высота — половина.</summary>
        public static float HalfWidth(float impactRadius) => Clamp(impactRadius, .6f, 2.2f) * 1.9f;

        /// <summary>Высота звезды над землёй, м: голова якоря на ударе лежит у земли, звезда — чуть выше неё.</summary>
        public const float StarHeight = .55f;

        /// <summary>Кольцо удара по земле до круга Sim (наружный край), трещины чуть шире.</summary>
        public static float RingRadius(float impactRadius) => Clamp(impactRadius, .6f, 2.2f);

        public static float CrackDiameter(float impactRadius) => Clamp(impactRadius, .6f, 2.2f) * 2.4f;

        /// <summary>Знак на задетом выпадом: круг удара крупнее, вал — меньше (L1: ×1,35 белые звёзды перекрывали гребень).</summary>
        public const float CircleHitScale = 1.1f, WaveHitScale = .95f;

        // ---------------------------------------------------------------- куски по фронту

        /// <summary>
        /// Крупный камень с шага фронта: по жребию <paramref name="roll"/> (0…1) — не на каждом шаге, чтобы по линии
        /// легло ~5 крупных камней на 8 шагов, как на целевом кадре; на последнем — всегда.
        /// </summary>
        public static bool BigRockAt(int step, int travel, float roll) => step >= travel || roll < .62f;

        /// <summary>Искр с шага фронта (с верха гребня вперёд и вверх) и в конце полосы.</summary>
        public const int StepSparks = 5, EndSparks = 10;

        public static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        public static float Clamp01(float x) => Clamp(x, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
