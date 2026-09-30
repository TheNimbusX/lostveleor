using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Эффект героя в строке над портретом (этап 4, выбор владельца 30.09 — кадр 1a).
    /// Порядок перечисления — порядок в строке и он не меняется: сначала контроль (корни,
    /// оглушение, защита от него), замедление, затем зелья, артефакт, Blaze. Новый эффект
    /// встаёт на своё место, а не в конец, — строка не перетасовывается.
    /// </summary>
    public enum HudEffectKind : byte
    {
        Root, Stun, ControlImmune, Slow, Resin, Surge, Clear, Artifact, Blaze,
    }

    /// <summary>Как читается эффект: угроза (приглушённый красный), защита (холодный), помощь (кремово-золотой).</summary>
    public enum HudEffectTone : byte { Threat, Guard, Boon }

    /// <summary>
    /// Список эффектов строки над портретом — без Unity, чтобы порядок, секунды, стаки и
    /// переполнение проверялись тестами (tools/Combat.Presentation.Tests, HudEffectListTests).
    ///
    /// Каждый кадр: <see cref="Begin"/>, <see cref="Report"/> по каждому эффекту, что есть,
    /// <see cref="Commit"/>. После Commit известно, какие эффекты видны и в каком порядке,
    /// сколько ушло в «+N», какие только что появились, обновились (выпит заново) и кончились.
    ///
    /// Полная длина кольца: если её знает источник (зелье — 6 с, артефакт — его длительность),
    /// то она; иначе — остаток в миг появления или обновления (корни, оглушение и замедление
    /// сим отдаёт только остатком). Без выделения памяти в кадре.
    /// </summary>
    public sealed class HudEffectList
    {
        public const int KindCount = 9;

        readonly int[] _input = new int[KindCount], _inputFull = new int[KindCount], _inputStacks = new int[KindCount];
        readonly int[] _left = new int[KindCount], _full = new int[KindCount], _stacks = new int[KindCount];
        readonly bool[] _active = new bool[KindCount], _shown = new bool[KindCount];
        readonly bool[] _appeared = new bool[KindCount], _refreshed = new bool[KindCount], _ended = new bool[KindCount];
        readonly HudEffectKind[] _order = new HudEffectKind[KindCount];

        public HudEffectList(int ticksPerSecond = Simulation.TicksPerSecond, int maxVisible = 7)
        {
            TicksPerSecond = ticksPerSecond > 0 ? ticksPerSecond : 1;
            MaxVisible = maxVisible;
        }

        public int TicksPerSecond { get; }

        /// <summary>
        /// Сколько кругов помещается в строке, считая круг «+N». Эффектов больше — последние
        /// по порядку (помощь, а не угрозы) уходят в «+N». Меньше 1 — без ограничения.
        /// </summary>
        public int MaxVisible { get; set; }

        /// <summary>Сколько эффектов видно в строке (без круга «+N»).</summary>
        public int Count { get; private set; }

        /// <summary>Сколько действующих эффектов не поместилось и ушло в круг «+N».</summary>
        public int Overflow { get; private set; }

        /// <summary>Видимый эффект по порядку в строке, 0 … Count − 1.</summary>
        public HudEffectKind this[int index] => _order[index];

        public void Begin()
        {
            for (int i = 0; i < KindCount; i++)
            {
                _input[i] = 0;
                _inputFull[i] = 0;
                _inputStacks[i] = 1;
            }
        }

        /// <summary>
        /// Эффект действует ещё <paramref name="ticksLeft"/> тиков (0 и меньше — не действует).
        /// <paramref name="fullTicks"/> — полная длина, если её знает источник (0 — не знает);
        /// <paramref name="stacks"/> — сколько раз наложен (1 — без стаков).
        /// </summary>
        public void Report(HudEffectKind kind, int ticksLeft, int fullTicks = 0, int stacks = 1)
        {
            int i = (int)kind;
            if ((uint)i >= KindCount) return;
            _input[i] = ticksLeft > 0 ? ticksLeft : 0;
            _inputFull[i] = fullTicks > 0 ? fullTicks : 0;
            _inputStacks[i] = stacks > 1 ? stacks : 1;
        }

        public void Commit()
        {
            int active = 0;
            for (int i = 0; i < KindCount; i++)
            {
                int left = _input[i];
                bool was = _active[i];
                _appeared[i] = _refreshed[i] = _ended[i] = false;
                if (left <= 0)
                {
                    _ended[i] = was;
                    _active[i] = false;
                    _left[i] = 0;
                    _stacks[i] = 1;
                    continue;
                }
                int known = _inputFull[i];
                if (!was)
                {
                    _appeared[i] = true;
                    _full[i] = known > 0 ? System.Math.Max(known, left) : left;
                }
                else if (left > _left[i] || _inputStacks[i] > _stacks[i])
                {
                    // Выпит заново, продлён или наложен ещё раз: кольцо снова полное.
                    _refreshed[i] = true;
                    _full[i] = known > 0 ? System.Math.Max(known, left) : left;
                }
                else _full[i] = System.Math.Max(_full[i], known > 0 ? System.Math.Max(known, left) : left);
                _active[i] = true;
                _left[i] = left;
                _stacks[i] = _inputStacks[i];
                active++;
            }

            int room = MaxVisible >= 1 && active > MaxVisible ? MaxVisible - 1 : active;
            Count = 0;
            for (int i = 0; i < KindCount; i++)
            {
                _shown[i] = _active[i] && Count < room;
                if (_shown[i]) _order[Count++] = (HudEffectKind)i;
            }
            Overflow = active - Count;
        }

        /// <summary>Всё снять без вспышек (смена забега, выход в лагерь).</summary>
        public void Clear()
        {
            for (int i = 0; i < KindCount; i++)
            {
                _input[i] = _left[i] = _full[i] = 0;
                _stacks[i] = _inputStacks[i] = 1;
                _active[i] = _shown[i] = _appeared[i] = _refreshed[i] = _ended[i] = false;
            }
            Count = Overflow = 0;
        }

        public bool Active(HudEffectKind kind) => _active[(int)kind];
        /// <summary>Эффект виден кругом в строке (действует и не ушёл в «+N»).</summary>
        public bool Shown(HudEffectKind kind) => _shown[(int)kind];
        public bool Appeared(HudEffectKind kind) => _appeared[(int)kind];
        public bool Refreshed(HudEffectKind kind) => _refreshed[(int)kind];
        /// <summary>Эффект кончился в этот Commit — круг вспыхивает один раз и гаснет.</summary>
        public bool Ended(HudEffectKind kind) => _ended[(int)kind];
        public int TicksLeft(HudEffectKind kind) => _left[(int)kind];
        public int FullTicks(HudEffectKind kind) => _full[(int)kind];
        public int Stacks(HudEffectKind kind) => _stacks[(int)kind];
        public int Seconds(HudEffectKind kind) => SecondsOf(_left[(int)kind], TicksPerSecond);

        /// <summary>Доля кольца-таймера: 1 — только что наложен, 0 — кончается.</summary>
        public float Fill(HudEffectKind kind)
        {
            int full = _full[(int)kind];
            if (full <= 0) return 0f;
            float fill = _left[(int)kind] / (float)full;
            return fill < 0f ? 0f : fill > 1f ? 1f : fill;
        }

        /// <summary>
        /// Секунды в углу значка: вверх до целой (1 тик — «1», ровно 30 — «1», 31 — «2»):
        /// «0» при живом эффекте не показывается никогда.
        /// </summary>
        public static int SecondsOf(int ticksLeft, int ticksPerSecond)
        {
            if (ticksLeft <= 0) return 0;
            int tps = ticksPerSecond > 0 ? ticksPerSecond : 1;
            return (ticksLeft + tps - 1) / tps;
        }

        public static HudEffectTone ToneOf(HudEffectKind kind)
        {
            switch (kind)
            {
                case HudEffectKind.Root:
                case HudEffectKind.Stun:
                case HudEffectKind.Slow:
                    return HudEffectTone.Threat;
                case HudEffectKind.ControlImmune:
                case HudEffectKind.Clear:
                    return HudEffectTone.Guard;
                default:
                    return HudEffectTone.Boon;
            }
        }

        const int CachedNumbers = 100;
        static readonly string[] Numbers = BuildNumbers(), StackLabels = BuildStacks();

        static string[] BuildNumbers()
        {
            var numbers = new string[CachedNumbers];
            for (int i = 0; i < numbers.Length; i++) numbers[i] = i.ToString();
            return numbers;
        }

        static string[] BuildStacks()
        {
            var labels = new string[CachedNumbers];
            for (int i = 0; i < labels.Length; i++) labels[i] = i >= 2 ? "×" + i : string.Empty;
            return labels;
        }

        /// <summary>Число секунд в углу: готовые строки 0–99, дольше — «99+». Без выделения памяти.</summary>
        public static string SecondsLabel(int seconds)
            => seconds <= 0 ? string.Empty : seconds < CachedNumbers ? Numbers[seconds] : "99+";

        /// <summary>Стаки в углу значка: «×2», «×3»…; один стак — пусто.</summary>
        public static string StacksLabel(int stacks)
            => stacks < 2 ? string.Empty : stacks < CachedNumbers ? StackLabels[stacks] : "×99+";

        /// <summary>Круг переполнения: «+2».</summary>
        public static string OverflowLabel(int overflow)
            => overflow <= 0 ? string.Empty : overflow < CachedNumbers ? "+" + Numbers[overflow] : "+99";
    }

    /// <summary>
    /// Движение строки эффектов, без Unity (проверяется тестами). Место круга — сумма «весов»
    /// кругов слева: новый круг раздвигает соседей, пока его вес растёт 0 → 1, ушедший
    /// сдвигает их обратно, пока вес падает до 0. Порядок кругов постоянный, поэтому строка
    /// перетекает плавно и без отдельной анимации мест.
    /// </summary>
    public static class HudEffectRowMath
    {
        /// <summary>Шаг веса к 1 (круг нужен) или к 0 за <paramref name="grow"/> / <paramref name="shrink"/> секунд.</summary>
        public static float StepWeight(float weight, bool wanted, float dt, float grow, float shrink)
        {
            if (dt <= 0f) return Clamp01(weight);
            float time = wanted ? grow : shrink;
            float step = time > 0f ? dt / time : 1f;
            return Clamp01(wanted ? weight + step : weight - step);
        }

        /// <summary>
        /// Места кругов слева направо: x[i] = шаг · (сумма весов до i). Возвращает ширину строки
        /// (шаг · сумма всех весов). Веса сглаживаются «мягким выходом», чтобы соседи не ехали рывком.
        /// </summary>
        public static float Place(float[] weights, int count, float pitch, float[] xs)
        {
            float x = 0f;
            for (int i = 0; i < count; i++)
            {
                xs[i] = x;
                x += pitch * Smooth(weights[i]);
            }
            return x;
        }

        /// <summary>
        /// Масштаб появления: с 0,55 рывком к 1,12 и мягко к 1 (за t = 0 … 1). Кольцо «выпрыгивает»
        /// из дыма, а не вырастает линейно.
        /// </summary>
        public static float PopScale(float t)
        {
            t = Clamp01(t);
            const float peakAt = .45f, start = .55f, peak = 1.12f;
            if (t < peakAt)
            {
                float k = t / peakAt;
                k = 1f - (1f - k) * (1f - k);
                return start + (peak - start) * k;
            }
            float r = (t - peakAt) / (1f - peakAt);
            r = r * r * (3f - 2f * r);
            return peak + (1f - peak) * r;
        }

        /// <summary>
        /// Уход кончившегося эффекта за t = 0 … 1: первые 30% — вспышка (прозрачность 1), дальше
        /// круг гаснет и сжимается до 0,7. Возвращает прозрачность; масштаб — <see cref="LeaveScale"/>.
        /// </summary>
        public static float LeaveAlpha(float t)
        {
            t = Clamp01(t);
            if (t <= .3f) return 1f;
            float k = (t - .3f) / .7f;
            return 1f - k * k * (3f - 2f * k);
        }

        public static float LeaveScale(float t)
        {
            t = Clamp01(t);
            return t <= .3f ? 1f + .08f * (t / .3f) : 1.08f - .38f * ((t - .3f) / .7f);
        }

        /// <summary>Сила вспышки ухода: пик в начале, к концу первых 30% — ноль.</summary>
        public static float LeaveFlash(float t)
        {
            t = Clamp01(t);
            if (t >= .3f) return 0f;
            float k = t / .3f;
            return 1f - k;
        }

        public static float Smooth(float w)
        {
            w = Clamp01(w);
            return w * w * (3f - 2f * w);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>
    /// Подписи подсказки строки эффектов: имя и одна строка «что делает» (Nunito, кит-подсказка).
    /// Числа — те же, что в симуляции (Simulation.PotionEffects, HeroSlow, Artifacts, SabreKit).
    /// </summary>
    public static class HudEffectTexts
    {
        public static string Name(HudEffectKind kind)
        {
            switch (kind)
            {
                case HudEffectKind.Root: return "Корни";
                case HudEffectKind.Stun: return "Оглушение";
                case HudEffectKind.ControlImmune: return "Защита от контроля";
                case HudEffectKind.Slow: return "Замедление";
                case HudEffectKind.Resin: return "Живица";
                case HudEffectKind.Surge: return "Порыв";
                case HudEffectKind.Clear: return "Ясный настой";
                case HudEffectKind.Artifact: return "Артефакт";
                case HudEffectKind.Blaze: return "Ладно смазал";
                default: return string.Empty;
            }
        }

        /// <summary>Что делает эффект, одной строкой. <paramref name="slowPercent"/> — только для замедления.</summary>
        public static string Line(HudEffectKind kind, int slowPercent = 0)
        {
            switch (kind)
            {
                case HudEffectKind.Root: return "Ни шага, ни рывка — бить на месте можно";
                case HudEffectKind.Stun: return "Ни шага, ни удара, ни приёма";
                case HudEffectKind.ControlImmune: return "Корни и оглушение сейчас не лягут";
                case HudEffectKind.Slow: return slowPercent > 0 ? "Бег медленнее на " + slowPercent + "%" : "Бег медленнее";
                case HudEffectKind.Resin: return "−25% получаемого урона";
                case HudEffectKind.Surge: return "+20% к бегу и приёмам";
                case HudEffectKind.Clear: return "Корни и замедление не лягут";
                case HudEffectKind.Blaze: return "Сабля горит: +20% урона атак, 20% уклонения";
                default: return string.Empty;
            }
        }

        /// <summary>Действие включённого артефакта одной строкой (полное — в подсказке медальона).</summary>
        public static string ArtifactLine(RunArtifact artifact)
        {
            switch (artifact)
            {
                case RunArtifact.SunSeal: return "Полная неуязвимость, бить можно";
                case RunArtifact.VengeanceMirror: return "Урон по Пелагу −25% и уходит обидчику";
                case RunArtifact.WinterHeart: return "Враги рядом во льду — способность раскалывает";
                case RunArtifact.Hourglass: return "Враги стоят, урон по ним копится";
                case RunArtifact.VoidVisage: return "Не бьют, сквозь врагов; атаковать нельзя";
                case RunArtifact.CrimsonHeart: return "Урон и скорость растут с потерянным здоровьем";
                case RunArtifact.GuardianVow: return "Обет спас: неуязвим";
                default: return string.Empty;
            }
        }

        /// <summary>Строка секунд подсказки: «Ещё 3 с».</summary>
        public static string Remaining(int seconds) => seconds > 0 ? "Ещё " + seconds + " с" : "Кончается";
    }
}
