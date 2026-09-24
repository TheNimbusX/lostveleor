using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Значок «здесь есть дело» над жителем лагеря на паке (владелец 24 сентября: «у Лео есть
    /// заказ, у кузнеца можно перековать»): префаб Resources/UI/Prefabs/CampGuideWc. Только ссылки
    /// и вид — где показывать, решает CampGuideView. Свой файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class CampGuidePanel : MonoBehaviour
    {
        [Tooltip("Шаблон значка над целью; сам остаётся выключенным")] public RectTransform MarkerTemplate;
        [Tooltip("Кузнец, торговец, алхимик")] public Texture[] Icons = new Texture[3];
    }
}
