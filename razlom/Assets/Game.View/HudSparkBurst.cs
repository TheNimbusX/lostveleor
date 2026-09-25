using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Веер искр из точки (баннер нового уровня). Искры — готовые Image в префабе, без
    /// системы частиц: их на Overlay-холсте нет. Направления и скорости заданы номером
    /// искры, поэтому кадр на любой момент одинаков — редактор снимает раскадровку через
    /// <see cref="Apply"/>. Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudSparkBurst : MonoBehaviour
    {
        public Image[] Sparks = new Image[0];
        [Tooltip("Секунд живёт искра")] public float Life = .9f;
        [Tooltip("Как далеко улетает самая быстрая, единицы Canvas")] public float Distance = 170f;
        [Tooltip("Размер искры в начале, единицы Canvas")] public float Size = 26f;
        [Tooltip("Больше по горизонтали, чем по вертикали: баннер широкий")] public float Stretch = 1.6f;
        [Tooltip("Кадр ставит хозяин (баннер уровня) через Apply — свои часы не идут")] public bool Driven;

        float _start = -100f;

        public void Play() => _start = UiMotion.Now;

        void LateUpdate()
        {
            if (!Driven) Apply(UiMotion.Now - _start);
        }

        /// <summary>Искры на момент <paramref name="t"/> секунд после вспышки.</summary>
        public void Apply(float t)
        {
            float k = t / Mathf.Max(.05f, Life);
            for (int i = 0; i < Sparks.Length; i++)
            {
                Image spark = Sparks[i];
                if (spark == null) continue;
                // У каждой искры свои направление, скорость и задержка — из номера.
                float seed = Mathf.Repeat(i * .6180339f, 1f);
                float angle = (i / (float)Sparks.Length + seed * .08f) * Mathf.PI * 2f;
                float speed = .55f + .45f * Mathf.Repeat(i * .3819660f + .21f, 1f);
                float own = k - seed * .12f;
                bool on = own > 0f && own < 1f;
                spark.enabled = on;
                if (!on) continue;
                float travel = 1f - (1f - own) * (1f - own) * (1f - own);
                var dir = new Vector2(Mathf.Cos(angle) * Stretch, Mathf.Sin(angle));
                spark.rectTransform.anchoredPosition = dir * (Distance * speed * travel);
                float size = Size * (.6f + .4f * speed) * (1f - own * .6f);
                spark.rectTransform.sizeDelta = new Vector2(size, size);
                spark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, own * 90f * (i % 2 == 0 ? 1f : -1f));
                HudFx.SetAlpha(spark, Mathf.Pow(1f - own, 1.4f));
            }
        }
    }
}
