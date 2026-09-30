using System;
using System.Globalization;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Прямоугольник в пикселях экрана, y вверх (как Screen и GetWorldCorners у холста поверх экрана).
    /// Свой, а не UnityEngine.Rect: раскладку меток проверяют тесты вне Unity.
    /// </summary>
    public struct EdgeRect
    {
        public float XMin, YMin, XMax, YMax;

        public EdgeRect(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax;
        }

        public float Width => XMax - XMin;
        public float Height => YMax - YMin;
        public bool IsEmpty => !(XMax > XMin) || !(YMax > YMin);
        public bool Contains(float x, float y) => x >= XMin && x <= XMax && y >= YMin && y <= YMax;

        /// <summary>Уже на <paramref name="by"/> с каждой стороны; минус — шире.</summary>
        public EdgeRect Inset(float by) => new EdgeRect(XMin + by, YMin + by, XMax - by, YMax - by);
    }

    /// <summary>Одна цель за краем экрана: место на обходе края, важность и слитые в неё соседи.</summary>
    public struct EdgeMarkCandidate
    {
        /// <summary>Сущность (1…), выход (<see cref="WorldEdgeMarksLayout.ExitKeyBase"/> + n) или тайник.</summary>
        public int Key;
        /// <summary>Выше — важнее: угроза, элита, стрелок, выход, тайник.</summary>
        public int Priority;
        /// <summary>Место на обходе края вставки, px (<see cref="WorldEdgeMarksLayout.ToPerimeter"/>).</summary>
        public float S;
        /// <summary>До героя, м: при равной важности ближняя цель впереди.</summary>
        public float Distance;
        /// <summary>Сколько целей в метке (1 — одна; больше — слитые соседи, «×N» в углу).</summary>
        public int Count;
        /// <summary>В метке есть атака за кадром: метка красная и пульсирует.</summary>
        public bool Threat;
        /// <summary>Куда смотрит шеврон: направление на цель на экране, градусы от +x против часовой.</summary>
        public float Angle;
        /// <summary>Что за цель для знака и подписи: номер EnemyKind у врага, свои числа у выхода и тайника.</summary>
        public int Tag;
    }

    /// <summary>
    /// Раскладка тлеющих меток у края экрана без UnityEngine (владелец 30.09, выбор 2a на доске
    /// concepts-2026-09-30-hud-polish): что за кадром, где на краю встаёт метка, как она обходит панели
    /// HUD, как сливаются соседи, сколько меток сразу и как гаснет подпись «Вендиго · 14 м».
    /// Рисует по этим числам WorldEdgeMarks; проверяют их tools/Combat.Presentation.Tests/WorldEdgeMarksLayoutTests.cs.
    ///
    /// Край — прямоугольник вставки (экран минус поле): метка стоит на нём центром. Место на краю —
    /// одно число s, обход против часовой стрелки: низ слева направо, правый край снизу вверх, верх справа
    /// налево, левый край сверху вниз. Так скольжение вдоль края и слияние соседей — одномерные, а угол
    /// экрана метка проходит без скачка.
    /// </summary>
    public static class WorldEdgeMarksLayout
    {
        public const int ThreatPriority = 40, ElitePriority = 30, RangedPriority = 20, ExitPriority = 12, CachePriority = 10;
        public const int ExitKeyBase = 100000, CacheKeyBase = 200000;

        /// <summary>Подпись: проявление, держится, гаснет до одного знака, с.</summary>
        public const float LabelIn = .25f, LabelHold = 2.4f, LabelOut = .7f;

        public const int SideBottom = 0, SideRight = 1, SideTop = 2, SideLeft = 3;

        // ---------------------------------------------------------------- за кадром ли цель

        /// <summary>
        /// Цель за кадром. <paramref name="inFront"/> false — точка за камерой: за кадром всегда.
        /// Ближе к краю экрана, чем <paramref name="margin"/>, цель тоже считается за кадром (видна
        /// только половина тела). Показанная метка гаснет, только когда цель вошла в кадр ещё на
        /// <paramref name="hysteresis"/> глубже: у самой кромки метка не мигает.
        /// </summary>
        public static bool OffScreen(EdgeRect screen, float x, float y, bool inFront, bool wasOff, float margin, float hysteresis)
        {
            if (!inFront || float.IsNaN(x) || float.IsNaN(y)) return true;
            float inset = wasOff ? margin + Math.Max(0f, hysteresis) : margin;
            return !screen.Inset(inset).Contains(x, y);
        }

        // ---------------------------------------------------------------- проекция на край

        /// <summary>
        /// Где луч из (<paramref name="ox"/>, <paramref name="oy"/>) — обычно герой на экране — в сторону
        /// цели выходит на край вставки. Начало луча зажимается внутрь вставки; нулевое направление — вниз.
        /// </summary>
        public static void EdgePoint(EdgeRect inset, float ox, float oy, float dx, float dy, out float ex, out float ey)
        {
            ox = Clamp(ox, inset.XMin, inset.XMax);
            oy = Clamp(oy, inset.YMin, inset.YMax);
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (!(length > 1e-6f)) { dx = 0f; dy = -1f; }
            else { dx /= length; dy /= length; }
            float t = float.MaxValue;
            if (dx > 1e-6f) t = Math.Min(t, (inset.XMax - ox) / dx);
            else if (dx < -1e-6f) t = Math.Min(t, (inset.XMin - ox) / dx);
            if (dy > 1e-6f) t = Math.Min(t, (inset.YMax - oy) / dy);
            else if (dy < -1e-6f) t = Math.Min(t, (inset.YMin - oy) / dy);
            if (t == float.MaxValue) t = 0f;
            ex = Clamp(ox + dx * t, inset.XMin, inset.XMax);
            ey = Clamp(oy + dy * t, inset.YMin, inset.YMax);
        }

        public static float Perimeter(EdgeRect r) => 2f * (Math.Max(0f, r.Width) + Math.Max(0f, r.Height));

        /// <summary>Место на обходе края для точки: берётся ближайшая сторона, точка зажимается на неё.</summary>
        public static float ToPerimeter(EdgeRect r, float x, float y)
        {
            float w = Math.Max(0f, r.Width), h = Math.Max(0f, r.Height);
            x = Clamp(x, r.XMin, r.XMax);
            y = Clamp(y, r.YMin, r.YMax);
            float bottom = y - r.YMin, top = r.YMax - y, left = x - r.XMin, right = r.XMax - x;
            float best = Math.Min(Math.Min(bottom, top), Math.Min(left, right));
            if (best == bottom) return x - r.XMin;
            if (best == right) return w + (y - r.YMin);
            if (best == top) return w + h + (r.XMax - x);
            return Wrap(2f * w + h + (r.YMax - y), 2f * (w + h));
        }

        /// <summary>Точка края по месту на обходе и сторона (<see cref="SideBottom"/>…<see cref="SideLeft"/>).</summary>
        public static void FromPerimeter(EdgeRect r, float s, out float x, out float y, out int side)
        {
            float w = Math.Max(0f, r.Width), h = Math.Max(0f, r.Height);
            float p = 2f * (w + h);
            if (!(p > 0f)) { x = r.XMin; y = r.YMin; side = SideBottom; return; }
            s = Wrap(s, p);
            if (s < w) { x = r.XMin + s; y = r.YMin; side = SideBottom; return; }
            s -= w;
            if (s < h) { x = r.XMax; y = r.YMin + s; side = SideRight; return; }
            s -= h;
            if (s < w) { x = r.XMax - s; y = r.YMax; side = SideTop; return; }
            s -= w;
            x = r.XMin; y = r.YMax - Math.Min(s, h); side = SideLeft;
        }

        /// <summary>Место на обходе в [0; perimeter).</summary>
        public static float Wrap(float s, float perimeter)
        {
            if (!(perimeter > 0f)) return 0f;
            s %= perimeter;
            if (s < 0f) s += perimeter;
            return s >= perimeter ? 0f : s;
        }

        /// <summary>Кратчайший сдвиг по обходу от <paramref name="from"/> к <paramref name="to"/> (со знаком).</summary>
        public static float WrapDelta(float from, float to, float perimeter)
        {
            if (!(perimeter > 0f)) return 0f;
            float d = Wrap(to - from, perimeter);
            return d > perimeter * .5f ? d - perimeter : d;
        }

        /// <summary>
        /// Скольжение метки вдоль края к новому месту: экспонента со скоростью <paramref name="rate"/>
        /// (1/с). Сдвиг больше <paramref name="snap"/> (цель ушла за спину, на другой край) — сразу:
        /// метка, едущая через полэкрана по кромке, читалась бы как вторая цель.
        /// </summary>
        public static float Glide(float current, float target, float perimeter, float dt, float rate, float snap)
        {
            float delta = WrapDelta(current, target, perimeter);
            if (Math.Abs(delta) > snap || !(dt > 0f) || !(rate > 0f)) return Wrap(Math.Abs(delta) > snap ? target : current, perimeter);
            float k = 1f - (float)Math.Exp(-rate * dt);
            return Wrap(current + delta * k, perimeter);
        }

        // ---------------------------------------------------------------- панели HUD

        /// <summary>
        /// Какие куски края закрыты панелями HUD: метка радиуса <paramref name="radius"/> в этом месте
        /// задела бы панель. Результат — отрезки обхода [from; to], по возрастанию, без пересечений;
        /// отрезок через начало обхода записан как [from; perimeter + to]. Возвращает число отрезков.
        /// Массивы <paramref name="from"/> и <paramref name="to"/> — рабочие, на 4 места на панель.
        /// </summary>
        public static int BlockedIntervals(EdgeRect inset, EdgeRect[] blocked, int blockedCount, float radius, float[] from, float[] to)
        {
            float w = Math.Max(0f, inset.Width), h = Math.Max(0f, inset.Height), p = 2f * (w + h);
            int capacity = Math.Min(from.Length, to.Length), n = 0;
            if (!(p > 0f)) return 0;
            for (int i = 0; i < blockedCount && i < blocked.Length; i++)
            {
                EdgeRect b = blocked[i];
                if (b.IsEmpty) continue;
                float a0, a1;
                // Низ: y = YMin, s = x − XMin.
                if (Across(b.YMin, b.YMax, inset.YMin, radius, b.XMin, b.XMax, inset.XMin, inset.XMax, out a0, out a1))
                    Add(ref n, capacity, from, to, a0 - inset.XMin, a1 - inset.XMin);
                // Правый край: x = XMax, s = w + (y − YMin).
                if (Across(b.XMin, b.XMax, inset.XMax, radius, b.YMin, b.YMax, inset.YMin, inset.YMax, out a0, out a1))
                    Add(ref n, capacity, from, to, w + a0 - inset.YMin, w + a1 - inset.YMin);
                // Верх: y = YMax, s = w + h + (XMax − x) — идёт справа налево.
                if (Across(b.YMin, b.YMax, inset.YMax, radius, b.XMin, b.XMax, inset.XMin, inset.XMax, out a0, out a1))
                    Add(ref n, capacity, from, to, w + h + inset.XMax - a1, w + h + inset.XMax - a0);
                // Левый край: x = XMin, s = 2w + h + (YMax − y) — сверху вниз.
                if (Across(b.XMin, b.XMax, inset.XMin, radius, b.YMin, b.YMax, inset.YMin, inset.YMax, out a0, out a1))
                    Add(ref n, capacity, from, to, 2f * w + h + inset.YMax - a1, 2f * w + h + inset.YMax - a0);
            }
            // По возрастанию начала (вставками: отрезков единицы) и слияние перекрытых и касающихся.
            for (int i = 1; i < n; i++)
            {
                float f = from[i], t = to[i];
                int j = i - 1;
                while (j >= 0 && from[j] > f) { from[j + 1] = from[j]; to[j + 1] = to[j]; j--; }
                from[j + 1] = f; to[j + 1] = t;
            }
            int merged = 0;
            for (int i = 0; i < n; i++)
            {
                if (merged > 0 && from[i] <= to[merged - 1] + .5f) { to[merged - 1] = Math.Max(to[merged - 1], to[i]); continue; }
                from[merged] = from[i]; to[merged] = to[i]; merged++;
            }
            // Закрытый угол у начала обхода (левый нижний): последний отрезок доходит до конца, первый
            // начинается с нуля — это один отрезок через начало.
            if (merged > 1 && to[merged - 1] >= p - .5f && from[0] <= .5f)
            {
                to[merged - 1] = p + to[0];
                for (int i = 1; i < merged; i++) { from[i - 1] = from[i]; to[i - 1] = to[i]; }
                merged--;
            }
            return merged;
        }

        /// <summary>
        /// Место вне закрытых отрезков: внутри отрезка — к ближнему его концу, с запасом полпикселя.
        /// Край закрыт целиком — место не меняется.
        /// </summary>
        public static float PushOut(float s, float perimeter, float[] from, float[] to, int count)
        {
            if (!(perimeter > 0f)) return s;
            s = Wrap(s, perimeter);
            for (int i = 0; i < count; i++)
            {
                float f = from[i], t = to[i];
                if (t - f >= perimeter - 1f) return s;
                float x = s;
                if (x < f && x + perimeter <= t) x += perimeter;
                if (x < f || x > t) continue;
                return Wrap(x - f <= t - x ? f - .5f : t + .5f, perimeter);
            }
            return s;
        }

        // ---------------------------------------------------------------- слияние и предел

        /// <summary>
        /// Слияние соседей и предел числа меток. Цели идут по важности (выше приоритет, потом ближе,
        /// потом меньший ключ — порядок не зависит от порядка сущностей в кадре); каждая либо встаёт
        /// своей меткой, либо вливается в уже поставленную ближе <paramref name="distance"/> по краю
        /// (та остаётся на месте, растёт «×N», угроза делает её красной). Сверх <paramref name="max"/>
        /// одиночные цели не показываются. <paramref name="marks"/> сортируется на месте.
        /// Возвращает число меток в <paramref name="output"/>.
        /// </summary>
        public static int Merge(EdgeMarkCandidate[] marks, int count, float perimeter, float distance, int max, EdgeMarkCandidate[] output)
        {
            count = Math.Min(count, marks.Length);
            for (int i = 1; i < count; i++)
            {
                EdgeMarkCandidate m = marks[i];
                int j = i - 1;
                while (j >= 0 && Before(m, marks[j])) { marks[j + 1] = marks[j]; j--; }
                marks[j + 1] = m;
            }
            int n = 0;
            max = Math.Min(max, output.Length);
            for (int i = 0; i < count; i++)
            {
                EdgeMarkCandidate m = marks[i];
                if (m.Count < 1) m.Count = 1;
                int into = -1;
                float nearest = distance;
                for (int j = 0; j < n; j++)
                {
                    float gap = Math.Abs(WrapDelta(output[j].S, m.S, perimeter));
                    if (gap < nearest) { nearest = gap; into = j; }
                }
                if (into >= 0)
                {
                    output[into].Count += m.Count;
                    output[into].Threat |= m.Threat;
                    continue;
                }
                if (n < max) output[n++] = m;
            }
            return n;
        }

        static bool Before(EdgeMarkCandidate a, EdgeMarkCandidate b)
        {
            if (a.Priority != b.Priority) return a.Priority > b.Priority;
            if (a.Distance != b.Distance) return a.Distance < b.Distance;
            return a.Key < b.Key;
        }

        // ---------------------------------------------------------------- что метить

        /// <summary>Важность врага для метки; 0 — не метить (обычный ближний враг без атаки за кадром).</summary>
        public static int EnemyPriority(bool threat, bool elite, bool ranged)
            => threat ? ThreatPriority : elite ? ElitePriority : ranged ? RangedPriority : 0;

        /// <summary>Стрелки леса: бьют издалека, их метят и без элиты.</summary>
        public static bool IsRanged(EnemyKind kind) => kind == EnemyKind.ForestThorncaster || kind == EnemyKind.ForestBud;

        /// <summary>Короткое имя для подписи метки (полные — EnemyTexts, для табличек и полосы босса).</summary>
        public static string ShortName(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.ForestGuardian: return "Хранитель";
                case EnemyKind.ForestRootSwarm: return "Корнеполз";
                case EnemyKind.ForestBud: return "Плюй-плод";
                case EnemyKind.ForestWendigo: return "Вендиго";
                case EnemyKind.ForestStonehoof: return "Камнекопыт";
                case EnemyKind.ForestThorncaster: return "Шипомёт";
                case EnemyKind.ForestRootSnarer: return "Корнехват";
                case EnemyKind.ForestSplitter: return "Расщепень";
                case EnemyKind.ForestSplitling: return "Детёныш";
                default: return "Враг";
            }
        }

        // ---------------------------------------------------------------- подпись

        /// <summary>
        /// Прозрачность подписи при первом появлении метки: проявляется <see cref="LabelIn"/>, держится
        /// <see cref="LabelHold"/>, мягко гаснет за <see cref="LabelOut"/> — дальше у края только знак.
        /// </summary>
        public static float LabelAlpha(float age)
        {
            if (!(age > 0f)) return 0f;
            if (age < LabelIn) return age / LabelIn;
            float fade = age - LabelIn - LabelHold;
            if (fade <= 0f) return 1f;
            if (fade >= LabelOut) return 0f;
            float k = fade / LabelOut;
            return 1f - k * k * (3f - 2f * k);
        }

        /// <summary>Сколько метров писать: округление, не меньше 1.</summary>
        public static int Meters(float distance) => float.IsNaN(distance) ? 1 : Math.Max(1, (int)Math.Round(distance, MidpointRounding.AwayFromZero));

        /// <summary>«Вендиго · 14 м».</summary>
        public static string Label(string name, int meters) => name + " · " + meters.ToString(CultureInfo.InvariantCulture) + " м";

        /// <summary>«×3» в углу слитой метки; одна цель — пусто.</summary>
        public static string CountText(int count) => count > 1 ? "×" + count.ToString(CultureInfo.InvariantCulture) : string.Empty;

        // ---------------------------------------------------------------- служебное

        /// <summary>
        /// Сторона края на линии <paramref name="line"/> (поперёк — ось панели от <paramref name="bMin"/>
        /// до <paramref name="bMax"/>) задета кругом радиуса <paramref name="radius"/> на отрезке вдоль
        /// [<paramref name="a0"/>; <paramref name="a1"/>], зажатом в [<paramref name="lo"/>; <paramref name="hi"/>].
        /// </summary>
        static bool Across(float bMin, float bMax, float line, float radius, float along0, float along1, float lo, float hi,
            out float a0, out float a1)
        {
            a0 = a1 = 0f;
            float off = Math.Max(0f, Math.Max(bMin - line, line - bMax));
            if (off >= radius) return false;
            float reach = (float)Math.Sqrt(radius * radius - off * off);
            a0 = Math.Max(lo, along0 - reach);
            a1 = Math.Min(hi, along1 + reach);
            return a1 > a0;
        }

        static void Add(ref int n, int capacity, float[] from, float[] to, float f, float t)
        {
            if (n >= capacity) return;
            from[n] = f; to[n] = t; n++;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
