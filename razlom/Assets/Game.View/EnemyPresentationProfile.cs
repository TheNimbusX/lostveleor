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

        public EnemyDeathPresentation ForestBud = new EnemyDeathPresentation
        {
            ClipSeconds = 1.2f, StartNormalized = 0f, RestNormalized = 1f,
            StateSpeed = 1f, BlendSeconds = .09f, RestSeconds = .45f,
            DissolveSeconds = .45f, RecoilMeters = .035f, EdgeGlow = .03f,
            EdgeColor = new Color(.38f, .30f, .13f, 1f)
        };

        public EnemyDeathPresentation ForestStonehoof = new EnemyDeathPresentation {
            ClipSeconds = 2f, StartNormalized = 0, RestNormalized = 1, StateSpeed = 1,
            BlendSeconds = .09f, RestSeconds = .55f, DissolveSeconds = .55f,
            RecoilMeters = 0, EdgeGlow = .025f, EdgeColor = new Color(.36f,.3f,.19f,1)
        };
        private static EnemyPresentationProfile _current;
        public EnemyDeathPresentation ForestWendigo = new EnemyDeathPresentation {
            ClipSeconds = 4f, StartNormalized = 18f/96f, RestNormalized = 1f,
            StateSpeed = 1f, BlendSeconds = .07f, RestSeconds = .6f,
            DissolveSeconds = .65f, RecoilMeters = .02f, EdgeGlow = .03f
        };
        // Шипомёт (клип Death, 48 кадров): касание земли на 39-м — ThorncasterAnimatorView
        // играет кадры 0–39 за время падения этого профиля, 39–48 за стойку.
        public EnemyDeathPresentation ForestThorncaster = new EnemyDeathPresentation {
            ClipSeconds = 48f / 30f, StartNormalized = 0f, RestNormalized = 39f / 48f,
            StateSpeed = 1f, BlendSeconds = .07f, RestSeconds = .45f,
            DissolveSeconds = .5f, RecoilMeters = .04f, EdgeGlow = .03f,
            EdgeColor = new Color(.36f, .30f, .17f, 1f)
        };
        // Корнехват (Death, 45 кадров): брюхом в землю на 25-м. Тело под URP Lit без
        // растворения — к концу показа RootSnarerAnimatorView уводит его в землю.
        public EnemyDeathPresentation ForestRootSnarer = new EnemyDeathPresentation {
            ClipSeconds = 45f / 30f, StartNormalized = 0f, RestNormalized = 25f / 45f,
            StateSpeed = 1f, BlendSeconds = .08f, RestSeconds = .5f,
            DissolveSeconds = .4f, RecoilMeters = .04f, EdgeGlow = .03f,
            EdgeColor = new Color(.30f, .26f, .16f, 1f)
        };
        /// <summary>
        /// Смерть вида. Расщепень не падает, а раскалывается (SplitterCombatView): тело
        /// прячется через 0,2 с, профиль Хранителя лишь держит слот до конца распада;
        /// детёныш — по корнеползу.
        /// </summary>
        public static EnemyDeathPresentation Death(EnemyKind kind)
        {
            if (_current == null)
                _current = Resources.Load<EnemyPresentationProfile>("Combat/EnemyPresentation")
                    ?? CreateInstance<EnemyPresentationProfile>();
            switch (kind)
            {
                case EnemyKind.ForestStonehoof: return _current.ForestStonehoof;
                case EnemyKind.ForestWendigo: return _current.ForestWendigo;
                case EnemyKind.ForestBud: return _current.ForestBud;
                case EnemyKind.ForestThorncaster: return _current.ForestThorncaster;
                case EnemyKind.ForestRootSnarer: return _current.ForestRootSnarer;
                case EnemyKind.ForestRootSwarm:
                case EnemyKind.ForestSplitling: return _current.RootSwarm;
                default: return _current.Guardian;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _current = null;
    }
}
