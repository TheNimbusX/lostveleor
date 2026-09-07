using UnityEngine;
using Game.Data;

namespace Game.View
{
    [CreateAssetMenu(fileName = "LocationTheme", menuName = "Разлом/Локации/Оформление локации")]
    public sealed class LocationTheme : ScriptableObject
    {
        public LocationProfileAsset Gameplay;
        public LayoutStyle Style = new LayoutStyle();
    }
}
