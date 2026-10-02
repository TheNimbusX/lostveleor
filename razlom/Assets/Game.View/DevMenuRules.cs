using System;

namespace Game.View
{
    /// <summary>Вкладка меню разработчика (F8): «Забег · Бой · Пелаг · Визуал».</summary>
    public enum DevTab
    {
        Run = 0,
        Combat = 1,
        Pelag = 2,
        Visual = 3,
    }

    /// <summary>Где стоит секция: левая или правая колонка, либо вся ширина вкладки.</summary>
    public enum DevColumn
    {
        Left = 0,
        Right = 1,
        Full = 2,
    }

    /// <summary>Как пункт меню ведёт себя и какие последствия показывает рядом с собой.</summary>
    [Flags]
    public enum DevFlags
    {
        None = 0,
        /// <summary>Ещё и в быструю строку под шапкой.</summary>
        Quick = 1 << 0,
        /// <summary>После удачного действия меню закрывается: бой начинается сразу.</summary>
        CloseMenu = 1 << 1,
        /// <summary>Красная кнопка.</summary>
        Danger = 1 << 2,
        /// <summary>Всегда второе нажатие в течение трёх секунд.</summary>
        Confirm = 1 << 3,
        /// <summary>Второе нажатие, только если идёт обычный (не тестовый) забег: действие его заменит.</summary>
        ConfirmIfRealRun = 1 << 4,
        /// <summary>Чип «сделает забег тестовым» — в обычном забеге. Пометку ставит само действие.</summary>
        MarksTestRun = 1 << 5,
        /// <summary>Чип «пишет сохранение».</summary>
        WritesSave = 1 << 6,
        /// <summary>Чип «запоминается» (PlayerPrefs).</summary>
        Remembered = 1 << 7,
    }

    /// <summary>Цвет чипа: состояние или последствие.</summary>
    public enum DevChip
    {
        Neutral = 0,
        On = 1,
        Off = 2,
        Warn = 3,
        Danger = 4,
        Accent = 5,
    }

    /// <summary>
    /// Правила меню разработчика (F8) без Unity: масштаб, размер панели, колонки, подтверждения, подписи.
    /// Отрисовка — DeveloperMenu (IMGUI); эти числа и решения проверяет tools/Combat.Presentation.Tests.
    /// Раскладка — аудит F8 02.10: панель 1000×820 при 1080p, две колонки на «Забеге» и «Пелаге»,
    /// одна колонка 640 на «Бое» и «Визуале».
    /// </summary>
    public static class DevMenuRules
    {
        public const int TabCount = 4;
        public const float BaseScreenHeight = 1080f;
        public const float MinScale = .75f, MaxScale = 2f;
        public const float PanelWidth = 1000f, PanelHeight = 820f, ScreenMargin = 16f;
        public const float ColumnGap = 16f, SingleColumnWidth = 640f, TwoColumnMinWidth = 760f;
        /// <summary>Сколько секунд живёт взвод подтверждения (по неигровым часам: timeScale в меню — ноль).</summary>
        public const float ConfirmSeconds = 3f;

        // Что запоминается между запусками (PlayerPrefs). Бессмертие, таланты и набор — нет.
        public const string TabKey = "razlom.dev.tab";
        public const string ArenaKey = "razlom.dev.arena";
        public const string SeedKey = "razlom.dev.seed";
        public const string TempoKey = "razlom.dev.tempo";
        public const string TalentLineKey = "razlom.dev.talent-line";

        public const string SeedError = "Сид должен быть целым неотрицательным числом.";

        /// <summary>
        /// Масштаб меню: высота экрана к 1080 (0,75…2) на настройку «Масштаб UI». Масштабируются размеры
        /// шрифтов и отступов, а не GUI.matrix — матрица растягивает готовые глифы, и на 4K текст мылится.
        /// </summary>
        public static float Scale(float screenHeight, float uiScale)
        {
            float screen = Clamp(screenHeight / BaseScreenHeight, MinScale, MaxScale);
            float user = uiScale > 0f ? uiScale : 1f;
            return screen * user;
        }

        /// <summary>Панель по центру: 1000×820 в масштабе, но с полями 16 px до края экрана.</summary>
        public static void PanelSize(float screenWidth, float screenHeight, float scale, out float width, out float height)
        {
            width = Math.Max(1f, Math.Min(PanelWidth * scale, screenWidth - 2f * ScreenMargin));
            height = Math.Max(1f, Math.Min(PanelHeight * scale, screenHeight - 2f * ScreenMargin));
        }

        /// <summary>Хватает ли ширины на две колонки; уже — колонки складываются в одну.</summary>
        public static bool TwoColumns(float contentWidth, float scale) => contentWidth >= TwoColumnMinWidth * scale;

        /// <summary>Ширина одной из двух колонок.</summary>
        public static float ColumnWidth(float contentWidth, float scale) => Math.Max(1f, (contentWidth - ColumnGap * scale) / 2f);

        /// <summary>Ширина единственной колонки короткой вкладки («Бой», «Визуал»): 640, но не шире тела.</summary>
        public static float SingleWidth(float contentWidth, float scale) => Math.Max(1f, Math.Min(SingleColumnWidth * scale, contentWidth));

        /// <summary>Нужно ли второе нажатие: всегда (Confirm) или только поверх обычного забега (ConfirmIfRealRun).</summary>
        public static bool NeedsConfirm(DevFlags flags, bool realRun)
            => (flags & DevFlags.Confirm) != 0 || ((flags & DevFlags.ConfirmIfRealRun) != 0 && realRun);

        /// <summary>Подпись взведённой кнопки — что случится после второго нажатия.</summary>
        public static string ConfirmLabel(DevFlags flags, bool realRun)
        {
            if ((flags & DevFlags.WritesSave) != 0) return "Ещё раз — уровень запишется в сохранение";
            if ((flags & DevFlags.ConfirmIfRealRun) != 0 && realRun) return "Ещё раз — обычный забег пропадёт";
            return "Ещё раз — подтвердить";
        }

        /// <summary>Взвод ещё действует.</summary>
        public static bool Armed(float armedAt, float now) => armedAt >= 0f && now >= armedAt && now - armedAt < ConfirmSeconds;

        /// <summary>Следующая вкладка по кругу (Tab / Shift+Tab).</summary>
        public static int NextTab(int current, int step) => ((current + step) % TabCount + TabCount) % TabCount;

        /// <summary>Подпись вкладки; у «Пелага» — число включённых отладочных талантов.</summary>
        public static string TabTitle(DevTab tab, int talents)
        {
            switch (tab)
            {
                case DevTab.Run: return "Забег";
                case DevTab.Combat: return "Бой";
                case DevTab.Pelag: return talents > 0 ? "Пелаг · " + talents : "Пелаг";
                default: return "Визуал";
            }
        }

        /// <summary>Кнопка сетки арен: номер, у арены босса — «Б».</summary>
        public static string ArenaCell(int arena, bool boss) => boss ? "Б" : arena.ToString();

        /// <summary>«арена 9 (босс)».</summary>
        public static string ArenaTitle(int arena, bool boss) => "арена " + arena + (boss ? " (босс)" : "");

        /// <summary>Номер арены в пределах локации (1…count).</summary>
        public static int ClampArena(int arena, int count) => Math.Max(1, Math.Min(arena, Math.Max(1, count)));

        /// <summary>Сид тестового забега: целое неотрицательное число.</summary>
        public static bool TryParseSeed(string text, out ulong seed)
            => ulong.TryParse(text == null ? null : text.Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out seed);

        /// <summary>«ВИХРЬ» → «Вихрь», «УДАР ЯКОРЕМ» → «Удар якорем» — для списка линий талантов.</summary>
        public static string SentenceCase(string caps)
        {
            if (string.IsNullOrEmpty(caps)) return caps ?? string.Empty;
            string lower = caps.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        /// <summary>Режим в шапке: где сейчас игра.</summary>
        public static string ModeTitle(bool sandbox, bool rift, bool provingGround, int depth, int count, bool boss)
        {
            if (sandbox) return "Стенд мобов";
            if (rift) return "Забег · " + ArenaTitle(depth, boss) + (count > 0 && !boss ? " из " + count : "");
            if (provingGround) return "Полигон";
            return "Лагерь";
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
