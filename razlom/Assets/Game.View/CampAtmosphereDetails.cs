using UnityEngine;

namespace Game.View
{
    /// <summary>Движение и мерцание объектов атмосферы, размещённых в SampleScene.</summary>
    [DisallowMultipleComponent]
    public sealed class CampAtmosphereDetails : MonoBehaviour
    {
        [Header("Мост — объекты находятся в сцене")]
        public ParticleSystem RiverMistLeft;
        public ParticleSystem RiverMistRight;

        [Header("Алхимик — объекты находятся в сцене")]
        public ParticleSystem AlchemyVapor;
        public Light AlchemyLight;
        [Min(0f)] public float AlchemyLightIntensity = 1.95f;
        [Range(0f, 1f)] public float AlchemyFlicker = .2f;
        [Min(0.5f)] public float VaporInterval = 5.2f;
        [Range(1, 20)] public int VaporBurst = 8;

        float _nextVapor;

        void OnEnable()
        {
            if (Application.isPlaying) _nextVapor = Time.time + 1.6f;
        }

        void Update()
        {
            if (AlchemyLight != null)
                AlchemyLight.intensity = AlchemyLightIntensity + AlchemyFlicker * Mathf.Sin(Time.time * 1.7f);
            if (AlchemyVapor == null || Time.time < _nextVapor) return;
            AlchemyVapor.Emit(VaporBurst);
            _nextVapor = Time.time + VaporInterval + .9f * Mathf.Sin(Time.time * .43f);
        }
    }
}
