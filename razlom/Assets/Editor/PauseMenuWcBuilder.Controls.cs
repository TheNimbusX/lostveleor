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
        const float KeySize = 34f;

        // ---------------------------------------------------------------- управление
        static void BuildControls(RectTransform root, PauseMenuView view)
        {
            RectTransform content = Window(root, "Управление");
            view.ControlsPanel = (RectTransform)content.parent;

            RectTransform title = Box(Node("Заголовок", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -26f), new Vector2(600f, 56f));
            UiInkKit.Label(title, "Надпись", "Управление", FontRole.Heading, 38f, Role.Text, TextAlignmentOptions.Center, 1f, 1f, .05f);
            Box(UiInkKit.Divider(content, "Линия", 420f, true, .6f), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -94f), new Vector2(420f, 16f));

            view.AbilityLayout = Segmented(Control(Row(content, 0, "Движение", "Как ходить: мышью или клавишами."), 320f), 2);
            RectTransform hint = Box(Node("Пояснение", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -182f), new Vector2(RowW, 40f));
            view.ControlsHint = UiInkKit.Label(hint, "Надпись", "", FontRole.Body, 17f, Role.TextMuted);

            RectTransform columns = Box(Node("Колонки", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -226f), new Vector2(RowW, 410f));
            view.BindingsCombat = Column(columns, "Бой", 0f);
            view.BindingsWorld = Column(columns, "Мир", 470f);
            view.BindingTemplate = BindingRow(view.BindingsCombat);

            // Мышь не переназначается — притушенная строка-напоминание под пятью строками «Боя»
            // (в «Мире» строк семь, там места нет). Клавиши — те же капсулы, что у строк.
            RectTransform mouse = Box(Node("Мышь", (RectTransform)view.BindingsCombat.parent), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -40f - 5f * 52f - 10f), new Vector2(450f, 46f));
            mouse.gameObject.AddComponent<CanvasGroup>().alpha = .8f;
            RectTransform action = Box(Node("Действие", mouse), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(260f, 34f));
            UiInkKit.Label(action, "Надпись", "Идти · атаковать", FontRole.Body, 18f, Role.TextMuted);
            RectTransform keys = Box(Node("Клавиши", mouse), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-12f, 0f), new Vector2(200f, KeySize));
            RectTransform attack = UiInkKit.Keycap(keys, "ЛКМ", "ЛКМ", KeySize);
            RightAt(attack, 0f);
            RectTransform walk = UiInkKit.Keycap(keys, "ПКМ", "ПКМ", KeySize);
            RightAt(walk, -attack.sizeDelta.x - 10f);

            Footer(content, false, out view.ControlsReset, out _, out TMP_Text status, out view.ControlsBack);
            view.ControlsBack.gameObject.SetActive(true);
            // Строки клавиш свою подсказку пишут в ControlsHint; описание футера у управления не нужно.
            Object.DestroyImmediate(status.transform.parent.gameObject);
            view.ControlsPanel.gameObject.SetActive(false);
        }

        /// <summary>Правый край узла — у правого края родителя со сдвигом <paramref name="x"/>.</summary>
        static void RightAt(RectTransform rect, float x)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(1f, .5f);
            rect.anchoredPosition = new Vector2(x, 0f);
        }

        /// <summary>Колонка клавиш: заголовок капителью и тусклая нить до края, под ними строки раскладкой.</summary>
        static RectTransform Column(RectTransform parent, string title, float left)
        {
            RectTransform column = Box(Node(title, parent), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, 0f), new Vector2(450f, 410f));
            RectTransform head = Box(Node("Заголовок", column), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(440f, 32f));
            TMP_Text label = UiInkKit.Label(head, "Надпись", title, FontRole.Heading, 22f, Role.TextMuted, TextAlignmentOptions.MidlineLeft, 3f);
            label.fontStyle = FontStyles.UpperCase;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float text = label.GetPreferredValues(title.ToUpperInvariant()).x;
            float from = 8f + text + 22f, width = 440f - from;
            Box(UiInkKit.Divider(column, "Линия", width, false, .25f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(from, -16f), new Vector2(width, 16f));
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

        /// <summary>
        /// Строка клавиши: действие слева, клавиша «Дыма и света» справа — круг под одну букву, капсула
        /// под длинную подпись (ширину после переназначения подгоняет UiKeyBindingRow). Наведение — кольцо
        /// разгорается и за клавишей встаёт свет; ожидание — пульсирующее тёплое свечение; обмен — вспышка
        /// оранжевого дыма по строке.
        /// </summary>
        static UiKeyBindingRow BindingRow(RectTransform column)
        {
            RectTransform row = Node("Шаблон клавиши", column);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = 46f;
            var binding = row.gameObject.AddComponent<UiKeyBindingRow>();
            Image flash = UiInkKit.SmokeLayer(row, "Вспышка", "smoke_plate", .45f, 16f, 6f, Role.Accent);
            binding.FlashGraphic = flash;
            flash.gameObject.SetActive(false);
            RectTransform action = Box(Node("Действие", row), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(22f, 0f), new Vector2(260f, 36f));
            binding.Action = UiInkKit.Label(action, "Надпись", "Действие", FontRole.Body, 18f, Role.Text);
            binding.Action.enableAutoSizing = true;
            binding.Action.fontSizeMin = 13f;
            binding.Action.fontSizeMax = 18f;
            binding.Action.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform key = UiInkKit.Keycap(row, "Клавиша", "Q", KeySize);
            RightAt(key, -12f);
            // Свет — поверх дыма клавиши и под кольцом с буквой: под дымом его почти не видно.
            RectTransform waiting = Stretch(Node("Ожидание", key), -14f);
            waiting.SetSiblingIndex(1);
            Image glow = UiInkKit.LightLayer(waiting, "Свечение", "light_glow", .9f, 6f, delay: 0f);
            waiting.gameObject.AddComponent<UiPulse>().Graphic = glow;
            waiting.gameObject.SetActive(false);
            binding.Waiting = waiting.gameObject;
            Image hover = UiInkKit.LightLayer(key, "Наведение", "light_glow", .5f, 10f, delay: .15f);
            hover.transform.SetSiblingIndex(2);
            Image hit = UiInkKit.HitArea(key);
            var button = key.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            NoNavigation(button);
            binding.Key = button;
            var states = key.gameObject.AddComponent<ThemeStates>();
            states.Target = key.Find("Кольцо").GetComponent<ThemeColor>();
            states.Normal = Role.PanelLine;
            states.Hover = Role.Accent;
            states.Pressed = Role.AccentPressed;
            states.Disabled = Role.Disabled;
            states.Apply();
            var motion = key.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = hover;
            binding.KeyLabel = key.Find("Буква").GetComponent<TMP_Text>();
            binding.KeyLabel.fontStyle = FontStyles.Bold;
            // Цвет клавиши (своя или стандартная) ведёт UiKeyBindingRow.
            Object.DestroyImmediate(binding.KeyLabel.GetComponent<ThemeColor>());
            binding.KeyDefault = T.Text;
            binding.KeyCustom = T.Accent;
            binding.KeySize = KeySize;
            row.gameObject.SetActive(false);
            return binding;
        }

        // ---------------------------------------------------------------- подтверждение
        static void BuildConfirm(RectTransform root, PauseMenuView view)
        {
            RectTransform content = InkWindow(root, "Подтверждение", Vector2.zero, new Vector2(640f, 360f));
            RectTransform panel = (RectTransform)content.parent;
            // Подтверждение проявляется от середины: взгляд уже там.
            panel.GetComponent<UiInkGroup>().Direction = UiInkGroup.Sweep.FromCenter;
            view.ConfirmPanel = panel;
            // Пауза под подтверждением гаснет целиком: притушенные пункты просвечивали сквозь дым под кнопками.
            view.UnderConfirmAlpha = 0f;

            RectTransform title = Box(Node("Заголовок", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -28f), new Vector2(560f, 50f));
            view.ConfirmTitle = UiInkKit.Label(title, "Надпись", "Выйти из игры?", FontRole.Heading, 36f, Role.Text, TextAlignmentOptions.Center, 1f, 1f, .05f);
            view.ConfirmTitle.enableAutoSizing = true;
            view.ConfirmTitle.fontSizeMin = 20f;
            view.ConfirmTitle.fontSizeMax = 36f;
            Box(UiInkKit.Divider(content, "Линия", 360f, true, .6f), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -92f), new Vector2(360f, 16f));
            RectTransform text = Box(Node("Текст", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -112f), new Vector2(540f, 84f));
            view.ConfirmText = UiInkKit.Label(text, "Надпись", "", FontRole.Body, 20f, Role.Text, TextAlignmentOptions.Center);
            RectTransform countdown = Box(Node("Отсчёт", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -198f), new Vector2(540f, 30f));
            view.ConfirmCountdown = UiInkKit.Label(countdown, "Надпись", "", FontRole.Body, 17f, Role.TextMuted, TextAlignmentOptions.Center);

            view.ConfirmYes = ConfirmButton(content, true, "Выйти", -150f, out view.ConfirmYesLabel);
            view.ConfirmNo = ConfirmButton(content, false, "Отмена", 150f, out view.ConfirmNoLabel);
            view.ConfirmNo.GetComponent<UiHoverMotion>().ClickSound = UiSoundEvent.Back;
            panel.gameObject.SetActive(false);
        }

        static Button ConfirmButton(RectTransform content, bool primary, string text, float x, out TMP_Text label)
        {
            RectTransform button = UiInkKit.Button(content, text, text, primary, new Vector2(240f, 56f), 26f);
            Box(button, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(x, 34f), new Vector2(240f, 56f));
            label = button.Find("Надпись").GetComponent<TMP_Text>();
            label.enableAutoSizing = true;
            label.fontSizeMin = 16f;
            label.fontSizeMax = 26f;
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }
    }
}
