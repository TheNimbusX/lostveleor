using Game.Sim;
using UnityEngine;
namespace Game.View
{
    /// <summary>Подсветка подтверждаемого врага; не подменяет проверку симуляции.</summary>
    public sealed class PelagTargetAimView : MonoBehaviour
    {
        private TickDriver _driver;
        private LineRenderer _line;
        private Material _material;
        private Texture2D _cursor;
        private bool _aiming;
        private GUIStyle _hint;

        /// <summary>
        /// Радиус кольца прицела по земле, м.
        ///
        /// Примерно с фигуру героя: игрок должен понимать, что притянется
        /// «сюда», а не «в эту точку с точностью до сантиметра».
        /// </summary>
        private const float GroundAimRadius = 0.55f;
        private void OnDisable() { if (_aiming) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); _aiming = false; }
        private void OnGUI()
        {
            if (!_aiming) return;
            if (_hint == null) _hint = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            var point = Event.current.mousePosition;
            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(point.x + 24, point.y + 20, 290, 52), "ВЫБЕРИ ВРАГА\nЛКМ — рывок · ПКМ / Esc — отмена", _hint);
            GUI.color = new Color(1f, 0.97f, 0.88f);
            GUI.Label(new Rect(point.x + 23, point.y + 19, 290, 52), "ВЫБЕРИ ВРАГА\nЛКМ — рывок · ПКМ / Esc — отмена", _hint);
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
            _cursor = new Texture2D(40, 40, TextureFormat.RGBA32, false);
            _cursor.name = "Squall target cursor";
            var pixels = new Color[1600];
            for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++)
            {
                float dx = x - 19.5f, dy = y - 19.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                bool ring = radius >= 10 && radius <= 13;
                bool cross = (Mathf.Abs(dx) < 1.7f || Mathf.Abs(dy) < 1.7f) && radius > 6 && radius < 18;
                bool outline = radius >= 9 && radius <= 14 || ((Mathf.Abs(dx) < 2.7f || Mathf.Abs(dy) < 2.7f) && radius > 5 && radius < 19);
                pixels[y * 40 + x] = ring || cross ? new Color(1, .97f, .88f) : outline ? new Color(.08f,.08f,.1f) : Color.clear;
            }
            _cursor.SetPixels(pixels); _cursor.Apply();
        }
        private void LateUpdate()
        {
            if (_line == null || _driver == null) return;
            bool aiming = _driver.AimingAbilityTarget;
            if (aiming != _aiming)
            {
                _aiming = aiming;
                Cursor.SetCursor(aiming ? _cursor : null, aiming ? new Vector2(20, 20) : Vector2.zero, CursorMode.Auto);
            }
            var sim = _driver.Sim;

            // ПРИЦЕЛ В ТОЧКУ: у Броска якоря нет цели-врага.
            //
            // Кольцо ставится там, куда игрок реально попадёт, а не под
            // курсором: дальность обрезает симуляция, и показывать надо
            // обрезанную точку. Иначе игрок целится за 12 метров, прилетает
            // на 7 и считает это багом.
            if (aiming && sim != null && _driver.GroundTargetedSlot(_driver.AbilityTargetAimSlot))
            {
                Vector3 origin = new Vector3(
                    sim.Entities.Position[Simulation.PlayerId].X.ToFloat(), 0f,
                    sim.Entities.Position[Simulation.PlayerId].Y.ToFloat());
                Vector3 wanted = new Vector3(_driver.CursorWorld.X.ToFloat(), 0f,
                    _driver.CursorWorld.Y.ToFloat());
                Vector3 landing = origin + Vector3.ClampMagnitude(wanted - origin,
                    AnchorKit.LeapRange.ToFloat());
                _line.enabled = true;
                for (int i = 0; i < 48; i++)
                {
                    float a = i * 2f * Mathf.PI / 48;
                    _line.SetPosition(i, new Vector3(landing.x + Mathf.Cos(a) * GroundAimRadius,
                        0.07f, landing.z + Mathf.Sin(a) * GroundAimRadius));
                }
                return;
            }

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
                    0.07f, p.Y.ToFloat() + Mathf.Sin(angle) * radius));
            }
        }
        private void OnDestroy() { OnDisable(); if (_material != null) Destroy(_material); if (_cursor != null) Destroy(_cursor); }
    }
}
