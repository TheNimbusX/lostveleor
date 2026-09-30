using UnityEngine;

namespace Game.View
{
    /// <summary>Ходьбу ограничивает основание, а не листва, ткань или видимый меш.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Препятствие для ходьбы")]
    public sealed class CampNavigationObstacle : MonoBehaviour
    {
        public CampObstacleRole Role = CampObstacleRole.Solid;
        [Tooltip("Размер основания по видимой модели. Для замены модели без изменения проходов выключить: Center/Size/Radius задают постоянный объём на стабильном корне.")]
        public bool FitVisualFootprint = false;
        [Tooltip("Локальная точка простого препятствия; позиция самой модели не меняется.")]
        public Vector3 Center = new Vector3(0, 1, 0);
        public Vector3 Size = new Vector3(1, 2, 1);
        [Min(.05f)] public float Radius = .25f;
        [Range(.25f, 1f)] public float FootprintScale = .9f;
    }
}
