using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed class CampTrainingView : MonoBehaviour
    {
        public static CampTrainingView Instance { get; private set; }
        [Min(.5f)] public float ActivationDistance = 2.75f;
        CampDummyView[] _dummies;
        TickDriver _driver;
        int _generation = -1;
        // Панель на паке (CampTrainingWc); без префаба — прежняя IMGUI-панель.
        CampTrainingPanel _panel;
        float _shown;
        readonly long[] _values = { -1, -1, -1, -1, -1, -1 };
        bool Nearby()
        {
            var camp = CampPlayerView.Instance;
            if (camp == null || !camp.Active || camp.InputBlocked || _driver == null || _driver.GameplayPaused) return false;
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
            driver.Session.ConfigureCampTraining(definitions);
            SyncIds();
            var prefab = Resources.Load<GameObject>("UI/Prefabs/CampTrainingWc");
            if (prefab != null && _panel == null)
            {
                _panel = Instantiate(prefab).GetComponent<CampTrainingPanel>();
                if (_panel != null)
                {
                    _panel.name = "Тренировка — панель";
                    if (_panel.Group != null) { _panel.Group.alpha = 0f; _panel.Group.blocksRaycasts = false; }
                    if (_panel.Reset != null) _panel.Reset.onClick.AddListener(() => _driver?.Session?.Training.ResetCounters());
                }
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
        /// <summary>Панель на паке: появляется у манекенов, числа замера, подписи «Манекен» над полосками.</summary>
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
            bool nearby = false;
            foreach (var dummy in _dummies)
            {
                if (!IsNear(dummy, camp.Position)) continue;
                nearby = true;
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
