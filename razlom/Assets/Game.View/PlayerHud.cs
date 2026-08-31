using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Боевой HUD: здоровье слева внизу и только реально экипированные
    /// способности по нижней кромке.
    ///
    /// СЕРЕДИНА ЭКРАНА НЕ ЗАНЯТА НИЧЕМ И НИКОГДА. Всё, что накладывается поверх
    /// боя, — конкурент главному аттракциону, поэтому HUD прижат к краям и
    /// не имеет ни рамок, ни подложек во весь экран.
    ///
    /// Ресурса (маны) в игре пока нет: способности стоят на кулдаунах, и
    /// рисовать вторую полосу было бы враньём про систему, которой не существует.
    /// Появится ресурс — появится и полоса, место под неё уже оставлено.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class PlayerHud : MonoBehaviour
    {
        [Header("Полоса здоровья")]
        public float BarWidth = 260f;
        public float BarHeight = 22f;
        public float Margin = 18f;

        [Header("Слоты способностей")]
        public float SlotSize = 54f;
        public float SlotGap = 8f;

        private TickDriver _driver;

        private GUIStyle _label;
        private GUIStyle _slotLabel;
        private GUIStyle _slotKey;
        private GUIStyle _slotName;
        private GUIStyle _cooldownLabel;
        private Texture2D _white;
        private float _canvasWidth;
        private float _canvasHeight;
        private float _safeLeft;
        private float _safeBottom;
        private float _safeCenterX;

        private static readonly Color HealthBack = new Color(0.06f, 0.07f, 0.09f, 0.82f);
        private static readonly Color HealthFill = new Color(0.82f, 0.22f, 0.24f, 0.95f);
        private static readonly Color HealthLow = new Color(1.00f, 0.45f, 0.20f, 0.98f);
        private static readonly Color SlotBack = new Color(0.08f, 0.09f, 0.11f, 0.82f);
        private static readonly Color SlotReady = new Color(0.20f, 0.78f, 0.85f, 0.92f);
        private static readonly Color SlotCooling = new Color(0.16f, 0.20f, 0.26f, 0.92f);
        private static readonly Color SlotEmpty = new Color(0.14f, 0.15f, 0.17f, 0.55f);
        private static readonly Color Ink = new Color(0.07f, 0.025f, 0.065f, 0.96f);
        private static readonly Color Coral = new Color(1.00f, 0.39f, 0.28f, 0.98f);

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void OnGUI()
        {
            GameSession session = _driver.Session;
            if (session == null || session.Mode != GameMode.Rift) return;

            Simulation sim = _driver.Sim;
            if (sim == null) return;
            if (sim.Entities.Count == 0) return;

            EnsureStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Rect safe = Screen.safeArea;
            _canvasWidth = Screen.width / scale;
            _canvasHeight = Screen.height / scale;
            _safeLeft = safe.xMin / scale;
            _safeBottom = (Screen.height - safe.yMax) / scale;
            _safeCenterX = (safe.xMin + safe.width * 0.5f) / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                DrawHealth(sim);
                DrawAbilities(sim);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawHealth(Simulation sim)
        {
            int health = Mathf.Max(0, sim.Entities.Health[Simulation.PlayerId]);
            int max = Mathf.Max(1, sim.Entities.MaxHealth[Simulation.PlayerId]);
            float fill = Mathf.Clamp01(health / (float)max);

            float x = _safeLeft + Margin;
            float y = _canvasHeight - _safeBottom - Margin - BarHeight;

            GUI.Label(new Rect(x, y - 22f, BarWidth, 20f), "ПЕЛАГ", _label);
            Frame(new Rect(x - 2f, y - 2f, BarWidth + 4f, BarHeight + 4f), Ink, 2f);
            Fill(new Rect(x, y, BarWidth, BarHeight), HealthBack);

            // Полоса меняет цвет на четверти: цифры читать некогда, а цвет
            // виден боковым зрением.
            Color fillColor = fill <= 0.25f ? HealthLow : HealthFill;
            Fill(new Rect(x + 2f, y + 2f, (BarWidth - 4f) * fill, BarHeight - 4f), fillColor);

            GUI.Label(new Rect(x + 8f, y, BarWidth, BarHeight), health + " / " + max, _label);
        }

        private void DrawAbilities(Simulation sim)
        {
            int activeCount = 0;
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                if (sim.GetAbility(slot) != null) activeCount++;
            if (activeCount == 0) return;

            float total = activeCount * SlotSize + (activeCount - 1) * SlotGap;
            float x = _safeCenterX - total * 0.5f;
            float y = _canvasHeight - _safeBottom - Margin - SlotSize;
            int visualSlot = 0;

            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
            {
                AbilityBuild build = sim.GetAbility(slot);
                if (build == null) continue;

                var box = new Rect(x + visualSlot * (SlotSize + SlotGap), y, SlotSize, SlotSize);
                visualSlot++;

                int cooldown = build.CooldownTicks;
                int readyAt = sim.AbilityReadyTick(slot);
                int left = readyAt - sim.Tick;

                Fill(box, SlotBack);
                Frame(box, left <= 0 ? Coral : Ink, left <= 0 ? 3f : 2f);

                if (left <= 0)
                {
                    Fill(Inset(box, 3f), SlotReady);
                }
                else
                {
                    Fill(Inset(box, 3f), SlotCooling);

                    // Заливка растёт снизу вверх по мере остывания: так это
                    // читается в жанре, и так видно «почти готово».
                    float ready = cooldown > 0 ? 1f - Mathf.Clamp01(left / (float)cooldown) : 1f;
                    var inner = Inset(box, 3f);
                    Fill(new Rect(inner.x, inner.yMax - inner.height * ready,
                        inner.width, inner.height * ready), SlotReady);

                    float seconds = left / (float)Simulation.TicksPerSecond;
                    Rect shadow = new Rect(box.x + 1f, box.y + 1f, box.width, box.height);
                    GUI.Label(shadow, seconds.ToString("0.0"), _slotLabel);
                    GUI.Label(box, seconds.ToString("0.0"), _cooldownLabel);
                }

                string abilityName = build.DefinitionId == AbilityDefinition.WhirlwindId
                    ? "ВИХРЬ"
                    : "СПОСОБНОСТЬ";
                GUI.Label(new Rect(box.x - 32f, box.y - 20f, box.width + 64f, 18f),
                    abilityName, _slotName);

                // Кнопка остаётся видимой и во время cooldown; число в центре
                // теперь означает только время, а не внезапно сменившуюся клавишу.
                Rect key = new Rect(box.x + 4f, box.y + 4f, 18f, 18f);
                Fill(key, Ink);
                GUI.Label(key, (slot + 1).ToString(), _slotKey);
            }
        }

        private static Rect Inset(Rect r, float by)
            => new Rect(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);

        private void Fill(Rect rect, Color color)
        {
            Color was = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = was;
        }

        private void Frame(Rect rect, Color color, float width)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, width), color);
            Fill(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Fill(new Rect(rect.x, rect.y, width, rect.height), color);
            Fill(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private void EnsureStyles()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }

            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            _label.normal.textColor = new Color(1f, 0.96f, 0.92f);

            _slotLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _slotLabel.normal.textColor = new Color(0.06f, 0.08f, 0.10f);

            _cooldownLabel = new GUIStyle(_slotLabel);
            _cooldownLabel.normal.textColor = new Color(1f, 0.97f, 0.90f);

            _slotKey = new GUIStyle(_slotLabel)
            {
                fontSize = 12,
            };
            _slotKey.normal.textColor = Color.white;

            _slotName = new GUIStyle(_slotLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
            };
            _slotName.normal.textColor = new Color(1f, 0.92f, 0.80f);
        }
    }
}
