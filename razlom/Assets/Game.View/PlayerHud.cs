using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Боевой HUD: здоровье слева внизу, четыре слота способностей по нижней
    /// кромке.
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
        private Texture2D _white;

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

            DrawHealth(sim);
            DrawAbilities(sim);
        }

        private void DrawHealth(Simulation sim)
        {
            int health = Mathf.Max(0, sim.Entities.Health[Simulation.PlayerId]);
            int max = Mathf.Max(1, sim.Entities.MaxHealth[Simulation.PlayerId]);
            float fill = Mathf.Clamp01(health / (float)max);

            float x = Margin;
            float y = Screen.height - Margin - BarHeight;

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
            float total = Simulation.AbilitySlots * SlotSize + (Simulation.AbilitySlots - 1) * SlotGap;
            float x = (Screen.width - total) * 0.5f;
            float y = Screen.height - Margin - SlotSize;

            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
            {
                var box = new Rect(x + slot * (SlotSize + SlotGap), y, SlotSize, SlotSize);
                AbilityBuild build = sim.GetAbility(slot);

                if (build == null)
                {
                    // Пустой слот рисуется всё равно: игрок должен видеть, что
                    // кнопок четыре и три из них он ещё не получил.
                    Fill(box, SlotEmpty);
                    Frame(box, Ink, 2f);
                    GUI.Label(box, (slot + 1).ToString(), _slotLabel);
                    continue;
                }

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
                    GUI.Label(box, seconds.ToString("0.0"), _slotLabel);
                }

                if (left <= 0) GUI.Label(box, (slot + 1).ToString(), _slotLabel);
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
        }
    }
}
