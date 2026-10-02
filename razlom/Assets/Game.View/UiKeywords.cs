using System;
using System.Collections.Generic;
using System.Text;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// КЛЮЧЕВЫЕ СЛОВА ОПИСАНИЙ (план, этап 4, п. 6; владелец 29.09 — «как в Hades»): в описаниях
    /// способностей, усилений, артефактов и вещей игровые термины — «Корни», «Оглушение», «Концентрация»…
    /// — подсвечены цветом и открывают вложенную подсказку с определением.
    ///
    /// Здесь только данные и разметка, без Unity (проверяется в tools/Combat.Presentation.Tests):
    /// * словарь: ключ → название, одно предложение определения, значок, цветовая роль и словоформы;
    /// * авторская разметка: <c>{kw:roots}</c> — название слова, <c>{kw:roots|корнями}</c> — своя форма;
    /// * автопометка старых строк: словоформы из словаря находятся в тексте сами.
    /// Результат — rich text TMP: <c>&lt;link="kw:roots"&gt;&lt;color=#…&gt;корнями&lt;/color&gt;&lt;/link&gt;</c>;
    /// id ссылки разбирает <see cref="TryParseLink"/> (TMP_TextUtilities.FindIntersectingLink → GetLinkID).
    /// Цвета темы по роли — UiKeywords.Theme.cs; здесь запасная палитра с умолчаниями UiTheme.
    ///
    /// ПРАВИЛА АВТОПОМЕТКИ — каждое из-за конкретной строки:
    /// * только целое слово: «корни» не находится в «Корнехват», «крит» — в «критерий»;
    /// * регистр и ё не важны: «Оглушение» в начале фразы и «оглушенным» без точек — то же слово;
    ///   в выводе остаётся написание автора;
    /// * сначала самая длинная форма: «горящей саблей» — горящая сабля, а не горение, «защитой от
    ///   контроля» — целиком;
    /// * внутрь разметки не лезем: содержимое тегов, уже готовые &lt;link&gt; и &lt;noparse&gt; не трогаются,
    ///   поэтому повторный прогон ничего не меняет и ссылка в ссылку не вкладывается;
    /// * в «ёлочках» не метим: там имена способностей и усилений («Неуязвимость», «Долгий стан»).
    ///
    /// Словоформы — явный список, а не стеммер: глагол «поджигает» у «Ладно смазал» значит «поджигает
    /// саблю», а у «Поджига» — горение цели, «горит» — и сабля, и лужа. Такие места помечаются только
    /// словосочетанием или авторской разметкой. Новое слово в описании — дописать форму сюда.
    ///
    /// Числа определений берутся из констант симуляции там, где они открыты: поменялись корни
    /// Корнехвата — поменялась и подсказка. Закрытые (шанс крита, уклонение сабли) — числом с пометкой.
    /// </summary>
    public static partial class UiKeywords
    {
        /// <summary>Слово словаря. Номера нигде не хранятся — порядок можно менять.</summary>
        public enum Id : byte
        {
            None = 0,
            Roots,
            Stun,
            ControlImmunity,
            Slow,
            Knockback,
            Pull,
            Interrupt,
            Freeze,
            Burn,
            Blaze,
            Evasion,
            Invulnerability,
            Crit,
            Lavidium,
            Armor,
            FireResist,
            Resin,
            Surge,
            Elite,
            Boss,
        }

        /// <summary>
        /// Цветовая роль слова. В игре — цвет роли UiTheme (UiKeywords.Theme.cs, <c>ThemeRole</c>),
        /// в тестах и без темы — <see cref="DefaultHex"/>. Цвета предварительные: вид ещё не выбран.
        /// </summary>
        public enum Tone : byte
        {
            /// <summary>Контроль и помехи: корни, оглушение, замедление, отброс, заморозка.</summary>
            Control,
            /// <summary>Защита героя: неуязвимость, уклонение, защита от контроля, броня, Живица.</summary>
            Defense,
            /// <summary>Огонь: горение, горящая сабля, сопротивление огню.</summary>
            Fire,
            /// <summary>Ресурс способностей: концентрация.</summary>
            Resource,
            /// <summary>Сила удара и темп: крит, Порыв.</summary>
            Offense,
            /// <summary>Особые враги: элита, босс.</summary>
            Enemy,
        }

        /// <summary>Статья словаря.</summary>
        public sealed class Entry
        {
            public readonly Id Id;
            /// <summary>Ключ разметки и ссылки: <c>{kw:roots}</c>, <c>link="kw:roots"</c>.</summary>
            public readonly string Key;
            /// <summary>Название во вложенной подсказке и в <c>{kw:key}</c> без своей формы.</summary>
            public readonly string Title;
            /// <summary>Одно предложение простыми словами, числа — как в симуляции.</summary>
            public readonly string Definition;
            /// <summary>Значок: Resources/UI/Keywords/{Icon}. Картинок пока нет — вид не выбран.</summary>
            public readonly string Icon;
            public readonly Tone Tone;
            /// <summary>Словоформы для автопометки: строчные, «ё» уже заменена на «е», слова через один пробел.</summary>
            public readonly string[] Forms;

            internal Entry(Id id, string key, string title, Tone tone, string forms, string definition)
            {
                Id = id;
                Key = key;
                Title = title;
                Tone = tone;
                Icon = "kw_" + key;
                Definition = definition;
                string[] split = forms.Split('|');
                for (int i = 0; i < split.Length; i++) split[i] = Fold(split[i]);
                Forms = split;
            }

            /// <summary>Id ссылки TMP: «kw:roots».</summary>
            public string LinkId => LinkPrefix + Key;
        }

        /// <summary>Приставка id ссылки TMP и авторской разметки.</summary>
        public const string LinkPrefix = "kw:";

        const int Tps = Simulation.TicksPerSecond;

        /// <summary>Тики как в подписях игры: «1 с», «1,5 с», «6 с».</summary>
        public static string Seconds(int ticks)
        {
            int tenths = (ticks * 10 + Tps / 2) / Tps;
            return (tenths % 10 == 0 ? (tenths / 10).ToString() : (tenths / 10) + "," + (tenths % 10)) + " с";
        }

        static readonly Entry[] Glossary =
        {
            // Корни Корнехвата — решение владельца 29.09 (Simulation.HeroSlow, MovesHero в Simulation.Tempo).
            new Entry(Id.Roots, "roots", "Корни", Tone.Control,
                "корни|корней|корням|корнями|корнях",
                "Пелаг не ходит и не может применить способности, которые его двигают (рывок, «На вылет», «Отбой», «Абордаж», «Шквал»), "
                + "но бьёт и применяет остальные на месте; корни Корнехвата держат " + Seconds(Simulation.RootSnarerRootTicks) + "."),

            // Общее оглушение StatusStore: и героя (таран Камнекопыта), и врагов (якорь, таланты).
            new Entry(Id.Stun, "stun", "Оглушение", Tone.Control,
                "оглушение|оглушения|оглушению|оглушением|оглушении|оглушает|оглушают|оглушить|оглушил|оглушила|оглушило|оглушили|оглушит|оглушат"
                + "|оглушён|оглушена|оглушено|оглушены|оглушённый|оглушённого|оглушённому|оглушённым|оглушённом|оглушённая|оглушённую|оглушённой"
                + "|оглушённое|оглушённые|оглушённых|оглушёнными",
                "Оглушённый не ходит, не бьёт и не применяет способностей, а начатый замах или приём срывается; "
                + "таран Камнекопыта оглушает Пелага на " + Seconds(Simulation.StonehoofChargeStunTicks) + "."),

            // Simulation.HeroControlImmunityTicks: иммунитет считается от конца контроля, на замедление не действует.
            new Entry(Id.ControlImmunity, "control_immunity", "Защита от контроля", Tone.Defense,
                "защита от контроля|защиты от контроля|защите от контроля|защиту от контроля|защитой от контроля"
                + "|иммунитет к контролю|иммунитета к контролю|иммунитету к контролю|иммунитетом к контролю|иммунитете к контролю",
                "Пока Пелаг в корнях или оглушён и ещё " + Seconds(Simulation.HeroControlImmunityTicks)
                + " после, новые корни и оглушение на него не ложатся, а замедление и отброс проходят как обычно."),

            // ApplyHeroSlow: сильнейшее побеждает, срок — самый поздний.
            new Entry(Id.Slow, "slow", "Замедление", Tone.Control,
                "замедление|замедления|замедлению|замедлением|замедлении|замедляет|замедляют|замедлить|замедлит"
                + "|замедлен|замедлена|замедлено|замедлены|замедленный|замедленного|замедленному|замедленным|замедленном"
                + "|замедленная|замедленную|замедленной|замедленное|замедленные|замедленных|замедленными",
                "Замедленный бегает медленнее — вой Вендиго отнимает у Пелага " + Simulation.WendigoHowlSlowPercent + "% скорости на "
                + Seconds(Simulation.WendigoHowlSlowTicks) + "; два замедления не складываются, действует сильнейшее."),

            // ForcedMotion.Knockback (Вендиго, Камнекопыт, Шипомёт, Расщепень) и волна Обета Хранителя (2,5 м).
            new Entry(Id.Knockback, "knockback", "Отброс", Tone.Control,
                "отброс|отброса|отбросу|отбросом|отбросе|отбрасывает|отбрасывают|отбросить|отбросит|отбросят"
                + "|отброшен|отброшена|отброшено|отброшены|отброшенный|отброшенного|отброшенному|отброшенным"
                + "|отброшенная|отброшенную|отброшенной|отброшенные|отброшенных",
                "Удар отшвыривает тело на 1–2,5 м: пока оно летит, своим шагом не управлять, стена останавливает полёт, "
                + "а защита от контроля от отброса не спасает."),

            // ForcedMotion.Dragged: «Затягивает» Вихря. Чужая тяга сильнее собственного шага и сбивает замах.
            new Entry(Id.Pull, "pull", "Подтягивание", Tone.Control,
                "подтягивание|подтягивания|подтягиванию|подтягиванием|подтягивает|подтягивают|подтянуть|подтянет"
                + "|подтянут|подтянутый|подтянутого|подтянутому|подтянутым|подтянутые|подтянутых",
                "Способность тащит врагов к Пелагу: пока их тащат, они не идут сами, а начатый замах срывается."),

            // Simulation.EnemyMelee: оглушение, чужая тяга или стёртая цель гасят замах до удара.
            new Entry(Id.Interrupt, "interrupt", "Срыв замаха", Tone.Control,
                "срыв замаха|срыва замаха|срывом замаха|начатый замах|начатого замаха|начатому замаху|начатым замахом|начатом замахе"
                + "|срывает замах|сбивает замах|сбить замах|сорвать замах",
                "Если враг, уже начавший замах, оглушён, отброшен или подтянут, его удар не прилетает вовсе."),

            // Сердце Вечной Зимы: оглушение талантом на WinterTicks, раскол — только ударом способности.
            new Entry(Id.Freeze, "freeze", "Заморозка", Tone.Control,
                "заморозка|заморозки|заморозке|заморозку|заморозкой|замораживает|замораживают|заморозить|заморозит"
                + "|заморожен|заморожена|заморожены|замороженный|замороженного|замороженному|замороженным|замороженные|замороженных"
                + "|замерзает|замерзают|замёрзнет|замёрзнут|замёрз|замёрзла|замёрзли|замёрзший|замёрзшего|замёрзшему|замёрзшим"
                + "|замёрзшем|замёрзшая|замёрзшую|замёрзшей|замёрзшие|замёрзших|замёрзшими",
                "Замёрзший враг стоит и не бьёт, как оглушённый; удар способностью раскалывает лёд и наносит +"
                + Simulation.WinterShatterDamage + " урона, а босса и элиту заморозить нельзя — их только замедляет."),

            // Урон по времени: поджиг «Ладно смазал», огненный след, лужа Взрывной смеси (огонь, overTime).
            new Entry(Id.Burn, "burn", "Горение", Tone.Fire,
                "горение|горения|горению|горением|горении|горящий|горящего|горящему|горящим|горящем|горящая|горящую|горящей"
                + "|горящее|горящие|горящих|горящими|подожжён|подожжена|подожжены|подожжённый|подожжённого|подожжённому"
                + "|подожжённым|подожжённые|подожжённых|поджигает цель|поджигают цель|поджигает врага|поджигает врагов|поджигают врагов"
                + "|огненный след|огненного следа|огненному следу|огненным следом|огненном следе",
                "Огонь ранит по времени короткими толчками, пока цель горит или стоит в огне; его гасит сопротивление огню, "
                + "а не броня, и от него не уклониться."),

            // «Ладно смазал»: DurationTicks 90, добавка и уклонение по пятой части (владелец 12.09, SabreKit).
            new Entry(Id.Blaze, "blaze", "Горящая сабля", Tone.Fire,
                "горящая сабля|горящей сабли|горящей сабле|горящую саблю|горящей саблей|сабля горит|сабля горела|под огнём"
                + "|огненная добавка|огненной добавки|огненной добавке|огненную добавку|огненной добавкой",
                "После «Ладно смазал» сабля горит 3 с: каждая обычная атака добавляет огненный удар в 20% своей силы, "
                + "а от прямого удара Пелаг уклоняется с шансом 20%."),

            // SabreKit.BlazeEvades: бросок только под горящей саблей, урон по времени не уклоняется.
            new Entry(Id.Evasion, "evasion", "Уклонение", Tone.Defense,
                "уклонение|уклонения|уклонению|уклонением|уклонении|уклонений|уклоняется|уклоняются|уклониться|уклонится|уклонился|уклонилась",
                "Шанс целиком уйти от прямого удара — сейчас он есть только под горящей саблей (20%); урон по времени не уклоняется, "
                + "а уклонённый удар не вешает ни замедления, ни контроля."),

            // PlayerImmune: Солнечная Печать, Лик Пустоты, Обет Хранителя, прыжки Шквала с усилением.
            new Entry(Id.Invulnerability, "invulnerability", "Неуязвимость", Tone.Defense,
                "неуязвимость|неуязвимости|неуязвимостью|неуязвим|неуязвима|неуязвимо|неуязвимы|неуязвимый|неуязвимого"
                + "|неуязвимому|неуязвимым|неуязвимом|неуязвимая|неуязвимую|неуязвимой|неуязвимое|неуязвимые|неуязвимых",
                "Урон по Пелагу не проходит вовсе, а удар, который не ранил, не вешает ни замедления, ни корней, ни оглушения."),

            // Simulation.BaseCritChance 15%, BaseCritMultiplier ×2; бросок на крит — только в ApplyAttack.
            new Entry(Id.Crit, "crit", "Крит", Tone.Offense,
                "крит|крита|криту|критом|крите|криты|критов|критам|критами|критах|критует|критуют"
                + "|критический|критического|критическому|критическим|критическом|критическая|критическую|критической"
                + "|критическое|критические|критических|критическими",
                "Обычная атака с шансом (у Пелага базово 15%) бьёт сильнее — базово вдвое; способности не критуют."),

            // StatType.MaxLavidium / LavidiumRegen (PlayerBaseLavidiumRegen = 3). Игроку ресурс — «Концентрация»
            // (владелец 01.10: «лавидий у Пелага звучит глупо»); Id и ключ разметки «lavidium» прежние — стабильные id.
            // Слова «лавидий» в словаре больше нет: это металл и валюта мира («Кольцо с лавидием», CurrencyType.Lavidium),
            // а не механика из подсказок — с определением ресурса он врал бы.
            new Entry(Id.Lavidium, "lavidium", "Концентрация", Tone.Resource,
                "концентрация|концентрации|концентрацию|концентрацией|концентрациею",
                "Ресурс способностей: каждая тратит концентрацию при применении, а запас понемногу восстанавливается сам "
                + "(базово 3 в секунду) и пополняется зельями."),

            // CombatStats.Mitigate: броня — физический урон по кривой, огонь — сопротивление.
            // Без «брони»/«броне»: так пишут о надетых вещах («растёт от брони и талисманов»), а не о характеристике.
            new Entry(Id.Armor, "armor", "Броня", Tone.Defense,
                "броня|броню|бронёй|бронею",
                "Снижает физический урон — чем сильнее удар, тем меньше броня от него спасает, а огонь она не гасит."),

            new Entry(Id.FireResist, "fire_resist", "Сопротивление огню", Tone.Fire,
                "сопротивление огню|сопротивления огню|сопротивлению огню|сопротивлением огню|сопротивлении огню",
                "Снижает урон огнём, в том числе горением, но не больше чем на "
                + (CombatStats.MaxResistance * 100).ToInt() + "%."),

            // Simulation.PotionEffects: ApplyResinReduction — 75% входящего урона.
            new Entry(Id.Resin, "resin", "Живица", Tone.Defense,
                "живица|живицы|живице|живицу|живицей|живицею",
                "Эффект зелья Живица: " + Seconds(Simulation.PotionEffectTicks) + " Пелаг получает на 25% меньше урона."),

            // ApplySurgePotion: +20% бега (Increased) и +0,2 скорости приёмов (Flat AbilitySpeed).
            // Зелье «Порыв» — с 01.10 без «Лавидиевого»: ресурс героя теперь концентрация.
            new Entry(Id.Surge, "surge", "Порыв", Tone.Offense,
                "порыв|порыва|порыву|порывом|порыве|порывы",
                "Эффект зелья Порыв: " + Seconds(Simulation.PotionEffectTicks)
                + " Пелаг бегает на 20% быстрее и на 20% быстрее исполняет способности."),

            // _eliteMask: элита встречи и босс (SetupBossArena), EliteKillXp.
            new Entry(Id.Elite, "elite", "Элита", Tone.Enemy,
                "элита|элиты|элите|элиту|элитой|элитою|элит|элитам|элитами|элитах|элитный|элитного|элитному|элитным|элитном"
                + "|элитная|элитную|элитной|элитное|элитные|элитных|элитными",
                "Усиленный враг со своей полосой здоровья, за которого дают больше опыта; босс тоже считается элитой."),

            new Entry(Id.Boss, "boss", "Босс", Tone.Enemy,
                "босс|босса|боссу|боссом|боссе|боссы|боссов|боссам|боссами|боссах",
                "Главный враг арены босса: на " + Simulation.BossAddFirstPercent + "% и " + Simulation.BossAddSecondPercent
                + "% здоровья зовёт подмогу и считается элитой."),
        };

        static readonly Entry[] ById = BuildIndex();

        struct Candidate
        {
            public string Form;
            public Id Id;
        }

        /// <summary>Формы по первой букве, длинные раньше: «горящей саблей» проверяется до «горящей».</summary>
        static readonly Dictionary<char, Candidate[]> ByFirstLetter = BuildCandidates();

        static Entry[] BuildIndex()
        {
            var index = new Entry[Enum.GetValues(typeof(Id)).Length];
            foreach (Entry entry in Glossary) index[(int)entry.Id] = entry;
            return index;
        }

        static Dictionary<char, Candidate[]> BuildCandidates()
        {
            var lists = new Dictionary<char, List<Candidate>>();
            foreach (Entry entry in Glossary)
                foreach (string form in entry.Forms)
                {
                    if (form.Length == 0) continue;
                    if (!lists.TryGetValue(form[0], out List<Candidate> list)) lists[form[0]] = list = new List<Candidate>();
                    list.Add(new Candidate { Form = form, Id = entry.Id });
                }
            var result = new Dictionary<char, Candidate[]>(lists.Count);
            foreach (KeyValuePair<char, List<Candidate>> pair in lists)
            {
                pair.Value.Sort((a, b) => a.Form.Length != b.Form.Length
                    ? b.Form.Length.CompareTo(a.Form.Length)
                    : string.CompareOrdinal(a.Form, b.Form));
                result[pair.Key] = pair.Value.ToArray();
            }
            return result;
        }

        /// <summary>Все статьи в порядке словаря.</summary>
        public static IReadOnlyList<Entry> All => Glossary;

        /// <summary>Статья по слову; null — <see cref="Id.None"/> или нет такого.</summary>
        public static Entry Get(Id id) => (uint)id < (uint)ById.Length ? ById[(int)id] : null;

        /// <summary>Статья по ключу разметки («roots»). Регистр ключа важен: ключи строчные.</summary>
        public static bool TryGet(string key, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(key)) return false;
            foreach (Entry candidate in Glossary)
                if (candidate.Key == key) { entry = candidate; return true; }
            return false;
        }

        /// <summary>Id ссылки TMP («kw:roots») → слово. Чужие ссылки и пустые — false.</summary>
        public static bool TryParseLink(string linkId, out Id id)
        {
            id = Id.None;
            if (linkId == null || !linkId.StartsWith(LinkPrefix, StringComparison.Ordinal)) return false;
            if (!TryGet(linkId.Substring(LinkPrefix.Length), out Entry entry)) return false;
            id = entry.Id;
            return true;
        }

        /// <summary>Цвет роли без темы — умолчания UiTheme: Rare, Good, Unique, Lavidium, Epic, Bad.</summary>
        public static string DefaultHex(Tone tone)
        {
            switch (tone)
            {
                // Умолчания темы — одно место (UiTheme.Tokens.cs), не копия чисел.
                case Tone.Control: return "#" + UiTheme.RareHex;
                case Tone.Defense: return "#" + UiTheme.GoodHex;
                case Tone.Fire: return "#" + UiTheme.UniqueHex;
                case Tone.Resource: return "#" + UiTheme.LavidiumHex;
                case Tone.Offense: return "#" + UiTheme.EpicHex;
                default: return "#" + UiTheme.BadHex;
            }
        }

        /// <summary>
        /// Размечает текст описания: разворачивает <c>{kw:key}</c> и <c>{kw:key|форма}</c>, а при
        /// <paramref name="autoTag"/> ещё и находит словоформы словаря в обычном тексте.
        /// </summary>
        /// <param name="found">Сюда дописываются найденные слова — по разу, в порядке первого появления.
        /// Это список для вложенной подсказки.</param>
        /// <param name="colorHex">Цвет роли, «#RRGGBB»; null — <see cref="DefaultHex"/>.</param>
        /// <param name="exclude">Слово, которое не метится автоматически: определение не ссылается само на себя.</param>
        /// <returns>Тот же экземпляр строки, если метить нечего.</returns>
        public static string Markup(string text, ICollection<Id> found = null, Func<Tone, string> colorHex = null,
            bool autoTag = true, Id exclude = Id.None)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder output = null;
            int flushed = 0, linkDepth = 0, quoteDepth = 0, i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '<')
                {
                    int close = TagEnd(text, i);
                    if (close > 0)
                    {
                        string name = TagName(text, i, close);
                        if (name == "noparse")
                        {
                            // Внутри noparse TMP ничего не разбирает — и мы тоже.
                            int end = text.IndexOf("</noparse>", close + 1, StringComparison.OrdinalIgnoreCase);
                            i = end < 0 ? text.Length : end + "</noparse>".Length;
                            continue;
                        }
                        if (name == "link") linkDepth++;
                        else if (name == "/link" && linkDepth > 0) linkDepth--;
                        i = close + 1;
                        continue;
                    }
                }
                else if (c == '{' && TryReadMarkup(text, i, out int markupEnd, out Id marked, out string shown))
                {
                    if (marked != Id.None)
                    {
                        Flush(ref output, text, ref flushed, i);
                        if (linkDepth > 0) output.Append(shown);
                        else AppendLink(output, marked, shown, colorHex, found);
                        flushed = markupEnd;
                    }
                    // Неизвестный ключ остаётся как написан: опечатку видно сразу, а не пустым местом.
                    i = markupEnd;
                    continue;
                }
                else if (c == '«') quoteDepth++;
                else if (c == '»' && quoteDepth > 0) quoteDepth--;
                else if (autoTag && linkDepth == 0 && quoteDepth == 0 && IsWordStart(text, i)
                         && TryMatch(text, i, out int matchEnd, out Id hit))
                {
                    if (hit != exclude)
                    {
                        Flush(ref output, text, ref flushed, i);
                        AppendLink(output, hit, text.Substring(i, matchEnd - i), colorHex, found);
                        flushed = matchEnd;
                    }
                    // Исключённое слово пропускается целиком: более короткая форма внутри него не всплывает.
                    i = matchEnd;
                    continue;
                }
                i++;
            }
            if (output == null) return text;
            output.Append(text, flushed, text.Length - flushed);
            return output.ToString();
        }

        /// <summary>Определение для вложенной подсказки: другие слова в нём тоже ссылки, само слово — нет.</summary>
        public static string DefinitionMarkup(Id id, ICollection<Id> found = null, Func<Tone, string> colorHex = null)
        {
            Entry entry = Get(id);
            return entry == null ? string.Empty : Markup(entry.Definition, found, colorHex, true, id);
        }

        static void Flush(ref StringBuilder output, string text, ref int flushed, int upTo)
        {
            if (output == null) output = new StringBuilder(text.Length + 96);
            output.Append(text, flushed, upTo - flushed);
            flushed = upTo;
        }

        static void AppendLink(StringBuilder output, Id id, string shown, Func<Tone, string> colorHex, ICollection<Id> found)
        {
            Entry entry = ById[(int)id];
            string color = colorHex != null ? colorHex(entry.Tone) : null;
            if (string.IsNullOrEmpty(color)) color = DefaultHex(entry.Tone);
            output.Append("<link=\"").Append(LinkPrefix).Append(entry.Key).Append("\"><color=").Append(color).Append('>')
                .Append(shown).Append("</color></link>");
            if (found != null && !found.Contains(id)) found.Add(id);
        }

        /// <summary>
        /// Тег TMP: «&lt;» и сразу буква, «/» или «#», дальше до «&gt;» без переноса строки и нового «&lt;».
        /// Иначе «&lt;» — просто знак в тексте («&lt; 5 м»). Возвращает индекс «&gt;» или −1.
        /// </summary>
        static int TagEnd(string text, int open)
        {
            if (open + 1 >= text.Length) return -1;
            char first = text[open + 1];
            if (!char.IsLetter(first) && first != '/' && first != '#') return -1;
            for (int i = open + 2; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '>') return i;
                if (c == '<' || c == '\n') return -1;
            }
            return -1;
        }

        static string TagName(string text, int open, int close)
        {
            int end = open + 1;
            while (end < close && text[end] != '=' && text[end] != ' ') end++;
            return text.Substring(open + 1, end - open - 1).ToLowerInvariant();
        }

        /// <summary><c>{kw:key}</c> или <c>{kw:key|форма}</c>. Ключ неизвестен — marked = None, форма не нужна.</summary>
        static bool TryReadMarkup(string text, int open, out int end, out Id marked, out string shown)
        {
            end = open;
            marked = Id.None;
            shown = null;
            int keyStart = open + 1 + LinkPrefix.Length;
            if (keyStart >= text.Length || string.CompareOrdinal(text, open + 1, LinkPrefix, 0, LinkPrefix.Length) != 0) return false;
            int close = -1, bar = -1;
            for (int i = keyStart; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '}') { close = i; break; }
                if (c == '{' || c == '\n') return false;
                if (c == '|' && bar < 0) bar = i;
            }
            if (close < 0) return false;
            end = close + 1;
            string key = text.Substring(keyStart, (bar < 0 ? close : bar) - keyStart);
            if (!TryGet(key, out Entry entry)) return true;
            marked = entry.Id;
            shown = bar < 0 || bar + 1 == close ? entry.Title : text.Substring(bar + 1, close - bar - 1);
            return true;
        }

        static bool IsWordStart(string text, int i) => IsWordChar(text[i]) && (i == 0 || !IsWordChar(text[i - 1]));

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '́';

        static bool IsGap(char c) => c == ' ' || c == ' ';

        /// <summary>Самая длинная словоформа с начала слова i; конец совпадения — на границе слова.</summary>
        static bool TryMatch(string text, int start, out int end, out Id id)
        {
            end = start;
            id = Id.None;
            if (!ByFirstLetter.TryGetValue(Fold(text[start]), out Candidate[] candidates)) return false;
            foreach (Candidate candidate in candidates)
            {
                string form = candidate.Form;
                int j = start;
                bool ok = true;
                for (int k = 0; k < form.Length && ok; k++)
                {
                    if (form[k] == ' ')
                    {
                        if (j >= text.Length || !IsGap(text[j])) { ok = false; break; }
                        while (j < text.Length && IsGap(text[j])) j++;
                        continue;
                    }
                    ok = j < text.Length && Fold(text[j]) == form[k];
                    j++;
                }
                if (!ok || (j < text.Length && IsWordChar(text[j]))) continue;
                end = j;
                id = candidate.Id;
                return true;
            }
            return false;
        }

        static char Fold(char c)
        {
            c = char.ToLowerInvariant(c);
            return c == 'ё' ? 'е' : c;
        }

        static string Fold(string text)
        {
            var chars = new char[text.Length];
            for (int i = 0; i < text.Length; i++) chars[i] = Fold(text[i]);
            return new string(chars);
        }
    }
}
