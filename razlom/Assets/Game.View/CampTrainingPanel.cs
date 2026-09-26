using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Панель тренировки у манекенов (концепт training, владелец 23 сентября; с 26 сентября —
    /// материал «Дым и свет»): префаб Resources/UI/Prefabs/CampTrainingWc. Только ссылки и вид —
    /// числа, появление и подписи над манекенами ставит CampTrainingView. Дым и буквы карточки и
    /// подписей проявляют их UiInkGroup при каждом включении.
    /// Свой файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class CampTrainingPanel : MonoBehaviour
    {
        public CanvasGroup Group;
        [Tooltip("Карточка панели: по ней ловится мышь (прозрачный ловец на самой карточке)")] public RectTransform Card;
        [Tooltip("Урон, DPS, последний, попадания, криты, огонь")] public TMP_Text[] Values = new TMP_Text[6];
        public Button Reset;
        [Tooltip("Подписи «Манекен» над полосками; встают над манекенами рядом с героем")] public RectTransform[] Names = new RectTransform[0];
        [Tooltip("Секунд на появление и исчезание")] public float Fade = .15f;
    }
}
