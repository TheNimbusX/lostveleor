using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Экран награды и строка состояния забега.
    ///
    /// Намеренно уродливо: три строчки текста и подсказка по клавишам.
    /// Рисовать это красиво до того, как доказано, что в забег интересно
    /// играть, — работа не в ту сторону. Ровно та же причина, по которой
    /// дерево способностей сейчас список чекбоксов.
    /// </summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed class RunHud : MonoBehaviour
    {
        private TickDriver _driver;
        private GUIStyle _title;
        private GUIStyle _line;
        private Texture2D _white;

        private static readonly Color Panel = new Color(0.035f, 0.04f, 0.065f, 0.90f);
        private static readonly Color Coral = new Color(1.00f, 0.38f, 0.26f, 0.96f);

        private readonly GeneratedItem _itemBuffer = new GeneratedItem();

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
        }

        private void OnGUI()
        {
            // Экран итогов и лагерь рисует CampHud: этот отвечает только
            // за то, что происходит внутри Разлома.
            if (_driver.Session == null || _driver.Session.Mode != GameMode.Rift) return;

            RiftRun run = _driver.Run;
            if (run == null) return;

            EnsureStyles();

            if (run.Phase == RunPhase.Clearing)
            {
                DrawCombatStatus(run);
                return;
            }

            GUILayout.BeginArea(new Rect(16, 16, 560, 400));

            GUILayout.Label($"Разлом {run.Depth}   зачищено {run.RiftsCleared}   " +
                            $"наград {run.TakenRewardCount}", _title);

            switch (run.Phase)
            {
                case RunPhase.ChoosingReward:
                    GUILayout.Label("РАЗЛОМ ЗАЧИЩЕН. Выбери одну награду:", _title);
                    for (int i = 0; i < RiftRun.RewardChoices; i++)
                        GUILayout.Label($"  {i + 1}.  {Describe(run.GetOffer(i), run)}", _line);
                    GUILayout.Label("1–3 — взять и идти глубже,  L — уйти с добычей", _line);
                    break;

                // Ветки Ended здесь нет намеренно: как только забег кончился,
                // сессия тем же тиком уходит на экран итогов, и рисует его CampHud.
            }

            GUILayout.EndArea();
        }

        private void DrawCombatStatus(RiftRun run)
        {
            var panel = new Rect(18f, 18f, 238f, 58f);
            Fill(panel, Panel);
            Fill(new Rect(panel.x, panel.y, 4f, panel.height), Coral);
            GUI.Label(new Rect(32f, 21f, 210f, 26f), $"РАЗЛОМ {run.Depth}", _title);
            GUI.Label(new Rect(32f, 45f, 210f, 22f),
                $"ЦЕЛЕЙ: {run.Sim.CountAliveEnemies()}", _line);
        }

        private void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
        }

        /// <summary>
        /// Человекочитаемое описание награды. Предмет разворачивается из рецепта
        /// прямо здесь: в награде лежит рецепт, а не посчитанные статы.
        /// </summary>
        private string Describe(in RewardOffer offer, RiftRun run)
        {
            switch (offer.Kind)
            {
                case RewardKind.StatBoost:
                    return $"Стат: {offer.Stat} {offer.Op} +{offer.Value.ToFloat() * 100f:0}%";

                case RewardKind.AbilityNode:
                    return "Узел дерева «Печати пламени»";

                case RewardKind.Item:
                    if (!ItemGenerator.Generate(offer.Item, run.Items, _itemBuffer))
                        return "Предмет (база не найдена)";

                    string text = $"Предмет ур.{offer.Item.ItemLevel} {offer.Item.Rarity}, " +
                                  $"аффиксов {_itemBuffer.AffixCount}";
                    for (int i = 0; i < _itemBuffer.AffixCount; i++)
                    {
                        RolledAffix a = _itemBuffer.GetAffix(i);
                        text += $" | {a.Stat} {a.Op} {a.Value.ToFloat():0.##}";
                    }
                    return text;

                default:
                    return "?";
            }
        }

        private void EnsureStyles()
        {
            if (_title != null) return;

            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _line = new GUIStyle(GUI.skin.label) { fontSize = 14 };

            _title.normal.textColor = Color.white;
            _line.normal.textColor = new Color(0.88f, 0.88f, 0.85f);
        }
    }
}
