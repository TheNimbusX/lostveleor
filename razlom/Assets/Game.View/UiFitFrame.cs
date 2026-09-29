using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Раскладка окна, нарисованная на 1920×1080 (окна лагерных NPC): целиком помещается в холст при
    /// любом масштабе интерфейса и соотношении сторон. Опора — низ по середине: портрет NPC стоит на
    /// нижнем крае экрана. Больше эталона не растёт (80% — окно меньше, как весь интерфейс), меньше —
    /// ужимается целиком (120%, 16:10): до 29.09 правая колонка кузнеца и торговца уходила за край.
    /// Масштаб считает <see cref="CampShopDeals.FitScale"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiFitFrame : MonoBehaviour
    {
        [Tooltip("Размер, на который нарисована раскладка, в единицах холста")] public Vector2 Design = new Vector2(1920f, 1080f);

        Vector2 _seen = new Vector2(-1f, -1f);

        void OnEnable()
        {
            _seen = new Vector2(-1f, -1f);
            Fit();
        }

        // Размер холста меняют масштаб интерфейса и окно игры; сравнение дешёвое, окно открыто недолго.
        void LateUpdate() => Fit();

        void Fit()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            Vector2 size = parent.rect.size;
            if (size == _seen) return;
            _seen = size;
            float scale = CampShopDeals.FitScale(size.x, size.y, Design.x, Design.y);
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
