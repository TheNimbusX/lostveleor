using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Состояние забега и центральный экран выбора награды.</summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class RunHud : MonoBehaviour
    {
        private TickDriver _driver;
        private GUIStyle _title;
        private GUIStyle _subtitle;
        private GUIStyle _body;
        private GUIStyle _eyebrow;
        private GUIStyle _cardButton;
        private Texture2D _white;
        private readonly GeneratedItem _itemBuffer = new GeneratedItem();

        private static readonly Color Panel = new Color(0.94f, 0.84f, 0.68f, 0.97f);
        private static readonly Color Card = new Color(0.98f, 0.91f, 0.76f, 0.98f);
        private static readonly Color CardHover = new Color(1.00f, 0.96f, 0.87f, 1f);
        private static readonly Color Ink = new Color(0.20f, 0.10f, 0.065f, 0.98f);
        private static readonly Color Coral = new Color(0.78f, 0.24f, 0.17f, 0.98f);
        private static readonly Color Gold = new Color(0.77f, 0.48f, 0.14f, 0.98f);
        private static readonly Color Cyan = new Color(0.10f, 0.48f, 0.49f, 0.98f);

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void OnGUI()
        {
            if (_driver.GameplayPaused) return;
            if (_driver.Session == null || _driver.Session.Mode != GameMode.Rift) return;

            RiftRun run = _driver.Run;
            if (run == null) return;
            EnsureStyles();

            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Rect safe = Screen.safeArea;
            float canvasWidth = Screen.width / scale;
            float canvasHeight = Screen.height / scale;
            float safeLeft = safe.xMin / scale;
            float safeRight = canvasWidth - safe.xMax / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                if (run.Phase == RunPhase.Clearing)
                    DrawCombatStatus(run, safeLeft);
                else if (run.Phase == RunPhase.ChoosingReward)
                    DrawRewardChoice(run, canvasWidth, canvasHeight, safeLeft, safeRight);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawCombatStatus(RiftRun run, float safeLeft)
        {
            Rect panel = new Rect(safeLeft + 18f, 18f, 230f, 62f);
            Fill(panel, Panel);
            Fill(new Rect(panel.x, panel.y, 4f, panel.height), Coral);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 6f, 205f, 26f),
                "РАЗЛОМ  " + run.Depth, _title);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 34f, 205f, 20f),
                "ЦЕЛЕЙ: " + run.Sim.CountAliveEnemies(), _subtitle);
        }

        private void DrawRewardChoice(RiftRun run, float canvasWidth, float canvasHeight,
            float safeLeft, float safeRight)
        {
            float panelWidth = Mathf.Min(920f, canvasWidth - safeLeft - safeRight - 32f);
            float panelHeight = Mathf.Min(410f, canvasHeight - 34f);
            Rect panel = new Rect((canvasWidth - panelWidth) * 0.5f, (canvasHeight - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            Fill(panel, Panel);
            Frame(panel, Ink, 1f);
            Fill(new Rect(panel.x, panel.y, panel.width, 5f), Coral);

            GUI.Label(new Rect(panel.x + 28f, panel.y + 20f, panel.width - 56f, 30f),
                "РАЗЛОМ ЗАЧИЩЕН", _title);
            GUI.Label(new Rect(panel.x + 28f, panel.y + 51f, panel.width - 56f, 22f),
                "Выбери награду и продолжи путь", _subtitle);

            float gap = 12f;
            float cardsTop = panel.y + 88f;
            float cardsHeight = panel.height - 136f;
            float cardWidth = (panel.width - 56f - gap * 2f) / RiftRun.RewardChoices;
            for (int i = 0; i < RiftRun.RewardChoices; i++)
            {
                Rect card = new Rect(panel.x + 28f + i * (cardWidth + gap), cardsTop,
                    cardWidth, cardsHeight);
                DrawOfferCard(card, i, run.GetOffer(i), run);
                if (GUI.Button(card, GUIContent.none, _cardButton))
                    _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseReward1 + i));
            }

            GUI.Label(new Rect(panel.x + 28f, panel.yMax - 35f, panel.width - 56f, 22f),
                "1–3  выбрать награду     L  уйти с добычей", _subtitle);
        }

        private void DrawOfferCard(Rect card, int index, in RewardOffer offer, RiftRun run)
        {
            Color accent = offer.Kind == RewardKind.Item ? Gold
                : offer.Kind == RewardKind.StatBoost ? Cyan : Coral;
            Fill(card, Card);
            Frame(card, accent, 1f);
            Fill(new Rect(card.x, card.y, card.width, 4f), accent);

            Rect badge = new Rect(card.x + 14f, card.y + 14f, 34f, 34f);
            Fill(badge, accent);
            GUI.Label(badge, (index + 1).ToString(), _cardButton);

            string kind = offer.Kind == RewardKind.Item ? "ПРЕДМЕТ"
                : offer.Kind == RewardKind.StatBoost ? "СТАТ"
                : "УЗЕЛ СПОСОБНОСТИ";
            GUI.Label(new Rect(card.x + 58f, card.y + 15f, card.width - 72f, 18f), kind, _eyebrow);

            if (offer.Kind == RewardKind.AbilityNode)
            {
                GUI.Label(new Rect(card.x + 16f, card.y + 72f, card.width - 32f, 42f),
                    NodeTitle(offer.Node), _title);
                GUI.Label(new Rect(card.x + 16f, card.y + 124f, card.width - 32f, card.height - 138f),
                    NodeDescription(offer.Node), _body);
                return;
            }

            if (offer.Kind == RewardKind.StatBoost)
            {
                GUI.Label(new Rect(card.x + 16f, card.y + 72f, card.width - 32f, 54f),
                    StatTitle(offer.Stat), _title);
                GUI.Label(new Rect(card.x + 16f, card.y + 132f, card.width - 32f, card.height - 150f),
                    (offer.Op == ModifierOp.Flat ? "+" + offer.Value.ToFloat().ToString("0.##")
                        : "+" + (offer.Value.ToFloat() * 100f).ToString("0.#") + "%") + " к характеристике", _body);
                return;
            }

            if (!ItemGenerator.Generate(offer.Item, run.Items, _itemBuffer))
            {
                GUI.Label(new Rect(card.x + 16f, card.y + 76f, card.width - 32f, 70f),
                    "Предмет не найден", _body);
                return;
            }

            string itemName = _itemBuffer.Category == ItemCategory.Weapon ? "РЖАВЫЙ МЕЧ" : "КОЖАНАЯ КУРТКА";
            GUI.Label(new Rect(card.x + 16f, card.y + 72f, card.width - 32f, 32f), itemName, _title);
            GUI.Label(new Rect(card.x + 16f, card.y + 110f, card.width - 32f, 20f),
                "УР. " + offer.Item.ItemLevel + "  /  " + offer.Item.Rarity.ToString().ToUpperInvariant(), _eyebrow);
            string details = ItemDetails(_itemBuffer);
            GUI.Label(new Rect(card.x + 16f, card.y + 144f, card.width - 32f, card.height - 158f),
                details, _body);
        }

        private static string ItemDetails(GeneratedItem item)
        {
            string text = item.HasImplicit
                ? StatTitle(item.ImplicitStat) + "  " + ValueText(item.ImplicitOp, item.ImplicitValue)
                : string.Empty;
            for (int i = 0; i < item.AffixCount; i++)
            {
                RolledAffix affix = item.GetAffix(i);
                if (text.Length > 0) text += "\n";
                text += StatTitle(affix.Stat) + "  " + ValueText(affix.Op, affix.Value);
            }
            return text.Length == 0 ? "Без дополнительных свойств" : text;
        }

        private static string ValueText(ModifierOp op, Fix64 value)
            => op == ModifierOp.Flat ? "+" + value.ToFloat().ToString("0.##")
                : "+" + (value.ToFloat() * 100f).ToString("0.#") + "%";

        private static string StatTitle(StatType stat)
        {
            switch (stat)
            {
                case StatType.MaxHealth: return "МАКС. ЗДОРОВЬЕ";
                case StatType.Damage: return "УРОН";
                case StatType.AttackSpeed: return "СКОРОСТЬ АТАКИ";
                case StatType.MoveSpeed: return "СКОРОСТЬ ДВИЖЕНИЯ";
                case StatType.CritChance: return "ШАНС КРИТА";
                case StatType.CritMultiplier: return "МНОЖИТЕЛЬ КРИТА";
                case StatType.Armor: return "БРОНЯ";
                case StatType.FireResist: return "СОПРОТИВЛЕНИЕ ОГНЮ";
                default: return "ХАРАКТЕРИСТИКА";
            }
        }

        private static string NodeTitle(AbilityNode node)
            => node.Kind == NodeKind.Flag ? "РАЗДЕЛЁННЫЙ ЗНАК"
                : node.Kind == NodeKind.EffectInsert ? "РАСПРОСТРАНЕНИЕ ОГНЯ" : "ГОРЯЧАЯ ПЕЧАТЬ";

        private static string NodeDescription(AbilityNode node)
            => node.Kind == NodeKind.Flag ? "Печать выпускает три снаряда. Урон каждого снижен на 45%."
                : node.Kind == NodeKind.EffectInsert ? "Горящий враг при смерти поджигает ближайшего противника."
                : "Увеличивает урон Печати пламени на 20%.";

        private void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
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
            if (_title != null) return;

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
            };
            _title.normal.textColor = Ink;
            _subtitle = new GUIStyle(_title) { fontSize = 13, fontStyle = FontStyle.Normal };
            _subtitle.normal.textColor = new Color(0.30f, 0.20f, 0.14f);
            _body = new GUIStyle(_subtitle) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft };
            _body.normal.textColor = new Color(0.25f, 0.16f, 0.11f);
            _eyebrow = new GUIStyle(_subtitle) { fontSize = 11, fontStyle = FontStyle.Bold };
            _eyebrow.normal.textColor = new Color(0.56f, 0.27f, 0.12f);
            _cardButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            _cardButton.normal.background = MakeTexture(new Color(0f, 0f, 0f, 0f));
            _cardButton.hover.background = MakeTexture(CardHover);
            _cardButton.active.background = MakeTexture(new Color(0.18f, 0.20f, 0.27f, 1f));
            _cardButton.normal.textColor = Color.white;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
