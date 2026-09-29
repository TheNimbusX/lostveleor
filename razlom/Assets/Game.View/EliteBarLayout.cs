using System;
using System.Globalization;

namespace Game.View
{
    /// <summary>
    /// Числа полоски здоровья над врагом без UnityEngine: надпись в полосе элиты, низ полосы элиты с
    /// рогами и высота полоски над макушкой. Отдельно от HealthBars, чтобы их проверяли тесты вне Unity
    /// (tools/Combat.Presentation.Tests/EliteBarLayoutTests.cs); HealthBars только рисует по ним.
    /// </summary>
    public static class EliteBarLayout
    {
        /// <summary>
        /// Надпись в полосе элиты: «1240 / 2000» — текущее здоровье и максимум через « / », как у
        /// полосы героя в боевом HUD (CombatHudView). Без разделителей тысяч и культуры системы.
        /// Здоровье зажимается в [0; максимум]: отрицательного и «больше полного» в полосе не бывает.
        /// </summary>
        public static string Numbers(int health, int max)
        {
            if (max < 0) max = 0;
            if (health < 0) health = 0;
            else if (health > max) health = max;
            return health.ToString(CultureInfo.InvariantCulture) + " / " + max.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Сколько полосы элиты ниже её середины, м — по этому низу полоса встаёт над макушкой
        /// (<see cref="Target"/>, halfHeight): половина полосы или основание рога, что ниже. Рога на
        /// концах полосы (выбор владельца 29.09 «5 — Рога») растут вверх, но их срез может свисать под
        /// полосу — тогда полоса поднимается, чтобы рог не лёг на голову моба.
        /// </summary>
        /// <param name="barHeight">Высота полосы, м.</param>
        /// <param name="antlerHeight">Высота холста рога, м.</param>
        /// <param name="antlerLift">Точка крепления рога над серединой полосы, м (минус — ниже).</param>
        /// <param name="antlerBelow">Сколько рога ниже точки крепления, в долях высоты холста
        /// (make-elite-bar-mark.py печатает это число).</param>
        public static float EliteBelow(float barHeight, float antlerHeight, float antlerLift, float antlerBelow)
        {
            float half = Math.Max(0f, barHeight) * .5f;
            // Рога нет (высота 0) — только полоса; !(… > …) ловит и NaN.
            if (!(antlerHeight > 0f) || !(antlerBelow > 0f)) return half;
            float antler = antlerBelow * antlerHeight - antlerLift;
            return antler > half ? antler : half;
        }

        /// <summary>Ниже этой макушки (м) замер не верится: тело ещё в земле или его рендереры скрыты.</summary>
        public const float MinTop = .3f;

        /// <summary>
        /// На какой высоте над землёй должна висеть середина полоски, чтобы её низ был выше макушки
        /// модели на экране на <paramref name="gap"/> метров.
        /// </summary>
        /// <param name="top">Макушка: самая высокая на экране точка тела, пересчитанная в метры по
        /// вертикали над точкой сущности (HealthBars.MeasureTop).</param>
        /// <param name="measured">Замер есть (у тела нашлись видимые рендереры).</param>
        /// <param name="fallback">Прежняя высота середины полоски из таблицы вида — по ней, когда замера
        /// нет; она же ограничивает замер сверху: вдвое выше таблицы бывают только раздутые границы
        /// кожи (у рантайм-префабов мобов они 4,5 м), а не рога.</param>
        /// <param name="halfHeight">Сколько полоски ниже её середины, м: половина высоты (у элиты —
        /// <see cref="EliteBelow"/>).</param>
        public static float Target(float top, bool measured, float fallback, float gap, float halfHeight)
        {
            // !(top > …) ловит и NaN.
            if (!measured || !(top > MinTop)) return fallback;
            float ceiling = Math.Max(fallback * 2f, MinTop + 1f);
            if (top > ceiling) top = ceiling;
            return top + gap + halfHeight;
        }

        /// <summary>
        /// Следующая высота полоски за кадр <paramref name="dt"/>: к более высокой макушке — быстро
        /// (<paramref name="rise"/>, 1/с), к более низкой — медленно (<paramref name="fall"/>). Вырос в
        /// замахе или прыжке — полоска сразу уходит вверх и не тонет; опустил лапу — не прыгает
        /// за каждым взмахом вниз-вверх.
        /// </summary>
        public static float Follow(float current, float target, float dt, float rise, float fall)
        {
            if (!(dt > 0f)) return current;
            float rate = target > current ? rise : fall;
            float k = 1f - (float)Math.Exp(-Math.Max(0f, rate) * dt);
            return current + (target - current) * k;
        }
    }
}
