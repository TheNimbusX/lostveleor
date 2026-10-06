using Game.Sim;

namespace Game.View
{
    /// <summary>Группа клятв на доске (DESIGN 06.10): герой, выживание, удача забега, добыча.</summary>
    public enum OathGroup : byte { Hero = 0, Survival = 1, Fortune = 2, Loot = 3 }

    /// <summary>Что делает основная кнопка карточки клятвы.</summary>
    public enum OathAction : byte
    {
        /// <summary>Не куплена — «Поклясться» за пепел.</summary>
        Swear = 0,
        /// <summary>Куплена не до потолка (клятвы героя) — следующая ступень за пепел.</summary>
        Strengthen = 1,
        /// <summary>На потолке и выключена — «Включить».</summary>
        Enable = 2,
        /// <summary>На потолке и в силе — «Выключить».</summary>
        Disable = 3,
    }

    /// <summary>Почему кнопка карточки притухла или о чём карточка предупреждает.</summary>
    public enum OathWarning : byte
    {
        None = 0,
        /// <summary>Пепла меньше цены следующей покупки.</summary>
        NotEnoughAsh = 1,
        /// <summary>Все слоты заняты — включить нельзя, сначала выключить другую.</summary>
        NoFreeSlot = 2,
        /// <summary>Новая клятва купится, но слотов нет — ляжет в запас выключенной.</summary>
        GoesToReserve = 3,
    }

    /// <summary>Как выглядит печать на доске (таблица состояний ui-tent §3).</summary>
    public enum OathLook : byte
    {
        /// <summary>Не куплена, пепла хватает — тёмный диск, знак приглушён.</summary>
        NotBought = 0,
        /// <summary>Не куплена и не по карману — ещё тусклее, цена красным.</summary>
        Unaffordable = 1,
        /// <summary>Куплена, выключена — знак в полную силу, огонь тлеет.</summary>
        Owned = 2,
        /// <summary>В силе — огонь горит, печать стоит и в ряду слотов.</summary>
        Active = 3,
    }

    /// <summary>
    /// Доска клятв и атлас палатки без Unity (06.10, вкладки «Клятвы» / «Атлас» CampTentWc): группы, имена и действие клятв,
    /// состояние печати и кнопки карточки, ряд слотов, пороги слотов, правило «можно взять на столе». Числа — константы
    /// Sim (Simulation.Oaths, RunBoons, RunEconomy): поменяют баланс — тексты поменяются сами. Тексты здесь — русские
    /// запасные; окно ищет перевод по ключу (<see cref="NameKey"/> и т. п.) через CampWindowText. Проверяет
    /// tools/Combat.Presentation.Tests/CampOathRulesTests.cs.
    /// </summary>
    public static class CampOathRules
    {
        /// <summary>Мест в ряду слотов: столько, сколько слотов даёт 18-й уровень.</summary>
        public const int SlotPlaces = 6;

        /// <summary>
        /// Уровень лагеря, с которого открыт слот (номер от 0): 3 сразу, 4-й — с 6-го, 5-й — с 12-го, 6-й — с 18-го (Camp.OathSlots).
        /// На концепте «Ур. 10/15/20» — выдумка генератора, подписи берутся отсюда.
        /// </summary>
        public static int SlotUnlockLevel(int slot) => slot < 3 ? 1 : slot == 3 ? 6 : slot == 4 ? 12 : 18;

        /// <summary>Мест в сетке атласа 3×4: восемь артефактов акта I и четыре места акта II.</summary>
        public const int AtlasPlaces = 12;

        /// <summary>Место атласа за пределами набора акта I — тёмное «?» с подписью «Акт II».</summary>
        public static bool AtlasPlaceIsFuture(int place) => place >= RunArtifacts.Count;

        // ---------------------------------------------------------------- группы

        static readonly OathId[] HeroOaths = { OathId.ToughHide, OathId.HeavyHand, OathId.LightStep, OathId.KeenEye, OathId.DeepReserve, OathId.QuickRoll };
        static readonly OathId[] SurvivalOaths = { OathId.LastBreath, OathId.Steadfast, OathId.GenerousSpring, OathId.EnemyBlood };
        static readonly OathId[] FortuneOaths = { OathId.SecondLook, OathId.Instinct, OathId.Favor };
        static readonly OathId[] LootOaths = { OathId.TenaciousHands, OathId.RingingCoin, OathId.AshTrail, OathId.RuneSage };

        /// <summary>Группа клятвы: номера OathId уже идут группами (RunBoons.cs: герой 1–6, выживание 7–10, удача 11–13, добыча 14–17).</summary>
        public static OathGroup GroupOf(OathId id)
            => id <= OathId.QuickRoll ? OathGroup.Hero : id <= OathId.EnemyBlood ? OathGroup.Survival : id <= OathId.Favor ? OathGroup.Fortune : OathGroup.Loot;

        /// <summary>Клятвы группы в порядке печатей на доске (порядок номеров).</summary>
        public static OathId[] Members(OathGroup group)
        {
            switch (group)
            {
                case OathGroup.Hero: return HeroOaths;
                case OathGroup.Survival: return SurvivalOaths;
                case OathGroup.Fortune: return FortuneOaths;
                default: return LootOaths;
            }
        }

        public static string GroupKey(OathGroup group) => "tent.oath.group." + (int)group;

        /// <summary>Имена групп — наши («Фортуна» концепта — выдумка генератора).</summary>
        public static string GroupRu(OathGroup group)
        {
            switch (group)
            {
                case OathGroup.Hero: return "Герой";
                case OathGroup.Survival: return "Выживание";
                case OathGroup.Fortune: return "Удача забега";
                default: return "Добыча";
            }
        }

        // ---------------------------------------------------------------- имена и значки

        /// <summary>Строки ключей OathIds без «oath.»: имя файла значка (Resources/UI/OathIcons/&lt;файл&gt;.png) и ключей текстов.</summary>
        static readonly string[] Files =
        {
            "tough-hide", "heavy-hand", "light-step", "keen-eye", "deep-reserve", "quick-roll",
            "last-breath", "steadfast", "generous-spring", "enemy-blood",
            "second-look", "instinct", "favor",
            "tenacious-hands", "ringing-coin", "ash-trail", "rune-sage",
        };

        static readonly string[] Names =
        {
            "Крепкая шкура", "Тяжёлая рука", "Лёгкий шаг", "Острый глаз", "Глубокий запас", "Быстрый кувырок",
            "Последний вдох", "Стойкость", "Щедрый родник", "Кровь врага",
            "Второй взгляд", "Чутьё", "Благосклонность",
            "Цепкие руки", "Звонкая монета", "Пепельный след", "Знаток рун",
        };

        /// <summary>
        /// Временные белые маски, пока нет своего семейства печатей (ui-common 6.2): так связь «клятва → строка листа героя»
        /// читается сразу. Имена файлов без расширения — CampInkParts.KitTexture ищет их в CampShops, RunIcons, Kit/Icons, Kit/Watercolor.
        /// </summary>
        static readonly string[] Placeholders =
        {
            "wc_stat_heart", "wc_stat_damage", "wc_stat_move_speed", "wc_stat_crit_chance", "wc_stat_lavidium", "wc_stat_cooldown",
            "death", "wc_stat_armor", "health", "encounter",
            "repeat", "ability", "talent",
            "cache", "gold", "rift", "salvage",
        };

        static bool Known(OathId id) => id != OathId.None && (int)id <= OathIds.Count;

        /// <summary>Номер печати 0..16 (OathId − 1); −1 у неизвестного.</summary>
        public static int IndexOf(OathId id) => Known(id) ? (int)id - 1 : -1;

        /// <summary>Клятва по номеру печати 0..16.</summary>
        public static OathId At(int index) => index >= 0 && index < OathIds.Count ? (OathId)(index + 1) : OathId.None;

        /// <summary>Имя файла окончательного значка: Resources/UI/OathIcons/&lt;это&gt;.png.</summary>
        public static string IconFile(OathId id) => Known(id) ? Files[(int)id - 1] : "";

        /// <summary>Временный знак до своих печатей (имя файла белой маски).</summary>
        public static string PlaceholderIcon(OathId id) => Known(id) ? Placeholders[(int)id - 1] : "";

        public static string NameKey(OathId id) => "tent.oath." + IconFile(id) + ".name";
        public static string NameRu(OathId id) => Known(id) ? Names[(int)id - 1] : "";

        public static string EffectKey(OathId id) => "tent.oath." + IconFile(id) + ".effect";

        /// <summary>Что делает клятва — одной фразой для карточки (у клятв героя — «за ступень»).</summary>
        public static string EffectRu(OathId id)
        {
            switch (id)
            {
                case OathId.ToughHide: return "+" + Simulation.ToughHideHealthPerRank + " к здоровью за каждую ступень.";
                case OathId.HeavyHand: return "+" + Simulation.HeavyHandPercentPerRank + "% к урону серии ударов за ступень. В листе героя не видно — работает в бою.";
                case OathId.LightStep: return "+" + Simulation.LightStepPercentPerRank + "% к скорости бега за ступень.";
                case OathId.KeenEye: return "+" + Simulation.KeenEyePercentPerRank + "% к шансу крита за ступень.";
                case OathId.DeepReserve: return "+" + Simulation.DeepReserveLavidiumPerRank + " к запасу концентрации за ступень.";
                case OathId.QuickRoll: return "Кувырок перезаряжается на " + Simulation.QuickRollPercentPerRank + "% быстрее за ступень. В листе героя не видно.";
                case OathId.LastBreath: return "Раз за забег смертельный удар не убивает: Пелаг встаёт с " + Simulation.LastBreathHealthPercent + "% здоровья.";
                case OathId.Steadfast: return "Урон от элит и боссов меньше на " + (100 - Simulation.SteadfastPercent) + "%.";
                case OathId.GenerousSpring: return "Родник и привал лечат на " + RunBoons.GenerousSpringBonusPercent + "% больше.";
                case OathId.EnemyBlood: return "Убитая элита лечит " + Simulation.EnemyBloodHealPercent + "% здоровья.";
                case OathId.SecondLook: return "Один бесплатный переброс награды за забег.";
                case OathId.Instinct: return "Первый выбор навыка в забеге — из четырёх.";
                case OathId.Favor: return "+5% к шансу эпического таланта.";
                case OathId.TenaciousHands: return "При смерти доезжает " + RunBoons.TenaciousHandsGoldPercent + "% золота вместо " + RunEconomy.DeathGoldKeptPercent + "%.";
                case OathId.RingingCoin: return "+" + (RunBoons.RingingCoinGoldPercent - 100) + "% золота в забеге.";
                case OathId.AshTrail: return "+" + (RunBoons.AshTrailPercent - 100) + "% пепла в забеге.";
                case OathId.RuneSage: return "+" + RunBoons.RuneSageBonusPercent + "% осколков при разборе у Эни.";
                default: return "";
            }
        }

        /// <summary>
        /// Чутьё и Благосклонность пока только покупаются — их ждёт блок лута (F8: «ждут блока лута»). Карточка говорит
        /// это прямо, чтобы пепел не уходил молча в клятву без действия.
        /// </summary>
        public static bool WaitsForLootBlock(OathId id) => id == OathId.Instinct || id == OathId.Favor;

        public const string LootBlockKey = "tent.oath.loot-block";
        public const string LootBlockRu = "Эффект появится вместе с блоком лута — пока не действует.";

        /// <summary>Сколько даёт клятва героя на ступени <paramref name="rank"/>: «+60 здоровья»; у прочих — пусто.</summary>
        public static string HeroValueRu(OathId id, int rank)
        {
            switch (id)
            {
                case OathId.ToughHide: return "+" + Simulation.ToughHideHealthPerRank * rank + " здоровья";
                case OathId.HeavyHand: return "+" + Simulation.HeavyHandPercentPerRank * rank + "% урона серии";
                case OathId.LightStep: return "+" + Simulation.LightStepPercentPerRank * rank + "% бега";
                case OathId.KeenEye: return "+" + Simulation.KeenEyePercentPerRank * rank + "% шанса крита";
                case OathId.DeepReserve: return "+" + Simulation.DeepReserveLavidiumPerRank * rank + " концентрации";
                case OathId.QuickRoll: return "−" + Simulation.QuickRollPercentPerRank * rank + "% перезарядки кувырка";
                default: return "";
            }
        }

        /// <summary>
        /// Строка «сейчас и дальше» под действием клятвы героя: «Сейчас +60 здоровья · дальше +90 здоровья»; до покупки —
        /// «Первая ступень: +30 здоровья», на потолке — «Сейчас +90 здоровья — последняя ступень». У клятв в одну ступень — пусто.
        /// </summary>
        public static string ProgressRu(OathId id, int rank)
        {
            int max = RunBoons.MaxRank(id);
            if (max <= 1) return "";
            if (rank <= 0) return "Первая ступень: " + HeroValueRu(id, 1);
            if (rank >= max) return "Сейчас " + HeroValueRu(id, rank) + " — последняя ступень";
            return "Сейчас " + HeroValueRu(id, rank) + " · дальше " + HeroValueRu(id, rank + 1);
        }

        // ---------------------------------------------------------------- состояние

        /// <summary>Как выглядит печать клятвы сейчас.</summary>
        public static OathLook LookOf(Camp camp, OathId id)
        {
            if (camp.OathActive(id)) return OathLook.Active;
            if (camp.OathRank(id) > 0) return OathLook.Owned;
            return camp.Money(CurrencyType.Ash) >= camp.NextOathPrice ? OathLook.NotBought : OathLook.Unaffordable;
        }

        /// <summary>Действие основной кнопки карточки (ui-tent §3): поклясться → укрепить → включить / выключить.</summary>
        public static OathAction ActionOf(Camp camp, OathId id)
        {
            int rank = camp.OathRank(id);
            if (rank <= 0) return OathAction.Swear;
            if (rank < RunBoons.MaxRank(id)) return OathAction.Strengthen;
            return camp.OathActive(id) ? OathAction.Disable : OathAction.Enable;
        }

        /// <summary>Покупка (поклясться или укрепить), а не переключение.</summary>
        public static bool IsPurchase(OathAction action) => action == OathAction.Swear || action == OathAction.Strengthen;

        /// <summary>
        /// Что сказать под кнопкой. Нехватка пепла и занятые слоты запрещают действие (<see cref="Blocks"/>); «ляжет в запас» —
        /// только предупреждение: Sim купит клятву, но включит её, лишь когда появится свободный слот.
        /// </summary>
        public static OathWarning WarningOf(Camp camp, OathId id)
        {
            OathAction action = ActionOf(camp, id);
            if (IsPurchase(action))
            {
                if (camp.Money(CurrencyType.Ash) < camp.NextOathPrice) return OathWarning.NotEnoughAsh;
                if (action == OathAction.Swear && camp.ActiveOathCount >= camp.OathSlots) return OathWarning.GoesToReserve;
                return OathWarning.None;
            }
            if (action == OathAction.Enable && camp.ActiveOathCount >= camp.OathSlots) return OathWarning.NoFreeSlot;
            return OathWarning.None;
        }

        /// <summary>Предупреждение запрещает нажатие (кнопка притухает).</summary>
        public static bool Blocks(OathWarning warning) => warning == OathWarning.NotEnoughAsh || warning == OathWarning.NoFreeSlot;

        public static string ActionKey(OathAction action) => "tent.oath.action." + (int)action;

        public static string ActionRu(OathAction action)
        {
            switch (action)
            {
                case OathAction.Swear: return "Поклясться";
                case OathAction.Strengthen: return "Укрепить";
                case OathAction.Enable: return "Включить";
                default: return "Выключить";
            }
        }

        public static string WarningKey(OathWarning warning) => "tent.oath.warning." + (int)warning;

        public static string WarningRu(OathWarning warning, int price, int ash)
        {
            switch (warning)
            {
                case OathWarning.NotEnoughAsh: return "Не хватает пепла: нужно " + price + ", есть " + ash + ".";
                case OathWarning.NoFreeSlot: return "Все слоты заняты — выключи клятву в ряду сверху.";
                case OathWarning.GoesToReserve: return "Слоты заняты — клятва ляжет в запас выключенной.";
                default: return "";
            }
        }

        /// <summary>Отказ Sim словами (как Explain меню F8): на случай, если состояние сменилось между показом и нажатием.</summary>
        public static string ResultRu(OathResult result)
        {
            switch (result)
            {
                case OathResult.NotEnoughAsh: return "Не хватает пепла.";
                case OathResult.NoFreeSlot: return "Все слоты заняты — выключи клятву в ряду сверху.";
                case OathResult.MaxRank: return "Клятва уже на последней ступени.";
                case OathResult.NotOwned: return "Сначала поклянись.";
                case OathResult.InvalidOath: return "Такой клятвы нет.";
                default: return "";
            }
        }

        /// <summary>
        /// Ряд слотов: включённые клятвы в порядке номеров — в том же порядке их берёт в забег Camp.CreateRunBoons, — не больше
        /// Camp.OathSlots. Пишет в <paramref name="into"/> (длина ≥ <see cref="SlotPlaces"/>), отдаёт, сколько слотов занято.
        /// </summary>
        public static int SlotOrder(Camp camp, OathId[] into)
        {
            int slots = camp.OathSlots, taken = 0;
            for (int id = 1; id <= OathIds.Count && taken < slots && taken < into.Length; id++)
                if (camp.OathActive((OathId)id)) into[taken++] = (OathId)id;
            for (int i = taken; i < into.Length; i++) into[i] = OathId.None;
            return taken;
        }

        // ---------------------------------------------------------------- атлас

        /// <summary>
        /// Артефакты идут на стол сборов после первой победы над любым боссом — то же правило, что закрытое
        /// Camp.ArtifactsJoinCarry (Camp.Preparation), собранное из открытых BossDefeated и RunBossKeys.
        /// </summary>
        public static bool ArtifactsCarryUnlocked(Camp camp)
        {
            for (int i = 0; i < RunBossKeys.Count; i++) if (camp.BossDefeated(RunBossKeys.At(i))) return true;
            return false;
        }
    }
}
