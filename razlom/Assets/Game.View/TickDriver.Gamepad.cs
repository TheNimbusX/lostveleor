using Game.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    public sealed partial class TickDriver
    {
        private bool _usingGamepad;
        private FixVec2 _gamepadAimDirection = new FixVec2(Fix64.One, Fix64.Zero);
        private readonly bool[] _gamepadSlots = new bool[5];

        public bool UsingGamepad => _usingGamepad;
        public static bool GamepadLastUsed { get; private set; }

        private Vector2 GamepadMovement()
        {
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad == null) return Vector2.zero;
            Vector2 stick = pad.leftStick.ReadValue();
            return stick.sqrMagnitude < 0.035f ? Vector2.zero : stick;
#else
            return Vector2.zero;
#endif
        }

        private void CaptureGamepad(bool choosing)
        {
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad == null) { _usingGamepad = GamepadLastUsed = false; return; }
            Vector2 move = GamepadMovement();
            Vector2 look = pad.rightStick.ReadValue();
            bool padActivity = move.sqrMagnitude > 0.001f || look.sqrMagnitude > 0.035f
                || pad.rightTrigger.isPressed || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed
                || pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed
                || pad.buttonNorth.isPressed || pad.dpad.ReadValue().sqrMagnitude > 0.01f;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool pointerActivity = mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f
                || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);
            bool keyActivity = keyboard != null && keyboard.anyKey.wasPressedThisFrame;
            if (pointerActivity || keyActivity) _usingGamepad = false;
            else if (padActivity) _usingGamepad = true;
            GamepadLastUsed = _usingGamepad;
            if (!_usingGamepad) return;

            // Ни старое положение мыши, ни её незавершённый клик не могут
            // подменить прицел или атаку контроллера на следующем тике.
            _pointerPressLatched = false;
            FixVec2 origin = Sim != null ? Sim.Entities.Position[Simulation.PlayerId] : FixVec2.Zero;
            // При выборе цели левый стик не должен уводить прицел вслед за
            // движением: до нового отклонения правого сохраняем направление.
            Vector2 axis = look.sqrMagnitude >= 0.09f ? look
                : _targetAimSlot >= 0 ? Vector2.zero : move;
            if (axis.sqrMagnitude >= 0.035f)
            {
                FixVec2 direction = CameraMovement(axis.x, axis.y,
                    _camera != null ? _camera.transform.rotation : Quaternion.identity);
                if (direction.LengthSq > Fix64.Zero) _gamepadAimDirection = direction.Normalized();
            }
            else if (Sim != null && _gamepadAimDirection.LengthSq == Fix64.Zero)
                _gamepadAimDirection = Sim.Entities.Facing[Simulation.PlayerId];
            _pending.Aim = origin + _gamepadAimDirection * Fix64.FromInt(8);
            _pending.Flags = pad.rightTrigger.isPressed && !choosing ? (byte)InputFlags.Attack : (byte)0;
            _pending.AttackTarget = -1;
            HoveredEntity = -1;
            AttackHeld = pad.rightTrigger.isPressed && !choosing;
            MoveOrderHeld = move.sqrMagnitude > 0.001f;
            MoveOrderPressedThisFrame = false;
            if (pad.rightTrigger.wasPressedThisFrame && !choosing)
            {
                _pointerPressFrame = _pending;
                _pointerPressLatched = true;
            }

            System.Array.Clear(_gamepadSlots, 0, _gamepadSlots.Length);
            if (choosing)
            {
                _gamepadSlots[0] = pad.dpad.left.wasPressedThisFrame;
                _gamepadSlots[1] = pad.dpad.up.wasPressedThisFrame;
                _gamepadSlots[2] = pad.dpad.right.wasPressedThisFrame;
                _gamepadSlots[3] = pad.dpad.down.wasPressedThisFrame;
            }
            else
            {
                _gamepadSlots[0] = pad.leftShoulder.wasPressedThisFrame;
                _gamepadSlots[1] = pad.rightShoulder.wasPressedThisFrame;
                _gamepadSlots[2] = pad.buttonWest.wasPressedThisFrame;
                _gamepadSlots[3] = pad.buttonNorth.wasPressedThisFrame;
                _gamepadSlots[4] = pad.buttonEast.wasPressedThisFrame && _targetAimSlot < 0;
                if (pad.leftShoulder.isPressed) _pending.AbilityHoldMask |= 1;
                if (pad.rightShoulder.isPressed) _pending.AbilityHoldMask |= 2;
                if (pad.buttonWest.isPressed) _pending.AbilityHoldMask |= 4;
                if (pad.buttonNorth.isPressed) _pending.AbilityHoldMask |= 8;
                if (pad.buttonEast.isPressed && _targetAimSlot < 0) _pending.AbilityHoldMask |= 16;
            }
            LatchSlots(_gamepadSlots, choosing);

            if (_targetAimSlot >= 0 && !choosing)
            {
                bool valid = Sim != null && Sim.Entities.Alive[Simulation.PlayerId]
                    && TargetedSlot(_targetAimSlot);
                if (valid && !GroundTargetedSlot(_targetAimSlot))
                    HoveredEntity = GamepadTarget(_targetAimSlot, origin, _gamepadAimDirection);
                if (pad.buttonEast.wasPressedThisFrame) ResolveTargetAim(false, true);
                else if (pad.rightTrigger.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame)
                    ResolveTargetAim(true, !valid);
                _pending.Flags = 0;
                AttackHeld = false;
                _pointerPressLatched = false;
            }
            if (Session.Mode == GameMode.Summary)
                LatchKeys(false, false, pad.buttonSouth.wasPressedThisFrame,
                    pad.buttonEast.wasPressedThisFrame, false, false);
#endif
        }

        private int GamepadTarget(int slot, FixVec2 origin, FixVec2 direction)
        {
            if (Sim == null || direction.LengthSq == Fix64.Zero) return -1;
            int best = -1;
            Fix64 bestDistance = Fix64.MaxValue;
            for (int i = 1; i < Sim.Entities.Count; i++)
            {
                if (!Sim.ValidAbilityTarget(i, Sim.GetAbility(slot))) continue;
                FixVec2 delta = Sim.Entities.Position[i] - origin;
                if (delta.LengthSq == Fix64.Zero ||
                    FixVec2.Dot(direction, delta.Normalized()) < Fix64.Ratio(1, 2)) continue;
                if (delta.LengthSq >= bestDistance) continue;
                bestDistance = delta.LengthSq;
                best = i;
            }
            return best;
        }
    }
}
