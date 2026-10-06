using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Что показать индикатором серии в этот кадр (HudWreckSeriesTracker.Observe): сколько звеньев в ряду и сколько
    /// горит, какое звено только что зажглось, финал серии и сколько осталось окна.
    /// </summary>
    public struct HudWreckSeriesShow
    {
        /// <summary>Звеньев в ряду: 3, с «Четвёртым ударом» 4; 0 — у слота нет Крушения, ряд спрятан.</summary>
        public int Links;

        /// <summary>Сколько звеньев горит (ударов серии этого слота).</summary>
        public int Lit;

        /// <summary>Яркость горящих звеньев: 1 в серии, после конца серии гаснет за EndFadeSeconds.</summary>
        public float Glow;

        /// <summary>Последнее зажжённое звено (−1 — ни одного) и секунды от его удара.</summary>
        public int Newest;
        public float NewestAge;

        /// <summary>Все удары серии сделаны (после третьего, с талантом — четвёртого): звенья вспыхивают разом.</summary>
        public bool Final;

        /// <summary>Остаток окна следующего нажатия 1 … 0; меньше нуля — окна нет, кольцо спрятано.</summary>
        public float Window;
    }

    /// <summary>
    /// ИНДИКАТОР СЕРИИ КРУШЕНИЯ (06.10, целевой кадр ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/
    /// series-ui.png, решение владельца — «и там, и там»). Числа без Unity: проверяются тестами представления на
    /// живой Sim (tools/Combat.Presentation.Tests/HudWreckSeriesRulesTests.cs).
    ///
    /// * Под плиткой Крушения — ряд маленьких железных звеньев (тёмные — пусто), удар серии зажигает следующее
    ///   цветом формы: звено вскакивает и коротко раскаляется к белому. Ряд — под кейкапом плитки (у нашей плитки
    ///   клавиша сидит на нижней кромке), дуга точек над плиткой — усиления, её не трогаем.
    /// * Кольцо слота светится цветом формы и гаснет по кругу, пока открыто окно следующего нажатия: от удара
    ///   (ContactTick) до WindowEndTick включительно — сроки только из WreckState, тиков здесь нет.
    /// * После последнего удара все звенья вспыхивают, за ними — короткие лучи; слот уходит в перезарядку сам
    ///   (CombatHudView). Серия кончилась — звенья гаснут за EndFadeSeconds.
    /// * Над героем (PelagWreckSeriesView) на каждый удар вскакивает столько же звеньев и гаснет за MarkSeconds;
    ///   на последнем — разлёт сходится в короткую цепь и лопается лучами.
    ///
    /// Цвет — цвет формы (таблица <see cref="FormHex"/>): база #4FA8FF, Волнорез #1FB37E, Девятый вал #4B3FD0,
    /// Якорная броня #E4EEF6. Красного, оранжевого и золота нет.
    /// </summary>
    public static class HudWreckSeriesRules
    {
        // ---------------------------------------------------------------- ряд под плиткой (пиксели холста 1920×1080)

        /// <summary>Звено: ширина, высота (у рисунка 128 × 72), шаг между серединами.</summary>
        public const float LinkWidth = 20f, LinkHeight = 11.25f, LinkPitch = 22f;

        /// <summary>Середина ряда — ниже нижней кромки плитки (под кейкапом 26 px, сидящим на кромке).</summary>
        public const float RowDrop = 28f;

        /// <summary>Узлов звеньев в префабе: три серии + «Четвёртый удар».</summary>
        public const int MaxLinks = 4;

        /// <summary>Сколько звеньев в ряду: три, с «Четвёртым ударом» — четыре (как Simulation.WreckStageCount).</summary>
        public static int LinkCount(bool fourthStrike) => Simulation.WreckStages + (fourthStrike ? 1 : 0);

        /// <summary>
        /// Серия идёт в этом слоте (от первого нажатия до конца выхода или окна): плитка — иконка со звеньями и
        /// кольцом окна, без вуали и цифры перезарядки; цифра — только после конца серии (CombatHudView, 06.10).
        /// </summary>
        public static bool InSeries(in WreckState s, int slot) => s.Phase != WreckPhase.None && s.Slot == slot;

        /// <summary>Середина звена по x от середины плитки.</summary>
        public static float LinkX(int index, int count) => (index - (count - 1) * .5f) * LinkPitch;

        // ---------------------------------------------------------------- время

        /// <summary>Звено зажглось: вскакивает (доля размера на пике) и остывает от белого к цвету формы.</summary>
        public const float PopSeconds = .16f, PopOvershoot = .45f, HotSeconds = .22f;

        /// <summary>Финал серии: звенья разом вскакивают, лучи вспышки живут BurstSeconds.</summary>
        public const float FinalOvershoot = .3f, BurstSeconds = .45f;

        /// <summary>Серия кончилась: горящие звенья гаснут за столько.</summary>
        public const float EndFadeSeconds = .3f;

        /// <summary>Яркость кольца окна (аддитивное, поверх огненного кольца).</summary>
        public const float WindowAlpha = .9f;

        public static float Smooth01(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }

        /// <summary>Горб 0 → 1 → 0 за <paramref name="seconds"/>; вне — 0.</summary>
        public static float Bump(float age, float seconds)
            => age <= 0f || age >= seconds ? 0f : (float)Math.Sin(Math.PI * age / seconds);

        /// <summary>Жар: 1 в миг удара, остывает экспонентой; через 4 срока — ноль.</summary>
        public static float Heat(float age, float seconds)
            => age < 0f || age >= seconds * 4f ? 0f : (float)Math.Exp(-age / seconds);

        // ---------------------------------------------------------------- окно

        /// <summary>
        /// Остаток окна следующего нажатия: 1 в тик удара, 0 — к WindowEndTick. Нажатие, поданное в кадре тика
        /// показа t, Sim примет на тике t + 1, а окно держит до WindowEndTick включительно — поэтому кольцо пусто уже
        /// к t = WindowEndTick: кольцо горит ровно пока Simulation.WreckComboOpen (тик Sim t + 1 ≤ WindowEndTick).
        /// Окна нет — −1: замах (нажатие уже принято), заряд, серия кончилась или все удары сделаны.
        /// <paramref name="now"/> — время показа (тик − 1 + Alpha).
        /// </summary>
        public static float WindowLeft(in WreckState s, int links, float now)
        {
            if (s.Phase == WreckPhase.None || s.Phase == WreckPhase.Windup || s.Phase == WreckPhase.Charge) return -1f;
            if (s.Strikes <= 0 || s.Strikes >= links || s.WindowEndTick < 0) return -1f;
            float end = s.WindowEndTick, start = s.ContactTick;
            if (end <= start || now >= end) return -1f;
            float left = (end - now) / (end - start);
            return left > 1f ? 1f : left;
        }

        // ---------------------------------------------------------------- вид звена

        /// <summary>Размер звена (1 — покой): только что зажжённое вскакивает, в финале — все горящие.</summary>
        public static float LinkScale(int index, in HudWreckSeriesShow show)
        {
            if (index >= show.Lit) return 1f;
            float scale = 1f;
            if (index == show.Newest) scale += PopOvershoot * Bump(show.NewestAge, PopSeconds);
            if (show.Final) scale += FinalOvershoot * Bump(show.NewestAge - PopSeconds * .5f, PopSeconds * 1.6f);
            return scale;
        }

        /// <summary>Прозрачность свечения звена (0 — тёмное железо).</summary>
        public static float LinkGlow(int index, in HudWreckSeriesShow show) => index < show.Lit ? show.Glow : 0f;

        /// <summary>Доля белого в цвете звена: раскалённое в миг удара, в финале — все разом.</summary>
        public static float LinkHot(int index, in HudWreckSeriesShow show)
        {
            if (index >= show.Lit) return 0f;
            float hot = index == show.Newest ? Heat(show.NewestAge, HotSeconds) : 0f;
            return show.Final ? Math.Max(hot, Heat(show.NewestAge, HotSeconds * 1.4f)) : hot;
        }

        /// <summary>Лучи финала: прозрачность (вскакивают за 0,05 с и гаснут к BurstSeconds) и размер.</summary>
        public static float BurstAlpha(in HudWreckSeriesShow show)
        {
            if (!show.Final || show.NewestAge < 0f || show.NewestAge >= BurstSeconds) return 0f;
            float rise = Smooth01(show.NewestAge / .05f);
            return rise * (1f - Smooth01(show.NewestAge / BurstSeconds)) * show.Glow;
        }

        public static float BurstScale(in HudWreckSeriesShow show)
            => .7f + .55f * (1f - (float)Math.Pow(1f - Smooth01(show.NewestAge / BurstSeconds), 2));

        // ---------------------------------------------------------------- цвет формы

        /// <summary>Цвет формы — ТАБЛИЦА ДЛЯ ПРАВКИ (тон звеньев «холодного железа» и иконок форм, 06.10).</summary>
        public static int FormHex(PelagForm form)
        {
            switch (form)
            {
                case PelagForm.WreckBreakwater: return 0x1FB37E; // морская зелень
                case PelagForm.WreckNinthWave: return 0x4B3FD0;  // индиго
                case PelagForm.WreckGhostAnchor: return 0xE4EEF6; // жемчуг (номер 13: была «Якорная броня», с 06.10 — Призрачный якорь)
                default: return 0x4FA8FF;                        // холодный голубой базы
            }
        }

        /// <summary>Цвет формы, смешанный к белому на долю <paramref name="hot"/> (0…1), каналы 0…1.</summary>
        public static void FormColour(PelagForm form, float hot, out float r, out float g, out float b)
        {
            int hex = FormHex(form);
            float h = hot < 0f ? 0f : hot > 1f ? 1f : hot;
            r = Mix((hex >> 16) & 0xFF, h);
            g = Mix((hex >> 8) & 0xFF, h);
            b = Mix(hex & 0xFF, h);
        }

        private static float Mix(int channel, float hot) => channel / 255f + (1f - channel / 255f) * hot * .7f;
    }
}
