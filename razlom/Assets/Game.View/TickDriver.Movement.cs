using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        private void CaptureDirectionalMovement(ref InputFrame frame)
        {
            if (CaptureRig.Installed || CampIntegrationCapture.IsRunning ||
                (!_usingGamepad && !GameUserSettings.WasdMovement)) return;
            float x, y;
            if (_usingGamepad)
            {
                Vector2 stick = GamepadMovement();
                x = stick.x; y = stick.y;
            }
            else
            {
                x = (GameKeyBindings.HeldKey(KeyCode.D) ? 1f : 0f) - (GameKeyBindings.HeldKey(KeyCode.A) ? 1f : 0f);
                y = (GameKeyBindings.HeldKey(KeyCode.W) ? 1f : 0f) - (GameKeyBindings.HeldKey(KeyCode.S) ? 1f : 0f);
            }
            if(CampServicesProbe.IsRunning)x=y=0;
            frame.MoveDirection = CameraMovement(x,y,_camera!=null?_camera.transform.rotation:Quaternion.identity);
            frame.Flags = (byte)((frame.Flags & (byte)InputFlags.Attack) | (byte)InputFlags.DirectMovement);
            frame.AttackTarget = -1;
            MoveOrderHeld = frame.MoveDirection.LengthSq > Fix64.Zero;
        }
        public static FixVec2 CameraMovement(float x,float y,Quaternion cameraRotation)
        {
            Vector3 right = cameraRotation * Vector3.right;
            Vector3 forward = cameraRotation * Vector3.forward;
            right.y = forward.y = 0f;
            Vector3 direction = Vector3.ClampMagnitude(right.normalized * x + forward.normalized * y, 1f);
            return new FixVec2(QuantizePosition(direction.x), QuantizePosition(direction.z));
        }
    }
}
