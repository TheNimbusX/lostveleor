using System;
using System.Collections.Generic;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Role = Game.View.UiTheme.Role;

namespace Game.View
{
    /// <summary>
    /// Медальон окна «Перед походом» — ссылки, которые собирает CampTravelWcBuilder (копия CampInkParts.IconMedallion):
    /// тёмный диск, рисунок под круглой маской, тихое кольцо покоя, огненное кольцо «выбрано», тонкая тлеющая кромка
    /// наведения (её ведёт UiHoverMotion сам), замок у закрытого рецепта, подписи под медальоном.
    /// </summary>
    [Serializable]
    public sealed class CampTravelMedal
    {
        /// <summary>Узел медальона: размер — диаметр, опора — центр (окно двигает его, когда умений меньше трёх).</summary>
        public RectTransform Root;
        /// <summary>Кнопка на весь медальон; null у большой ячейки «с собой» — она только показывает.</summary>
        public Button Button;
        /// <summary>Наведение мыши → описание под рядом; null у некликабельного.</summary>
        public CampHoverRelay Hover;
        /// <summary>Рисунок под маской: расписной во весь круг, бутылка с полями или белый знак с краской темы.</summary>
        public RawImage Art;
        /// <summary>Тонкое кольцо покоя (ThemeColor): у артефакта — краска уникальной вещи.</summary>
        public Image Ring;
        /// <summary>Огненное кольцо light_ring «выбрано»; узел выключен, сила — альфа цвета.</summary>
        public Image Fire;
        /// <summary>Мягкий свет семьи зелья внутри диска (только у зелий): у четырёх зелий без своей бутылки — единственное отличие рисунка.</summary>
        public Image Glow;
        /// <summary>Закрытый рецепт: тень, замок; null без замка.</summary>
        public GameObject Locked;
        /// <summary>Имя под медальоном; null, если имени нет (малые зелья).</summary>
        public TMP_Text Name;
        /// <summary>Строка под именем: «× 3», «Лео · ранг 2»; null, если её нет.</summary>
        public TMP_Text Note;
    }

    /// <summary>
    /// Окно «Перед походом» у стола сборов (06.10, концепт table-a в материале «Дым и свет», три колонки на дыме без рамок):
    /// умение — 1 из 3 предложений стола, два разных зелья и ряд «Другие зелья», ячейка «Возьми с собой» с тремя
    /// вариантами, кнопки «Отправиться [E]» и «Остаться [Esc]». Префаб — Resources/UI/Prefabs/CampTravelWc.prefab
    /// (CampTravelWcBuilder); здесь — ссылки и показ состояния лагеря (<see cref="Show"/>). Выбор, клавиши и уход в
    /// забег ведёт CampPreparationView: решения принимает Sim, окно только честно показывает их. Тот же Show рисует и кадр
    /// сборщика без Play — кадр не врёт про игру.
    /// </summary>
    public sealed class CampTravelPanel : MonoBehaviour
    {
        /// <summary>Версия раскладки префаба (CampTravelWcBuilder.LayoutVersion): миграции доводят готовый префаб поверх ручных правок.</summary>
        [HideInInspector] public int LayoutVersion;

        public CanvasGroup Group;
        public TMP_Text Title;

        [Header("Умение")]
        public TMP_Text SkillHeader;
        public CampTravelMedal[] Skills;
        [Tooltip("Середина ряда умений, шаг и высота центра — единицы раскладки 1920×1080 от левого верхнего угла")]
        public float SkillCenterX = 360f, SkillStep = 200f, SkillY = 419f;
        public TMP_Text SkillBody, SkillRule;

        [Header("Зелья")]
        public TMP_Text PotionHeader;
        public CampTravelMedal[] PotionSlots;
        public TMP_Text OthersCaption;
        public CampTravelMedal[] OtherPotions;
        public TMP_Text PotionName, PotionBody;

        [Header("Возьми с собой")]
        public TMP_Text CarryHeader;
        public CampTravelMedal CarryMain;
        public TMP_Text CarryName, CarryEffect, CarryUse;
        public TMP_Text OffersHeader;
        public CampTravelMedal[] CarryOffers;
        public TMP_Text CarryRule;

        [Header("Кнопки")]
        public Button Depart, Stay;
        public TMP_Text DepartLabel, DepartKey, StayLabel, CloseLabel;

        [Header("Временные знаки")]
        [Tooltip("Белые знаки даров по номеру CampGift (Assets/UI/RunIcons лежит вне Resources — ссылки в префабе). " +
                 "Свой рисунок дара из Resources/UI/GiftIcons/<ключ>.png берётся первым")]
        public Texture[] GiftSigns;
        [Tooltip("Пустая ячейка «с собой» — белый знак сумки")]
        public Texture EmptyCarrySign;

        /// <summary>Виды зелий ряда «Другие зелья» на последнем показе (клик по i-му медальону — Others[i]).</summary>
        [NonSerialized] public readonly PotionKind[] Others = new PotionKind[CampTravelRules.OtherPotionSlots];
        [NonSerialized] public int OtherCount;

        /// <summary>Как лежит рисунок в медальоне.</summary>
        enum ArtFit
        {
            /// <summary>Расписной круг во весь диск (умения, артефакты, свой рисунок дара).</summary>
            Painted,
            /// <summary>Бутылка с полями: рисунок вещи не круглый, край маски не режет пробку.</summary>
            Item,
            /// <summary>Белый знак с полями и кремовой краской темы (временные знаки даров, пустая ячейка).</summary>
            WhiteMask,
        }

        static readonly Dictionary<PotionKind, Texture2D> PotionArt = new Dictionary<PotionKind, Texture2D>();
        static readonly Dictionary<CampGift, Texture2D> GiftArt = new Dictionary<CampGift, Texture2D>();
        static readonly Dictionary<RunArtifact, Texture2D> ArtifactArt = new Dictionary<RunArtifact, Texture2D>();

        /// <summary>
        /// Показать состояние лагеря. <paramref name="activeSlot"/> — ячейка зелья, куда встанут зелья без семьи и чьё
        /// действие описано; hover* — наведённое умение (индекс предложения), зелье (номер PotionKind) и вариант «с собой»
        /// (индекс предложения), −1 — ничего.
        /// </summary>
        public void Show(Camp camp, int activeSlot, int hoverSkill, int hoverPotion, int hoverCarry)
        {
            if (camp == null) return;
            ShowTexts();
            ShowSkills(camp, hoverSkill);
            ShowPotions(camp, activeSlot, hoverPotion);
            ShowCarry(camp, hoverCarry);
            if (Depart != null)
            {
                Depart.interactable = CampTravelRules.CanDepart(camp);
                // Кадр сборщика идёт без Update: состояние кнопки — сразу.
                if (Depart.TryGetComponent(out ThemeStates states)) states.Apply();
            }
            // Клавиша — из назначений игрока (Пауза → Управление), по умолчанию E.
            if (DepartKey != null) UiKeyHint.SetKeycap(DepartKey, GameKeyBindings.Label(GameAction.EnterRift));
        }

        void ShowTexts()
        {
            Set(Title, CampWindowText.Get("table.title", "Перед походом"));
            Set(SkillHeader, CampWindowText.Get("table.skill.title", "Умение"));
            Set(SkillRule, CampWindowText.Get("table.skill.rule", "Одно из умений прошлых походов · без талантов"));
            Set(PotionHeader, CampWindowText.Get("table.potions.title", "Зелья"));
            Set(OthersCaption, CampWindowText.Get("table.potions.others", "Другие зелья (можно заменить)"));
            Set(CarryHeader, CampWindowText.Get("table.carry.title", "Возьми с собой"));
            Set(OffersHeader, CampWindowText.Get("table.carry.offers", "Варианты на выбор"));
            Set(DepartLabel, CampWindowText.Get("table.depart", "Отправиться"));
            Set(StayLabel, CampWindowText.Get("table.stay", "Остаться"));
            Set(CloseLabel, UiKeyHint.Verb(CampWindowText.Get("close.action", "Закрыть")));
        }

        // ---------------------------------------------------------------- умение

        void ShowSkills(Camp camp, int hoverSkill)
        {
            int count = camp.SkillOfferCount;
            int chosen = camp.PreparedStarterPoolIndex;
            for (int i = 0; Skills != null && i < Skills.Length; i++)
            {
                CampTravelMedal medal = Skills[i];
                if (medal?.Root == null) continue;
                int pool = i < count ? camp.SkillOfferAt(i) : -1;
                medal.Root.gameObject.SetActive(pool >= 0);
                if (pool < 0) continue;
                int id = PelagKit.PoolDefinition(pool).Id;
                // Умений меньше трёх (взятых мало) — ряд по центру колонки, а не прижат влево с пустыми местами.
                medal.Root.anchoredPosition = new Vector2(SkillCenterX + CampTravelRules.SkillOffset(i, count) * SkillStep, -SkillY);
                SetArt(medal, AbilityIcons.Get(id), ArtFit.Painted);
                Set(medal.Name, Capitalized(PlayerHud.AbilityName(id)));
                SetFire(medal, pool == chosen ? 1f : 0f);
            }
            // Описание одно на колонку (у Броска якоря оно в две фразы — под медальоном 170 px не влезет): наведённое, иначе выбранное.
            int described = CampTravelRules.DescribedSkill(camp, hoverSkill);
            Set(SkillBody, described >= 0 ? PlayerHud.AbilityDescription(PelagKit.PoolDefinition(described).Id) : string.Empty);
        }

        // ---------------------------------------------------------------- зелья

        void ShowPotions(Camp camp, int activeSlot, int hoverPotion)
        {
            for (int slot = 0; PotionSlots != null && slot < PotionSlots.Length && slot < 2; slot++)
            {
                CampTravelMedal medal = PotionSlots[slot];
                if (medal?.Root == null) continue;
                PotionKind kind = camp.SelectedPotion(slot);
                SetArt(medal, Potion(kind), ArtFit.Item);
                SetGlow(medal, kind);
                Set(medal.Name, CampFeatureText.PotionName(kind));
                int stock = camp.PotionCount(kind);
                // Пустой запас выбрать можно, но в забеге зелье не пьётся (CanUsePotionSlot) — сказать честно и куда идти.
                Set(medal.Note, stock > 0 ? CampWindowText.Format("table.potion.stock", "× {0}", stock)
                    : CampWindowText.Get("table.potion.none", "× 0 · купить у Лео"));
                SetRole(medal.Note, stock > 0 ? Role.TextMuted : Role.Bad);
                // Оба зелья взяты; активная ячейка (куда встанут зелья без семьи, чьё действие описано) горит ярче.
                SetFire(medal, slot == activeSlot ? 1f : .4f);
            }

            OtherCount = CampTravelRules.OtherPotions(camp.SelectedPotion(0), camp.SelectedPotion(1), Others);
            for (int i = 0; OtherPotions != null && i < OtherPotions.Length; i++)
            {
                CampTravelMedal medal = OtherPotions[i];
                if (medal?.Root == null) continue;
                bool used = i < OtherCount;
                medal.Root.gameObject.SetActive(used);
                if (!used) continue;
                PotionKind kind = Others[i];
                bool open = camp.PotionUnlocked(kind);
                SetArt(medal, Potion(kind), ArtFit.Item);
                SetGlow(medal, kind);
                if (medal.Locked != null) medal.Locked.SetActive(!open);
                // Закрытый рецепт не встаёт в ячейку и не щёлкает; наведение по-прежнему показывает, когда его откроет Лео.
                if (medal.Button != null) medal.Button.interactable = open;
                int stock = camp.PotionCount(kind);
                Set(medal.Note, open ? CampWindowText.Format("table.potion.stock", "× {0}", stock)
                    : CampWindowText.Format("table.potion.locked", "Лео · ранг {0}", CampTravelRules.PotionRank(kind)));
                SetRole(medal.Note, open && stock == 0 ? Role.Bad : Role.TextMuted);
                SetFire(medal, 0f);
            }

            PotionKind described = CampTravelRules.DescribedPotion(camp, activeSlot, hoverPotion);
            Set(PotionName, CampFeatureText.PotionName(described));
            string effect = CampFeatureText.PotionEffect(described);
            if (!camp.PotionUnlocked(described))
                effect += "\n" + CampWindowText.Format("table.potion.locked.body", "Рецепт откроет Лео на ранге {0}.", CampTravelRules.PotionRank(described));
            Set(PotionBody, effect);
        }

        /// <summary>
        /// Свет семьи зелья внутри диска: у Живицы, Порыва, Смешанного и Ясного своей бутылки нет (как и в HUD) —
        /// различает их цвет свечения и имя.
        /// </summary>
        static Role GlowRole(PotionKind kind)
        {
            switch (kind)
            {
                case PotionKind.SmallHealth: case PotionKind.LargeHealth: return Role.Health;
                case PotionKind.LivingResin: return Role.Good;
                case PotionKind.SmallLavidium: case PotionKind.LargeLavidium: return Role.Lavidium;
                case PotionKind.LavidiumSurge: return Role.Experience;
                case PotionKind.Mixed: return Role.Epic;
                default: return Role.Text;
            }
        }

        static void SetGlow(CampTravelMedal medal, PotionKind kind)
        {
            if (medal.Glow == null) return;
            Color color = UiTheme.Current.Get(GlowRole(kind));
            color.a = .35f;
            medal.Glow.color = color;
        }

        static Texture2D Potion(PotionKind kind)
        {
            if (PotionArt.TryGetValue(kind, out Texture2D cached) && cached != null) return cached;
            Texture2D texture = Resources.Load<Texture2D>("UI/Items/" + CampTravelRules.PotionArtFile(kind));
            PotionArt[kind] = texture;
            return texture;
        }

        // ---------------------------------------------------------------- с собой

        void ShowCarry(Camp camp, int hoverCarry)
        {
            CarryChoice chosen = camp.PreparedCarry;
            int count = camp.CarryOfferCount;
            for (int i = 0; CarryOffers != null && i < CarryOffers.Length; i++)
            {
                CampTravelMedal medal = CarryOffers[i];
                if (medal?.Root == null) continue;
                CarryChoice offer = camp.CarryOfferAt(i);
                bool used = i < count && offer.Kind != CarryKind.None;
                medal.Root.gameObject.SetActive(used);
                if (!used) continue;
                ShowCarryArt(medal, offer);
                // Под малыми — только имена: действие артефакта бывает в 200 знаков, оно — в большой ячейке.
                Set(medal.Name, OfferName(offer));
                SetFire(medal, offer.SameAs(in chosen) ? 1f : 0f);
            }

            CarryChoice shown = CampTravelRules.ShownCarry(camp, hoverCarry);
            if (CarryMain?.Root != null)
            {
                if (shown.Kind == CarryKind.None)
                {
                    SetArt(CarryMain, EmptyCarrySign, ArtFit.WhiteMask);
                    SetRing(CarryMain, false);
                }
                else ShowCarryArt(CarryMain, shown);
                // Огонь — только у взятого; подсмотренный наведением вариант горит лишь кромкой своего медальона.
                SetFire(CarryMain, shown.Kind != CarryKind.None && shown.SameAs(in chosen) ? 1f : 0f);
            }
            if (shown.Kind == CarryKind.None)
            {
                Set(CarryName, CampWindowText.Get("table.carry.empty", "Выбери, что взять с собой"));
                Set(CarryEffect, CampWindowText.Get("table.carry.empty.body", "Одна вещь на этот поход — без неё в путь не выйти."));
                Set(CarryUse, string.Empty);
            }
            else if (shown.Kind == CarryKind.Artifact)
            {
                Set(CarryName, RunArtifactTexts.Name(shown.Artifact));
                Set(CarryEffect, RunArtifactTexts.Effect(shown.Artifact));
                Set(CarryUse, RunArtifactTexts.Use(shown.Artifact));
            }
            else
            {
                Set(CarryName, CampFeatureText.GiftName(shown.Gift));
                Set(CarryEffect, CampFeatureText.GiftEffect(shown.Gift));
                Set(CarryUse, CampWindowText.Get("table.carry.gift.use", "Дар · только на этот поход"));
            }
            // Пока ячейка пуста, «Отправиться» неактивна — заголовок колонки загорается акцентом: вот чего не хватает.
            SetRole(CarryHeader, chosen.Kind == CarryKind.None ? Role.Accent : Role.Text);
            Set(CarryRule, CampTravelRules.ArtifactsJoinCarry(camp)
                ? CampWindowText.Get("table.carry.rule.artifacts", "Дар или открытый артефакт — только на этот поход.")
                : CampWindowText.Get("table.carry.rule.gifts", "Дар действует только в этом походе. После первого босса здесь появятся и открытые артефакты."));
        }

        static string OfferName(in CarryChoice c)
            => c.Kind == CarryKind.Artifact ? RunArtifactTexts.Name(c.Artifact) : CampFeatureText.GiftName(c.Gift);

        void ShowCarryArt(CampTravelMedal medal, in CarryChoice c)
        {
            if (c.Kind == CarryKind.Artifact)
            {
                SetArt(medal, Artifact(c.Artifact), ArtFit.Painted);
                SetRing(medal, true);
                return;
            }
            SetRing(medal, false);
            // Свой знак дара (Resources/UI/GiftIcons), пока его нет — белый знак забега из префаба. Знаки даров 06.10 —
            // белые силуэты, как у клятв и валют: им нужны поля и кремовая краска темы. Как «расписной» они растягивались
            // белым пятном во весь круг. Если однажды положат расписные круги — здесь вернуть ArtFit.Painted.
            Texture2D own = Gift(c.Gift);
            int index = (int)c.Gift;
            Texture sign = own != null ? own : GiftSigns != null && index >= 0 && index < GiftSigns.Length ? GiftSigns[index] : null;
            SetArt(medal, sign, ArtFit.WhiteMask);
        }

        static Texture2D Gift(CampGift gift)
        {
            // null тоже запоминается: значков даров пока нет, Resources не ищется на каждое наведение.
            if (GiftArt.TryGetValue(gift, out Texture2D cached)) return cached;
            string key = CampTravelRules.GiftKey(gift);
            Texture2D texture = key != null ? Resources.Load<Texture2D>("UI/GiftIcons/" + key) : null;
            GiftArt[gift] = texture;
            return texture;
        }

        static Texture2D Artifact(RunArtifact artifact)
        {
            if (ArtifactArt.TryGetValue(artifact, out Texture2D cached) && cached != null) return cached;
            Texture2D texture = RunArtifactTexts.Icon(artifact);
            ArtifactArt[artifact] = texture;
            return texture;
        }

        /// <summary>Кольцо покоя: у артефакта — краска уникальной вещи (как в атласе и награде), у остального — тихое кремовое.</summary>
        static void SetRing(CampTravelMedal medal, bool artifact)
        {
            var tint = medal.Ring != null ? medal.Ring.GetComponent<ThemeColor>() : null;
            if (tint != null) tint.SetRole(artifact ? Role.Unique : Role.Text, artifact ? .7f : .45f);
        }

        // ---------------------------------------------------------------- общее

        static void SetArt(CampTravelMedal medal, Texture texture, ArtFit fit)
        {
            if (medal.Art == null) return;
            // Пустая RawImage рисует белый квадрат — без картинки рисунок выключен.
            medal.Art.texture = texture;
            medal.Art.enabled = texture != null;
            float size = medal.Root != null ? medal.Root.sizeDelta.x : 100f;
            float pad = fit == ArtFit.WhiteMask ? size * .18f : fit == ArtFit.Item ? size * .1f : 0f;
            RectTransform rect = medal.Art.rectTransform;
            rect.offsetMin = new Vector2(pad, pad);
            rect.offsetMax = new Vector2(-pad, -pad);
            // Краска белого знака — из темы (знаки забега белые силуэты); расписной рисунок — как есть.
            medal.Art.color = fit == ArtFit.WhiteMask ? UiTheme.Current.Get(Role.Text) : Color.white;
        }

        static void SetFire(CampTravelMedal medal, float strength)
        {
            if (medal.Fire == null) return;
            medal.Fire.gameObject.SetActive(strength > 0f);
            medal.Fire.color = new Color(1f, 1f, 1f, strength);
        }

        static void Set(TMP_Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        /// <summary>Краска надписи через её ThemeColor (иначе следующая правка темы затёрла бы цвет).</summary>
        static void SetRole(TMP_Text label, Role role)
        {
            if (label == null) return;
            var tint = label.GetComponent<ThemeColor>();
            if (tint != null) { if (tint.Role != role) tint.SetRole(role); }
            else label.color = UiTheme.Current.Get(role);
        }

        /// <summary>Имя умения из подсказки HUD прописными — в окне с заглавной («Рассекающий удар»), как на экранах забега.</summary>
        static string Capitalized(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string lower = text.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }
    }
}
