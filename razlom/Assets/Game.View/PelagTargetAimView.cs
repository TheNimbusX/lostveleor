using Game.Sim;
using UnityEngine;
namespace Game.View
{
    /// <summary>
    /// Подсветка подтверждаемого врага (Шквал, Абордаж — с 02.10 тоже по врагу); не подменяет проверку симуляции.
    /// </summary>
    public sealed class PelagTargetAimView : MonoBehaviour
    {
        private TickDriver _driver;
        private LineRenderer _line;
        private Material _material;
        private bool _aiming;
        private GUIStyle _hint;

        private void OnDisable() { _aiming = false; }
        private void OnGUI()
        {
            // Подсказку рисует пак (RunWorldView) — IMGUI только запасной вид.
            if (!_aiming || RunWorldView.AimHintShown) return;
            if (_hint == null) _hint = new GUIStyle(GameTypography.Label) { fontSize = 15, fontStyle = FontStyle.Bold };
            var point = _driver != null && _driver.UsingGamepad
                ? new Vector2(Screen.width * .5f - 120f, Screen.height * .73f)
                : Event.current.mousePosition;
            bool ground = _driver != null && _driver.GroundTargetedSlot(_driver.AbilityTargetAimSlot);
            string text = (ground ? "Выбери точку" : "Выбери врага")
                + (_driver != null && _driver.UsingGamepad
                    ? "\n" + UiKeyHint.AimPad
                    : "\n" + UiKeyHint.AimMouse);
            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(point.x + 24, point.y + 20, 290, 52), text, _hint);
            GUI.color = new Color(1f, 0.97f, 0.88f);
            GUI.Label(new Rect(point.x + 23, point.y + 19, 290, 52), text, _hint);
            GUI.color = previousColor;
        }
        private void Start()
        {
            _driver = GetComponent<TickDriver>();
            var root = new GameObject("Squall target selection");
            root.transform.SetParent(transform, false);
            _line = root.AddComponent<LineRenderer>();
            _material = new Material(Shader.Find("Sprites/Default"));
            _line.sharedMaterial = _material;
            _line.loop = true; _line.positionCount = 48; _line.widthMultiplier = 0.045f;
            _line.startColor = _line.endColor = new Color(1f, 0.97f, 0.88f);
            _line.enabled = false;
        }
        private void LateUpdate()
        {
            if (_line == null || _driver == null) return;
            bool aiming = _driver.AimingAbilityTarget;
            if (aiming != _aiming)
            {
                _aiming = aiming;
            }
            var sim = _driver.Sim;
            float groundHeight = CampPlayerView.Instance?.Active == true ? CampPlayerView.Instance.GroundHeight : 0f;

            // Кольцо на точке пола (прицел «в точку» Броска якоря) убрано 02.10: Абордаж выбирает врага,
            // как Шквал, — подсветка одна на оба навыка. Пунктир к посадке рисует HUD (PlayerHud.DrawAbilityReach).
            int target = _driver.HoveredEntity;
            bool valid = _driver.AimingAbilityTarget && sim != null && target > 0
                && sim.ValidAbilityTarget(target, sim.GetAbility(_driver.AbilityTargetAimSlot));
            _line.enabled = valid;
            if (!valid) return;
            FixVec2 p = sim.Entities.Position[target];
            float radius = sim.Entities.BodyRadius[target].ToFloat() + 0.18f;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * 2f * Mathf.PI / 48;
                _line.SetPosition(i, new Vector3(p.X.ToFloat() + Mathf.Cos(angle) * radius,
                    groundHeight + .07f, p.Y.ToFloat() + Mathf.Sin(angle) * radius));
            }
        }
        private void OnDestroy() { OnDisable(); if (_material != null) Destroy(_material); }
    }
}
