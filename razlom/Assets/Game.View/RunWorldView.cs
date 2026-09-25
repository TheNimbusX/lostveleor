using System.Collections.Generic;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Интерфейс, привязанный к точкам мира, на паке «Ночная акварель» (владелец, 24 сентября,
    /// концепт 3-rift-world): метки «Вход», «Выход», «Тайник · охрана 4», табличка элиты с полоской
    /// здоровья, подпись лежащей добычи, мини-меню способности при полной панели («Разобрать» и
    /// «Заменить 1–4» на одной карточке) и подсказка выбора цели у курсора. Раньше это рисовал IMGUI
    /// в RunHud и PelagTargetAimView; они остаются запасным видом, пока префаба нет.
    /// Префаб Resources/UI/Prefabs/RunWorldWc, добавляет RunHud.
    /// </summary>
    public sealed class RunWorldView : MonoBehaviour
    {
        [Header("Метки")]
        [Tooltip("Шаблон метки: плашка, значок, надпись, необязательная полоска здоровья")] public RunWorldMarker MarkerTemplate;
        public Texture ExitIcon, CacheIcon, EntryIcon, EliteIcon, ItemIcon;

        [Header("Мини-меню добычи")]
        public RectTransform DropMenu;
        public RawImage DropIcon;
        public TMP_Text DropTitle;
        public Button DropSalvage;
        public TMP_Text DropSalvageLabel;
        public Button[] DropReplace = new Button[4];
        public RawImage[] DropReplaceIcons = new RawImage[4];

        [Header("Подсказка цели")]
        public RectTransform AimHint;
        public TMP_Text AimHintText;

        /// <summary>Подсказку выбора цели рисует Canvas — PelagTargetAimView свою не рисует.</summary>
        public static bool AimHintShown { get; private set; }

        TickDriver _driver;
        readonly List<RunWorldMarker> _markers = new List<RunWorldMarker>();
        int _used;
        int _menuDrop = -1;
        Canvas _canvas;

        public void Initialize(TickDriver driver)
        {
            _driver = driver;
            _canvas = GetComponent<Canvas>();
            if (MarkerTemplate != null) MarkerTemplate.gameObject.SetActive(false);
            if (DropMenu != null) DropMenu.gameObject.SetActive(false);
            if (AimHint != null) AimHint.gameObject.SetActive(false);
            if (DropSalvage != null) DropSalvage.onClick.AddListener(() =>
            {
                GameSound.Play("salvage", .8f);
                _driver.QueueRunCommand(RunCommand.PickupSalvage);
            });
            for (int i = 0; i < DropReplace.Length; i++)
            {
                int slot = i;
                if (DropReplace[i] != null)
                    DropReplace[i].onClick.AddListener(() => _driver.QueueRunCommand((RunCommand)((int)RunCommand.PickupReplaceSlot1 + slot)));
            }
            PauseMenuView.EnsureEventSystem();
        }

        void OnDestroy() => AimHintShown = false;

        float Scale => _canvas != null ? _canvas.scaleFactor : 1f;

        /// <summary>Курсор над мини-меню добычи: клик по кнопке не должен стать ударом или приказом идти.</summary>
        public bool PointerOverMenu(Vector2 screen)
        {
            if (DropMenu == null || !DropMenu.gameObject.activeInHierarchy) return false;
            var corners = new Vector3[4];
            DropMenu.GetWorldCorners(corners);
            return screen.x >= corners[0].x && screen.x <= corners[2].x && screen.y >= corners[0].y && screen.y <= corners[2].y;
        }

        void LateUpdate()
        {
            _used = 0;
            RiftRun run = _driver != null ? _driver.Run : null;
            bool live = _driver != null && !_driver.GameplayPaused && _driver.Session != null && _driver.Session.Mode == GameMode.Rift
                        && run != null && (run.Phase == RunPhase.Clearing || run.Phase == RunPhase.SeekingExit);
            Camera camera = Camera.main;
            if (live && camera != null)
            {
                Landmarks(run, camera);
                Drops(run, camera);
            }
            for (int i = _used; i < _markers.Count; i++)
                if (_markers[i].gameObject.activeSelf) _markers[i].gameObject.SetActive(false);
            Menu(live ? run : null, camera);
            Aim();
        }

        // ---------------------------------------------------------------- метки

        RunWorldMarker Next()
        {
            if (_used == _markers.Count)
            {
                RunWorldMarker marker = Instantiate(MarkerTemplate, MarkerTemplate.transform.parent);
                marker.name = "Метка " + (_markers.Count + 1);
                _markers.Add(marker);
            }
            return _markers[_used++];
        }

        void Place(Camera camera, FixVec2 point, float height, Texture icon, string text, float health = -1f, bool elite = false)
        {
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(point.X.ToFloat(), height, point.Y.ToFloat()));
            if (screen.z <= 0f || screen.x < -60f || screen.x > Screen.width + 60f || screen.y < -40f || screen.y > Screen.height + 40f) return;
            if (MarkerTemplate == null) return;
            RunWorldMarker marker = Next();
            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
            marker.Show(icon, text, health, elite);
            marker.transform.position = new Vector3(screen.x, screen.y, 0f);
        }

        void Landmarks(RiftRun run, Camera camera)
        {
            if (run.Map.Routes == null) return;
            Place(camera, run.Map.EntryPoint, .6f, EntryIcon, "Вход");
            for (int e = 0; e < run.Map.ExitCount; e++)
                Place(camera, run.Map.ExitPoint(e), .6f, ExitIcon, run.Phase == RunPhase.SeekingExit ? "Выход" : "Выход · после боя");
            for (int b = 0; b < run.Map.RewardBranchCount; b++)
            {
                int guards = run.BranchGuardsAlive(b);
                string label = run.IsBranchClaimed(b) ? "Тайник пуст" : guards > 0 ? "Тайник · охрана " + guards : "Тайник · подойди";
                Place(camera, run.Map.CenterOf(run.Map.GetRewardBranch(b)), .6f, CacheIcon, label);
            }
            if (run.Encounters == null) return;
            Simulation sim = run.Sim;
            for (int i = 1; i < sim.Entities.Count; i++)
            {
                // Босса ведёт полоса сверху экрана — над ним таблички нет. У элиты табличка только с
                // именем: полоса здоровья одна — мировая (HealthBars), вторая в табличке её дублировала.
                if (i == run.BossId || !sim.Entities.Alive[i] || !run.Encounters.IsElite(i)) continue;
                Place(camera, sim.Entities.Position[i], 3.2f, EliteIcon, EnemyTexts.Name(sim.Entities.Kind[i]), -1f, true);
            }
        }

        void Drops(RiftRun run, Camera camera)
        {
            int menu = DropMenuTarget(run);
            for (int d = 0; d < run.DropCount; d++)
            {
                RunDrop drop = run.GetDrop(d);
                if (drop.Claimed || d == menu) continue;
                AbilityDefinition definition = drop.Offer.Kind == RewardKind.Ability ? PelagKit.PoolDefinition(drop.Offer.PoolIndex) : null;
                Place(camera, drop.Position, .9f, definition != null ? AbilityIcon(definition.Id) : ItemIcon,
                    definition != null ? PlayerHud.AbilityName(definition.Id) : "Предмет · подойди");
            }
        }

        static Texture AbilityIcon(int definitionId)
        {
            string file = PlayerHud.IconFile(definitionId);
            return file != null ? Resources.Load<Texture2D>("UI/Abilities/" + file) : null;
        }

        /// <summary>Меню нужно только при полной панели: иначе способность поднимается сама.</summary>
        static int DropMenuTarget(RiftRun run)
            => (run.Phase == RunPhase.Clearing || run.Phase == RunPhase.SeekingExit) && run.Loadout.IsFull
                ? run.NearestAbilityDrop(RiftRun.DropMenuRadius)
                : -1;

        // ---------------------------------------------------------------- мини-меню добычи

        void Menu(RiftRun run, Camera camera)
        {
            if (DropMenu == null) return;
            int index = run != null ? DropMenuTarget(run) : -1;
            AbilityDefinition incoming = index >= 0 ? PelagKit.PoolDefinition(run.GetDrop(index).Offer.PoolIndex) : null;
            bool shown = incoming != null && camera != null;
            Vector3 screen = Vector3.zero;
            if (shown)
            {
                FixVec2 at = run.GetDrop(index).Position;
                screen = camera.WorldToScreenPoint(new Vector3(at.X.ToFloat(), 1.6f, at.Y.ToFloat()));
                shown = screen.z > 0f;
            }
            if (DropMenu.gameObject.activeSelf != shown) DropMenu.gameObject.SetActive(shown);
            if (!shown) { _menuDrop = -1; return; }

            if (index != _menuDrop)
            {
                _menuDrop = index;
                if (DropIcon != null) DropIcon.texture = AbilityIcon(incoming.Id);
                RunHudView.SetText(DropTitle, PlayerHud.AbilityName(incoming.Id));
            }
            RunHudView.SetText(DropSalvageLabel, "Разобрать · +" + run.SalvageGold);
            for (int slot = 0; slot < DropReplaceIcons.Length && slot < RunLoadout.Slots; slot++)
            {
                AbilityDefinition current = run.Loadout.DefinitionAt(slot);
                if (DropReplaceIcons[slot] == null) continue;
                Texture icon = current != null ? AbilityIcon(current.Id) : null;
                if (DropReplaceIcons[slot].texture != icon) DropReplaceIcons[slot].texture = icon;
                DropReplaceIcons[slot].enabled = icon != null;
            }
            DropMenu.position = new Vector3(screen.x, screen.y + 14f * Scale, 0f);
        }

        // ---------------------------------------------------------------- подсказка цели

        void Aim()
        {
            bool aiming = _driver != null && _driver.AimingAbilityTarget && AimHint != null;
            AimHintShown = aiming;
            if (AimHint == null) return;
            if (AimHint.gameObject.activeSelf != aiming) AimHint.gameObject.SetActive(aiming);
            if (!aiming) return;
            bool pad = _driver.UsingGamepad;
            bool ground = _driver.GroundTargetedSlot(_driver.AbilityTargetAimSlot);
            RunHudView.SetText(AimHintText, (ground ? "Выбери точку" : "Выбери врага")
                + (pad ? "\n<size=80%>Правый стик — прицел · RT/A — применить · B — отмена</size>"
                       : "\n<size=80%>ЛКМ — применить · ПКМ / Esc — отмена</size>"));
            Vector2 point = pad ? new Vector2(Screen.width * .5f, Screen.height * .27f) : (Vector2)PointerPosition();
            AimHint.position = new Vector3(point.x + 26f * Scale, point.y - 18f * Scale, 0f);
        }

#if ENABLE_INPUT_SYSTEM
        static Vector2 PointerPosition() => UnityEngine.InputSystem.Mouse.current != null
            ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() : Vector2.zero;
#else
        static Vector2 PointerPosition() => Input.mousePosition;
#endif
    }
}
