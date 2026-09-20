using System;
using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.CombatHudBuilder;
using Object = UnityEngine.Object;

namespace Game.EditorTools
{
    /// <summary>
    /// Миграции меню паузы поверх ручных правок. Каждая берёт текущие значения
    /// префаба и меняет только сказанное; координаты — только локальные
    /// (у Canvas в сцене префаба нулевой масштаб, см. CombatHudBuilder.MigrateTo3).
    /// </summary>
    public static partial class PauseMenuBuilder
    {
        const string SoundBankPath = "Assets/Resources/UI/Sounds/UiSoundBank.asset";

        const string UiSoundFolder = "Assets/Resources/Audio/UI/Prepared";

        /// <summary>
        /// Какие файлы из <see cref="UiSoundFolder"/> берёт событие: первое имя, у
        /// которого есть файлы (<c>ui_click.ogg</c>, <c>ui_click_02.ogg</c>…). Пока своего
        /// звука нет, событие звучит близким по смыслу — придёт свой файл, заменит.
        /// </summary>
        static readonly (UiSoundEvent sound, string[] names)[] UiSoundFiles =
        {
            (UiSoundEvent.Hover, new[] { "ui_hover" }),
            (UiSoundEvent.Click, new[] { "ui_click" }),
            (UiSoundEvent.Back, new[] { "ui_back" }),
            (UiSoundEvent.Tab, new[] { "ui_tab" }),
            (UiSoundEvent.Toggle, new[] { "ui_toggle" }),
            (UiSoundEvent.SliderStep, new[] { "ui_slider" }),
            (UiSoundEvent.ListOpen, new[] { "ui_list_open" }),
            (UiSoundEvent.ListClose, new[] { "ui_list_close", "ui_list_open" }),
            (UiSoundEvent.WindowOpen, new[] { "ui_window_open" }),
            (UiSoundEvent.WindowClose, new[] { "ui_window_close", "ui_window_open" }),
            (UiSoundEvent.PauseOpen, new[] { "ui_pause_open", "ui_window_open" }),
            (UiSoundEvent.PauseClose, new[] { "ui_pause_close", "ui_window_close", "ui_window_open" }),
            (UiSoundEvent.Denied, new[] { "ui_denied" }),
            (UiSoundEvent.KeyWaiting, new[] { "ui_key_waiting" }),
            (UiSoundEvent.KeyAssigned, new[] { "ui_key_assigned", "ui_click" }),
            (UiSoundEvent.Apply, new[] { "ui_apply", "ui_click" }),
            (UiSoundEvent.Reset, new[] { "ui_reset", "ui_back" }),
        };

        [MenuItem("Разлом/UI/Заполнить банк звуков")]
        static void FillSoundBankFromMenu() => EnsureSoundBank();

        /// <summary>
        /// Банк звуков со всеми событиями. Пустые события и заполненные этим же кодом
        /// берут файлы из <see cref="UiSoundFolder"/> по имени; клипы, которые владелец
        /// поставил руками, не трогаются.
        /// </summary>
        static void EnsureSoundBank()
        {
            var bank = AssetDatabase.LoadAssetAtPath<UiSoundBank>(SoundBankPath);
            bool created = bank == null;
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SoundBankPath));
                bank = ScriptableObject.CreateInstance<UiSoundBank>();
            }

            bool changed = created;
            var entries = new System.Collections.Generic.List<UiSoundBank.Entry>(bank.Entries ?? Array.Empty<UiSoundBank.Entry>());
            foreach (UiSoundEvent sound in (UiSoundEvent[])Enum.GetValues(typeof(UiSoundEvent)))
            {
                if (bank.Find(sound) != null) continue;
                bool quiet = sound == UiSoundEvent.Hover || sound == UiSoundEvent.SliderStep;
                entries.Add(new UiSoundBank.Entry { Event = sound, Volume = quiet ? 0.45f : 0.8f, MinInterval = quiet ? 0.05f : 0.02f });
                changed = true;
            }
            bank.Entries = entries.ToArray();

            foreach ((UiSoundEvent sound, string[] names) in UiSoundFiles)
            {
                UiSoundBank.Entry entry = bank.Find(sound);
                bool empty = entry.Clips == null || entry.Clips.Length == 0 || Array.TrueForAll(entry.Clips, c => c == null);
                if (!empty && !entry.AutoFilled) continue;
                AudioClip[] clips = Array.Empty<AudioClip>();
                foreach (string name in names)
                    if ((clips = PreparedClips(name)).Length > 0) break;
                if (SameClips(entry.Clips, clips)) continue;
                entry.Clips = clips;
                entry.AutoFilled = clips.Length > 0;
                changed = true;
            }

            if (!changed) return;
            if (created) AssetDatabase.CreateAsset(bank, SoundBankPath);
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
        }

        static AudioClip[] PreparedClips(string name)
        {
            if (!AssetDatabase.IsValidFolder(UiSoundFolder)) return Array.Empty<AudioClip>();
            var clips = new System.Collections.Generic.List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets(name + " t:AudioClip", new[] { UiSoundFolder }))
            {
                string file = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                bool variant = file.Length > name.Length + 1 && file.StartsWith(name + "_") && char.IsDigit(file[name.Length + 1]);
                if (file == name || variant) clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips.ToArray();
        }

        static bool SameClips(AudioClip[] a, AudioClip[] b)
        {
            if ((a?.Length ?? 0) != b.Length) return false;
            for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// v5 по замечаниям владельца 16 сентября: объёмная шапка; живые рамки у
        /// кнопок, вкладок и клавиш; иконка и текст кнопок по центру, футер у
        /// кромки над ромбом; шире пауза; клавиши в две колонки без прокрутки и
        /// строка мыши; размытый фон; описания при наведении; блик на кораллах.
        /// </summary>
        static void MigrateTo5(PauseMenuView view)
        {
            if (_regular == null) _regular = EnsureFont("Regular");
            if (_semibold == null) _semibold = EnsureFont("SemiBold");
            if (_bold == null) _bold = EnsureFont("Bold");

            MigrateHeader(view);
            WidenPause(view);
            foreach (Button button in view.GetComponentsInChildren<Button>(true)) LiveFrame(view, button);
            FooterOf(view.SettingsPanel, view.Reset, view.Apply, view.Status);
            FooterOf(view.ControlsPanel, view.ControlsReset, null, null);
            foreach (Button button in new[] { view.Continue, view.Settings, view.Controls, view.Camp, view.Quit, view.Reset, view.Apply,
                         view.ControlsReset, view.SettingsBack, view.ControlsBack, view.ConfirmYes, view.ConfirmNo })
                CenterContent(button);
            TwoColumnBindings(view);
            BlurredBackdrop(view);
            Hints(view);
            foreach (Button button in new[] { view.Continue, view.Apply, view.ConfirmYes }) AddShine(button);
        }

        /// <summary>
        /// v6 по кадрам v5: вспышка обмена горела на всех строках клавиш (слой
        /// включён с полной альфой — прячется до вызова Flash); «Назад» стояла
        /// ниже кнопок футера — теперь на той же линии.
        /// </summary>
        static void MigrateTo6(PauseMenuView view)
        {
            if (view.BindingTemplate != null && view.BindingTemplate.FlashGraphic != null)
                view.BindingTemplate.FlashGraphic.gameObject.SetActive(false);
            foreach (Button back in new[] { view.SettingsBack, view.ControlsBack })
                if (back != null) ((RectTransform)back.transform).anchoredPosition = new Vector2(40f, 30f);
        }

        // ---- шапка ----
        static void MigrateHeader(PauseMenuView view)
        {
            if (view.ConfirmPanel != null && view.ConfirmPanel.Find("Title Ribbon") is RectTransform confirm && confirm.GetComponent<Image>() is Image plate)
            {
                plate.sprite = Kit("pause_header");
                plate.type = Image.Type.Sliced;
                confirm.sizeDelta = new Vector2(560f, 110f);
            }
            if (view.PausePanel == null || !(view.PausePanel.Find("Title Ribbon") is RectTransform ribbon)) return;
            var image = ribbon.GetComponent<Image>();
            if (image != null) { image.sprite = Kit("pause_header"); image.type = Image.Type.Sliced; }
            // Родной размер ленты на канве 1080: 1797×370 при PPU 336.
            ribbon.anchoredPosition = new Vector2(0f, -22f);
            ribbon.sizeDelta = new Vector2(536f, 110f);

            // Хвосты ниже и снаружи загнутых концов ленты; петля хвоста прячется под загибом.
            for (int side = 0; side < 2; side++)
            {
                string name = side == 0 ? "Header Tail Left" : "Header Tail Right";
                if (view.PausePanel.Find(name) != null) continue;
                RectTransform tail = Node(name, view.PausePanel, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(.5f, 1f),
                    new Vector2(side == 0 ? -301f : 301f, -60f), new Vector2(172f, 85f));
                Img(tail, Kit(side == 0 ? "pause_header_tail_left" : "pause_header_tail_right"), Color.white, false).preserveAspect = true;
                tail.SetSiblingIndex(ribbon.GetSiblingIndex());
            }
            if (ribbon.Find("Title") is RectTransform titleRect && titleRect.GetComponent<TMP_Text>() is TMP_Text title)
            {
                title.color = new Color32(0xFF, 0xF6, 0xEC, 0xFF);
                title.characterSpacing = 26f;
                title.margin = new Vector4(90f, 0f, 90f, 8f);
            }
        }

        // ---- ширина паузы ----
        static void WidenPause(PauseMenuView view)
        {
            if (view.PausePanel == null) return;
            view.PausePanel.sizeDelta = new Vector2(470f, view.PausePanel.sizeDelta.y);
            view.PauseShiftedX = -545f;
            foreach (Button button in new[] { view.Continue, view.Settings, view.Controls, view.Camp, view.Quit })
            {
                if (button == null) continue;
                var rect = (RectTransform)button.transform;
                rect.sizeDelta = new Vector2(410f, rect.sizeDelta.y);
                if (rect.Find("Label") is RectTransform label && label.GetComponent<TMP_Text>() is TMP_Text text) text.characterSpacing = 8f;
            }
        }

        // ---- живые рамки ----
        static void LiveFrame(PauseMenuView view, Button button)
        {
            if (button == null || button.transition != Selectable.Transition.SpriteSwap || button.image == null) return;
            string sprite = button.image.sprite != null ? button.image.sprite.name : string.Empty;
            // Спрайт наведения — тот же, что был в SpriteSwap: той же геометрии, ложится ровно поверх.
            string glow = sprite.StartsWith("pause_tab") ? "pause_tab_hover"
                : sprite.StartsWith("pause_keycap") ? "pause_keycap_glow"
                : sprite.StartsWith("pause_button_coral") ? "pause_button_coral_hover"
                : "pause_button_hover";
            button.transition = Selectable.Transition.None;

            var rect = (RectTransform)button.transform;
            RectTransform frame = rect.Find("Hover Frame") as RectTransform;
            if (frame == null)
            {
                frame = Stretch("Hover Frame", rect, 0f);
                Img(frame, Kit(glow), Color.white);
                frame.SetSiblingIndex(0);
            }
            var motion = rect.GetComponent<UiHoverMotion>();
            if (motion == null) motion = rect.gameObject.AddComponent<UiHoverMotion>();
            motion.Highlight = frame.GetComponent<Image>();
            motion.PulseWhileHovered = true;
            bool tab = button == view.TabGraphics || button == view.TabAudio || button == view.TabGame;
            motion.SilentClick = tab;
            if (button == view.SettingsBack || button == view.ControlsBack || button == view.ConfirmNo) motion.ClickSound = UiSoundEvent.Back;
        }

        // ---- центровка кнопок и футер ----
        static void CenterContent(Button button)
        {
            if (button == null) return;
            var rect = (RectTransform)button.transform;
            if (rect.Find("Content") != null) return;
            var icon = rect.Find("Icon") as RectTransform;
            var label = rect.Find("Label") as RectTransform;
            if (label == null) return;

            RectTransform content = Stretch("Content", rect, 0f);
            var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 14f;
            row.padding = new RectOffset(28, 28, 0, 0);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            if (icon != null)
            {
                Vector2 size = icon.sizeDelta;
                Vector3 angles = icon.localEulerAngles;
                icon.SetParent(content, false);
                Layout(icon, size.x, size.y);
                icon.localEulerAngles = angles;
            }
            label.SetParent(content, false);
            var text = label.GetComponent<TMP_Text>();
            if (text != null)
            {
                text.enableAutoSizing = false;
                text.fontSize = Mathf.Min(text.fontSize, 23f);
                text.margin = Vector4.zero;
                text.alignment = TextAlignmentOptions.Center;
            }
        }

        static void FooterOf(RectTransform panel, Button first, Button second, TMP_Text status)
        {
            if (panel == null || panel.Find("Footer") != null) return;
            RectTransform footer = Node("Footer", panel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 30f), new Vector2(560f, 72f));
            var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            row.spacing = 20f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            foreach (Button button in new[] { first, second })
            {
                if (button == null) continue;
                button.transform.SetParent(footer, false);
                Layout((RectTransform)button.transform, 250f, 72f);
            }
            if (status != null)
            {
                RectTransform rect = status.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                rect.anchoredPosition = new Vector2(44f, 30f);
                rect.sizeDelta = new Vector2(Mathf.Max(260f, panel.rect.width - 44f - 40f - 560f), 72f);
                status.alignment = TextAlignmentOptions.MidlineLeft;
                status.textWrappingMode = TextWrappingModes.Normal;
                if (panel.Find("Divider") is RectTransform divider) divider.anchoredPosition = new Vector2(40f, 106f);
            }
        }

        // ---- клавиши в две колонки ----
        static void TwoColumnBindings(PauseMenuView view)
        {
            RectTransform panel = view.ControlsPanel;
            if (panel == null || view.BindingTemplate == null || panel.Find("Columns") != null) return;

            RectTransform columns = Node("Columns", panel, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -252f), new Vector2(930f, 372f));
            view.BindingsCombat = Column(columns, "Combat", "Бой", 0f);
            view.BindingsWorld = Column(columns, "World", "Мир", 475f);

            RectTransform template = (RectTransform)view.BindingTemplate.transform;
            template.SetParent(view.BindingsCombat, false);
            template.gameObject.SetActive(false);
            if (template.GetComponent<LayoutElement>() is LayoutElement element) { element.preferredHeight = element.minHeight = 58f; element.preferredWidth = -1f; }
            if (view.BindingTemplate.Action != null)
            {
                view.BindingTemplate.Action.fontSize = 19f;
                view.BindingTemplate.Action.rectTransform.sizeDelta = new Vector2(250f, 40f);
                view.BindingTemplate.Action.rectTransform.anchoredPosition = new Vector2(28f, 0f);
                view.BindingTemplate.Action.enableAutoSizing = true;
                view.BindingTemplate.Action.fontSizeMin = 14f;
                view.BindingTemplate.Action.fontSizeMax = 19f;
            }
            if (view.BindingTemplate.Key != null) ((RectTransform)view.BindingTemplate.Key.transform).sizeDelta = new Vector2(140f, 44f);
            if (template.Find("Flash") == null)
            {
                RectTransform flash = Stretch("Flash", template, -5f);
                view.BindingTemplate.FlashGraphic = Img(flash, Kit("pause_row_hover"), Color.white);
                flash.SetSiblingIndex(0);
            }
            view.BindingsContent = null;
            if (panel.Find("Bindings") is RectTransform scroll) Object.DestroyImmediate(scroll.gameObject);

            // Мышь не переназначается — отдельная притушенная строка-напоминание.
            RectTransform mouse = Node("Mouse", panel, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -636f), new Vector2(930f, 48f));
            Img(mouse, Kit("pause_row"), Color.white);
            mouse.gameObject.AddComponent<CanvasGroup>().alpha = 0.72f;
            Label(Node("Action", mouse, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(40f, 0f), new Vector2(520f, 36f)),
                "Идти · атаковать", _semibold, 19f, NavyInk, TextAlignmentOptions.MidlineLeft);
            Label(Node("Keys", mouse, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-40f, 0f), new Vector2(300f, 36f)),
                "ПКМ · ЛКМ", _bold, 19f, NavyInk, TextAlignmentOptions.MidlineRight, 4f);
        }

        static RectTransform Column(RectTransform parent, string name, string title, float left)
        {
            RectTransform column = Node(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, 0f), new Vector2(455f, 372f));
            Label(Node("Title", column, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 1f), Vector2.zero, new Vector2(0f, 34f)),
                title, _bold, 20f, Idle, TextAlignmentOptions.MidlineLeft, 14f, true).margin = new Vector4(12f, 0f, 0f, 0f);
            RectTransform rows = Node("Rows", column, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -40f), new Vector2(0f, 330f));
            var list = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8f;
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            return rows;
        }

        // ---- размытый фон ----
        static void BlurredBackdrop(PauseMenuView view)
        {
            if (view.Backdrop == null || view.BackdropBlur != null) return;
            var backdrop = (RectTransform)view.Backdrop.transform;
            var dimImage = backdrop.GetComponent<Image>();
            Color dim = dimImage != null ? dimImage.color : new Color(.01f, .03f, .06f, .62f);

            RectTransform blur = Stretch("Blur", backdrop, 0f);
            var raw = blur.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            var component = blur.gameObject.AddComponent<UiBackdropBlur>();
            component.Target = raw;
            view.BackdropBlur = component;

            // Затемнение — поверх снимка; сам фон остаётся прозрачным ловцом мыши.
            Img(Stretch("Dim", backdrop, 0f), null, new Color(dim.r, dim.g, dim.b, Mathf.Min(dim.a, 0.45f)));
            if (dimImage != null) dimImage.color = new Color(0f, 0f, 0f, 0f);
        }

        // ---- описания при наведении ----
        static void Hints(PauseMenuView view)
        {
            (Component where, string text)[] hints =
            {
                (Row(view.DisplayMode), "Весь экран — эксклюзивный режим; Окно — с рамкой; Без рамок — окно размером с монитор."),
                (Row(view.Resolution), "Размер кадра. В режиме «Без рамок» всегда равен монитору."),
                (Row(view.Quality), "Пресет: масштаб рендера, сглаживание и тени. Низкое — для слабых видеокарт."),
                (Row(view.VSync), "Кадры в такт монитору, без разрывов. Пока включена, ограничение кадров не действует."),
                (Row(view.FrameLimit), "Верхний предел кадров, когда синхронизация выключена."),
                (Row(view.Shadows), "Дальность и чёткость теней. На слабой видеокарте — Низкие."),
                (Row(view.UiScale), "Размер HUD и меню, 80–120%."),
                (Row(view.Master), "Громкость всей игры."),
                (Row(view.Effects), "Бой, шаги и интерфейс."),
                (Row(view.Music), "Музыка лагеря и Разлома."),
                (view.Reset, "Вернуть стандартные значения этой вкладки. Экран не меняется."),
                (view.Apply, "Применить режим экрана и разрешение — с проверкой 15 секунд."),
            };
            foreach ((Component where, string text) in hints)
            {
                if (where == null) continue;
                var hint = where.GetComponent<UiHint>();
                if (hint == null) hint = where.gameObject.AddComponent<UiHint>();
                hint.Text = text;
            }
        }

        /// <summary>Строка настройки — ближайший предок элемента с откликом на мышь.</summary>
        static Component Row(Component control)
        {
            for (Transform t = control != null ? control.transform.parent : null; t != null; t = t.parent)
                if (t.GetComponent<UiHoverMotion>() != null) return t;
            return null;
        }

        // ---- блик ----
        static void AddShine(Button button)
        {
            if (button == null || button.image == null) return;
            var rect = (RectTransform)button.transform;
            if (rect.Find("Shine Lane") != null) return;
            RectTransform lane = Stretch("Shine Lane", rect, 4f);
            var mask = Img(lane, button.image.sprite, Color.white);
            lane.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            mask.raycastTarget = false;
            RectTransform shine = Node("Shine", lane, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(70f, 160f));
            Img(shine, Chrome("pause_shine"), Color.white, false);
            shine.gameObject.SetActive(false);
            // Над подложкой и рамкой, но под иконкой и текстом.
            int content = rect.Find("Content") is Transform group ? group.GetSiblingIndex() : rect.childCount - 1;
            lane.SetSiblingIndex(Mathf.Max(0, content));
            var motion = rect.GetComponent<UiHoverMotion>();
            if (motion != null) motion.Shine = shine;
        }
    }
}
