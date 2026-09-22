using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Действия, которые игрок может переназначить. ТОЛЬКО ДОПИСЫВАТЬ В КОНЕЦ:
    /// номер действия лежит в сохранённых настройках.
    /// </summary>
    public enum GameAction : byte
    {
        Ability1 = 0,
        Ability2 = 1,
        Ability3 = 2,
        Ability4 = 3,
        Dash = 4,
        Interact = 5,
        EnterRift = 6,
        LeaveRift = 7,
        RepeatRun = 8,
        ReturnToCamp = 9,
        HealthPotion = 10,
        LavidiumPotion = 11,
    }

    /// <summary>
    /// Клавиши действий: раскладка QWER/1234 задаёт умолчания ряда способностей,
    /// поверх неё игрок назначает любую клавишу сам.
    ///
    /// Хранится KeyCode — он есть при любой системе ввода; для Input System
    /// переводится по таблице. Мышь не переназначается: ПКМ — идти, ЛКМ — бить.
    /// Escape не назначается никогда — это пауза.
    ///
    /// Конфликт решается обменом, но только внутри группы: бой (способности и
    /// кувырок) и команды мира. Совпадение между группами задумано — E и способность,
    /// и вход в Разлом (см. GameUserSettings.AbilityRowUsesLetters).
    /// </summary>
    public static class GameKeyBindings
    {
        public const int Count = 12;
        static string Prefix => GameUserSettings.WasdMovement ? "settings.keys.wasd." : "settings.keys.";
        static bool _loadedWasd;

        static readonly KeyCode[] Custom = new KeyCode[Count];
        static bool _loaded;

        /// <summary>Сменилась любая клавиша — подписи на панели способностей обновляются.</summary>
        public static event Action Changed;

        public static string ActionName(GameAction action)
        {
            switch (action)
            {
                case GameAction.Ability1: return "Способность 1";
                case GameAction.Ability2: return "Способность 2";
                case GameAction.Ability3: return "Способность 3";
                case GameAction.Ability4: return "Способность 4";
                case GameAction.Dash: return "Кувырок";
                case GameAction.HealthPotion: return "Зелье здоровья";
                case GameAction.LavidiumPotion: return "Зелье лавидия";
                case GameAction.Interact: return "Взаимодействие · сумка";
                case GameAction.EnterRift: return "Войти в Разлом";
                case GameAction.LeaveRift: return "Уйти из Разлома с добычей";
                case GameAction.RepeatRun: return "Повторить забег";
                default: return "Вернуться в лагерь";
            }
        }

        public static KeyCode Default(GameAction action)
        {
            bool letters = GameUserSettings.AbilityRowUsesLetters;
            switch (action)
            {
                case GameAction.Ability1: return letters ? KeyCode.Q : KeyCode.Alpha1;
                case GameAction.Ability2: return letters ? KeyCode.W : KeyCode.Alpha2;
                case GameAction.Ability3: return letters ? KeyCode.E : KeyCode.Alpha3;
                case GameAction.Ability4: return letters ? KeyCode.R : KeyCode.Alpha4;
                case GameAction.Dash: return KeyCode.Space;
                case GameAction.HealthPotion: return KeyCode.Alpha5;
                case GameAction.LavidiumPotion: return KeyCode.Alpha6;
                case GameAction.Interact: return KeyCode.I;
                case GameAction.EnterRift: return KeyCode.E;
                case GameAction.LeaveRift: return KeyCode.L;
                case GameAction.RepeatRun: return KeyCode.R;
                default: return KeyCode.C;
            }
        }

        public static KeyCode KeyFor(GameAction action)
        {
            Load();
            KeyCode custom = Custom[(int)action];
            return custom != KeyCode.None ? custom : Default(action);
        }

        public static bool IsCustom(GameAction action)
        {
            Load();
            return Custom[(int)action] != KeyCode.None;
        }

        static int Group(GameAction action) => action <= GameAction.Dash || action >= GameAction.HealthPotion ? 0 : 1;

        /// <summary>Назначить клавишу; действие своей группы с той же клавишей получает прежнюю клавишу этого.</summary>
        public static bool Rebind(GameAction action, KeyCode key) => Rebind(action, key, out _);

        /// <summary>То же; <paramref name="swapped"/> — действие, отдавшее клавишу (−1 — обмена не было).</summary>
        public static bool Rebind(GameAction action, KeyCode key, out int swapped)
        {
            Load();
            swapped = -1;
            if (key == KeyCode.None || key == KeyCode.Escape || !IsSupported(key)) return false;
            if (GameUserSettings.WasdMovement && (key == KeyCode.W || key == KeyCode.A || key == KeyCode.S || key == KeyCode.D)) return false;
            KeyCode previous = KeyFor(action);
            if (previous == key) return true;
            for (int i = 0; i < Count; i++)
            {
                var other = (GameAction)i;
                if (other == action || Group(other) != Group(action) || KeyFor(other) != key) continue;
                Store(other, previous);
                swapped = i;
            }
            Store(action, key);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        public static void ResetAll()
        {
            Load();
            for (int i = 0; i < Count; i++)
            {
                Custom[i] = KeyCode.None;
                PlayerPrefs.DeleteKey(Prefix + i);
            }
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Раскладка ряда сменилась: подписи зависят от неё, свои клавиши остаются.</summary>
        internal static void NotifyLayoutChanged() => Changed?.Invoke();

        static void Store(GameAction action, KeyCode key)
        {
            // Клавиша, совпадающая с умолчанием, хранится как «не назначено»: смена
            // раскладки QWER/1234 тогда продолжает двигать этот слот.
            Custom[(int)action] = key == Default(action) ? KeyCode.None : key;
            if (Custom[(int)action] == KeyCode.None) PlayerPrefs.DeleteKey(Prefix + (int)action);
            else PlayerPrefs.SetInt(Prefix + (int)action, (int)key);
        }

        static void Load()
        {
            if (_loaded && _loadedWasd == GameUserSettings.WasdMovement) return;
            _loaded = true;
            _loadedWasd = GameUserSettings.WasdMovement;
            for (int i = 0; i < Count; i++)
            {
                var key = (KeyCode)PlayerPrefs.GetInt(Prefix + i, (int)KeyCode.None);
                Custom[i] = IsSupported(key) ? key : KeyCode.None;
            }
        }

        public static bool Pressed(GameAction action) => KeyDown(KeyFor(action));
        public static bool Held(GameAction action) => KeyHeld(KeyFor(action));
        public static bool HeldKey(KeyCode key) => KeyHeld(key);

        // ---- таблица клавиш: KeyCode ↔ имя Key в Input System ----
        static readonly (KeyCode code, string input, string label)[] Table = BuildTable();

        static (KeyCode, string, string)[] BuildTable()
        {
            var rows = new System.Collections.Generic.List<(KeyCode, string, string)>();
            for (KeyCode c = KeyCode.A; c <= KeyCode.Z; c++) rows.Add((c, c.ToString(), c.ToString()));
            for (int d = 0; d <= 9; d++) rows.Add((KeyCode.Alpha0 + d, "Digit" + d, d.ToString()));
            for (int d = 0; d <= 9; d++) rows.Add((KeyCode.Keypad0 + d, "Numpad" + d, "Num " + d));
            for (int f = 1; f <= 12; f++) rows.Add((KeyCode.F1 + f - 1, "F" + f, "F" + f));
            rows.Add((KeyCode.Space, "Space", "Space"));
            rows.Add((KeyCode.Tab, "Tab", "Tab"));
            rows.Add((KeyCode.CapsLock, "CapsLock", "Caps"));
            rows.Add((KeyCode.LeftShift, "LeftShift", "Shift"));
            rows.Add((KeyCode.RightShift, "RightShift", "П.Shift"));
            rows.Add((KeyCode.LeftControl, "LeftCtrl", "Ctrl"));
            rows.Add((KeyCode.RightControl, "RightCtrl", "П.Ctrl"));
            rows.Add((KeyCode.LeftAlt, "LeftAlt", "Alt"));
            rows.Add((KeyCode.RightAlt, "RightAlt", "П.Alt"));
            rows.Add((KeyCode.BackQuote, "Backquote", "`"));
            rows.Add((KeyCode.Minus, "Minus", "-"));
            rows.Add((KeyCode.Equals, "Equals", "="));
            rows.Add((KeyCode.LeftBracket, "LeftBracket", "["));
            rows.Add((KeyCode.RightBracket, "RightBracket", "]"));
            rows.Add((KeyCode.Semicolon, "Semicolon", ";"));
            rows.Add((KeyCode.Quote, "Quote", "'"));
            rows.Add((KeyCode.Comma, "Comma", ","));
            rows.Add((KeyCode.Period, "Period", "."));
            rows.Add((KeyCode.Slash, "Slash", "/"));
            rows.Add((KeyCode.Backslash, "Backslash", "\\"));
            return rows.ToArray();
        }

        static int IndexOf(KeyCode key)
        {
            for (int i = 0; i < Table.Length; i++) if (Table[i].code == key) return i;
            return -1;
        }

        public static bool IsSupported(KeyCode key) => IndexOf(key) >= 0;

        /// <summary>Подпись клавиши для клавиш-плашек: Q, 1, Space, Shift.</summary>
        public static string Label(KeyCode key)
        {
            int i = IndexOf(key);
            return i >= 0 ? Table[i].label : "—";
        }

        public static string Label(GameAction action) => Label(KeyFor(action));

#if ENABLE_INPUT_SYSTEM
        static readonly Key[] InputKeys = BuildInputKeys();

        static Key[] BuildInputKeys()
        {
            var keys = new Key[Table.Length];
            for (int i = 0; i < Table.Length; i++)
                keys[i] = Enum.TryParse(Table[i].input, out Key key) ? key : Key.None;
            return keys;
        }

        static UnityEngine.InputSystem.Controls.KeyControl Control(KeyCode code)
        {
            int i = IndexOf(code);
            Keyboard keyboard = Keyboard.current;
            return i >= 0 && keyboard != null && InputKeys[i] != Key.None ? keyboard[InputKeys[i]] : null;
        }

        static bool KeyDown(KeyCode code) { var key = Control(code); return key != null && key.wasPressedThisFrame; }
        static bool KeyHeld(KeyCode code) { var key = Control(code); return key != null && key.isPressed; }

        /// <summary>Клавиша, нажатая в этом кадре, для окна «нажмите клавишу»; None — ничего.</summary>
        public static KeyCode PressedThisFrame()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return KeyCode.None;
            if (keyboard.escapeKey.wasPressedThisFrame) return KeyCode.Escape;
            for (int i = 0; i < Table.Length; i++)
                if (InputKeys[i] != Key.None && keyboard[InputKeys[i]].wasPressedThisFrame) return Table[i].code;
            return KeyCode.None;
        }
#else
        static bool KeyDown(KeyCode code) => Input.GetKeyDown(code);
        static bool KeyHeld(KeyCode code) => Input.GetKey(code);

        public static KeyCode PressedThisFrame()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) return KeyCode.Escape;
            for (int i = 0; i < Table.Length; i++)
                if (Input.GetKeyDown(Table[i].code)) return Table[i].code;
            return KeyCode.None;
        }
#endif
    }
}
