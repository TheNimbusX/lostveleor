using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Помощник отрисовки пунктов меню разработчика (F8) в общем скине: строки, чипы, кнопки с подтверждением,
    /// переключатели-строки, сетки. Только IMGUI внутри OnGUI меню.
    ///
    /// Кнопки сами ничего не исполняют: вернули true — пункт кладёт действие в очередь через <see cref="Defer"/>,
    /// меню выполнит его в Update (OnGUI зовётся несколько раз за кадр, а смена раскладки посреди события
    /// ломает IMGUI). Взвод подтверждения живёт здесь: один на всё меню, три секунды неигровых часов, Esc снимает.
    /// </summary>
    public sealed class DevUi
    {
        private readonly DeveloperMenuSkin _skin;
        private readonly Action<string, Action<DevContext>, DevFlags> _enqueue;
        private readonly Func<string, string> _errorOf;
        private string _armedKey;
        private float _armedAt = -1f;
        private static readonly Color Faded = new Color(1f, 1f, 1f, .45f);
        // Без своих размеров кнопка не шире колонки: иначе длинная подпись раздвигала колонку за край панели.
        private static readonly GUILayoutOption[] Shrinkable = { GUILayout.MinWidth(1f) };

        internal DevUi(DeveloperMenuSkin skin, Action<string, Action<DevContext>, DevFlags> enqueue, Func<string, string> errorOf)
        {
            _skin = skin;
            _enqueue = enqueue;
            _errorOf = errorOf;
        }

        /// <summary>Неигровое время кадра; ставит меню в Update, чтобы Layout и Repaint видели одно и то же.</summary>
        internal float Now { get; set; }

        internal DeveloperMenuSkin Skin => _skin;

        /// <summary>Масштаб меню (1 при 1080p и масштабе UI 1,0).</summary>
        public float Scale => _skin.Scale;

        /// <summary>Пиксели в масштабе меню.</summary>
        public int Px(float value) => _skin.Px(value);

        /// <summary>Действие с побочными эффектами — в очередь меню, исполнится в Update. id — куда писать ошибку.</summary>
        public void Defer(Action<DevContext> action, string id = null)
        {
            if (action != null) _enqueue(id, action, DevFlags.None);
        }

        internal void Enqueue(string id, Action<DevContext> action, DevFlags flags) => _enqueue(id, action, flags);

        /// <summary>Снять взвод подтверждения. true — взвод был.</summary>
        internal bool Disarm()
        {
            bool was = _armedKey != null && DevMenuRules.Armed(_armedAt, Now);
            _armedKey = null;
            _armedAt = -1f;
            return was;
        }

        internal bool IsArmed(string key) => key != null && _armedKey == key && DevMenuRules.Armed(_armedAt, Now);

        // ---- текст ---------------------------------------------------------------------------------

        public void Text(string text) => GUILayout.Label(text ?? string.Empty, _skin.Label);

        public void Hint(string text)
        {
            if (!string.IsNullOrEmpty(text)) GUILayout.Label(text, _skin.Hint);
        }

        /// <summary>Ошибка последнего действия пункта — красным под ним.</summary>
        public void Error(string id)
        {
            string error = _errorOf?.Invoke(id);
            if (!string.IsNullOrEmpty(error)) GUILayout.Label("Ошибка: " + error, _skin.ErrorText);
        }

        public void Chip(string text, DevChip kind) => GUILayout.Label(text, _skin.ChipFor(kind), GUILayout.ExpandWidth(false));

        /// <summary>«Подпись   значение» в одну строку.</summary>
        public void Value(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _skin.RowHint, GUILayout.Width(Px(120)));
            GUILayout.Label(value ?? string.Empty, _skin.RowLabel, GUILayout.MinWidth(1f));
            GUILayout.EndHorizontal();
        }

        public void Space(float pixels) => GUILayout.Space(Px(pixels));
        public void BeginRow() => GUILayout.BeginHorizontal();
        public void EndRow() => GUILayout.EndHorizontal();

        /// <summary>
        /// Чипы последствий пункта: «сделает забег тестовым» (в обычном забеге), «пишет сохранение», «запоминается».
        /// Показываются, только когда последствие действительно наступит.
        /// </summary>
        public void Consequences(DevFlags flags, in DevContext context)
        {
            bool test = (flags & DevFlags.MarksTestRun) != 0 && context.RealRun;
            bool save = (flags & DevFlags.WritesSave) != 0;
            bool remembered = (flags & DevFlags.Remembered) != 0;
            if (!test && !save && !remembered) return;
            GUILayout.BeginHorizontal();
            if (test) Chip("сделает забег тестовым", DevChip.Warn);
            if (save) Chip("пишет сохранение", DevChip.Danger);
            if (remembered) Chip("запоминается", DevChip.Neutral);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ---- кнопки --------------------------------------------------------------------------------

        /// <summary>
        /// Кнопка с подтверждением по флагам. Первое нажатие взводит (красная кнопка «Ещё раз — …» на 3 с), второе
        /// возвращает true. Без подтверждения — true сразу. key — id пункта (взвод один на меню).
        /// </summary>
        public bool Button(string key, string label, DevFlags flags, bool realRun, bool enabled = true, params GUILayoutOption[] options)
        {
            bool confirm = DevMenuRules.NeedsConfirm(flags, realRun);
            bool armed = confirm && IsArmed(key);
            GUIStyle style = armed ? _skin.ButtonArmed : (flags & DevFlags.Danger) != 0 ? _skin.ButtonDanger : _skin.Button;
            string text = armed ? DevMenuRules.ConfirmLabel(flags, realRun) : label;
            return Confirmed(key, confirm, armed, Pressed(text, style, enabled, options));
        }

        /// <summary>Кнопка быстрой строки: короткая подпись, взвод — «Ещё раз?».</summary>
        internal bool QuickButton(string key, string label, DevFlags flags, bool realRun, bool enabled)
        {
            bool confirm = DevMenuRules.NeedsConfirm(flags, realRun);
            bool armed = confirm && IsArmed(key);
            var style = armed ? _skin.QuickArmed : _skin.QuickButton;
            return Confirmed(key, confirm, armed, Pressed(armed ? "Ещё раз?" : label, style, enabled,
                new[] { GUILayout.ExpandWidth(true), GUILayout.MinWidth(Px(120)) }));
        }

        /// <summary>Нажатие без отрисовки (Enter в поле, съёмка): та же логика взвода. true — пора исполнять.</summary>
        internal bool Press(string key, DevFlags flags, bool realRun)
        {
            bool confirm = DevMenuRules.NeedsConfirm(flags, realRun);
            return Confirmed(key, confirm, confirm && IsArmed(key), true);
        }

        /// <summary>Простая кнопка без подтверждения.</summary>
        public bool Plain(string label, bool enabled = true, params GUILayoutOption[] options)
            => Pressed(label, _skin.Button, enabled, options);

        /// <summary>Маленькая кнопка («−», «+», «‹», «›»). on — подсвечена акцентом.</summary>
        public bool Small(string label, bool on = false, bool enabled = true, float width = 40f)
            => Pressed(label, on ? _skin.SmallOn : _skin.Small, enabled, new[] { GUILayout.Width(Px(width)) });

        /// <summary>Переключатель-строка: подпись слева, справа чип «ВКЛ» / «выкл», клик по всей строке.</summary>
        public bool ToggleRow(string label, bool on, bool enabled = true) => ToggleIn(label, on, enabled, _skin.ToggleRow, 10f);

        /// <summary>Переключатель быстрой строки.</summary>
        internal bool QuickToggle(string label, bool on, bool enabled)
        {
            return ToggleIn(label, on, enabled, _skin.QuickToggle, 8f, GUILayout.ExpandWidth(true), GUILayout.MinWidth(Px(150)));
        }

        /// <summary>Ряд кнопок выбора; выбранная — акцентом. Возвращает выбранный индекс (тот же, если не нажимали).</summary>
        public int Grid(int selected, string[] options, int perRow = 0, float minWidth = 40f, bool enabled = true)
        {
            int chosen = selected;
            int row = perRow > 0 ? perRow : options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                if (i % row == 0) GUILayout.BeginHorizontal();
                if (Pressed(options[i], i == selected ? _skin.SmallOn : _skin.Small, enabled,
                        new[] { GUILayout.MinWidth(Px(minWidth)), GUILayout.ExpandWidth(true) }))
                    chosen = i;
                if (i % row == row - 1 || i == options.Length - 1) GUILayout.EndHorizontal();
            }
            return chosen;
        }

        /// <summary>Поле ввода. name — имя для фокуса (Enter, Esc).</summary>
        public string Field(string name, string value, int maxLength, float width)
        {
            GUI.SetNextControlName(name);
            return GUILayout.TextField(value ?? string.Empty, maxLength, _skin.Field, GUILayout.Width(Px(width)));
        }

        private bool Confirmed(string key, bool confirm, bool armed, bool clicked)
        {
            if (!clicked) return false;
            if (!confirm || armed)
            {
                _armedKey = null;
                _armedAt = -1f;
                return true;
            }
            _armedKey = key;
            _armedAt = Now;
            return false;
        }

        private bool ToggleIn(string label, bool on, bool enabled, GUIStyle style, float chipInset, params GUILayoutOption[] options)
        {
            var content = new GUIContent(label);
            Rect rect = options != null && options.Length > 0
                ? GUILayoutUtility.GetRect(content, style, options)
                : GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true), GUILayout.MinWidth(1f));
            bool clicked;
            Color color = GUI.color;
            if (enabled) clicked = GUI.Button(rect, content, style);
            else
            {
                GUI.color = Faded;
                GUI.Label(rect, content, style);
                clicked = false;
            }
            string chip = on ? "ВКЛ" : "выкл";
            GUIStyle chipStyle = on ? _skin.ChipOn : _skin.ChipOff;
            Vector2 size = chipStyle.CalcSize(new GUIContent(chip));
            var chipRect = new Rect(rect.xMax - size.x - Px(chipInset), rect.y + (rect.height - size.y) * .5f, size.x, size.y);
            GUI.Label(chipRect, chip, chipStyle);
            GUI.color = color;
            return clicked;
        }

        /// <summary>Недоступная кнопка рисуется надписью в стиле кнопки: без наведения и нажатия, прозрачность 0,45.</summary>
        private bool Pressed(string text, GUIStyle style, bool enabled, GUILayoutOption[] options)
        {
            if (options == null || options.Length == 0) options = Shrinkable;
            if (enabled) return GUILayout.Button(text, style, options);
            Color color = GUI.color;
            GUI.color = Faded;
            GUILayout.Label(text, style, options);
            GUI.color = color;
            return false;
        }
    }
}
