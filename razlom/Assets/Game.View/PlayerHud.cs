using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Боевой HUD: состояние героя, способности и миникарта Разлома.</summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class PlayerHud : MonoBehaviour
    {
        [Header("Полоса здоровья")]
        public float BarWidth = 238f;
        public float BarHeight = 24f;
        public float Margin = 18f;

        [Header("Слоты способностей")]
        public float SlotSize = 64f;
        public float SlotGap = 10f;

        [Header("Миникарта")]
        public float MinimapWidth = 236f;
        public float MinimapHeight = 164f;

        private TickDriver _driver;
        private GUIStyle _label;
        private GUIStyle _slotLabel;
        private GUIStyle _slotKey;
        private GUIStyle _slotName;
        private GUIStyle _cooldownLabel;
        private GUIStyle _mapLabel;
        private Texture2D _white;
        // Иконка на каждую способность, а не одна на Вихрь. Нарезаны с
        // утверждённого листа `ART/.../PELAG/abilitys.png`; там же лежит
        // разбор того, что каждая делает, — если понадобится перерезать,
        // источник один и он в репозитории.
        private Texture2D[] _abilityIcons;
        private bool _abilityIconsLoaded;
        private Texture2D _mapArtwork;
        private float _canvasWidth;
        private float _canvasHeight;
        private float _safeLeft;
        private float _safeRight;
        private float _safeBottom;

        private static readonly Color HealthBack = new Color(0.16f, 0.09f, 0.07f, 0.90f);
        private static readonly Color HealthFill = new Color(0.86f, 0.23f, 0.19f, 0.98f);
        private static readonly Color HealthLow = new Color(1.00f, 0.47f, 0.16f, 0.98f);
        private static readonly Color SlotBack = new Color(0.13f, 0.075f, 0.06f, 0.92f);
        private static readonly Color SlotReady = new Color(0.97f, 0.58f, 0.28f, 0.58f);
        private static readonly Color SlotCooling = new Color(0.10f, 0.065f, 0.07f, 0.68f);
        private static readonly Color Ink = new Color(0.16f, 0.07f, 0.045f, 0.98f);
        private static readonly Color Coral = new Color(0.91f, 0.27f, 0.20f, 0.98f);
        private static readonly Color Gold = new Color(1.00f, 0.78f, 0.43f, 0.98f);
        private static readonly Color MapBack = new Color(0.16f, 0.09f, 0.07f, 0.84f);
        private static readonly Color MapRoom = new Color(0.21f, 0.60f, 0.58f, 0.86f);
        private static readonly Color MapEntrance = new Color(0.95f, 0.38f, 0.22f, 0.96f);

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void OnGUI()
        {
            if (_driver.GameplayPaused) return;
            GameSession session = _driver.Session;
            if (session == null || session.Mode != GameMode.Rift) return;

            Simulation sim = _driver.Sim;
            RiftRun run = _driver.Run;
            if (sim == null || run == null || sim.Entities.Count == 0) return;

            EnsureStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Rect safe = Screen.safeArea;
            _canvasWidth = Screen.width / scale;
            _canvasHeight = Screen.height / scale;
            _safeLeft = safe.xMin / scale;
            _safeRight = _canvasWidth - safe.xMax / scale;
            _safeBottom = (Screen.height - safe.yMax) / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                DrawMinimap(run, sim);
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

            int activeCount = 0;
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                if (sim.GetAbility(slot) != null) activeCount++;
            float total = activeCount > 0 ? activeCount * SlotSize + (activeCount - 1) * SlotGap : 0f;
            float groupWidth = BarWidth + (activeCount > 0 ? 34f + total : 0f);
            float groupX = _canvasWidth * 0.5f - groupWidth * 0.5f;
            float groupMaxX = Mathf.Max(_safeLeft + Margin,
                _canvasWidth - _safeRight - Margin - groupWidth);
            groupX = Mathf.Clamp(groupX, _safeLeft + Margin, groupMaxX);
            float x = groupX;
            float y = _canvasHeight - _safeBottom - Margin - BarHeight;
            Rect panel = new Rect(x - 10f, y - 28f, BarWidth + 20f, BarHeight + 38f);

            Fill(panel, HealthBack);
            Frame(panel, Ink, 1f);
            GUI.Label(new Rect(x, y - 25f, BarWidth, 20f), "ПЕЛАГ  /  ЖИЗНЬ", _label);
            Fill(new Rect(x, y, BarWidth, BarHeight), new Color(0.25f, 0.13f, 0.10f, 1f));
            Color fillColor = fill <= 0.25f ? HealthLow : HealthFill;
            Fill(new Rect(x + 3f, y + 3f, (BarWidth - 6f) * fill, BarHeight - 6f), fillColor);
            GUI.Label(new Rect(x + 8f, y + 1f, BarWidth - 16f, BarHeight - 2f),
                health + " / " + max, _label);
        }

        private void DrawAbilities(Simulation sim)
        {
            int activeCount = 0;
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
                if (sim.GetAbility(slot) != null) activeCount++;
            if (activeCount == 0) return;

            float total = activeCount * SlotSize + (activeCount - 1) * SlotGap;
            float groupWidth = BarWidth + 34f + total;
            float groupX = _canvasWidth * 0.5f - groupWidth * 0.5f;
            float groupMaxX = Mathf.Max(_safeLeft + Margin,
                _canvasWidth - _safeRight - Margin - groupWidth);
            groupX = Mathf.Clamp(groupX, _safeLeft + Margin, groupMaxX);
            float x = groupX + BarWidth + 34f;
            float y = _canvasHeight - _safeBottom - Margin - SlotSize;
            GUI.Label(new Rect(x, y - 25f, total, 20f), "СПОСОБНОСТИ", _slotName);

            int visualSlot = 0;
            for (int slot = 0; slot < Simulation.AbilitySlots; slot++)
            {
                AbilityBuild build = sim.GetAbility(slot);
                if (build == null) continue;

                Rect box = new Rect(x + visualSlot * (SlotSize + SlotGap), y, SlotSize, SlotSize);
                visualSlot++;
                int left = sim.AbilityReadyTick(slot) - sim.Tick;
                Fill(box, SlotBack);
                Frame(box, left <= 0 ? Gold : Ink, 1f);

                if (left <= 0) Fill(Inset(box, 3f), SlotReady);
                else
                {
                    Fill(Inset(box, 3f), SlotCooling);
                    float ready = 1f - Mathf.Clamp01(left / (float)Mathf.Max(1, build.CooldownTicks));
                    Rect inner = Inset(box, 3f);
                    Fill(new Rect(inner.x, inner.yMax - inner.height * ready,
                        inner.width, inner.height * ready), SlotReady);
                }

                Texture2D icon = AbilityIcon(slot, build.DefinitionId);
                if (icon != null)
                {
                    Color previous = GUI.color;
                    // Остывающая способность гасится и обесцвечивается: игрок
                    // читает готовность по иконке боковым зрением, не считая
                    // цифру. Полоса заполнения снизу говорит то же самое, но
                    // медленнее — она про «сколько осталось», иконка про
                    // «можно или нет».
                    GUI.color = left <= 0 ? Color.white : new Color(0.68f, 0.72f, 0.78f, 0.78f);
                    GUI.DrawTexture(Inset(box, 6f), icon, ScaleMode.ScaleToFit, true);
                    GUI.color = previous;
                }

                // Рисуем время поверх иллюстрации, чтобы яркий арт не прятал
                // самый важный боевой сигнал.
                if (left > 0)
                {
                    float seconds = left / (float)Simulation.TicksPerSecond;
                    GUI.Label(new Rect(box.x + 1f, box.y + 1f, box.width, box.height),
                        seconds.ToString("0.0"), _slotLabel);
                    GUI.Label(box, seconds.ToString("0.0"), _cooldownLabel);
                }

                GUI.Label(new Rect(box.x + 4f, box.y + 4f, 20f, 20f), (slot + 1).ToString(), _slotKey);
                GUI.Label(new Rect(box.x - 24f, box.yMax + 4f, box.width + 48f, 18f),
                    AbilityName(build.DefinitionId), _slotName);
            }
        }

        /// <summary>
        /// Имя способности под слотом.
        ///
        /// Иконка пока одна — у Вихря; у остальных трёх слот был бы пустым
        /// квадратом с цифрой, и игрок не знал бы, что нажимает. Подпись стоит
        /// ничего и снимает вопрос до появления иконок.
        ///
        /// Разбор по DefinitionId, а не по номеру слота: слот — это позиция на
        /// панели, и она уже один раз переехала.
        /// </summary>
        /// <summary>
        /// Иконка слота. Кэшируется по слоту, ищется по способности.
        ///
        /// Загрузка ленивая и одноразовая на слот: `Resources.Load` в OnGUI
        /// звался бы шестьдесят раз в секунду на каждый слот.
        /// </summary>
        private Texture2D AbilityIcon(int slot, int definitionId)
        {
            if (_abilityIcons == null || (uint)slot >= (uint)_abilityIcons.Length) return null;
            if (_abilityIcons[slot] != null) return _abilityIcons[slot];

            string file = IconFile(definitionId);
            if (file == null) return null;

            _abilityIcons[slot] = Resources.Load<Texture2D>("UI/Abilities/" + file);
            return _abilityIcons[slot];
        }

        private static string IconFile(int definitionId)
        {
            if (definitionId == AbilityDefinition.WhirlwindId) return "Icon_Whirlwind";
            if (definitionId == AbilityDefinition.AnchorLeapId) return "Icon_AnchorLeap";
            if (definitionId == AbilityDefinition.AnchorSweepId) return "Icon_AnchorSweep";
            if (definitionId == AbilityDefinition.ChainStepId) return "Icon_ChainStep";
            return null;
        }

        private static string AbilityName(int definitionId)
        {
            if (definitionId == AbilityDefinition.WhirlwindId) return "ВИХРЬ";
            if (definitionId == AbilityDefinition.AnchorLeapId) return "БРОСОК ЯКОРЯ";
            if (definitionId == AbilityDefinition.AnchorSweepId) return "ПОДСЕЧКА";
            if (definitionId == AbilityDefinition.ChainStepId) return "ШАГ ПО ЦЕПИ";
            return "СПОСОБНОСТЬ";
        }

        private void DrawMinimap(RiftRun run, Simulation sim)
        {
            LayoutMap map = run.Map;
            if (map == null || map.PlacedCount == 0) return;

            float width = Mathf.Max(180f, MinimapWidth);
            float height = Mathf.Max(120f, MinimapHeight);
            Rect panel = new Rect(_canvasWidth - _safeRight - Margin - width, 18f, width, height);
            Fill(panel, MapBack);
            if (_mapArtwork != null)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.78f);
                GUI.DrawTexture(panel, _mapArtwork, ScaleMode.ScaleToFit, true);
                GUI.color = previous;
            }
            Frame(panel, Ink, 1f);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 8f, width - 24f, 18f),
                "КАРТА РАЗЛОМА", _mapLabel);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < map.PlacedCount; i++)
            {
                PlacedModule placed = map.GetPlaced(i);
                minX = Mathf.Min(minX, placed.OriginX);
                minY = Mathf.Min(minY, placed.OriginY);
                maxX = Mathf.Max(maxX, placed.OriginX + placed.Width);
                maxY = Mathf.Max(maxY, placed.OriginY + placed.Height);
            }

            // Референс занимает широкую плашку, но сама карта находится в
            // круглом левом медальоне. Держим live-схему в этой области, чтобы
            // она не залезала на декоративный штандарт справа.
            float mapX = panel.x + 20f, mapY = panel.y + 32f;
            float mapW = width * 0.56f, mapH = height - 44f;
            float spanX = Mathf.Max(1f, maxX - minX), spanY = Mathf.Max(1f, maxY - minY);
            float mapScale = Mathf.Min(mapW / spanX, mapH / spanY);
            float offsetX = mapX + (mapW - spanX * mapScale) * 0.5f;
            float offsetY = mapY + (mapH - spanY * mapScale) * 0.5f;

            for (int i = 0; i < map.PlacedCount; i++)
            {
                PlacedModule placed = map.GetPlaced(i);
                Rect room = new Rect(offsetX + (placed.OriginX - minX) * mapScale,
                    offsetY + (maxY - placed.OriginY - placed.Height) * mapScale,
                    Mathf.Max(3f, placed.Width * mapScale), Mathf.Max(3f, placed.Height * mapScale));
                Fill(room, i == 0 ? MapEntrance : MapRoom);
                Frame(room, new Color(0.72f, 0.85f, 0.84f, 0.8f), 1f);
            }

            EntityStore entities = sim.Entities;
            float cell = LayoutMap.CellSize.ToFloat();
            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                FixVec2 position = entities.Position[i];
                float px = offsetX + (position.X.ToFloat() / cell - minX) * mapScale;
                float py = offsetY + (maxY - position.Y.ToFloat() / cell) * mapScale;
                float radius = i == Simulation.PlayerId ? 4.5f : 2.5f;
                Fill(new Rect(px - radius, py - radius, radius * 2f, radius * 2f),
                    i == Simulation.PlayerId ? Gold : new Color(0.94f, 0.26f, 0.28f, 0.92f));
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
            if (!_abilityIconsLoaded)
            {
                _abilityIconsLoaded = true;
                _abilityIcons = new Texture2D[Simulation.AbilitySlots];
            }
            if (_mapArtwork == null)
                _mapArtwork = Resources.Load<Texture2D>("UI/MapHud");
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
            };
            _label.normal.textColor = new Color(1f, 0.96f, 0.92f);
            _slotLabel = new GUIStyle(_label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            _slotLabel.normal.textColor = new Color(0.06f, 0.08f, 0.10f);
            _cooldownLabel = new GUIStyle(_slotLabel);
            _cooldownLabel.normal.textColor = new Color(1f, 0.97f, 0.90f);
            _slotKey = new GUIStyle(_slotLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _slotKey.normal.textColor = Color.white;
            _slotName = new GUIStyle(_slotLabel) { fontSize = 11 };
            _slotName.normal.textColor = new Color(1f, 0.92f, 0.80f);
            _mapLabel = new GUIStyle(_slotName) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            _mapLabel.normal.textColor = new Color(0.96f, 0.86f, 0.66f);
        }
    }
}
