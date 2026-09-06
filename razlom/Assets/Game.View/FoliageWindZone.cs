using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Один ветер на всю сцену.
    ///
    /// Шейдер <c>Razlom/Foliage Wind</c> читает глобальную
    /// <c>_RazlomWindDirection</c>: xyz — направление в мире, w — сила. Компонент
    /// её и ставит. Смысл в том, чтобы направление ветра было ОДНО: дерево,
    /// куст и трава, качающиеся в разные стороны, читаются не как погода, а как
    /// три несвязанных эффекта.
    ///
    /// Компонент — чистая презентация: он не трогает <c>Game.Sim</c>, не входит в
    /// хеш забега и на детерминизм повлиять не может в принципе.
    ///
    /// ЗОНА НЕ ОБЯЗАТЕЛЬНА. Без неё шейдер сам дует слабым ветром по умолчанию —
    /// иначе материал, брошенный на дерево, выглядел бы сломанным до тех пор,
    /// пока художник не догадается поставить ещё один компонент. Зона нужна,
    /// когда ветер надо направить, усилить или выключить.
    ///
    /// В Scene View время шейдера идёт только при включённых Animated Materials
    /// (значок в панели вида). Если в сцене всё стоит, а в Play качается —
    /// дело в этой галке, а не в шейдере.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Зона ветра для листвы")]
    public sealed class FoliageWindZone : MonoBehaviour
    {
        private static readonly int WindDirectionId =
            Shader.PropertyToID("_RazlomWindDirection");

        [Tooltip("Куда дует, градусы вокруг оси Y. 0 — вдоль +X мира.")]
        [SerializeField, Range(0f, 360f)] private float _directionDegrees = 45f;

        [Tooltip("Множитель амплитуды поверх настроек материала. 0 — штиль.")]
        [SerializeField, Range(0f, 3f)] private float _strength = 1f;

        /// <summary>Сила ветра. Меняется из кода — например на время боя с боссом.</summary>
        public float Strength
        {
            get => _strength;
            set { _strength = Mathf.Max(0f, value); Apply(); }
        }

        private void OnEnable() => Apply();

        private void OnDisable()
        {
            // ВЫКЛЮЧЕНИЕ ПИШЕТ ВАЛИДНОЕ НАПРАВЛЕНИЕ С НУЛЕВОЙ СИЛОЙ, А НЕ НОЛЬ.
            //
            // Нулевой вектор для шейдера означает «зоны в сцене нет вообще», и
            // он включает ветер по умолчанию. Обнули мы здесь всё — и снятая
            // галка компонента приводила бы к тому, что ветер УСИЛИВАЕТСЯ.
            Shader.SetGlobalVector(WindDirectionId, new Vector4(0.7071f, 0f, 0.7071f, 0f));
        }

        // В редакторе Update зовётся на перерисовке сцены — этого хватает,
        // чтобы крутить направление ручкой и сразу видеть результат.
        private void Update() => Apply();

        private void Apply()
        {
            float radians = _directionDegrees * Mathf.Deg2Rad;
            Shader.SetGlobalVector(WindDirectionId, new Vector4(
                Mathf.Cos(radians), 0f, Mathf.Sin(radians), Mathf.Max(0f, _strength)));
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            float radians = _directionDegrees * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
            Vector3 origin = transform.position;
            Vector3 tip = origin + direction * (2f + _strength);

            Gizmos.color = new Color(0.55f, 0.86f, 0.62f, 1f);
            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawLine(tip, tip + Quaternion.Euler(0f, 155f, 0f) * direction * 0.6f);
            Gizmos.DrawLine(tip, tip + Quaternion.Euler(0f, -155f, 0f) * direction * 0.6f);
        }
#endif
    }
}
