using UnityEngine;

namespace Game.View
{
    [CreateAssetMenu(menuName = "Разлом/Лагерь/Глубина земли", fileName = "CampDepthPalette")]
    public sealed class CampDepthPalette : ScriptableObject
    {
        [Header("Земля — спокойные крупные пятна")]
        [Range(1f, 2f)] public float PathTextureScale = 1.38f;
        [Range(0f, 2f)] public float TextureSoftness = .75f;
        [Range(0f, .5f)] public float ContactStrength = .12f;
        [Range(0f, 1f)] public float DryStrength = .32f;
        [Range(0f, 1f)] public float WetStrength = .46f;
        [Range(0f, 1f)] public float AshStrength = .36f;
        [Min(.5f)] public float WetBandMeters = 3.1f;
        public Color DryTint = new Color(1.04f, .94f, .80f, 1f);
        public Color WetTint = new Color(.67f, .79f, .72f, 1f);
        public Color AshTint = new Color(.66f, .64f, .58f, 1f);
        [Header("Группы растительности и дальний лес")]
        [Range(0f, 1f)] public float FoliageGrouping = .65f;
        [Range(0f, 1f)] public float ForestTintStrength = .38f;
        public Color ForestTint = new Color(.79f, .93f, 1.02f, 1f);
        public Color WarmCentreTint = new Color(1.03f, 1.005f, .95f, 1f);
        [Header("Вода — тихие разрывы береговой ряби")]
        [Range(0f, 1f)] public float ShoreBreakup = .8f;
        [Range(0f, 1f)] public float ShoreFoamStrength = .64f;
        int _revision;
        public int Revision => _revision;
        void OnValidate() { _revision++; }
    }
}
