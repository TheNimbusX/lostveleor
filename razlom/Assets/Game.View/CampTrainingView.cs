using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed class CampTrainingView : MonoBehaviour
    {
        public static CampTrainingView Instance { get; private set; }
        [Tooltip("Подписи «Манекен» над полосками — у каждого манекена в этом радиусе")]
        [Min(.5f)] public float ActivationDistance = 2.75f;
        [Tooltip("Огороженный полигон: по его забору меряется зона, где в лагере можно бить и колдовать. Пусто — родитель манекенов")]
        public Transform Ground;
        [Tooltip("На сколько метров зона выходит за рамку забора")]
        [Min(0f)] public float ZoneMargin = .5f;
        CampDummyView[] _dummies;
        TickDriver _driver;
        int _generation = -1;
        // Панель «Дыма и света» (CampTrainingWc); без префаба — прежняя IMGUI-панель.
        CampTrainingPanel _panel;
        float _shown;
        readonly long[] _values = { -1, -1, -1, -1, -1, -1 };
        /// <summary>
        /// Панель замера и подписи — там же, где боевой HUD и право бить: в зоне полигона.
        /// Зону держит Sim (<see cref="CampTraining.InZone"/>); без неё — прежний радиус у манекенов.
        /// </summary>
        bool Nearby()
        {
            var camp = CampPlayerView.Instance;
            if (camp == null || !camp.Active || camp.InputBlocked || _driver == null || _driver.GameplayPaused) return false;
            var session = _driver.Session;
            if (session?.Training != null) return session.Mode == GameMode.Camp && session.CampCombatAllowed;
            foreach (var dummy in _dummies) if (IsNear(dummy, camp.Position)) return true;
            return false;
        }
        public static bool IsNear(CampDummyView dummy, Vector3 position)
            => Instance != null && (dummy.TargetPosition - position).sqrMagnitude <= Instance.ActivationDistance * Instance.ActivationDistance;
        // Под миникартой и её подписью: на прежних 90 px панель уходила под карту.
        static Rect PanelRect => new Rect(Screen.width - 286, Mathf.Max(90f, PlayerHud.MinimapBottom + 10f), 266, 164);
        public static bool PointerOverPanel(Vector2 screenPosition)
        {
            if (Instance == null || !Instance.Nearby()) return false;
            if (Instance._panel != null)
                return Instance._panel.Card != null && RectTransformUtility.RectangleContainsScreenPoint(Instance._panel.Card, screenPosition, null);
            return PanelRect.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        }
        public void Initialize(TickDriver driver)
        {
            Instance = this; _driver = driver;
            _dummies = GetComponentsInChildren<CampDummyView>();
            var definitions = new CampDummyDefinition[_dummies.Length];
            for (int i = 0; i < definitions.Length; i++) definitions[i] = _dummies[i].Definition;
            // Зона боя — весь огороженный полигон (владелец, 29 сентября), а не 2,75 м у манекена.
            if (MeasureZone(_dummies, out Vector3 centre, out float radius))
                driver.Session.ConfigureCampTraining(definitions, Flat(centre), Fix64.FromRaw((long)(radius * Fix64.One.Raw)));
            else
            {
                Debug.LogWarning("[camp-training] забор полигона не найден — зона боя по манекенам.");
                driver.Session.ConfigureCampTraining(definitions);
            }
            SyncIds();
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampTrainingWc");
            if (prefab != null && _panel == null)
            {
                _panel = Instantiate(prefab).GetComponent<CampTrainingPanel>();
                if (_panel != null) UiScaleFollower.Attach(_panel.gameObject);
                if (_panel != null)
                {
                    _panel.name = "Тренировка — панель";
                    SettleUnderMinimap(_panel.Card);
                    if (_panel.Group != null) { _panel.Group.alpha = 0f; _panel.Group.blocksRaycasts = false; }
                    if (_panel.Reset != null) _panel.Reset.onClick.AddListener(() => _driver?.Session?.Training.ResetCounters());
                }
            }
        }
        /// <summary>Верх карточки тренировки и потолок её дыма (над верхом) — в единицах холста 1920×1080.</summary>
        public const float CardTop = -333f, CardSmokeAbove = 16f;
        /// <summary>
        /// Миникарта боевого HUD выросла 230 → 253 (29.09), низ её подписи опустился с −300 на −323 — а
        /// карточка стояла на −318 и касалась подписи. Карточка опускается к <see cref="CardTop"/> (зазор 10),
        /// дым над ней — не выше <see cref="CardSmokeAbove"/> (как было: заходит в подпись на 6). Холсты
        /// карточки и HUD одинаковые (высота 1080 / масштаб), поэтому зазор держится при 80–120%; снизу при
        /// 120% до плиток зелий остаётся ~19. Уже опущенную руками или сборщиком карточку не трогает.
        /// </summary>
        static void SettleUnderMinimap(RectTransform card)
        {
            if (card == null || card.anchorMin != Vector2.one || card.anchorMax != Vector2.one) return;
            if (card.anchoredPosition.y > CardTop) card.anchoredPosition = new Vector2(card.anchoredPosition.x, CardTop);
            foreach (string part in new[] { "Тень под текстом", "Дым", "Дым плотнее" })
                if (card.Find(part) is RectTransform layer && layer.offsetMax.y > CardSmokeAbove)
                    layer.offsetMax = new Vector2(layer.offsetMax.x, CardSmokeAbove);
        }
        /// <summary>
        /// Круг зоны полигона в мире: центр — середина рамки забора (с манекенами), радиус — до её
        /// дальнего угла плюс <see cref="ZoneMargin"/>, чтобы в круг вошёл весь забор. Деревья,
        /// ящики и фонари полигона в рамку не идут: они стоят снаружи и раздули бы зону.
        /// Забора нет — false, и зону строит Sim по самим манекенам.
        /// </summary>
        bool MeasureZone(CampDummyView[] dummies, out Vector3 centre, out float radius)
        {
            centre = Vector3.zero;
            radius = 0f;
            Transform ground = Ground != null ? Ground : dummies != null && dummies.Length > 0 ? dummies[0].transform.parent : null;
            if (ground == null) return false;
            bool any = false;
            Bounds bounds = default;
            foreach (var renderer in ground.GetComponentsInChildren<Renderer>())
            {
                if (!IsFence(renderer.transform, ground)) continue;
                if (any) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; any = true; }
            }
            if (!any) return false;
            foreach (var dummy in dummies) bounds.Encapsulate(dummy.TargetPosition);
            centre = new Vector3(bounds.center.x, ground.position.y, bounds.center.z);
            radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude + ZoneMargin;
            return true;
        }

        static bool IsFence(Transform part, Transform ground)
        {
            for (; part != null && part != ground; part = part.parent)
            {
                string name = part.name.ToLowerInvariant();
                if (name.Contains("fence") || name.Contains("забор") || name.Contains("ограда")) return true;
            }
            return false;
        }

        /// <summary>Зона полигона в сцене — чтобы её было видно и можно было подогнать ZoneMargin.</summary>
        void OnDrawGizmosSelected()
        {
            if (!MeasureZone(GetComponentsInChildren<CampDummyView>(), out Vector3 centre, out float radius)) return;
            Gizmos.color = new Color(1f, .54f, .3f, .9f);
            const int segments = 72;
            Vector3 previous = centre + new Vector3(radius, .05f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, .05f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        void SyncIds()
        {
            if (_driver == null || _driver.Session.Generation == _generation) return;
            _generation = _driver.Session.Generation;
            for (int i = 0; i < _dummies.Length; i++) _dummies[i].EntityId = _driver.Session.Training.EntityId(i);
        }
        public static CampDummyView Find(int entityId)
        {
            var instance = Instance;
            // Session бывает пустым на выходе из Play: без проверки сыпались NullReference.
            if (instance == null || instance._driver?.Session == null || instance._driver.Session.Mode != GameMode.Camp) return null;
            instance.SyncIds();
            foreach (var dummy in instance._dummies) if (dummy.EntityId == entityId) return dummy;
            return null;
        }
        public static FixVec2 Flat(Vector3 p) => new FixVec2(Fix64.FromRaw((long)(p.x * Fix64.One.Raw)), Fix64.FromRaw((long)(p.z * Fix64.One.Raw)));
        void LateUpdate()
        {
            RefreshPanel();
            if (_driver?.Session == null || _driver.Session.Mode != GameMode.Camp) return;
            foreach (var e in _driver.FrameEvents)
                if (e.Type == SimEventType.Damage || e.Type == SimEventType.DamageOverTime)
                {
                    var dummy = Find(e.Target);
                    if (dummy == null) continue;
                    dummy.ShowHit(e.Type == SimEventType.DamageOverTime);
                    // Удар по деревянной стойке и изредка её сухой скрип; горение не стучит.
                    if (e.Type == SimEventType.Damage)
                    {
                        GameSound.Play("dummy_hit", .7f, .06f, .05f);
                        if (Random.value < .25f) GameSound.Sequence(("dummy_creak", .1f, .4f));
                    }
                }
        }
        /// <summary>
        /// Панель «Дыма и света»: появляется у манекенов, числа замера, подписи «Манекен» над полосками.
        /// Включение панели и каждой подписи само запускает проявление их UiInkGroup; прозрачность здесь — поверх.
        /// </summary>
        void RefreshPanel()
        {
            if (_panel == null) return;
            bool camp = _driver?.Session != null && _driver.Session.Mode == GameMode.Camp && _dummies != null;
            bool nearby = camp && Nearby() && Camera.main != null;
            _shown = Mathf.MoveTowards(_shown, nearby ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.01f, _panel.Fade));
            if (_panel.Group != null)
            {
                _panel.Group.alpha = _shown;
                _panel.Group.blocksRaycasts = nearby;
                _panel.Group.interactable = nearby;
            }
            if (_panel.gameObject.activeSelf != _shown > 0f) _panel.gameObject.SetActive(_shown > 0f);
            if (_shown <= 0f) return;
            SyncIds();
            var training = _driver.Session.Training;
            SetValue(0, training.DamageTotal);
            SetValue(1, training.DamagePerSecond);
            SetValue(2, training.LastDamage);
            SetValue(3, training.Hits);
            SetValue(4, training.Crits);
            SetValue(5, training.FireDamage);
            var player = CampPlayerView.Instance;
            for (int i = 0; i < _panel.Names.Length; i++)
            {
                var label = _panel.Names[i];
                if (label == null) continue;
                bool show = nearby && i < _dummies.Length && player != null && IsNear(_dummies[i], player.Position);
                Vector3 p = show ? Camera.main.WorldToScreenPoint(_dummies[i].BarPosition) : Vector3.zero;
                show &= p.z > 0f;
                if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
                if (show) label.position = new Vector3(p.x, p.y + 12f * (label.lossyScale.y > 0f ? label.lossyScale.y : 1f), 0f);
            }
        }

        void SetValue(int index, long value)
        {
            if (index >= _panel.Values.Length || _panel.Values[index] == null || _values[index] == value) return;
            _values[index] = value;
            // Разряды — обычным пробелом: «1 240» (тонкого пробела в шрифте может не быть).
            _panel.Values[index].text = value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
        }

        void OnGUI()
        {
            if (_panel != null) return;
            var camp = CampPlayerView.Instance;
            if (camp == null || !camp.Active || camp.InputBlocked || _driver?.Session == null || _dummies == null || _driver.GameplayPaused || Camera.main == null) return;
            SyncIds();
            bool nearby = Nearby();
            foreach (var dummy in _dummies)
            {
                if (!nearby || !IsNear(dummy, camp.Position)) continue;
                Vector3 p = Camera.main.WorldToScreenPoint(dummy.BarPosition);
                if (p.z <= 0) continue;
                var entities = _driver.Sim.Entities;
                GUI.Label(new Rect(p.x - 100, Screen.height - p.y - 30, 200, 25),
                    $"{dummy.Label} · {entities.Health[dummy.EntityId]:N0} / {entities.MaxHealth[dummy.EntityId]:N0}",
                    new GUIStyle(GameTypography.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
            }
            if (!nearby) return;
            var training = _driver.Session.Training;
            var rect = PanelRect;
            GUI.Box(rect, "Тренировка");
            GUI.Label(new Rect(rect.x + 14, rect.y + 28, 244, 100),
                $"Урон: {training.DamageTotal:N0}   DPS: {training.DamagePerSecond:N0}\nПоследний: {training.LastDamage:N0}\nПопадания: {training.Hits}   Криты: {training.Crits}\nОгонь: {training.FireDamage:N0}");
            if (GUI.Button(new Rect(rect.x + 14, rect.y + 126, 238, 26), "Сбросить замер")) training.ResetCounters();
        }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_panel != null) Destroy(_panel.gameObject);
        }
    }
}
