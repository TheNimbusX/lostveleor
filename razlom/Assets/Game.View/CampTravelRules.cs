using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Правила окна «Перед походом» (стол сборов, 06.10, концепт table-a в материале «Дым и свет») без Unity: что показать
    /// и куда встаёт выбор. Решения принимает Sim (Camp.Preparation, Camp.Potions); здесь только то, что окно обязано
    /// показать честно, — поэтому правила проверяются тестами на настоящем Camp (CampTravelRulesTests).
    /// </summary>
    public static class CampTravelRules
    {
        /// <summary>Медальонов умений: 1 из 3 предложений стола.</summary>
        public const int SkillSlots = 3;
        /// <summary>«Другие зелья»: все виды, кроме двух выбранных.</summary>
        public const int OtherPotionSlots = Camp.PotionKindCount - 2;
        /// <summary>Вариантов «с собой»: стол всегда предлагает три, пока открыт.</summary>
        public const int CarrySlots = 3;

        /// <summary>
        /// Сдвиг медальона <paramref name="index"/> от середины колонки, в шагах: умений бывает 1–2 (взятых меньше трёх) —
        /// тогда они стоят по центру, а не прижаты влево с пустыми местами.
        /// </summary>
        public static float SkillOffset(int index, int count) => index - (count - 1) * .5f;

        /// <summary>
        /// В какую ячейку встаёт зелье по клику в ряду «Другие зелья»: в ячейку своей семьи (здоровье — 0, концентрация — 1),
        /// чтобы клик не оставил героя с двумя бутылками здоровья; у зелий без семьи (Смешанный отвар, Ясный настой) — в
        /// активную, последнюю нажатую.
        /// </summary>
        public static int PotionTarget(PotionKind kind, int activeSlot)
        {
            int family = Camp.PotionSlot(kind);
            return family >= 0 ? family : activeSlot == 1 ? 1 : 0;
        }

        /// <summary>
        /// Остальные виды зелий по порядку PotionKind, кроме двух выбранных: закрытые рецепты тоже, окно показывает их
        /// тускло с рангом Лео. Отдаёт число записанных (6, пока выбранные разные).
        /// </summary>
        public static int OtherPotions(PotionKind first, PotionKind second, PotionKind[] into)
        {
            int count = 0;
            for (int i = 0; i < Camp.PotionKindCount && into != null && count < into.Length; i++)
            {
                var kind = (PotionKind)i;
                if (kind != first && kind != second) into[count++] = kind;
            }
            return count;
        }

        /// <summary>Ранг лагеря, на котором Лео открывает рецепт (как Camp.PotionUnlocked); малые открыты всегда — 0.</summary>
        public static int PotionRank(PotionKind kind)
            => kind == PotionKind.SmallHealth || kind == PotionKind.SmallLavidium ? 0
                : kind == PotionKind.LargeHealth || kind == PotionKind.LargeLavidium ? 1
                : kind == PotionKind.LivingResin || kind == PotionKind.LavidiumSurge ? 2 : 3;

        /// <summary>
        /// Файл бутылки в Resources/UI/Items — то же правило, что у HUD (CombatHudView.PotionArt): здоровье или
        /// концентрация, малая или большая; у зелий без своей бутылки (Живица, Порыв, Смешанный, Ясный) — большая.
        /// </summary>
        public static string PotionArtFile(PotionKind kind)
        {
            bool health = Camp.PotionSlot(kind) == 0;
            bool large = kind == PotionKind.LargeHealth || kind == PotionKind.LargeLavidium || (int)kind >= 4;
            return "potion_" + (health ? "health" : "lavidium") + (large ? "_large" : "_small");
        }

        /// <summary>
        /// «Отправиться» активна: в ячейке «с собой» что-то лежит и зелья разные. Пустая ячейка — только первый подход
        /// (дальше прежний выбор переносится на первое предложение); правило окна, Sim уйти не мешает.
        /// </summary>
        public static bool CanDepart(Camp camp)
            => camp != null && camp.PreparedCarry.Kind != CarryKind.None && camp.SelectedPotion(0) != camp.SelectedPotion(1);

        /// <summary>
        /// Артефакты уже идут в «с собой»: побеждён хоть один босс. Повтор приватного Camp.ArtifactsJoinCarry — подпись
        /// колонки и атлас («можно взять на столе») должны говорить то же, что делает Sim.
        /// </summary>
        public static bool ArtifactsJoinCarry(Camp camp)
        {
            if (camp == null) return false;
            for (int i = 0; i < RunBossKeys.Count; i++) if (camp.BossDefeated(RunBossKeys.At(i))) return true;
            return false;
        }

        /// <summary>Что показать в большой ячейке «с собой»: наведённый вариант (подсмотреть), иначе выбранное.</summary>
        public static CarryChoice ShownCarry(Camp camp, int hoveredOffer)
        {
            if (camp == null) return default;
            if (hoveredOffer >= 0 && hoveredOffer < camp.CarryOfferCount) return camp.CarryOfferAt(hoveredOffer);
            return camp.PreparedCarry;
        }

        /// <summary>Индекс пула умения, чьё описание стоит под рядом: наведённое, иначе выбранное.</summary>
        public static int DescribedSkill(Camp camp, int hoveredOffer)
        {
            if (camp == null) return -1;
            if (hoveredOffer >= 0 && hoveredOffer < camp.SkillOfferCount) return camp.SkillOfferAt(hoveredOffer);
            return camp.PreparedStarterPoolIndex;
        }

        /// <summary>Зелье, чьё действие стоит под рядом: наведённое (номер PotionKind), иначе зелье активной ячейки.</summary>
        public static PotionKind DescribedPotion(Camp camp, int activeSlot, int hoveredKind)
        {
            if (hoveredKind >= 0 && hoveredKind < Camp.PotionKindCount) return (PotionKind)hoveredKind;
            return camp != null ? camp.SelectedPotion(activeSlot == 1 ? 1 : 0) : PotionKind.SmallHealth;
        }

        /// <summary>
        /// Имя файла значка дара: Resources/UI/GiftIcons/&lt;ключ&gt;.png. Положат свой рисунок — окно возьмёт его без
        /// правки кода; до тех пор — белый знак забега (<see cref="GiftPlaceholder"/>).
        /// Через дефис, как значки клятв (OathIcons/tough-hide): рисунки 06.10 легли в GiftIcons именно так (dry-ration.png),
        /// а ключ с подчёркиванием их не находил — окно молча показывало белые знаки.
        /// </summary>
        public static string GiftKey(CampGift gift)
        {
            switch (gift)
            {
                case CampGift.DryRation: return "dry-ration";
                case CampGift.EniWhetstone: return "eni-whetstone";
                case CampGift.LightPack: return "light-pack";
                case CampGift.SeaKnot: return "sea-knot";
                case CampGift.BackupPlan: return "backup-plan";
                case CampGift.SpareFlask: return "spare-flask";
                default: return null;
            }
        }

        /// <summary>
        /// Временный белый знак дара из Assets/UI/RunIcons (своих значков нет, ui-table): паёк — здоровье, точило —
        /// победа, поклажа — сумка, узел — встреча, резервный план — повтор, фляга — алхимик.
        /// </summary>
        public static string GiftPlaceholder(CampGift gift)
        {
            switch (gift)
            {
                case CampGift.DryRation: return "health";
                case CampGift.EniWhetstone: return "victory";
                case CampGift.LightPack: return "items";
                case CampGift.SeaKnot: return "encounter";
                case CampGift.BackupPlan: return "repeat";
                case CampGift.SpareFlask: return "alchemist";
                default: return null;
            }
        }
    }
}
