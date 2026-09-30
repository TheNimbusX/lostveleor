using UnityEngine;

namespace Game.View
{
    // Камера лагеря высоко над сценой: расстояние звука считаем от стоп героя.
    public static class CampAudioSpatial
    {
        public static float Attenuation(Vector3 listener, Vector3 anchor, float near, float far)
        {
            var delta = anchor - listener; delta.y = 0;
            return 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near, Mathf.Max(near + .1f, far), delta.magnitude));
        }

        public static float Pan(Vector3 listener, Vector3 anchor, Camera camera)
        {
            var delta = anchor - listener; delta.y = 0;
            return camera == null ? 0 : Mathf.Clamp(Vector3.Dot(delta, camera.transform.right) / 12, -.58f, .58f);
        }

        public static bool PanelOpen => CampPlayerView.Instance?.InventoryOpen == true
            || CampPlayerView.Instance?.EntranceOpen == true || CampServicesView.Instance?.IsOpen == true
            || CampPreparationView.Instance?.IsOpen == true || CampForgeView.Instance?.IsOpen == true;
    }
}
