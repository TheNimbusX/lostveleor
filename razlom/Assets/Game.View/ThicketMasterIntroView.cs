using System.Collections.Generic;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// ХОЗЯИН ЧАЩИ — КАТ-СЦЕНА ВСТУПЛЕНИЯ (владелец 02.10: «начало боя с боссом плохое, он тупо стоит…
    /// должно быть так, что мы идём снизу, заходим на арену и проигрывается скрипт, где босс выходит из
    /// спячки, это всё должно показываться как кат-сцена красивая»).
    ///
    /// Окно и всё время — из Sim (Simulation.ForestBoss.Intro: TryGetThicketIntro, ThicketIntroHoldsHero),
    /// правила кадра — <see cref="ThicketMasterIntroRules"/>. Здесь только руки:
    ///  • камера — CameraFollow.SetCinematic: от героя к боссу с приближением 6,2 → 4,6 и обратно
    ///    (обычное слежение не тронуто, ReleaseCinematic возвращает размер);
    ///  • чёрные полосы сверху и снизу с дымной кромкой — свой холст поверх HUD (порядок
    ///    <see cref="CanvasOrder"/>), без лучей;
    ///  • HUD гаснет и возвращается — все экземпляры боевого HUD (с миникартой), экранов забега, меток мира и
    ///    меток у края. Ревью 02.10 (вечер, «в кат-сцене виден HUD и миникарта»): CanvasGroup на объекте
    ///    одна (DisallowMultipleComponent), а на корне боевого HUD уже стоит группа PlayerHud — вторая не
    ///    добавлялась (ошибка AddComponent в логе), и HUD с миникартой стояли поверх кат-сцены. Теперь: холст
    ///    без своей группы гаснет нашей группой и выключается, когда погас; холст с чужой группой (её ведёт
    ///    PlayerHud — пауза, окна) не трогаем, а выключаем сам холст на середине затухания
    ///    (<see cref="ThicketMasterIntroRules.HudCanvasOn"/>). По возвращении — включаем только выключенное
    ///    нами, свои группы снимаем;
    ///  • титр «ХОЗЯИН ЧАЩИ» и строка под ним — шрифты и материалы полосы босса (RunHudView.BossName,
    ///    BossSubtitle; без неё — шрифты темы), размеры — шкала темы;
    ///  • тряска на контакте рёва — CombatCameraJuice.AddImpulse (сила — из настроек игрока).
    /// Пробуждение, рёв и вспышку кроны играют ThicketMasterAnimatorView и ThicketMasterCombatView.Vfx по
    /// своим событиям; полосу босса до конца окна держит RunHud (<see cref="ThicketMasterIntroRules.HoldsBossBar"/>).
    ///
    /// Пауза держит кадр (время — тики Sim). Новая симуляция (новый забег, F8 «К боссу», возврат в лагерь),
    /// смена поколения, выход из боя Разлома (итоги после L), гибель героя, выключение — всё снимается сразу:
    /// камера, HUD, полосы, титр. Объекты —
    /// дети объекта арены, без DontSave; холст и кромка создаются при первом вступлении.
    ///
    /// Ставит ThicketMasterCombatView (Awake) одной строкой <see cref="EnsureOn"/>.
    /// </summary>
    [DefaultExecutionOrder(670)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class ThicketMasterIntroView : MonoBehaviour
    {
        /// <summary>Холст полос и титра: над HUD (бой 10, экраны забега 50), под паузой (300) и переходом (900).</summary>
        public const int CanvasOrder = 60;

        private const string OverlayName = "Вступление босса — полосы и титр";
        private const int FeatherPixels = 32;

        private TickDriver _driver;
        private CameraFollow _follow;
        private CombatCameraJuice _juice;
        private Simulation _shown;
        private int _generation = -1;

        private bool _active, _aborted;
        private int _boss = -1, _start, _wake, _end, _release;
        private float _before = float.NaN;
        private Vector3 _focus;

        private Canvas _canvas;
        private RectTransform _top, _bottom, _titleBlock, _rule;
        private CanvasGroup _titleGroup;
        private TMP_Text _title;
        private Texture2D _feather;
        private float _barsShown = -1f, _settleShown = -1f;

        /// <summary>Свои группы прозрачности на холстах HUD без чужой группы — снимаются по возвращении.</summary>
        private readonly List<CanvasGroup> _veils = new List<CanvasGroup>(4);
        /// <summary>Холсты HUD этой кат-сцены и ведёт ли их прозрачность своя группа (иначе — только выключение).</summary>
        private readonly List<Canvas> _hudCanvases = new List<Canvas>(8);
        private readonly List<bool> _hudFades = new List<bool>(8);
        /// <summary>Холсты, выключенные кат-сценой: включает обратно она только их.</summary>
        private readonly List<Canvas> _hudOff = new List<Canvas>(8);
        private bool _veilsCollected;

        public static ThicketMasterIntroView EnsureOn(GameObject host)
        {
            if (host == null) return null;
            var view = host.GetComponent<ThicketMasterIntroView>();
            return view != null ? view : host.AddComponent<ThicketMasterIntroView>();
        }

        /// <summary>Идёт кат-сцена (с хвостом возврата): камера, полосы и HUD сейчас у неё.</summary>
        public bool Playing => _active;

        private void Awake() => _driver = GetComponent<TickDriver>();

        private void OnDisable() => Stop();

        private void OnDestroy()
        {
            Stop();
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_feather != null) Destroy(_feather);
            _canvas = null;
            _feather = null;
        }

        private void LateUpdate()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            int generation = _driver != null ? _driver.Generation : -1;
            if (!ReferenceEquals(sim, _shown) || generation != _generation)
            {
                // Новый забег, «К боссу», лагерь: тики и сущности начались заново — всё своё снять.
                Stop();
                _shown = sim;
                _generation = generation;
            }
            // Только в бою Разлома. Итоги забега (L — «уйти с добычей» посреди окна) держат ту же
            // симуляцию без нового поколения, а тики стоят: кат-сцена застыла бы поверх экрана итогов.
            if (sim == null || _driver.Session == null || _driver.Session.Mode != GameMode.Rift)
            {
                if (_active) Stop();
                return;
            }

            float now = sim.Tick - 1 + _driver.Alpha;
            if (!_active && !TryBegin(sim, now)) return;
            if (!Track(sim)) { Stop(); return; }

            var look = ThicketMasterIntroRules.LookAt(_start, _wake, _end, _release, now);
            if (look.Done) { Stop(); return; }
            Apply(look);
            if (ThicketMasterIntroRules.RoarShake(_before, now, _end, _release) && _juice != null)
                _juice.AddImpulse(ThicketMasterIntroRules.ShakeTrauma, ThicketMasterIntroRules.ShakeZoom);
            _before = now;
        }

        // ------------------------------------------------------------ окно Sim

        /// <summary>Окно вступления идёт (герой держится): найти его босса и забрать кадр.</summary>
        private bool TryBegin(Simulation sim, float now)
        {
            if (!sim.ThicketIntroHoldsHero || !sim.Entities.Alive[Simulation.PlayerId]) return false;
            var e = sim.Entities;
            int last = sim.Tick - 1;
            for (int id = 1; id < e.Count; id++)
            {
                if (e.Kind[id] != EnemyKind.ForestThicketMaster || !e.Alive[id]) continue;
                if (!sim.TryGetThicketIntro(id, out int start, out int wake, out int end)) continue;
                if (last < start || last >= end) continue;
                _boss = id;
                _start = start;
                _wake = wake;
                _end = end;
                _release = end;
                _aborted = false;
                _active = true;
                // Первый кадр вида не трясёт: рёв, прошедший до него, уже не наш.
                _before = now;
                FindCamera();
                Build();
                UpdateFocus(sim);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Окно каждый кадр: Часы двигают пробуждение и конец; босс умер в окне — герой свободен с тика
        /// смерти (release), всё уходит от него. false — кат-сцене не место (герой погиб).
        /// </summary>
        private bool Track(Simulation sim)
        {
            var e = sim.Entities;
            if (!e.Alive[Simulation.PlayerId]) return false;
            bool boss = (uint)_boss < (uint)e.Count && e.Kind[_boss] == EnemyKind.ForestThicketMaster;
            if (boss && sim.TryGetThicketIntro(_boss, out _, out int wake, out int end))
            {
                _wake = wake;
                _end = end;
                if (!_aborted) _release = end;
            }
            int last = sim.Tick - 1;
            if (!_aborted && (!boss || !e.Alive[_boss]) && last < _end)
            {
                _aborted = true;
                _release = Mathf.Max(_start, last);
            }
            if (boss && e.Alive[_boss]) UpdateFocus(sim);
            return true;
        }

        /// <summary>Точка кадра у босса: над землёй и чуть к герою (лапы и корни впереди тела).</summary>
        private void UpdateFocus(Simulation sim)
        {
            Vector3 boss = _driver.GetRenderPosition(_boss);
            Vector3 toHero = _driver.GetRenderPosition(Simulation.PlayerId) - boss;
            toHero.y = 0f;
            float distance = toHero.magnitude;
            Vector3 lead = distance > .01f
                ? toHero * (Mathf.Min(ThicketMasterIntroRules.FocusLead, distance) / distance)
                : Vector3.zero;
            _focus = boss + lead + Vector3.up * ThicketMasterIntroRules.FocusLift;
        }

        private void FindCamera()
        {
            if (_follow == null) _follow = FindAnyObjectByType<CameraFollow>();
            if (_juice == null && _follow != null) _juice = _follow.GetComponent<CombatCameraJuice>();
        }

        // ------------------------------------------------------------ кадр

        private void Apply(in ThicketMasterIntroRules.Look look)
        {
            if (_follow != null) _follow.SetCinematic(_focus, look.Camera, look.Zoom);
            SetHud(look.Hud);
            bool overlay = look.Bars > 0f || look.Title > 0f;
            if (_canvas != null && _canvas.enabled != overlay) _canvas.enabled = overlay;
            if (!overlay) return;
            PlaceBars(look.Bars);
            ShowTitle(look.Title, look.TitleSettle);
        }

        /// <summary>Снять всё своё: камера — обычная, HUD — как был, полосы и титр спрятаны.</summary>
        private void Stop()
        {
            if (_active && _follow != null) _follow.ReleaseCinematic();
            RestoreHud();
            if (_canvas != null) _canvas.enabled = false;
            if (_titleBlock != null && _titleBlock.gameObject.activeSelf) _titleBlock.gameObject.SetActive(false);
            _active = false;
            _aborted = false;
            _boss = -1;
            _before = float.NaN;
            _barsShown = _settleShown = -1f;
        }

        /// <summary>Полосы: amount 0 — за краем экрана вместе с кромкой, 1 — на месте.</summary>
        private void PlaceBars(float amount)
        {
            if (_top == null || Mathf.Abs(amount - _barsShown) < .0005f) return;
            _barsShown = amount;
            float h = ThicketMasterIntroRules.BarHeight;
            float shift = (h + ThicketMasterIntroRules.FeatherHeight) * (1f - amount);
            _top.anchorMin = new Vector2(0f, 1f - h + shift);
            _top.anchorMax = new Vector2(1f, 1f + shift);
            _bottom.anchorMin = new Vector2(0f, -shift);
            _bottom.anchorMax = new Vector2(1f, h - shift);
        }

        /// <summary>Титр: прозрачность и сход букв (интервал, подъём, черта под именем).</summary>
        private void ShowTitle(float alpha, float settle)
        {
            if (_titleBlock == null) return;
            bool visible = alpha > 0f;
            if (_titleBlock.gameObject.activeSelf != visible) _titleBlock.gameObject.SetActive(visible);
            if (!visible) return;
            _titleGroup.alpha = alpha;
            if (Mathf.Abs(settle - _settleShown) < .001f) return;
            _settleShown = settle;
            if (_title != null) _title.characterSpacing = ThicketMasterIntroRules.TitleSpacing(settle);
            _titleBlock.anchoredPosition = new Vector2(0f, -ThicketMasterIntroRules.TitleRise * (1f - settle));
            if (_rule != null) _rule.sizeDelta = new Vector2(ThicketMasterIntroRules.RuleWidth * settle, _rule.sizeDelta.y);
        }

        // ------------------------------------------------------------ HUD

        /// <summary>
        /// Прозрачность HUD на время окна (1 — как обычно). Холсты собираются с первым затуханием и держатся
        /// до конца кат-сцены (<see cref="Stop"/> возвращает всё): свои группы ведут затухание, а погасший
        /// холст выключается целиком — и миникарта, и всё, что рисуется мимо прозрачности группы.
        /// </summary>
        private void SetHud(float alpha)
        {
            if (!_veilsCollected)
            {
                if (alpha >= .999f) return;
                CollectVeils();
            }
            for (int i = 0; i < _veils.Count; i++)
                if (_veils[i] != null) _veils[i].alpha = alpha;
            for (int i = 0; i < _hudCanvases.Count; i++)
            {
                Canvas canvas = _hudCanvases[i];
                if (canvas == null) continue;
                bool on = ThicketMasterIntroRules.HudCanvasOn(alpha, _hudFades[i]);
                if (!on && canvas.enabled)
                {
                    canvas.enabled = false;
                    _hudOff.Add(canvas);
                }
                else if (on && !canvas.enabled && _hudOff.Remove(canvas)) canvas.enabled = true;
            }
        }

        /// <summary>Все экземпляры частей HUD: боевой HUD с миникартой, экраны забега, метки мира, метки у края.</summary>
        private void CollectVeils()
        {
            _veilsCollected = true;
            AddVeils(FindObjectsByType<CombatHudView>(FindObjectsInactive.Include));
            AddVeils(FindObjectsByType<RunHudView>(FindObjectsInactive.Include));
            AddVeils(FindObjectsByType<RunWorldView>(FindObjectsInactive.Include));
            AddVeils(FindObjectsByType<WorldEdgeMarks>(FindObjectsInactive.Include));
        }

        private void AddVeils<T>(T[] parts) where T : Component
        {
            for (int i = 0; i < parts.Length; i++) AddVeil(parts[i]);
        }

        /// <summary>
        /// Внешний холст части HUD. Своя группа — только на холсте без группы: CanvasGroup на объекте одна, а
        /// группу боевого HUD ведёт PlayerHud (пауза, окна) — её не трогаем, такой холст только выключается.
        /// </summary>
        private void AddVeil(Component part)
        {
            if (part == null) return;
            Canvas[] chain = part.GetComponentsInParent<Canvas>(true);
            Canvas canvas = chain.Length > 0 ? chain[chain.Length - 1] : null;
            if (canvas == null || canvas == _canvas || _hudCanvases.Contains(canvas)) return;
            bool fades = !canvas.TryGetComponent(out CanvasGroup _);
            if (fades)
            {
                var veil = canvas.gameObject.AddComponent<CanvasGroup>();
                if (veil != null) _veils.Add(veil);
                else fades = false;
            }
            _hudCanvases.Add(canvas);
            _hudFades.Add(fades);
        }

        /// <summary>HUD ровно как был: включить выключенные нами холсты, снять свои группы.</summary>
        private void RestoreHud()
        {
            for (int i = 0; i < _hudOff.Count; i++)
                if (_hudOff[i] != null) _hudOff[i].enabled = true;
            _hudOff.Clear();
            for (int i = 0; i < _veils.Count; i++)
            {
                if (_veils[i] == null) continue;
                _veils[i].alpha = 1f;
                Destroy(_veils[i]);
            }
            _veils.Clear();
            _hudCanvases.Clear();
            _hudFades.Clear();
            _veilsCollected = false;
        }

        // ------------------------------------------------------------ холст

        /// <summary>Холст полос и титра — один раз, при первом вступлении; дальше только включается.</summary>
        private void Build()
        {
            if (_canvas != null) return;
            var root = new GameObject(OverlayName, typeof(RectTransform));
            root.transform.SetParent(transform, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            // Титр — в масштабе интерфейса игрока, как HUD; полосы стоят долями экрана и от него не зависят.
            root.AddComponent<UiScaleFollower>();
            _canvas.enabled = false;

            UiTheme theme = UiTheme.Current;
            Color ink = theme.Get(UiTheme.Role.SmokeDeep);
            ink.a = 1f;
            if (_feather == null) _feather = FeatherTexture();
            _top = Bar(root.transform, "Полоса сверху", ink, true);
            _bottom = Bar(root.transform, "Полоса снизу", ink, false);
            BuildTitle(root.transform, theme, ink);
        }

        private RectTransform Bar(Transform parent, string name, Color ink, bool top)
        {
            RectTransform bar = Node(parent, name);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            var fill = bar.gameObject.AddComponent<Image>();
            fill.color = ink;
            fill.raycastTarget = false;

            // Кромка внутрь кадра: тушь растворяется, а не обрывается линейкой.
            RectTransform edge = Node(bar, "Кромка");
            float share = ThicketMasterIntroRules.FeatherHeight / ThicketMasterIntroRules.BarHeight;
            edge.anchorMin = new Vector2(0f, top ? -share : 1f);
            edge.anchorMax = new Vector2(1f, top ? 0f : 1f + share);
            edge.offsetMin = edge.offsetMax = Vector2.zero;
            var fade = edge.gameObject.AddComponent<RawImage>();
            fade.texture = _feather;
            fade.color = ink;
            fade.raycastTarget = false;
            // Текстура плотная сверху; у нижней полосы кромка над ней — плотная снизу.
            if (!top) fade.uvRect = new Rect(0f, 1f, 1f, -1f);
            return bar;
        }

        /// <summary>Титр: дымное пятно, имя (Philosopher полосы босса), черта акцента, строка под ним.</summary>
        private void BuildTitle(Transform parent, UiTheme theme, Color ink)
        {
            _titleBlock = Node(parent, "Титр");
            _titleBlock.anchorMin = _titleBlock.anchorMax = new Vector2(.5f, ThicketMasterIntroRules.TitleY);
            _titleBlock.pivot = new Vector2(.5f, .5f);
            _titleBlock.sizeDelta = new Vector2(1400f, 220f);
            _titleGroup = _titleBlock.gameObject.AddComponent<CanvasGroup>();
            _titleGroup.blocksRaycasts = false;
            _titleGroup.interactable = false;

            if (theme.Blob != null)
            {
                RectTransform smoke = Node(_titleBlock, "Дым под титром");
                smoke.sizeDelta = new Vector2(1100f, 250f);
                var image = smoke.gameObject.AddComponent<Image>();
                image.sprite = theme.Blob;
                Color shade = ink;
                shade.a = .62f;
                image.color = shade;
                image.raycastTarget = false;
            }

            RunHudView hud = FindAnyObjectByType<RunHudView>(FindObjectsInactive.Include);
            TMP_Text nameSource = hud != null ? hud.BossName : null;
            TMP_Text lineSource = hud != null ? hud.BossSubtitle : null;

            _title = Label(_titleBlock, "Имя", nameSource, theme.Get(UiTheme.FontRole.Heading),
                theme.Size(UiTheme.TextStep.Display), theme.Get(UiTheme.Role.Text), ThicketMasterIntroRules.Title);
            ((RectTransform)_title.transform).anchoredPosition = new Vector2(0f, 24f);
            _title.characterSpacing = ThicketMasterIntroRules.TitleSpacingFrom;

            _rule = Node(_titleBlock, "Черта");
            _rule.anchoredPosition = new Vector2(0f, -18f);
            _rule.sizeDelta = new Vector2(0f, 2f);
            var rule = _rule.gameObject.AddComponent<Image>();
            rule.sprite = theme.Pixel;
            Color accent = theme.Get(UiTheme.Role.Accent);
            accent.a = .9f;
            rule.color = accent;
            rule.raycastTarget = false;

            TMP_FontAsset lineFont = lineSource != null && lineSource.font != null ? lineSource.font : theme.Get(UiTheme.FontRole.Body);
            // Ступень строки по её гарнитуре (лист 5): Philosopher — 24, Nunito — 18.
            UiTheme.TextStep lineStep = lineFont != null && lineFont == theme.Heading ? UiTheme.TextStep.Heading : UiTheme.TextStep.Body;
            TMP_Text line = Label(_titleBlock, "Подпись", lineSource, theme.Get(UiTheme.FontRole.Body),
                theme.Size(lineStep), theme.Get(UiTheme.Role.TextMuted), ThicketMasterIntroRules.Subtitle);
            ((RectTransform)line.transform).anchoredPosition = new Vector2(0f, -48f);

            _titleBlock.gameObject.SetActive(false);
        }

        private static TMP_Text Label(Transform parent, string name, TMP_Text source, TMP_FontAsset fallback, float size,
            Color color, string text)
        {
            RectTransform rect = Node(parent, name);
            rect.sizeDelta = new Vector2(1400f, size * 1.6f);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = source != null && source.font != null ? source.font : fallback;
            if (font != null) label.font = font;
            // Материал полосы босса (тень и свечение «Дыма и света») — только если он того же шрифта.
            if (source != null && source.font == font && source.fontSharedMaterial != null)
                label.fontSharedMaterial = source.fontSharedMaterial;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        private static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Кромка полосы: белая, прозрачность от плотной (верх) к нулю (низ), мягким шагом.</summary>
        private static Texture2D FeatherTexture()
        {
            var texture = new Texture2D(1, FeatherPixels, TextureFormat.RGBA32, false)
            {
                name = "Вступление босса — кромка полосы",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[FeatherPixels];
            for (int y = 0; y < FeatherPixels; y++)
            {
                float k = y / (FeatherPixels - 1f);
                pixels[y] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * k * k * (3f - 2f * k)));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
