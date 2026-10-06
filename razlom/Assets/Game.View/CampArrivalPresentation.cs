using System.Collections.Generic;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Одно спокойное представление каждого нового открытия лагеря: прибытие Вена и Лео, походный стол, новый ранг.
    /// Что ещё не показано, хранит сам лагерь (Camp.PendingUnlocks, в сохранении): открытие, случившееся
    /// в Разломе или перед выходом из игры, объявится при следующем возвращении, а увиденное — никогда.
    /// Уведомление ждёт возвращения управления, не открывает окна и не двигает камеру.
    /// </summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Представление прибытий")]
    public sealed class CampArrivalPresentation : MonoBehaviour
    {
        public static CampArrivalPresentation Instance { get; private set; }
        [Min(0)] public float SettleTime = .7f;
        [Min(.5f)] public float LabelLife = 3.4f;
        /// <summary>Что объявится следующим; Smith — новый ранг лагеря (подпись над Эни).</summary>
        public CampServiceKind? PendingKind => _pending;

        const CampUnlock RankFlags = CampUnlock.Rank1 | CampUnlock.Rank2 | CampUnlock.Rank3;

        TickDriver _driver;
        Camp _camp;
        CampServiceKind? _pending;
        CampUnlock _pendingFlag;
        // Ранги снимает окно «Развитие лагеря» (значок над жителями держится до него), поэтому здесь
        // только помним, какие уже объявлены в этой сессии, чтобы не повторять тост.
        CampUnlock _presentedRanks;
        bool _previewPending;
        float _settled, _labelAge;
        Transform _installedRoot;
        RectTransform _label;
        CanvasGroup _labelGroup;
        TMP_Text _labelTitle, _labelNote, _labelRole;
        CampServiceNpc _labelTarget;
        Camera _camera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() => Install(FindAnyObjectByType<TickDriver>());

        /// <summary>Необязательный явный hook для загрузчика или съёмочного стенда.</summary>
        public static CampArrivalPresentation Install(TickDriver driver)
        {
            if (!Application.isPlaying) return null;
            if (Instance == null)
            {
                var host = new GameObject("Прибытия в лагерь");
                DontDestroyOnLoad(host);
                host.AddComponent<CampArrivalPresentation>();
            }
            if (driver != null) Instance._driver = driver;
            Instance.ObserveState();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Update()
        {
            if (_driver == null) _driver = FindAnyObjectByType<TickDriver>();
            // Итоги похода могут открыть место ещё в Summary. Сравнение идёт до всех UI-проверок.
            ObserveState();
            InstallWorkstations();
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            bool ready = ReadyToPresent();
            if (!ready) { _settled = 0; HideLabel(); return; }
            if (_pending.HasValue)
            {
                // Несколько открытий разом (уровень 3 вместе с первым забегом) идут по очереди:
                // следующее ждёт, пока погаснет подпись предыдущего.
                if (_label != null && _label.gameObject.activeSelf) _settled = 0;
                else _settled += dt;
                if (_settled >= SettleTime && Present(_pending.Value)) Presented();
            }
            UpdateLabel(dt);
        }

        void ObserveState()
        {
            Camp camp = _driver != null && _driver.Session != null ? _driver.Session.Camp : null;
            if (!ReferenceEquals(camp, _camp)) { _camp = camp; Baseline(); }
            if (camp == null || _previewPending) return;
            CampUnlock open = camp.PendingUnlocks;
            if (!camp.UsesCampProgression)
            {
                // В песочнице всё открыто с начала: объявлять нечего, флаги снимаются молча.
                if (open != CampUnlock.None) camp.AcknowledgeUnlocks(open);
                _pending = null; _pendingFlag = CampUnlock.None; _settled = 0;
                return;
            }
            // Ранг, уже снятый в окне жителя, больше не помним: новый ранг объявится заново.
            _presentedRanks &= open;
            CampUnlock next = NextAnnouncement(open & ~_presentedRanks);
            if (next == _pendingFlag) return;
            _pendingFlag = next; _settled = 0;
            _pending = next == CampUnlock.None ? (CampServiceKind?)null
                : next == CampUnlock.Alchemist ? CampServiceKind.Alchemist
                : next == CampUnlock.TravelTable ? CampServiceKind.TravelTable
                : next == CampUnlock.Trader ? CampServiceKind.Trader : CampServiceKind.Smith;
        }

        /// <summary>Порядок плана 06.10: Лео → стол → Вен → ранг. Все новые ранги объявляются одним тостом.</summary>
        static CampUnlock NextAnnouncement(CampUnlock open) =>
            (open & CampUnlock.Alchemist) != 0 ? CampUnlock.Alchemist
            : (open & CampUnlock.TravelTable) != 0 ? CampUnlock.TravelTable
            : (open & CampUnlock.Trader) != 0 ? CampUnlock.Trader
            : open & RankFlags;

        void Presented()
        {
            if (!_previewPending && _camp != null && _pendingFlag != CampUnlock.None)
            {
                if ((_pendingFlag & RankFlags) != 0) _presentedRanks |= _pendingFlag;
                else _camp.AcknowledgeUnlocks(_pendingFlag);
            }
            _pending = null; _pendingFlag = CampUnlock.None; _previewPending = false; _settled = 0;
        }

        void Baseline()
        {
            _pending = null; _pendingFlag = CampUnlock.None; _presentedRanks = CampUnlock.None;
            _previewPending = false; _settled = 0; HideLabel();
        }

        bool ReadyToPresent()
        {
            var player = CampPlayerView.Instance;
            return _driver != null && _driver.Session != null && player != null && player.Active
                && !player.InputBlocked && !_driver.GameplayPaused && !MainMenuView.IsOpen
                && !CampTransition.Running && !CampTransition.Covering;
        }

        void InstallWorkstations()
        {
            if (_installedRoot != null) return;
            var world = FindAnyObjectByType<SceneWorldView>();
            Transform root = world != null && world.CampRoot != null ? world.CampRoot.transform : null;
            if (root == null || root == _installedRoot) return;
            _installedRoot = root;
            CampWorkstationStage.InstallCurrentContexts(root);
        }

        /// <summary>Съёмочное представление через тот же UI; состояние лагеря не меняется. Smith — новый ранг.</summary>
        public void PreviewArrival(CampServiceKind kind)
        {
            if (kind != CampServiceKind.Trader && kind != CampServiceKind.Alchemist
                && kind != CampServiceKind.TravelTable && kind != CampServiceKind.Smith) return;
            _pending = kind; _pendingFlag = CampUnlock.None; _previewPending = true; _settled = 0;
        }

        bool Present(CampServiceKind kind)
        {
            var hud = FindAnyObjectByType<CombatHudView>();
            if (hud == null || hud.Toasts == null || hud.Toasts.Template == null) return false;
            bool rank = kind == CampServiceKind.Smith;
            int campRank = _camp != null ? _camp.CampRank : 0;
            string title = rank ? (campRank > 0 ? "Ранг лагеря · " + campRank : "Новый ранг лагеря")
                : kind == CampServiceKind.Trader ? "Вен пришёл в лагерь"
                : kind == CampServiceKind.Alchemist ? "Лео пришёл в лагерь" : "Походный стол готов";
            string line = rank ? "Загляни к жителям: «Развитие лагеря»"
                : kind == CampServiceKind.Trader ? "Лавка открыта"
                : kind == CampServiceKind.Alchemist ? "Алхимик обустроил рабочее место"
                : "Выбери навык, дар и два вида зелий";
            Texture icon = null;
            var guide = FindAnyObjectByType<CampGuidePanel>(FindObjectsInactive.Include);
            int iconIndex = rank ? 0 : kind == CampServiceKind.Trader ? 1 : kind == CampServiceKind.Alchemist ? 2 : -1;
            if (guide != null && guide.Icons != null && iconIndex >= 0 && iconIndex < guide.Icons.Length)
                icon = guide.Icons[iconIndex];
            hud.Toasts.Push(icon, UiTheme.Role.Accent, title, line, UiTheme.Role.TextMuted);
            _labelTarget = FindTarget(kind);
            if (_labelTarget != null && EnsureLabel())
            {
                _labelTitle.text = kind == CampServiceKind.TravelTable ? "Походный стол"
                    : rank ? (campRank > 0 ? "Ранг лагеря " + campRank : "Новый ранг") : _labelTarget.Title;
                if (_labelNote != null) _labelNote.text = kind == CampServiceKind.TravelTable
                    ? "Подготовка к походу" : rank ? "Новое у жителей" : kind == CampServiceKind.Trader ? "Лавка открыта" : "Алхимик";
                if (_labelRole != null) _labelRole.gameObject.SetActive(false);
                _labelAge = 0; _label.gameObject.SetActive(true); _label.SetAsLastSibling();
            }
            return true;
        }

        static CampServiceNpc FindTarget(CampServiceKind kind)
        {
            foreach (var npc in FindObjectsByType<CampServiceNpc>(FindObjectsInactive.Include))
                if (npc.Kind == kind && npc.isActiveAndEnabled) return npc;
            return null;
        }

        bool EnsureLabel()
        {
            if (_label != null) return true;
            var shop = FindAnyObjectByType<CampShopView>(FindObjectsInactive.Include);
            if (shop == null || shop.Hint == null || shop.HintTitle == null) return false;
            _label = Instantiate(shop.Hint, shop.Hint.parent);
            _label.name = "Короткое представление места";
            _labelTitle = CopyPart(shop.Hint, shop.HintTitle.transform)?.GetComponent<TMP_Text>();
            _labelNote = shop.HintNote != null ? CopyPart(shop.Hint, shop.HintNote.transform)?.GetComponent<TMP_Text>() : null;
            _labelRole = shop.HintRole != null ? CopyPart(shop.Hint, shop.HintRole.transform)?.GetComponent<TMP_Text>() : null;
            var keyCap = shop.HintKeyCap != null ? CopyPart(shop.Hint, shop.HintKeyCap) : null;
            if (keyCap != null) keyCap.gameObject.SetActive(false);
            _labelGroup = _label.GetComponent<CanvasGroup>() ?? _label.gameObject.AddComponent<CanvasGroup>();
            _labelGroup.interactable = false; _labelGroup.blocksRaycasts = false;
            foreach (var graphic in _label.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            if (_labelTitle == null) { Destroy(_label.gameObject); _label = null; return false; }
            return true;
        }

        Transform CopyPart(Transform originalRoot, Transform originalPart)
        {
            var names = new List<string>();
            for (Transform at = originalPart; at != originalRoot; at = at.parent)
            {
                if (at == null) return null;
                names.Insert(0, at.name);
            }
            return names.Count == 0 ? _label : _label.Find(string.Join("/", names));
        }

        void UpdateLabel(float dt)
        {
            if (_label == null || !_label.gameObject.activeSelf) return;
            if (_labelTarget == null || !_labelTarget.isActiveAndEnabled) { HideLabel(); return; }
            _labelAge += dt;
            if (_labelAge >= LabelLife) { HideLabel(); return; }
            var player = CampPlayerView.Instance;
            // Подсказка взаимодействия важнее временного представления над тем же местом.
            if (CampServicesView.Instance?.HoveredService == true
                || player != null && _labelTarget.Near(player.InteractionPosition))
            { _labelGroup.alpha = 0; return; }
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) { HideLabel(); return; }
            Bounds bounds = _labelTarget.Shape;
            Vector3 screen = _camera.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y + .65f, bounds.center.z));
            if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height)
            { _labelGroup.alpha = 0; return; }
            RectTransform parent = _label.parent as RectTransform;
            var canvas = _label.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out var point);
            _label.anchoredPosition = point;
            _labelGroup.alpha = Mathf.Clamp01(_labelAge / .25f) * Mathf.Clamp01((LabelLife - _labelAge) / .5f);
        }

        void HideLabel()
        {
            if (_label != null) _label.gameObject.SetActive(false);
            _labelTarget = null;
        }

        void OnDisable() => HideLabel();
        void OnDestroy()
        {
            if (_label != null) Destroy(_label.gameObject);
            if (Instance == this) Instance = null;
        }
    }
}
