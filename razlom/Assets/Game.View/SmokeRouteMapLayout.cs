using System;

namespace Game.View
{
    /// <summary>Знак узла карты тушью — чем была или будет арена. Новые значения — только в конец.</summary>
    public enum SmokeRouteSign : byte
    {
        /// <summary>Пройденная арена, о которой карта ничего не запомнила: скрещённые клинки.</summary>
        Arena = 0,
        /// <summary>Начало пути из лагеря через арку.</summary>
        Camp = 1,
        /// <summary>Начало пути «Повторить» с итогов.</summary>
        Repeat = 2,
        /// <summary>Путь «Улучшение».</summary>
        Upgrade = 3,
        /// <summary>Путь «Магазин».</summary>
        Shop = 4,
        /// <summary>Опасная арена.</summary>
        Hard = 5,
        /// <summary>Арена Хранителя.</summary>
        Boss = 6,
    }

    /// <summary>Что ждёт на следующей арене — подпись под загоревшимся узлом карты тушью.</summary>
    public struct SmokeRouteAhead
    {
        /// <summary>Забег найден; без него подписи «впереди» нет, только номер арены.</summary>
        public bool Known;
        public bool Boss;
        public string BossName;
        public bool Elite;
        public string EliteName;
        /// <summary>Опасный путь: враги сильнее, бонус золота за зачистку.</summary>
        public bool Hard;
        public int BonusGold;
        /// <summary>Новый вид врага (урок шаблона встречи); null — знакомые.</summary>
        public string Lesson;
        public bool Ambush;
        public bool Survival;
        /// <summary>Награда пути — магазин; иначе улучшение.</summary>
        public bool Shop;
    }

    /// <summary>
    /// Карта тушью на закрытой дымной завесе (выбор владельца 30.09, концепты a5/a6 в
    /// ART/UI/concepts-2026-09-30-hud-final): раскладка узлов и дорог, кривая мазка, подпись «впереди» и
    /// расписание анимации — без UnityEngine, чтобы их проверяли тесты вне Unity
    /// (tools/Combat.Presentation.Tests/SmokeRouteMapLayoutTests.cs). SmokeRouteMap только рисует по ним.
    ///
    /// Координаты — единицы холста завесы (высота 1080), начало — середина экрана, y вверх. Дорога идёт
    /// слева направо: пройденные арены (с лагеря, глубина 0) — слева, выбранная следующая — на
    /// <see cref="NextX"/>, развилка из трёх путей — столбиком на той же x. Каждая арена стоит на своей
    /// высоте волны (<see cref="WaveY"/>): от перехода к переходу карта сдвигается на шаг влево, а узел
    /// остаётся на той же высоте — дорога читается одной картой, а не новой картинкой.
    /// </summary>
    public static class SmokeRouteMapLayout
    {
        /// <summary>Шаг узлов по x.</summary>
        public const float Spacing = 330f;
        /// <summary>Размах волны дороги по y.</summary>
        public const float Wave = 46f;
        /// <summary>Середина волны: чуть выше середины экрана — под узлом развилки место для подписи.</summary>
        public const float RoadY = 36f;
        /// <summary>Между соседними путями развилки по y: верхний круг остаётся под нитью заголовка (+382).</summary>
        public const float ForkSpread = 200f;
        /// <summary>Бледный путь развилки кончается у края пустого круга, а не в его середине, единиц.</summary>
        public const float RingEdge = 48f;
        /// <summary>Полуразмер пустого круга развилки (круг 104).</summary>
        public const float RingHalf = 52f;
        /// <summary>«Путь по Разлому» и нить света под ним, y: выше самого высокого круга развилки.</summary>
        public const float TitleY = 420f, ThreadY = 382f;
        /// <summary>Подпись «Арена N · впереди» — ниже середины загоревшегося узла на столько, единиц.</summary>
        public const float CaptionDrop = 132f;
        /// <summary>Полувысота подписи с «впереди» ниже её середины, единиц (строка 25 под названием 50).</summary>
        public const float CaptionBelow = 44f;
        /// <summary>Где встаёт следующая арена по x (правее середины: слева видна пройденная дорога).</summary>
        public const float NextX = 200f;
        /// <summary>Сколько пройденных узлов карта держит (включая лагерь); старые уходят за левый край.</summary>
        public const int MaxPast = 7;
        /// <summary>Узел ближе к краю экрана, чем на столько, тает, единиц.</summary>
        public const float EdgeFade = 200f;
        /// <summary>Узел, чья середина дальше края экрана на столько, не рисуется, единиц.</summary>
        public const float EdgeCut = 70f;
        /// <summary>Доля длины дороги, на которую ручки кривой уходят по горизонтали: S-образный мазок.</summary>
        public const float Bend = .46f;

        // ---------------------------------------------------------------- расписание, с
        // Часы карты τ идут с начала показа (накат дыма ≈ .55), часы мазка p — с PaintAt, но только когда
        // выбранный путь известен (сим уже перешёл на новую арену): без него кисти некуда идти.

        /// <summary>Пройденная дорога и узлы проявляются лесенкой слева направо.</summary>
        public const float InkIn = .32f, InkStagger = .05f;
        /// <summary>Бледная развилка впереди.</summary>
        public const float ForkAt = .12f, ForkTime = .3f;
        /// <summary>Мазок к выбранной арене: начало по τ и длительность по p.</summary>
        public const float PaintAt = .22f, PaintTime = .5f;
        /// <summary>Узел загорается: от чуть раньше прихода кисти.</summary>
        public const float LightLead = .05f, LightTime = .33f;
        /// <summary>Подпись: название по буквам, затем «впереди» (по p).</summary>
        public const float CaptionAt = .38f, SubAt = .5f, CaptionTime = .3f;
        /// <summary>Карта дочитана, завесу можно раскрывать (по p).</summary>
        public const float ReadUntil = 1.23f;
        /// <summary>Невыбранные пути гаснут до этой доли, пока узел загорается.</summary>
        public const float ForkRest = .15f;

        /// <summary>Мазок дошёл до узла.</summary>
        public static float Arrival => PaintTime;
        /// <summary>Карта полностью показана и прочитана: по τ, если данные были с самого начала.</summary>
        public static float DoneAt => PaintAt + ReadUntil;

        // ---------------------------------------------------------------- места

        /// <summary>Высота узла глубины <paramref name="depth"/> на волне дороги (0 — лагерь).</summary>
        public static float WaveY(int depth) => RoadY + Wave * (float)Math.Sin(depth * 2.2 + .7);

        /// <summary>x узла глубины <paramref name="depth"/>, когда следующая арена — <paramref name="next"/>.</summary>
        public static float X(int depth, int next) => NextX + (depth - next) * Spacing;

        /// <summary>
        /// Узел пути <paramref name="index"/> развилки из <paramref name="count"/> к арене <paramref name="next"/>:
        /// пути столбиком на x следующей арены, первый — сверху (как карточки выбора слева направо).
        /// Один путь — прямо на волне дороги.
        /// </summary>
        public static void Branch(int next, int index, int count, out float x, out float y)
        {
            if (count < 1) count = 1;
            if (index < 0) index = 0;
            else if (index >= count) index = count - 1;
            x = NextX;
            y = WaveY(next) + ((count - 1) * .5f - index) * ForkSpread;
        }

        /// <summary>
        /// Первая видимая глубина пройденной дороги: не больше <see cref="MaxPast"/> узлов и ни одного,
        /// чья середина дальше <see cref="EdgeCut"/> за левым краем экрана полушириной <paramref name="halfWidth"/>.
        /// </summary>
        public static int FirstShown(int from, int next, float halfWidth)
        {
            if (from < 0) return 0;
            int first = Math.Max(0, from - MaxPast + 1);
            while (first < from && X(first, next) < -halfWidth - EdgeCut) first++;
            return first;
        }

        /// <summary>Прозрачность узла у левого края: 1 — внутри, 0 — на краю и за ним.</summary>
        public static float EdgeAlpha(float x, float halfWidth)
        {
            float k = Clamp01((x + halfWidth - 10f) / EdgeFade);
            return k * k * (3f - 2f * k);
        }

        // ---------------------------------------------------------------- мазок

        /// <summary>
        /// Точки кривой мазка от (ax, ay) к (bx, by) и накопленная длина: S-кривая Безье, ручки уходят по
        /// горизонтали на <see cref="Bend"/> от разницы x — мазок выходит из узла и входит в следующий
        /// ровно, а не углом. Массивы одной длины (сегменты + 1). Возврат — вся длина.
        /// </summary>
        public static float Sample(float ax, float ay, float bx, float by, float[] xs, float[] ys, float[] length)
        {
            int n = xs.Length;
            float handle = Math.Abs(bx - ax) * Bend;
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? (float)i / (n - 1) : 0f;
                float u = 1f - t;
                float b0 = u * u * u, b1 = 3f * u * u * t, b2 = 3f * u * t * t, b3 = t * t * t;
                xs[i] = b0 * ax + b1 * (ax + handle) + b2 * (bx - handle) + b3 * bx;
                ys[i] = b0 * ay + b1 * ay + b2 * by + b3 * by;
                if (i > 0)
                {
                    float dx = xs[i] - xs[i - 1], dy = ys[i] - ys[i - 1];
                    total += (float)Math.Sqrt(dx * dx + dy * dy);
                }
                length[i] = total;
            }
            return total;
        }

        /// <summary>
        /// Нажим кисти вдоль мазка, доля полной толщины: кисть касается тонко, к середине полный нажим,
        /// к концу чуть отпускает; лёгкая дрожь руки по зерну.
        /// </summary>
        public static float Pressure(float s, float seed)
        {
            float land = Smooth(Clamp01(s / .12f));
            float lift = Smooth(Clamp01((s - .82f) / .18f));
            float wobble = 1f + .07f * (float)Math.Sin(s * 19f + seed * 5f);
            return (.55f + .45f * land) * (1f - .25f * lift) * wobble;
        }

        /// <summary>
        /// Ход кисти по времени, 0..1: кисть ложится медленно, разгоняется и мягко входит в узел
        /// (смесь прямой и плавной — без «замирания» в середине).
        /// </summary>
        public static float Brush(float t)
        {
            t = Clamp01(t);
            return .3f * t + .7f * Smooth(t);
        }

        // ---------------------------------------------------------------- подписи

        static readonly string[] Titles = new string[64];

        /// <summary>«Арена N» — строки кэшируются: карта не мусорит на каждом переходе.</summary>
        public static string Title(int depth)
        {
            if (depth < 0) depth = 0;
            if (depth >= Titles.Length) return "Арена " + depth;
            return Titles[depth] ?? (Titles[depth] = "Арена " + depth);
        }

        /// <summary>Подпись пройденного узла: лагерь, «Заново» или номер арены.</summary>
        public static string StopLabel(int depth, SmokeRouteSign start) =>
            depth > 0 ? Title(depth) : start == SmokeRouteSign.Repeat ? "Заново" : "Лагерь";

        /// <summary>
        /// Одна строка под названием следующей арены: самое важное из того, что известно. Хранитель,
        /// потом элита, потом опасный путь (игрок выбрал его ради золота), новый враг, засада или
        /// выживание, иначе — награда пути. Пусто — забег не найден.
        /// </summary>
        public static string Ahead(in SmokeRouteAhead a)
        {
            if (!a.Known) return string.Empty;
            if (a.Boss) return "Впереди — " + (string.IsNullOrEmpty(a.BossName) ? "Хранитель" : a.BossName);
            if (a.Elite) return string.IsNullOrEmpty(a.EliteName) ? "Впереди — элита" : "Впереди — элита: " + a.EliteName;
            if (a.Hard) return a.BonusGold > 0 ? "Опасная арена · +" + a.BonusGold + " золота за зачистку" : "Опасная арена";
            if (!string.IsNullOrEmpty(a.Lesson)) return "Впереди — новый враг: " + a.Lesson;
            if (a.Ambush) return "Впереди — засада";
            if (a.Survival) return "Впереди — выстоять до конца";
            return a.Shop ? "Награда — магазин · скоро" : "Награда — способность или талант";
        }

        /// <summary>Знак выбранного пути: Хранитель важнее награды, опасная — важнее вида награды.</summary>
        public static SmokeRouteSign Sign(bool boss, bool hard, bool shop) =>
            boss ? SmokeRouteSign.Boss : hard ? SmokeRouteSign.Hard : shop ? SmokeRouteSign.Shop : SmokeRouteSign.Upgrade;

        // ---------------------------------------------------------------- общее

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        public static float Smooth(float k)
        {
            k = Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        /// <summary>Доля отрезка [start; start + length], сглаженная.</summary>
        public static float Phase(float t, float start, float length) => Smooth((t - start) / Math.Max(length, .0001f));
    }
}
