using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    public static partial class PauseMenuWcBuilder
    {
        // ---------------------------------------------------------------- управление
        static void BuildControls(RectTransform root, PauseMenuView view)
        {
            RectTransform content = Window(root, "Управление");
            view.ControlsPanel = (RectTransform)content.parent;

            RectTransform title = Box(Node("Заголовок", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -26f), new Vector2(600f, 56f));
            Label(title, "Надпись", "Управление", FontRole.Heading, 38f, Role.Text, TextAlignmentOptions.Center, 1f);
            Box(Place("Divider", content, "Линия"), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -94f), new Vector2(420f, 16f));

            view.AbilityLayout = Segmented(Control(Row(content, 0, "Движение", "Как ходить: мышью или клавишами."), 320f), 2);
            RectTransform hint = Box(Node("Пояснение", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -182f), new Vector2(RowW, 40f));
            view.ControlsHint = Label(hint, "Надпись", "", FontRole.Body, 17f, Role.TextMuted);

            RectTransform columns = Box(Node("Колонки", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -226f), new Vector2(RowW, 410f));
            view.BindingsCombat = Column(columns, "Бой", 0f);
            view.BindingsWorld = Column(columns, "Мир", 470f);
            view.BindingTemplate = BindingRow(view.BindingsCombat);

            // Мышь не переназначается — притушенная строка-напоминание под пятью строками «Боя»
            // (в «Мире» строк семь, там места нет).
            RectTransform mouse = Box(Node("Мышь", (RectTransform)view.BindingsCombat.parent), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -40f - 5f * 52f - 10f), new Vector2(450f, 46f));
            mouse.gameObject.AddComponent<CanvasGroup>().alpha = .8f;
            Layer(mouse, "Полоса", T.FillSmall, Role.Track, .45f);
            RectTransform action = Box(Node("Действие", mouse), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(260f, 34f));
            Label(action, "Надпись", "Идти · атаковать", FontRole.Body, 18f, Role.TextMuted);
            RectTransform keys = Box(Node("Клавиши", mouse), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-22f, 0f), new Vector2(160f, 34f));
            Label(keys, "Надпись", "ПКМ · ЛКМ", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.MidlineRight);

            Footer(content, false, out view.ControlsReset, out _, out TMP_Text status, out view.ControlsBack);
            view.ControlsBack.gameObject.SetActive(true);
            // Строки клавиш свою подсказку пишут в ControlsHint; описание футера у управления не нужно.
            Object.DestroyImmediate(status.transform.parent.gameObject);
            view.ControlsPanel.gameObject.SetActive(false);
        }

        static RectTransform Column(RectTransform parent, string title, float left)
        {
            RectTransform column = Box(Node(title, parent), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, 0f), new Vector2(450f, 410f));
            RectTransform head = Box(Node("Заголовок", column), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(440f, 32f));
            TMP_Text label = Label(head, "Надпись", title, FontRole.Heading, 22f, Role.TextMuted, TextAlignmentOptions.MidlineLeft, 3f);
            label.fontStyle = FontStyles.UpperCase;
            RectTransform rows = Node("Строки", column);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(.5f, 1f);
            rows.anchoredPosition = new Vector2(0f, -40f);
            rows.sizeDelta = new Vector2(0f, 370f);
            var list = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 6f;
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            return rows;
        }

        /// <summary>Строка клавиши: действие слева, клавиша пака справа; ожидание — пульсирующее оранжевое свечение.</summary>
        static UiKeyBindingRow BindingRow(RectTransform column)
        {
            RectTransform row = Node("Шаблон клавиши", column);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = 46f;
            Layer(row, "Полоса", T.FillSmall, Role.Track, .6f).raycastTarget = true;
            var binding = row.gameObject.AddComponent<UiKeyBindingRow>();
            Image flash = Layer(row, "Вспышка", T.FillSmall, Role.Accent, .3f);
            binding.FlashGraphic = flash;
            flash.gameObject.SetActive(false);
            RectTransform action = Box(Node("Действие", row), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(260f, 36f));
            binding.Action = Label(action, "Надпись", "Действие", FontRole.Body, 18f, Role.Text);
            binding.Action.enableAutoSizing = true;
            binding.Action.fontSizeMin = 13f;
            binding.Action.fontSizeMax = 18f;
            binding.Action.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform key = Box(Node("Клавиша", row), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-8f, 0f), new Vector2(130f, 34f));
            RectTransform waiting = Stretch(Node("Ожидание", key), -12f);
            Image glow = Layer(waiting, "Свечение", T.GlowSmall, Role.Accent, .9f, 12f);
            waiting.gameObject.AddComponent<UiPulse>().Graphic = glow;
            waiting.gameObject.SetActive(false);
            binding.Waiting = waiting.gameObject;
            Image fill = Layer(key, "Заливка", T.FillSmall, Role.Panel);
            fill.raycastTarget = true;
            Layer(key, "Свет по кромке", T.HighlightSmall, Role.Highlight);
            Layer(key, "Рамка", T.FrameSmall, Role.PanelLine, .8f);
            var button = key.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            binding.Key = button;
            var motion = key.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = Layer(key, "Наведение", T.FrameBoldSmall, Role.Accent);
            binding.KeyLabel = Label(key, "Буква", "Q", FontRole.Body, 18f, Role.Text, TextAlignmentOptions.Center);
            binding.KeyLabel.fontStyle = FontStyles.Bold;
            // Цвет клавиши (своя или стандартная) ведёт UiKeyBindingRow.
            Object.DestroyImmediate(binding.KeyLabel.GetComponent<ThemeColor>());
            binding.KeyDefault = T.Text;
            binding.KeyCustom = T.Accent;
            row.gameObject.SetActive(false);
            return binding;
        }

        // ---------------------------------------------------------------- подтверждение
        static void BuildConfirm(RectTransform root, PauseMenuView view)
        {
            RectTransform panel = Place("Panel", root, "Подтверждение");
            Box(panel, Center, Center, Vector2.zero, new Vector2(640f, 360f));
            panel.gameObject.AddComponent<CanvasGroup>();
            view.ConfirmPanel = panel;
            var content = (RectTransform)panel.Find("Содержимое");

            RectTransform title = Box(Node("Заголовок", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -28f), new Vector2(560f, 50f));
            view.ConfirmTitle = Label(title, "Надпись", "Выйти из игры?", FontRole.Heading, 36f, Role.Text, TextAlignmentOptions.Center, 1f);
            view.ConfirmTitle.enableAutoSizing = true;
            view.ConfirmTitle.fontSizeMin = 20f;
            view.ConfirmTitle.fontSizeMax = 36f;
            Box(Place("Divider", content, "Линия"), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -92f), new Vector2(360f, 16f));
            RectTransform text = Box(Node("Текст", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -112f), new Vector2(540f, 84f));
            view.ConfirmText = Label(text, "Надпись", "", FontRole.Body, 20f, Role.Text, TextAlignmentOptions.Center);
            RectTransform countdown = Box(Node("Отсчёт", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -198f), new Vector2(540f, 30f));
            view.ConfirmCountdown = Label(countdown, "Надпись", "", FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.Center);

            view.ConfirmYes = ConfirmButton(content, "ButtonPrimary", "Выйти", -140f, out view.ConfirmYesLabel);
            view.ConfirmNo = ConfirmButton(content, "ButtonSecondary", "Отмена", 140f, out view.ConfirmNoLabel);
            view.ConfirmNo.GetComponent<UiHoverMotion>().ClickSound = UiSoundEvent.Back;
            panel.gameObject.SetActive(false);
        }

        static Button ConfirmButton(RectTransform content, string prefab, string text, float x, out TMP_Text label)
        {
            RectTransform button = Place(prefab, content, text);
            Box(button, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(x, 30f), new Vector2(240f, 56f));
            label = button.GetComponentInChildren<TMP_Text>();
            label.text = text;
            label.enableAutoSizing = true;
            label.fontSizeMin = 16f;
            label.fontSizeMax = 26f;
            button.gameObject.AddComponent<UiHoverMotion>().HoverScale = 1.03f;
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }
    }
}
