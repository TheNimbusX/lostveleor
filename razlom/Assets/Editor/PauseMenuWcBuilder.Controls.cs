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

        /// <summary>
        /// Вкладка «Управление» (раньше — своё окно): схема движения, ряд способностей, подсказка и две
        /// колонки клавиш «Бой» и «Мир». Колонки — по группам обмена клавиш (GameKeyBindings).
        /// </summary>
        static void BuildControlsPage(RectTransform options, PauseMenuView view)
        {
            view.ControlsPage = Page(options, "Управление");
            RectTransform page = (RectTransform)view.ControlsPage.transform;
            view.AbilityLayout = Segmented(Control(Row(page, 0, SettingId.Movement, ControlW), ControlW), 2);
            RectTransform abilityRow = Row(page, 1, SettingId.AbilityRow, ControlW);
            view.AbilityRowGroup = abilityRow.gameObject.AddComponent<CanvasGroup>();
            view.AbilityRow = Segmented(Control(abilityRow, ControlW), 2);

            view.ControlsHint = UiInkKit.Label(Corner(page, "Пояснение", 16f, 2f * RowStep + 4f, PageW - 16f, 34f), "Надпись", "", FontRole.Body, 16f, Role.TextMuted);
            view.ControlsHint.textWrappingMode = TextWrappingModes.NoWrap;
            view.ControlsHint.enableAutoSizing = true;
            view.ControlsHint.fontSizeMin = 12f;
            view.ControlsHint.fontSizeMax = 16f;

            const float columnsTop = 2f * RowStep + 44f, columnW = 310f, columnH = PageH - columnsTop;
            RectTransform columns = Corner(page, "Колонки", 0f, columnsTop, PageW, columnH);
            view.BindingsCombat = Column(columns, "Бой", 0f, columnW, columnH);
            view.BindingsWorld = Column(columns, "Мир", PageW - columnW, columnW, columnH);
            view.BindingTemplate = BindingRow(view.BindingsCombat);

            // Мышь не переназначается — притушенная строка-напоминание под пятью строками «Боя»
            // (в «Мире» строк восемь, там места нет). Клавиши — те же капсулы, что у строк.
            RectTransform mouse = Box(Node("Мышь", (RectTransform)view.BindingsCombat.parent), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -40f - 5f * 48f - 10f), new Vector2(columnW, 44f));
            mouse.gameObject.AddComponent<CanvasGroup>().alpha = .8f;
            RectTransform action = Box(Node("Действие", mouse), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(16f, 0f), new Vector2(160f, 34f));
            UiInkKit.Label(action, "Надпись", "Идти · атаковать", FontRole.Body, 17f, Role.TextMuted).textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform keys = Box(Node("Клавиши", mouse), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-8f, 0f), new Vector2(130f, KeySize));
            RectTransform attack = UiInkKit.Keycap(keys, "ЛКМ", "ЛКМ", KeySize);
            RightAt(attack, 0f);
            RectTransform walk = UiInkKit.Keycap(keys, "ПКМ", "ПКМ", KeySize);
            RightAt(walk, -attack.sizeDelta.x - 10f);
        }

        /// <summary>Правый край узла — у правого края родителя со сдвигом <paramref name="x"/>.</summary>
        static void RightAt(RectTransform rect, float x)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(1f, .5f);
            rect.anchoredPosition = new Vector2(x, 0f);
        }

        /// <summary>Колонка клавиш: заголовок капителью и тусклая нить до края, под ними строки раскладкой.</summary>
        static RectTransform Column(RectTransform parent, string title, float left, float width, float height)
        {
            RectTransform column = Box(Node(title, parent), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, 0f), new Vector2(width, height));
            RectTransform head = Box(Node("Заголовок", column), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(width - 10f, 32f));
            TMP_Text label = UiInkKit.Label(head, "Надпись", title, FontRole.Heading, 22f, Role.TextMuted, TextAlignmentOptions.MidlineLeft, 3f);
            label.fontStyle = FontStyles.UpperCase;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float text = label.GetPreferredValues(title.ToUpperInvariant()).x;
            float from = 8f + text + 22f, line = width - 10f - from;
            Box(UiInkKit.Divider(column, "Линия", line, false, .25f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(from, -16f), new Vector2(line, 16f));
            RectTransform rows = Node("Строки", column);
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(.5f, 1f);
            rows.anchoredPosition = new Vector2(0f, -40f);
            rows.sizeDelta = new Vector2(0f, height - 40f);
            var list = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 4f;
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            return rows;
        }

        /// <summary>
        /// Строка клавиши: действие слева, клавиша «Дыма и света» справа — круг под одну букву, капсула
        /// под длинную подпись (ширину после переназначения подгоняет UiKeyBindingRow). Наведение — кольцо
        /// разгорается и за клавишей встаёт свет; ожидание — пульсирующее тёплое свечение; обмен — вспышка
        /// оранжевого дыма по строке. Наведение на строку описывает справа «Клавиши» (UiSettingRow).
        /// </summary>
        static UiKeyBindingRow BindingRow(RectTransform column)
        {
            RectTransform row = Node("Шаблон клавиши", column);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = 44f;
            UiInkKit.HitArea(row);
            row.gameObject.AddComponent<UiSettingRow>().Setting = SettingId.KeyBindings;
            var binding = row.gameObject.AddComponent<UiKeyBindingRow>();
            Image flash = UiInkKit.SmokeLayer(row, "Вспышка", "smoke_plate", .45f, 16f, 6f, Role.Accent);
            binding.FlashGraphic = flash;
            flash.gameObject.SetActive(false);
            // Колонка в 310: подпись до 180, капсула клавиши не шире 3,2 высоты — длинные подписи ужимаются, а не наезжают.
            RectTransform action = Box(Node("Действие", row), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(16f, 0f), new Vector2(180f, 36f));
            binding.Action = UiInkKit.Label(action, "Надпись", "Действие", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.Text);
            binding.Action.enableAutoSizing = true;
            binding.Action.fontSizeMin = 12f;
            binding.Action.fontSizeMax = 18f;
            binding.Action.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform key = UiInkKit.Keycap(row, "Клавиша", "Q", KeySize);
            RightAt(key, -8f);
            // Свет — поверх дыма и тёмной подложки клавиши, под кромкой с буквой: под дымом его почти не видно.
            RectTransform waiting = Stretch(Node("Ожидание", key), -14f);
            waiting.SetSiblingIndex(UiInkKit.KeycapInnerIndex(key));
            Image glow = UiInkKit.LightLayer(waiting, "Свечение", "light_glow", .9f, 6f, delay: 0f);
            waiting.gameObject.AddComponent<UiPulse>().Graphic = glow;
            waiting.gameObject.SetActive(false);
            binding.Waiting = waiting.gameObject;
            Image hover = UiInkKit.LightLayer(key, "Наведение", "light_glow", .5f, 10f, delay: .15f);
            hover.transform.SetSiblingIndex(waiting.GetSiblingIndex() + 1);
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
            binding.KeyMaxWidth = 3.2f;
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
            // Окно подтверждения листа 5: заголовок — ступень Title, пояснение — Body, отсчёт — Caption.
            view.ConfirmTitle = UiInkKit.Label(title, "Надпись", "Выйти из игры?", FontRole.Heading, T.Size(UiTheme.TextStep.Title), Role.Text,
                TextAlignmentOptions.Center, 1f, 1f, .05f);
            view.ConfirmTitle.enableAutoSizing = true;
            view.ConfirmTitle.fontSizeMin = 20f;
            view.ConfirmTitle.fontSizeMax = T.Size(UiTheme.TextStep.Title);
            Box(UiInkKit.Divider(content, "Линия", 360f, true, .6f), new Vector2(.5f, 1f), new Vector2(.5f, .5f), new Vector2(0f, -92f), new Vector2(360f, 16f));
            RectTransform text = Box(Node("Текст", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -112f), new Vector2(540f, 84f));
            view.ConfirmText = UiInkKit.Label(text, "Надпись", "", FontRole.Body, T.Size(UiTheme.TextStep.Body), Role.Text, TextAlignmentOptions.Center);
            RectTransform countdown = Box(Node("Отсчёт", content), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -198f), new Vector2(540f, 30f));
            view.ConfirmCountdown = UiInkKit.Label(countdown, "Надпись", "", FontRole.Body, T.Size(UiTheme.TextStep.Caption), Role.TextMuted, TextAlignmentOptions.Center);

            view.ConfirmYes = ConfirmButton(content, true, "Выйти", -150f, out view.ConfirmYesLabel);
            view.ConfirmNo = ConfirmButton(content, false, "Отмена", 150f, out view.ConfirmNoLabel);
            view.ConfirmNo.GetComponent<UiHoverMotion>().ClickSound = UiSoundEvent.Back;
            // Отмена подтверждения — Esc (PauseMenu): кейкап на кнопке, как в окне листа 5.
            UiInkKit.ButtonKey((RectTransform)view.ConfirmNo.transform, "Esc", 30f);
            panel.gameObject.SetActive(false);
        }

        static Button ConfirmButton(RectTransform content, bool primary, string text, float x, out TMP_Text label)
        {
            RectTransform button = UiInkKit.Button(content, text, text, primary, new Vector2(240f, T.ButtonHeight), T.Size(UiTheme.TextStep.Heading));
            Box(button, new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(x, 34f), new Vector2(240f, T.ButtonHeight));
            label = button.Find("Надпись").GetComponent<TMP_Text>();
            label.enableAutoSizing = true;
            label.fontSizeMin = 16f;
            label.fontSizeMax = T.Size(UiTheme.TextStep.Heading);
            var result = button.GetComponent<Button>();
            NoNavigation(result);
            return result;
        }
    }
}
