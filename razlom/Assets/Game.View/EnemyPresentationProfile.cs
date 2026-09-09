using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    [Serializable]
    public sealed class EnemyDeathPresentation
    {
        [Min(0.01f)] public float ClipSeconds = 73f / 30f;
        [Range(0f, 1f)] public float StartNormalized = 24f / 73f;
        [Range(0f, 1f)] public float RestNormalized = 0.72f;
        [Min(0.01f)] public float StateSpeed = 1f;
        [Min(0f)] public float BlendSeconds = 0.09f;
        [Min(0f)] public float RestSeconds = 0.18f;
        [Min(0.01f)] public float DissolveSeconds = 0.38f;
        [Range(0f, 0.5f)] public float RecoilMeters = 0.12f;
        [Range(0f, 1f)] public float EdgeGlow = 0.10f;
        public Color EdgeColor = new Color(0.38f, 0.29f, 0.15f, 1f);

        public float FallSeconds => Mathf.Max(0.01f,
            (RestNormalized - StartNormalized) * ClipSeconds / Mathf.Max(0.01f, StateSpeed));
        public float DissolveAt => FallSeconds + RestSeconds;
        public float TotalSeconds => DissolveAt + DissolveSeconds;
    }

    [CreateAssetMenu(menuName = "Разлом/Профиль реакций врагов")]
    public sealed class EnemyPresentationProfile : ScriptableObject
    {
        public EnemyDeathPresentation Guardian = new EnemyDeathPresentation();
        public EnemyDeathPresentation RootSwarm = new EnemyDeathPresentation
        {
            ClipSeconds = 66f / 30f, StartNormalized = 0f,
            RestNormalized = 50f / 66f, StateSpeed = 2f,
            BlendSeconds = 0.055f, RestSeconds = 0.10f, DissolveSeconds = 0.28f,
            RecoilMeters = 0.07f, EdgeGlow = 0.06f,
            EdgeColor = new Color(0.27f, 0.32f, 0.13f, 1f)
        };

        private static EnemyPresentationProfile _current;
        public static EnemyDeathPresentation Death(EnemyKind kind)
        {
            if (_current == null)
                _current = Resources.Load<EnemyPresentationProfile>("Combat/EnemyPresentation")
                    ?? CreateInstance<EnemyPresentationProfile>();
            return kind == EnemyKind.ForestRootSwarm ? _current.RootSwarm : _current.Guardian;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _current = null;
    }
}
