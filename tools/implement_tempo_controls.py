from pathlib import Path
root=Path('razlom/Assets')
def edit(path,pairs):
 p=root/path;s=p.read_text(encoding='utf-8-sig')
 for a,b in pairs:
  assert a in s,(path,a[:100]);s=s.replace(a,b)
 p.write_text(s,encoding='utf-8')
edit('Game.View/GameUserSettings.cs',[
 ('private const string AbilityLayoutKey', 'public static bool WasdMovement { get; private set; }\n        private const string MovementKey = "settings.controls.wasd";\n        private const string AbilityLayoutKey'),
 ('FrameCap = PlayerPrefs.GetInt(FrameCapKey, 0);','WasdMovement = PlayerPrefs.GetInt(MovementKey, 0) == 1;\n            FrameCap = PlayerPrefs.GetInt(FrameCapKey, 0);'),
 ('public static void SetAbilityLayout(AbilityLayout layout)', '''public static void SetWasdMovement(bool value)
        {
            Load();
            if (WasdMovement == value) return;
            WasdMovement = value;
            PlayerPrefs.SetInt(MovementKey, value ? 1 : 0);
            PlayerPrefs.Save();
            GameKeyBindings.NotifyLayoutChanged();
        }

        public static void SetAbilityLayout(AbilityLayout layout)'''),
 ('public static bool AbilityRowUsesLetters => Abilities == AbilityLayout.Qwer;', 'public static bool AbilityRowUsesLetters => !WasdMovement && Abilities == AbilityLayout.Qwer;')])
edit('Game.View/GameKeyBindings.cs',[
 ('const string Prefix = "settings.keys.";', 'static string Prefix => GameUserSettings.WasdMovement ? "settings.keys.wasd." : "settings.keys.";\n        static bool _loadedWasd;'),
 ('if (_loaded) return;\n            _loaded = true;', 'if (_loaded && _loadedWasd == GameUserSettings.WasdMovement) return;\n            _loaded = true;\n            _loadedWasd = GameUserSettings.WasdMovement;'),
 ('if (key == KeyCode.None || key == KeyCode.Escape || !IsSupported(key)) return false;', 'if (key == KeyCode.None || key == KeyCode.Escape || !IsSupported(key)) return false;\n            if (GameUserSettings.WasdMovement && (key == KeyCode.W || key == KeyCode.A || key == KeyCode.S || key == KeyCode.D)) return false;'),
 ('public static bool Held(GameAction action) => KeyHeld(KeyFor(action));', 'public static bool Held(GameAction action) => KeyHeld(KeyFor(action));\n        public static bool HeldKey(KeyCode key) => KeyHeld(key);')])
edit('Game.View/PauseMenu.cs',[
 ('v.AbilityLayout.Chosen += index => GameUserSettings.SetAbilityLayout(AbilityLayouts[Mathf.Clamp(index, 0, AbilityLayouts.Length - 1)]);', 'v.AbilityLayout.Chosen += index => GameUserSettings.SetWasdMovement(index == 1);'),
 ('v.AbilityLayout.SetLabels(new[] { AbilityLayoutName(AbilityLayouts[0]), AbilityLayoutName(AbilityLayouts[1]) });', 'v.AbilityLayout.SetLabels(new[] { "Мышь", "WASD" });'),
 ('v.AbilityLayout.SetSelected(Mathf.Max(0, Array.IndexOf(AbilityLayouts, GameUserSettings.Abilities)));', 'v.AbilityLayout.SetSelected(GameUserSettings.WasdMovement ? 1 : 0);'),
 ('"Нажмите на клавишу справа, чтобы назначить свою. Раскладка задаёт клавиши способностей по умолчанию."', '(GameUserSettings.WasdMovement ? "WASD — движение · мышь — прицел · 1–4 — навыки · Space — кувырок. Нажмите на клавишу для переназначения." : "ПКМ — движение · ЛКМ — атака. Нажмите на клавишу справа для переназначения.")'),
 ('GUI.Label(new Rect(x, y, 260f, 38f), "Ряд клавиш", _label);', 'GUI.Label(new Rect(x, y, 260f, 38f), "Движение", _label);'),
 ('int layoutIndex = Array.IndexOf(AbilityLayouts, GameUserSettings.Abilities);','int layoutIndex = GameUserSettings.WasdMovement ? 1 : 0;'),
 ('AbilityLayoutName(AbilityLayouts[layoutIndex]), layoutIndex,\n                AbilityLayouts.Length);\n            GameUserSettings.SetAbilityLayout(AbilityLayouts[layoutIndex]);', '(layoutIndex == 1 ? "WASD" : "Мышь"), layoutIndex, 2);\n            GameUserSettings.SetWasdMovement(layoutIndex == 1);')])
edit('Editor/PauseMenuBuilder.cs',[
 ('LayoutVersion = 6;', 'LayoutVersion = 7;'),
 ('view.LayoutVersion = LayoutVersion;', '''if (view.LayoutVersion < 7 && view.ControlsPanel != null)
                foreach (var label in view.ControlsPanel.GetComponentsInChildren<TMP_Text>(true))
                    if (label.text == "Раскладка способностей") label.text = "Движение";
            view.LayoutVersion = LayoutVersion;'''),
 ('"Раскладка способностей", "menu_controls"','"Движение", "menu_controls"')])
edit('Game.View/TickDriver.cs',[
 ('InputFrame frame = ConsumeInput();','InputFrame frame = ConsumeInput();\n                CaptureDirectionalMovement(ref frame);'),
 ('internal void CaptureAim(Vector2 screenPosition, bool moveHeld, bool movePressed,\n            bool attackHeld, bool attackPressed = false)\n        {', 'internal void CaptureAim(Vector2 screenPosition, bool moveHeld, bool movePressed,\n            bool attackHeld, bool attackPressed = false)\n        {\n            if (GameUserSettings.WasdMovement) moveHeld = movePressed = false;')])
edit('Game.View/CampPlayerView.cs',[
 ('if (interact && NearTent())', 'if (GameUserSettings.WasdMovement) { click = held = false; CancelRoute(); }\n            if (interact && NearTent())'),
 ('if (!Active) return;\n            if (input.AbilityMask', 'if (!Active) return;\n            if (input.Has(InputFlags.DirectMovement)) { CancelRoute(); return; }\n            if (input.AbilityMask')])
