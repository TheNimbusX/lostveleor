using System;

namespace Game.View
{
    /// <summary>
    /// Звенья над героем на ударах серии Крушения (06.10, нижний ряд целевого кадра series-ui.png) — числа без
    /// Unity, тесты HudWreckSeriesRulesTests. Вид — PelagWreckSeriesView; рождается от события Sim WreckStage
    /// (Amount — этап, Flag — последний удар серии), не опросом.
    ///
    /// На удар N над головой вскакивает N звеньев в ряд (новое — с перелётом) и всё гаснет за <see cref="MarkSeconds"/>.
    /// Последний удар: звенья вылетают разлётом и за <see cref="SnapSeconds"/> сходятся в короткую цепь (шаг меньше
    /// ширины звена — звенья заходят друг в друга), в миг сцепки — лучи. Время — тик показа (тик − 1 + Alpha),
    /// пауза держит кадр. Размеры — метры мира; ряд лежит вдоль «вправо» камеры, лицом к камере.
    /// </summary>
    public static class PelagWreckSeriesMarkRules
    {
        /// <summary>Жизнь звеньев от удара, с; с этой секунды начинают гаснуть.</summary>
        public const float MarkSeconds = .5f, FadeFrom = .2f;

        /// <summary>Вскакивание нового звена: от доли размера через перелёт к покою, с.</summary>
        public const float PopSeconds = .12f, PopFrom = .45f, PopPeak = 1.22f;

        /// <summary>Финал: сцепка разлёта в цепь, с; лучи после сцепки, с.</summary>
        public const float SnapSeconds = .1f, BurstSeconds = .32f;

        /// <summary>Звено: ширина, м (высота — по рисунку 128 × 72); шаг ряда, шаг разлёта и шаг цепи, м.</summary>
        public const float LinkWidth = .3f, Pitch = .34f, SpreadPitch = .5f, ChainPitch = .22f;

        /// <summary>Над костью головы, м; без кости — над землёй, м.</summary>
        public const float HeadLift = .55f, FallbackHeight = 2.45f;

        /// <summary>Остывание к цвету формы от белого, с.</summary>
        public const float HotSeconds = .18f;

        /// <summary>Звеньев на ударе: столько, сколько ударов серии (1…4).</summary>
        public static int Links(int strikes) => strikes < 1 ? 1 : strikes > HudWreckSeriesRules.MaxLinks ? HudWreckSeriesRules.MaxLinks : strikes;

        /// <summary>Прозрачность всех звеньев удара: держатся до FadeFrom и гаснут к MarkSeconds; вне жизни — 0.</summary>
        public static float Alpha(float age)
        {
            if (age < 0f || age >= MarkSeconds) return 0f;
            return 1f - HudWreckSeriesRules.Smooth01((age - FadeFrom) / (MarkSeconds - FadeFrom));
        }

        /// <summary>Вскакивание: PopFrom → PopPeak за PopSeconds, к 1 за ещё столько же.</summary>
        public static float Pop(float age)
        {
            if (age <= 0f) return PopFrom;
            float u = age / PopSeconds;
            if (u >= 2f) return 1f;
            if (u <= 1f) return PopFrom + (PopPeak - PopFrom) * (1f - (1f - u) * (1f - u));
            return PopPeak + (1f - PopPeak) * HudWreckSeriesRules.Smooth01(u - 1f);
        }

        /// <summary>Размер звена <paramref name="index"/> из <paramref name="count"/>: вскакивает новое, в финале — все.</summary>
        public static float Scale(int index, int count, float age, bool final)
        {
            if (final) return Pop(age) + .18f * HudWreckSeriesRules.Bump(age - SnapSeconds, BurstSeconds * .6f);
            return index == count - 1 ? Pop(age) : 1f;
        }

        /// <summary>Середина звена вдоль «вправо» камеры, м: ряд по центру головы; в финале разлёт сходится в цепь.</summary>
        public static float Offset(int index, int count, float age, bool final)
        {
            float pitch = final ? SpreadPitch + (ChainPitch - SpreadPitch) * SnapProgress(age) : Pitch;
            return (index - (count - 1) * .5f) * pitch;
        }

        /// <summary>Сцепка финала 0 → 1 (резко к концу: звенья «защёлкиваются»).</summary>
        public static float SnapProgress(float age)
        {
            float u = age / SnapSeconds;
            u = u < 0f ? 0f : u > 1f ? 1f : u;
            return u * u;
        }

        /// <summary>Доля белого: новое звено (в финале — все) раскалено в миг удара; в сцепку — вторая вспышка.</summary>
        public static float Hot(int index, int count, float age, bool final)
        {
            if (!final) return index == count - 1 ? HudWreckSeriesRules.Heat(age, HotSeconds) : 0f;
            return Math.Max(HudWreckSeriesRules.Heat(age, HotSeconds), HudWreckSeriesRules.Heat(age - SnapSeconds, HotSeconds));
        }

        /// <summary>Лучи финала: появляются в миг сцепки, гаснут за BurstSeconds; до сцепки и не в финале — 0.</summary>
        public static float BurstAlpha(float age, bool final)
        {
            float b = age - SnapSeconds;
            if (!final || b < 0f || b >= BurstSeconds) return 0f;
            return HudWreckSeriesRules.Smooth01(b / .04f) * (1f - HudWreckSeriesRules.Smooth01(b / BurstSeconds));
        }

        /// <summary>Размер лучей (доля ширины цепи × 2): растут с ease-out.</summary>
        public static float BurstScale(float age)
        {
            float b = (age - SnapSeconds) / BurstSeconds;
            b = b < 0f ? 0f : b > 1f ? 1f : b;
            return .6f + .7f * (1f - (1f - b) * (1f - b));
        }
    }
}
