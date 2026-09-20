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
            => Instance != null && Instance.Nearby() && PanelRect.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        public void Initialize(TickDriver driver)
        {
            Instance = this; _driver = driver;
            _dummies = GetComponentsInChildren<CampDummyView>();
            var definitions = new CampDummyDefinition[_dummies.Length];
            for (int i = 0; i < definitions.Length; i++) definitions[i] = _dummies[i].Definition;
            driver.Session.ConfigureCampTraining(definitions);
            SyncIds();
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
            if (instance == null || instance._driver?.Session.Mode != GameMode.Camp) return null;
            instance.SyncIds();
            foreach (var dummy in instance._dummies) if (dummy.EntityId == entityId) return dummy;
            return null;
        }
        public static FixVec2 Flat(Vector3 p) => new FixVec2(Fix64.FromRaw((long)(p.x * Fix64.One.Raw)), Fix64.FromRaw((long)(p.z * Fix64.One.Raw)));
        void LateUpdate()
        {
            if (_driver?.Session.Mode != GameMode.Camp) return;
            foreach (var e in _driver.FrameEvents)
                if (e.Type == SimEventType.Damage || e.Type == SimEventType.DamageOverTime)
                    Find(e.Target)?.ShowHit(e.Type == SimEventType.DamageOverTime);
        }
        void OnGUI()
        {
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
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
