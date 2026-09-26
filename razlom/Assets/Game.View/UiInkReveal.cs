using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Данные элемента для шейдера «Razlom/UI Ink» (материал «Дым и свет», владелец 25 сентября):
    /// насколько элемент ещё скрыт, зерно (сдвиг шума, чтобы соседние клубы текли по-разному)
    /// и точка, откуда растекаются чернила. Пишет их в uv1 каждой вершины, а в uv2 — место
    /// вершины внутри элемента 0..1 (по нему течёт шум и идёт фронт), силу кромки и её ширину.
    ///
    /// Сам ничего не анимирует: <see cref="Hidden"/> двигает <see cref="UiInkGroup"/>.
    /// Холсту нужны каналы TexCoord1 и TexCoord2 — их включает UiInkGroup.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class UiInkReveal : BaseMeshEffect
    {
        [SerializeField, Range(0f, 1f), Tooltip("1 — не видно, 0 — видно целиком")] float _hidden;
        [Tooltip("Сдвиг шума: у соседних клубов дым течёт по-разному")] public float Seed;
        [Tooltip("Откуда растекаются чернила, доли элемента (0,0 — левый низ)")] public Vector2 Origin = new Vector2(.5f, .5f);
        [Tooltip("Задержка в группе, с — поверх её лесенки (текст после дыма, свет после текста)")] public float Delay;
        [Tooltip("Сила тлеющей кромки при проявлении: 1 — полная, 0 — чернила проявляются без огня (подсказки)")]
        [Range(0f, 1f)] public float Burn = 1f;
        [Tooltip("Ширина тлеющего фронта, доля от ширины материала: больше 1 — кромка шире, тусклее и гаснет дольше " +
                 "(итоги забега: медленное тление вместо быстрой красной вспышки). Обычно ставит UiInkGroup")]
        [Range(.5f, 3f)] public float EdgeScale = 1f;

        public float Hidden
        {
            get => _hidden;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, _hidden)) return;
                _hidden = value;
                if (graphic != null) graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            Rect rect = graphic.rectTransform.rect;
            float w = Mathf.Max(rect.width, .0001f), h = Mathf.Max(rect.height, .0001f);
            var data = new Vector4(_hidden, Seed, Origin.x, Origin.y);
            // Ширина кромки — в uv2.w; 0 шейдер читает как 1 (старые сетки без этого поля).
            float edge = Mathf.Clamp(EdgeScale, .5f, 3f);
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.uv1 = data;
                vertex.uv2 = new Vector4((vertex.position.x - rect.xMin) / w, (vertex.position.y - rect.yMin) / h, Burn, edge);
                vh.SetUIVertex(vertex, i);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (graphic != null) graphic.SetVerticesDirty();
        }
#endif
    }
}
