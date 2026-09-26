using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Экраны забега на Canvas: префаб Resources/UI/Prefabs/RunHudWc (материал «Дым и свет»,
    /// 25–26 сентября 2026) — выбор награды и следующей арены, замена способности,
    /// состояние забега, полоса босса и итог забега.
    ///
    /// ВИД — В ПРЕФАБЕ, СМЫСЛ — В <see cref="RunHud"/>: он кладёт тексты и иконки,
    /// включает части и получает клики. Подписи в мире и меню над добычей остаются
    /// в RunHud (IMGUI) — они привязаны к точкам сцены.
    /// </summary>
    public sealed class RunHudView : MonoBehaviour
    {
        [Header("Выбор награды")]
        public CanvasGroup Choice;
        public TMP_Text ChoiceTitle;
        public TMP_Text ChoiceSubtitle;
        public TMP_Text ChoiceHint;
        public RunOfferCard[] Offers = new RunOfferCard[3];
        [Tooltip("Значки вида награды: способность, талант, предмет, характеристика")] public Texture[] KindIcons = new Texture[4];
        [Tooltip("Белый знак родника (RunIcons/health): картинка и знак вида карточки лечения; краску " +
                 "здоровья даёт RunHud. Пусто (префаб до пересборки) — карточка без картинки")]
        public Texture2D SpringIcon;

        [Header("Выбор арены (тот же экран и карточки)")]
        [Tooltip("Знаки пути на карточке: улучшение, магазин, опасная арена. Белые знаки — краску даёт RunHud; " +
                 "пусто (префаб до пересборки) — берутся значки вида награды")]
        public Texture[] RouteIcons = new Texture[3];

        [Header("Артефакт (награда босса)")]
        [Tooltip("«Отказаться» — показывается только на выборе артефакта")] public Button Skip;
        [Tooltip("Окно «Заменить артефакт?»: старый → новый")] public CanvasGroup ArtifactReplace;
        public RawImage ArtifactOld;
        public RawImage ArtifactNew;
        public TMP_Text ArtifactReplaceText;
        public Button ArtifactConfirm;
        public Button ArtifactKeep;

        [Header("Замена способности")]
        public CanvasGroup Replace;
        public TMP_Text ReplaceTitle;
        public TMP_Text ReplaceSubtitle;
        public TMP_Text ReplaceHint;
        public RunSlotTile[] Slots = new RunSlotTile[4];
        public Button Salvage;
        public TMP_Text SalvageLabel;

        [Header("Состояние забега")]
        public RectTransform Status;
        public TMP_Text StatusTitle;
        public TMP_Text StatusLine;
        public TMP_Text StatusExtra;

        [Header("Выживание")]
        [Tooltip("Таймер выживания под панелью состояния («Выстоять 0:42»); пусто (префаб до пересборки) — " +
                 "время пишется в строку панели состояния")]
        public RectTransform Survival;
        public TMP_Text SurvivalLabel;

        [Header("Босс")]
        public RectTransform Boss;
        public TMP_Text BossName;
        public WcBar BossBar;

        [Header("Итог забега")]
        public CanvasGroup Summary;
        [Tooltip("Крупный значок исхода над заголовком")] public RawImage OutcomeIcon;
        [Tooltip("Значки исхода: гибель, победа, ушёл с добычей")] public Texture[] OutcomeIcons = new Texture[3];
        [Tooltip("Мягкое свечение за значком исхода: его цвет, как и значка, ставит RunHud")] public Graphic OutcomeHalo;
        public TMP_Text SummaryTitle;
        public TMP_Text SummarySubtitle;
        [Tooltip("Числа в плашках: разломы, глубина, предметы, золото")] public TMP_Text[] SummaryValues = new TMP_Text[4];
        public TMP_Text SummaryLoss;
        // Строки «В лагере ждёт» убраны (владелец 26 сентября); ссылки остались только ради префаба
        // до пересборки — вид гасит эти строки в Awake. Новый префаб их не строит.
        [HideInInspector] public TMP_Text SummaryCampTitle;
        [HideInInspector] public TMP_Text SummaryCamp;
        public Button Repeat;
        public TMP_Text RepeatLabel;
        public Button ToCamp;
        public TMP_Text ToCampLabel;

        [Header("Движение")]
        public float FadeDuration = .2f;

        public event Action<int> OfferClicked;
        public event Action<int> SlotClicked;
        public event Action SalvageClicked;
        public event Action RepeatClicked;
        public event Action CampClicked;
        public event Action SkipClicked;
        public event Action ArtifactConfirmClicked;
        public event Action ArtifactKeepClicked;

        void Awake()
        {
            for (int i = 0; i < Offers.Length; i++)
            {
                int index = i;
                if (Offers[i] != null && Offers[i].Button != null) Offers[i].Button.onClick.AddListener(() => OfferClicked?.Invoke(index));
            }
            for (int i = 0; i < Slots.Length; i++)
            {
                int index = i;
                if (Slots[i] != null && Slots[i].Button != null) Slots[i].Button.onClick.AddListener(() => SlotClicked?.Invoke(index));
            }
            if (Salvage != null) Salvage.onClick.AddListener(() => SalvageClicked?.Invoke());
            if (Repeat != null) Repeat.onClick.AddListener(() => RepeatClicked?.Invoke());
            if (ToCamp != null) ToCamp.onClick.AddListener(() => CampClicked?.Invoke());
            if (Skip != null) Skip.onClick.AddListener(() => SkipClicked?.Invoke());
            if (ArtifactConfirm != null) ArtifactConfirm.onClick.AddListener(() => ArtifactConfirmClicked?.Invoke());
            if (ArtifactKeep != null) ArtifactKeep.onClick.AddListener(() => ArtifactKeepClicked?.Invoke());
            SetShown(ArtifactReplace, false, true);
            SetShown(Summary, false, true);
            SetActive(SummaryCampTitle, false);
            SetActive(SummaryCamp, false);
            if (GetComponent<CanvasScaler>() != null && GetComponent<UiScaleFollower>() == null) gameObject.AddComponent<UiScaleFollower>();
            PauseMenuView.EnsureEventSystem();
            SetShown(Choice, false, true);
            SetShown(Replace, false, true);
        }

        /// <summary>Показ или скрытие экрана с проявлением; <paramref name="instant"/> — сразу.</summary>
        public void SetShown(CanvasGroup group, bool shown, bool instant = false)
        {
            if (group == null) return;
            bool active = group.gameObject.activeSelf;
            if (shown == active && (!shown || group.alpha > .99f)) return;
            UiMotion.Stop(group);
            group.interactable = group.blocksRaycasts = shown;
            if (instant) { group.gameObject.SetActive(shown); group.alpha = shown ? 1f : 0f; return; }
            if (shown)
            {
                if (!active) { group.gameObject.SetActive(true); group.alpha = 0f; }
                UiMotion.FadeTo(group, 1f, FadeDuration);
            }
            else UiMotion.FadeTo(group, 0f, FadeDuration * .7f, () => { if (group != null) group.gameObject.SetActive(false); });
        }

        public static void SetActive(Component part, bool active)
        {
            if (part != null && part.gameObject.activeSelf != active) part.gameObject.SetActive(active);
        }

        public static void SetText(TMP_Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        /// <summary>
        /// Буква клавиши «Дыма и света» (UiInkKit.Keycap: «Буква» внутри узла с «Кольцом»): одна буква —
        /// круг, длинная подпись после смены клавиш («Space», «Mouse4») — капсула по ширине текста.
        /// Кольцо — капсула 9-slice, растягивается без овала. У клавиши пака (префаб до пересборки) — только текст.
        /// </summary>
        public static void SetKey(TMP_Text label, string text)
        {
            if (label == null) return;
            SetText(label, text);
            if (!(label.transform.parent is RectTransform cap) || cap.Find("Кольцо") == null) return;
            float height = cap.sizeDelta.y;
            float width = string.IsNullOrEmpty(text) || text.Length < 2 ? height
                : Mathf.Max(height, label.GetPreferredValues(text).x + height * .5f);
            if (!Mathf.Approximately(cap.sizeDelta.x, width)) cap.sizeDelta = new Vector2(width, height);
        }
    }
}
