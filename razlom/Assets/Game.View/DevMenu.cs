using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Что видит пункт меню разработчика: драйвер, сессию, забег и выбор вкладки «Забег» (локация, арена, сид).
    /// Только чтение. Создаётся меню на каждый кадр и на каждое действие заново.
    /// </summary>
    public readonly struct DevContext
    {
        public readonly TickDriver Driver;
        /// <summary>Локация, выбранная на вкладке «Забег».</summary>
        public readonly LocationTheme Location;
        /// <summary>Арена, выбранная на вкладке «Забег» (1…ArenaCount).</summary>
        public readonly int Arena;
        public readonly int ArenaCount;
        private readonly string _seed;

        internal DevContext(TickDriver driver, LocationTheme location, int arena, int arenaCount, string seed)
        {
            Driver = driver;
            Location = location;
            Arena = arena;
            ArenaCount = arenaCount;
            _seed = seed;
        }

        public GameSession Session => Driver != null ? Driver.Session : null;
        public RiftRun Run => Driver != null ? Driver.Run : null;
        public bool InRift => Session != null && Session.Mode == GameMode.Rift && Run != null;
        public bool InCamp => Session != null && Session.Mode == GameMode.Camp;
        /// <summary>Забег помечен тестовым (переход из меню, бессмертие, правка набора).</summary>
        public bool TestRun => Session != null && Session.IsDeveloperRun;
        /// <summary>Идёт обычный забег: его добыча настоящая, переход из меню его заменит.</summary>
        public bool RealRun => InRift && !TestRun;
        /// <summary>Стенд мобов (EnemyTestArena).</summary>
        public bool Sandbox => Driver != null && Driver.EnemySandbox != null;
        public bool SeedValid => DevMenuRules.TryParseSeed(_seed, out _);

        /// <summary>Сид тестового забега. Не число — исключение с понятным текстом (оно встанет под пунктом).</summary>
        public ulong Seed => DevMenuRules.TryParseSeed(_seed, out ulong seed) ? seed : throw new ArgumentException(DevMenuRules.SeedError);
    }

    internal enum DevEntryKind
    {
        Button,
        Toggle,
        Choice,
        Custom,
    }

    /// <summary>Пункт меню из реестра <see cref="DevMenu"/>.</summary>
    internal sealed class DevEntry
    {
        public string Id;
        public DevEntryKind Kind;
        public DevTab Tab;
        public string Section;
        public string Label;
        public string Hint;
        public DevFlags Flags;
        public int Order;
        public int Sequence;
        public string QuickLabel;
        public int QuickOrder;
        public Action<DevContext> Run;
        public Func<DevContext, bool> IsOn;
        public Action<DevContext, bool> Set;
        public string[] Options;
        public Func<DevContext, int> Get;
        public Action<DevContext, int> Choose;
        public Action<DevContext, DevUi> Draw;
        public Func<DevContext, string> Blocked;
        public Func<DevContext, bool> Visible;
        public Func<DevContext, string> DynamicLabel;
        public Func<DevContext, string> DynamicHint;
        public Func<DevContext, string> DynamicQuickLabel;
        public Func<DevContext, bool> QuickVisible;

        public bool IsVisible(in DevContext context) => Visible == null || Visible(context);
        public bool InQuickBar(in DevContext context) => (Flags & DevFlags.Quick) != 0 && IsVisible(context)
                                                         && (QuickVisible == null || QuickVisible(context));
        public string BlockedReason(in DevContext context) => Blocked == null ? null : Blocked(context);
        public string LabelFor(in DevContext context) => DynamicLabel == null ? Label : DynamicLabel(context) ?? Label;
        public string HintFor(in DevContext context) => DynamicHint == null ? Hint : DynamicHint(context) ?? Hint;
        public string QuickLabelFor(in DevContext context)
            => DynamicQuickLabel != null ? DynamicQuickLabel(context) ?? LabelFor(context) : QuickLabel ?? LabelFor(context);
    }

    /// <summary>Секция вкладки: заголовок, колонка, порядок, подсказка серым под заголовком.</summary>
    internal sealed class DevSection
    {
        public DevTab Tab;
        public string Name;
        public int Order;
        public int Sequence;
        public DevColumn Column;
        public string Hint;
        /// <summary>Полоса «Осторожно»: красная рамка.</summary>
        public bool Danger;
        public readonly List<DevEntry> Entries = new List<DevEntry>();
    }

    /// <summary>
    /// Реестр пунктов меню разработчика (F8). Система добавляет свой пункт в нужную вкладку и секцию, не трогая
    /// DeveloperMenu.cs: <c>DevMenu.Button("pelag.forms.preview", DevTab.Pelag, "Формы", "Показать выбор формы", …)</c>.
    ///
    /// Файл компилируется всегда: типы нужны вызывающему коду и в релизе. Сами вызовы регистрации вырезает
    /// компилятор вне редактора и dev-сборки (<see cref="ConditionalAttribute"/>) вместе с вычислением
    /// аргументов-лямбд, поэтому в релизе реестр пуст и меню нет.
    ///
    /// Правила (аудит F8 02.10): действия исполняются в Update меню, а не в OnGUI; ошибка действия встаёт под
    /// пунктом и в подвал; повторная регистрация с тем же id заменяет пункт; порядок — по order секции и пункта,
    /// затем по порядку регистрации. Встроенные пункты — DeveloperMenu.Run/Combat/Pelag/Visual.cs, формы —
    /// DeveloperMenu.Forms.cs. Game.Sim про меню не знает: пункты зовут её Debug*-методы.
    /// </summary>
    public static class DevMenu
    {
        private const string InEditor = "UNITY_EDITOR", InDevBuild = "DEVELOPMENT_BUILD";

        private static readonly Dictionary<string, DevEntry> Entries = new Dictionary<string, DevEntry>();
        private static readonly Dictionary<string, DevSection> Sections = new Dictionary<string, DevSection>();
        private static readonly List<DevSection>[] SortedByTab = new List<DevSection>[DevMenuRules.TabCount];
        private static readonly List<DevEntry> SortedQuick = new List<DevEntry>();
        private static int _sequence;
        private static bool _dirty = true;

        /// <summary>Растёт при каждой регистрации и снятии — меню пересобирает свои списки.</summary>
        internal static int Version { get; private set; }

        /// <summary>
        /// Очистка перед загрузкой сборок: без перезагрузки домена при входе в Play статический реестр
        /// пережил бы прошлый запуск. Встроенные пункты регистрируются заново позже (AfterAssembliesLoaded, OnEnable).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            Entries.Clear();
            Sections.Clear();
            SortedQuick.Clear();
            for (int i = 0; i < SortedByTab.Length; i++) SortedByTab[i] = null;
            _sequence = 0;
            _dirty = true;
            Version++;
        }

        /// <summary>Секция вкладки. Без объявления секция встаёт в левую колонку с порядком 100.</summary>
        [Conditional(InEditor), Conditional(InDevBuild)]
        public static void Section(DevTab tab, string section, int order, DevColumn column = DevColumn.Left,
            string hint = null, bool danger = false)
        {
            var target = SectionFor(tab, section);
            target.Order = order;
            target.Column = column;
            target.Hint = hint;
            target.Danger = danger;
            Touch();
        }

        /// <summary>
        /// Кнопка. <paramref name="blocked"/> — null, если доступна, иначе причина серым под кнопкой.
        /// <paramref name="quickLabel"/> — короткая подпись в быстрой строке (с флагом Quick).
        /// </summary>
        [Conditional(InEditor), Conditional(InDevBuild)]
        public static void Button(string id, DevTab tab, string section, string label, Action<DevContext> run,
            DevFlags flags = DevFlags.None, Func<DevContext, string> blocked = null, Func<DevContext, bool> visible = null,
            Func<DevContext, string> dynamicLabel = null, string quickLabel = null, string hint = null, int order = 0,
            Func<DevContext, string> dynamicQuickLabel = null, Func<DevContext, bool> quickVisible = null, int quickOrder = 100,
            Func<DevContext, string> dynamicHint = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var entry = Add(id, DevEntryKind.Button, tab, section, label, flags, hint, order);
            entry.Run = run;
            entry.Blocked = blocked;
            entry.Visible = visible;
            entry.DynamicLabel = dynamicLabel;
            entry.DynamicHint = dynamicHint;
            entry.QuickLabel = quickLabel;
            entry.DynamicQuickLabel = dynamicQuickLabel;
            entry.QuickVisible = quickVisible;
            entry.QuickOrder = quickOrder;
        }

        /// <summary>Переключатель: строка целиком, справа чип «ВКЛ» / «выкл».</summary>
        [Conditional(InEditor), Conditional(InDevBuild)]
        public static void Toggle(string id, DevTab tab, string section, string label,
            Func<DevContext, bool> isOn, Action<DevContext, bool> set,
            DevFlags flags = DevFlags.None, Func<DevContext, string> blocked = null, Func<DevContext, bool> visible = null,
            string quickLabel = null, string hint = null, int order = 0, int quickOrder = 100)
        {
            if (isOn == null) throw new ArgumentNullException(nameof(isOn));
            if (set == null) throw new ArgumentNullException(nameof(set));
            var entry = Add(id, DevEntryKind.Toggle, tab, section, label, flags, hint, order);
            entry.IsOn = isOn;
            entry.Set = set;
            entry.Blocked = blocked;
            entry.Visible = visible;
            entry.QuickLabel = quickLabel;
            entry.QuickOrder = quickOrder;
        }

        /// <summary>Выбор из нескольких: подпись и ряд кнопок. <paramref name="set"/> зовётся только при смене.</summary>
        [Conditional(InEditor), Conditional(InDevBuild)]
        public static void Choice(string id, DevTab tab, string section, string label, string[] options,
            Func<DevContext, int> get, Action<DevContext, int> set, DevFlags flags = DevFlags.None,
            Func<DevContext, bool> visible = null, string hint = null, int order = 0)
        {
            if (options == null || options.Length == 0) throw new ArgumentException("Нужен хотя бы один вариант.", nameof(options));
            if (get == null) throw new ArgumentNullException(nameof(get));
            if (set == null) throw new ArgumentNullException(nameof(set));
            var entry = Add(id, DevEntryKind.Choice, tab, section, label, flags, hint, order);
            entry.Options = (string[])options.Clone();
            entry.Get = get;
            entry.Choose = set;
            entry.Visible = visible;
        }

        /// <summary>Своя отрисовка в общем стиле: строки слотов, сетка арен, список талантов. Побочные эффекты — через DevUi.Defer.</summary>
        [Conditional(InEditor), Conditional(InDevBuild)]
        public static void Custom(string id, DevTab tab, string section, Action<DevContext, DevUi> draw,
            Func<DevContext, bool> visible = null, int order = 0)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            var entry = Add(id, DevEntryKind.Custom, tab, section, null, DevFlags.None, null, order);
            entry.Draw = draw;
            entry.Visible = visible;
        }

        /// <summary>Снять пункт (например, в OnDisable системы, которая его добавила).</summary>
        [Conditional(InEditor), Conditional(InDevBuild)]
        public static void Unregister(string id)
        {
            if (id == null || !Entries.TryGetValue(id, out var entry)) return;
            Entries.Remove(id);
            if (Sections.TryGetValue(Key(entry.Tab, entry.Section), out var section)) section.Entries.Remove(entry);
            Touch();
        }

        // ---- для оболочки ----------------------------------------------------------------------------

        /// <summary>Есть ли пункт с таким id (проверка переноса и тесты).</summary>
        internal static bool Has(string id) => id != null && Entries.ContainsKey(id);

        /// <summary>Секции вкладки по порядку; пункты внутри уже отсортированы.</summary>
        internal static IReadOnlyList<DevSection> SectionsOf(DevTab tab)
        {
            Refresh();
            return SortedByTab[(int)tab];
        }

        /// <summary>Пункты быстрой строки по порядку (видимость решает меню).</summary>
        internal static IReadOnlyList<DevEntry> QuickEntries()
        {
            Refresh();
            return SortedQuick;
        }

        private static DevEntry Add(string id, DevEntryKind kind, DevTab tab, string section, string label,
            DevFlags flags, string hint, int order)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Нужен id пункта.", nameof(id));
            if (string.IsNullOrEmpty(section)) throw new ArgumentException("Нужна секция.", nameof(section));
            if (Entries.TryGetValue(id, out var old))
            {
                if (Sections.TryGetValue(Key(old.Tab, old.Section), out var oldSection)) oldSection.Entries.Remove(old);
            }
            var entry = new DevEntry
            {
                Id = id,
                Kind = kind,
                Tab = tab,
                Section = section,
                Label = label,
                Flags = flags,
                Hint = hint,
                Order = order,
                // Повторная регистрация заменяет пункт и сохраняет его место среди равных по order.
                Sequence = old != null ? old.Sequence : _sequence++,
            };
            Entries[id] = entry;
            SectionFor(tab, section).Entries.Add(entry);
            Touch();
            return entry;
        }

        private static DevSection SectionFor(DevTab tab, string name)
        {
            string key = Key(tab, name);
            if (!Sections.TryGetValue(key, out var section))
            {
                section = new DevSection { Tab = tab, Name = name, Order = 100, Column = DevColumn.Left, Sequence = _sequence++ };
                Sections[key] = section;
            }
            return section;
        }

        private static string Key(DevTab tab, string section) => (int)tab + "/" + section;

        private static void Touch()
        {
            _dirty = true;
            Version++;
        }

        private static void Refresh()
        {
            if (!_dirty && SortedByTab[0] != null) return;
            _dirty = false;
            for (int i = 0; i < SortedByTab.Length; i++)
            {
                if (SortedByTab[i] == null) SortedByTab[i] = new List<DevSection>();
                else SortedByTab[i].Clear();
            }
            SortedQuick.Clear();
            foreach (var section in Sections.Values)
            {
                section.Entries.Sort(CompareEntries);
                if (section.Entries.Count > 0) SortedByTab[(int)section.Tab].Add(section);
                foreach (var entry in section.Entries)
                    if ((entry.Flags & DevFlags.Quick) != 0) SortedQuick.Add(entry);
            }
            foreach (var list in SortedByTab) list.Sort(CompareSections);
            SortedQuick.Sort((a, b) => a.QuickOrder != b.QuickOrder ? a.QuickOrder.CompareTo(b.QuickOrder) : CompareEntries(a, b));
        }

        private static int CompareEntries(DevEntry a, DevEntry b)
            => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Sequence.CompareTo(b.Sequence);

        private static int CompareSections(DevSection a, DevSection b)
            => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Sequence.CompareTo(b.Sequence);
    }
}
