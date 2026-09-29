using Game.View;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;

namespace Game.EditorTools
{
    /// <summary>
    /// Кадры меню паузы без запуска игры: префаб в сцене предпросмотра поверх кадра
    /// игры (ART/no-ui.png), окна открыты и заполнены примером. В префаб ничего не пишется.
    /// </summary>
    public static partial class PauseMenuWcBuilder
    {
        public enum Shot { Pause, Settings, Controls, Confirm }

        public static string Capture(string outPath, Shot shot)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst => Preview(inst, shot));
        }

        static void Preview(GameObject inst, Shot shot)
        {
            var view = inst.GetComponent<PauseMenuView>();
            var root = (RectTransform)inst.transform;
            if (System.IO.File.Exists("../ART/no-ui.png"))
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(System.IO.File.ReadAllBytes("../ART/no-ui.png"));
                var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                back.texture = tex;
                back.uvRect = new Rect(.035f, 0f, .965f, .955f);
                back.transform.SetSiblingIndex(0);
            }

            // «Настройки Б»: и «Настройки», и «Управление» — одно окно во всю ширину, пауза под ним спрятана.
            bool window = shot == Shot.Settings || shot == Shot.Controls;
            view.PausePanel.gameObject.SetActive(!window);
            view.PausePanel.anchoredPosition = new Vector2(view.PauseCenterX, 0f);
            view.SettingsPanel.gameObject.SetActive(window);
            view.ConfirmPanel.gameObject.SetActive(shot == Shot.Confirm);
            view.Hint.text = "Esc — продолжить игру";

            if (window)
            {
                bool controls = shot == Shot.Controls;
                var tab = controls ? PauseMenuView.Tab.Controls : PauseMenuView.Tab.Graphics;
                foreach (PauseMenuView.Tab each in new[] { PauseMenuView.Tab.Graphics, PauseMenuView.Tab.Audio, PauseMenuView.Tab.Game,
                             PauseMenuView.Tab.Interface, PauseMenuView.Tab.Controls })
                {
                    CanvasGroup page = view.PageOf(each);
                    if (page != null) page.gameObject.SetActive(each == tab);
                    Button button = view.TabButton(each);
                    if (button == null) continue;
                    Color colour = each == tab ? view.TabTextOn : view.TabTextOff;
                    button.GetComponentInChildren<TMPro.TMP_Text>().color = colour;
                    if (button.transform.Find(PauseMenuView.TabIconName) is Transform icon) icon.GetComponent<Graphic>().color = colour;
                }
                var selected = (RectTransform)view.TabButton(tab).transform;
                view.TabIndicator.anchoredPosition = selected.anchoredPosition;
                view.TabIndicator.sizeDelta = selected.sizeDelta;
                view.Apply.gameObject.SetActive(!controls);
                SettingId described = controls ? SettingId.KeyBindings : SettingId.Quality;
                view.DescriptionTitle.text = SettingsCatalog.Title(described);
                view.DescriptionText.text = SettingsCatalog.Description(described);
                view.DescriptionDefault.text = "Стандартно: " + SettingsCatalog.DefaultText(described);
                view.FooterNote.text = controls ? "Изменения сохраняются сразу" : "Экран — кнопкой «Применить», остальное сохраняется сразу";
                foreach (UiSettingRow row in view.GetComponentsInChildren<UiSettingRow>(true))
                    if (row.Selected != null) row.Selected.SetActive(row.Setting == described);
            }

            if (shot == Shot.Settings)
            {
                Choose(view.DisplayMode, new[] { "Весь экран", "Окно", "Без рамок" }, 2);
                Choose(view.Quality, new[] { "Низкое", "Среднее", "Высокое" }, 2);
                Choose(view.Shadows, new[] { "Низкие", "Средние", "Высокие" }, 1);
                view.Resolution.Value.text = "1920 × 1080";
                view.VSync.SetValue(true);
                view.FrameLimit.value = .6f; view.FrameLimitValue.text = "120";
                view.FrameLimitRow.alpha = .45f;
                view.Brightness.value = .5f; view.BrightnessValue.text = "50%";
            }
            if (shot == Shot.Controls)
            {
                Choose(view.AbilityLayout, new[] { "Мышь", "WASD" }, 0);
                Choose(view.AbilityRow, new[] { "Q W E R", "1 2 3 4" }, 0);
                view.ControlsHint.text = "Нажми на клавишу, чтобы переназначить. Esc — отмена.";
                string[] combat = { "Способность 1", "Способность 2", "Способность 3", "Способность 4", "Кувырок" };
                string[] combatKeys = { "Q", "W", "E", "R", "Space" };
                string[] world = { "Взаимодействие · сумка", "Войти в Разлом", "Уйти из Разлома с добычей", "Повторить забег", "Вернуться в лагерь", "Зелье здоровья", "Зелье лавидия", "Артефакт забега" };
                string[] worldKeys = { "I", "E", "L", "R", "C", "5", "6", "F" };
                for (int i = 0; i < combat.Length; i++) Row(view, view.BindingsCombat, combat[i], combatKeys[i], i == 3);
                for (int i = 0; i < world.Length; i++) Row(view, view.BindingsWorld, world[i], worldKeys[i], false);
            }
            if (shot == Shot.Confirm)
            {
                // Как в игре: окна под подтверждением притухают.
                view.PausePanel.GetComponent<CanvasGroup>().alpha = .25f;
                view.ConfirmTitle.text = "Выйти из игры?";
                view.ConfirmText.text = "Прогресс забега не сохранится.\nЛагерь и снаряжение останутся.";
                view.ConfirmCountdown.text = "";
            }

            // В игре подсветки наведения гасит UiHoverMotion.OnEnable; в предпросмотре его нет.
            foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
            {
                if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
                if (motion.HighlightGroup != null) motion.HighlightGroup.alpha = 0f;
            }
        }

        static void Choose(UiSegmented segmented, string[] labels, int index)
        {
            segmented.SetLabels(labels);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)segmented.transform);
            segmented.SetSelected(index);
        }

        static void Row(PauseMenuView view, RectTransform column, string action, string key, bool custom)
        {
            UiKeyBindingRow row = Object.Instantiate(view.BindingTemplate, column);
            row.gameObject.SetActive(true);
            row.Show(action, key, false, custom);
        }
    }
}
