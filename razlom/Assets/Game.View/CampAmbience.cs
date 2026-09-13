using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>Тихое движение лагеря поверх авторского света, только в представлении.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Разлом/Лагерь/Ветер и огненный свет")]
    public sealed class CampAmbience : MonoBehaviour
    {
        [Tooltip("Предпросмотр в Scene: ветер и свет видны без запуска игры.")]
        public bool PreviewInScene = true;
        [Header("Лёгкий ветер")]
        [Tooltip("Множитель качания листьев и цветов. Ноль — штиль.")]
        [Range(0f, 2f)] public float BreezeStrength = .8f;
        [Range(0f, 360f)] public float BreezeDirection = 38f;
        [Range(0f, 2f)] public float FlagStrength = 1f;
        [Header("Мерцание вокруг исходной яркости")]
        [Range(0f, .65f)] public float FireVariation = .42f;
        [Range(0f, .55f)] public float LampVariation = .32f;

        static readonly int BreezeId = Shader.PropertyToID("_CampBreeze");
        static readonly int PreviousTimeId = Shader.PropertyToID("_CampBreezePreviousTime");
        struct LightState
        {
            public Light Source;
            public float Intensity, Phase, Minimum, Maximum;
            public bool Fire;
        }
        LightState[] _lights;
        float _previousTime;
        bool _capture;
        bool _captureStill;

        void OnEnable()
        {
            var fire = transform.Find("Campfire");
            CampFlameProView.Install(fire);
            var lights = new List<LightState>();
            foreach (var source in GetComponentsInChildren<Light>(true))
            {
                if(source.GetComponentInParent<CampMagicCircle>()!=null)continue;
                if ((source.type != LightType.Point && source.type != LightType.Spot) ||
                    source.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) continue;
                bool isFire = fire != null && source.transform.IsChildOf(fire);
                Vector3 p = source.transform.position;
                lights.Add(new LightState
                {
                    Source = source, Intensity = source.intensity, Fire = isFire,
                    // Три источника одного костра дышат вместе; отдельные лампы не синхронны.
                    Phase = isFire ? 3.71f : Mathf.Repeat(p.x * 2.173f + p.z * 3.719f + 19f, 71f),
                    Minimum = source.intensity, Maximum = source.intensity
                });
            }
            _lights = lights.ToArray();
            _previousTime = Time.time;
            _capture = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp") >= 0;
            _captureStill = _capture && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capture-camp-ambience-still") >= 0;
            if (_capture) Debug.Log($"[camp-ambience] lights={_lights.Length} breeze={BreezeStrength:F2} fire={FireVariation:F3} lamps={LampVariation:F3}");
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()=>ApplyAt(Time.time);
        public void PreviewAt(float time)
        {
            if(Application.isPlaying)return;
            if(_lights==null)OnEnable();
            _captureStill=false;ApplyAt(time);
        }
        void ApplyAt(float now)
        {
            float direction = BreezeDirection * Mathf.Deg2Rad;
            Shader.SetGlobalVector(BreezeId, new Vector4(Mathf.Cos(direction), Mathf.Sin(direction), _captureStill ? 0 : BreezeStrength, now));
            Shader.SetGlobalFloat(PreviousTimeId, _previousTime);
            Shader.SetGlobalFloat("_CampFlagStrength",FlagStrength);
            _previousTime = now;
            if (_lights == null) return;
            for (int i = 0; i < _lights.Length; i++)
            {
                ref var state = ref _lights[i];
                if (state.Source == null || !state.Source.isActiveAndEnabled) continue;
                float speed = state.Fire ? 1f : .66f;
                float t = now * speed;
                // Непериодические плавные огибающие не дают резких вспышек и одинакового пульса.
                float breathing = (Mathf.PerlinNoise(t * .91f, state.Phase) - .5f) * 2f;
                float flicker = (Mathf.PerlinNoise(t * 3.1f + 37f, state.Phase + 13f) - .5f) * 2f;
                float pulse = Mathf.Sin(t * 3.3f + state.Phase) * .58f + Mathf.Sin(t * 5.71f + state.Phase * 1.7f) * .22f;
                float modulation = Mathf.Clamp(pulse + breathing * .4f + flicker * .18f, -1f, 1f);
                float amount = _captureStill ? 0 : state.Fire ? FireVariation : LampVariation;
                state.Source.intensity = state.Intensity * (1f + modulation * amount);
                state.Minimum = Mathf.Min(state.Minimum, state.Source.intensity);
                state.Maximum = Mathf.Max(state.Maximum, state.Source.intensity);
            }
        }

        void OnDisable()=>StopPreview();
        public void StopPreview()
        {
            Shader.SetGlobalVector(BreezeId, Vector4.zero);
            if (_lights == null) return;
            foreach (var state in _lights)
            {
                if (state.Source == null) continue;
                if (_capture) Debug.Log($"[camp-ambience] {state.Source.name} base={state.Intensity:F3} observed={state.Minimum:F3}..{state.Maximum:F3} restored=True");
                // Выход из лагеря или выключение компонента возвращает ровно авторскую яркость.
                state.Source.intensity = state.Intensity;
            }
            _lights = null;
        }
    }
}
