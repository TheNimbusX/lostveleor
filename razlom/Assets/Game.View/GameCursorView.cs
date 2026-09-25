using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
#endif

namespace Game.View
{
    /// <summary>Один владелец аппаратного курсора для мира, боя и выбора цели.</summary>
    [DefaultExecutionOrder(500)]
    [RequireComponent(typeof(TickDriver))]
    public sealed class GameCursorView : MonoBehaviour
    {
        private enum Kind : byte { Pointer, Attack, Interact, Aim, Blocked }
        private readonly Texture2D[] _textures = new Texture2D[5];
        private static readonly Vector2[] Hotspots =
        {
            new Vector2(8, 3),  // pointer tip; also used for ground movement
            new Vector2(8, 4),  // sabre tip
            new Vector2(24, 24),
            new Vector2(24, 24),
            new Vector2(8, 3),
        };
        private TickDriver _driver;
        private CombatIndicators _indicators;
        private int _shown = -1;
        private bool _hiddenForPad;

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            _indicators = GetComponent<CombatIndicators>();
            string[] names = { "pointer", "attack", "interact", "aim", "blocked" };
            for (int i = 0; i < names.Length; i++)
                _textures[i] = Resources.Load<Texture2D>("UI/Cursors/" + names[i]);
        }

        private void LateUpdate()
        {
            if (_driver == null) return;
            bool pad = _driver.UsingGamepad && !_driver.GameplayPaused && !MainMenuView.IsOpen
                && CampServicesView.Instance?.IsOpen != true
                && CampPlayerView.Instance?.InventoryOpen != true;
            if (pad != _hiddenForPad)
            {
                _hiddenForPad = pad;
                Cursor.visible = !pad;
            }
            if (pad) return;

            Kind next = Kind.Pointer;
            bool ui = _driver.GameplayPaused || MainMenuView.IsOpen
                || CampServicesView.Instance?.IsOpen == true
                || CampPlayerView.Instance?.InventoryOpen == true;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                ui |= _driver.PointerOverHud(mouse.position.ReadValue())
                    || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
#endif
            if (!ui)
            {
                if (_driver.AimingAbilityTarget)
                    next = _driver.GroundTargetedSlot(_driver.AbilityTargetAimSlot) || _driver.HoveredEntity >= 0
                        ? Kind.Aim : Kind.Blocked;
                else if (CampServicesView.Instance?.HoveredService == true)
                    next = Kind.Interact;
                else if (_driver.HoveredEntity >= 0 && (_indicators == null || _indicators.ShowAttackCursor))
                    next = Kind.Attack;
            }
            if (_shown == (int)next) return;
            _shown = (int)next;
            Texture2D texture = _textures[_shown];
            Cursor.SetCursor(texture, Hotspots[_shown], CursorMode.Auto);
        }

        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (_hiddenForPad) Cursor.visible = true;
            _hiddenForPad = false;
            _shown = -1;
        }
    }
}
