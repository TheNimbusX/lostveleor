using UnityEngine;

namespace Game.View
{
    public sealed partial class LayoutView
    {
        private Texture2D _campSurfaceMap;
        private Color32[] _campSurfacePixels;

        private Material CreateLocationGround(Color tint, Texture2D grass, Texture2D dirt, float tiling, float earth)
        {
            if (_style.CampSurfaceMaterial == null)
                return ViewMaterials.CreateMeadowGround(tint, grass, dirt, tiling, earth);
            // Общий ассет лагеря остаётся неизменным: у каждой карты собственный экземпляр.
            var material = new Material(_style.CampSurfaceMaterial);
            material.SetFloat("_IsSurface", 1);
            material.SetFloat("_IsPath", 0);
            material.SetFloat("_IsRiverBank", 0);
            return material;
        }

        private void ApplyCampSurface()
        {
            if (_style.CampSurfaceMaterial == null || _trailMask == null) return;
            if (_campSurfaceMap == null)
            {
                _campSurfaceMap = new Texture2D(TrailResolution, TrailResolution, TextureFormat.RGBA32, false, true)
                { name = "Лагерная земля: маска разлома", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _campSurfacePixels = new Color32[TrailResolution * TrailResolution];
            }
            for (int y = 0; y < TrailResolution; y++)
                for (int x = 0; x < TrailResolution; x++)
                {
                    int i = y * TrailResolution + x;
                    float px = _trailBounds.x + (x + .5f) / TrailResolution * _trailBounds.z;
                    float pz = _trailBounds.y + (y + .5f) / TrailResolution * _trailBounds.w;
                    float stones = Mathf.Lerp(.64f, 1, Mathf.SmoothStep(0, 1,
                        Mathf.InverseLerp(.3f, .7f, Mathf.PerlinNoise(px * .32f + 5, pz * .32f + 13))));
                    float turf = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .72f,
                        Mathf.PerlinNoise(px * .43f + 8, pz * .43f + 17) * .65f
                        + Mathf.PerlinNoise(px * 1.3f + 31, pz * 1.3f + 2) * .35f));
                    byte path = (byte)Mathf.Max(_trailPixels[i], _wearPixels[i] * .62f);
                    // Нулевой край не растягивает дорогу по всему фону при Clamp.
                    if (x == 0 || y == 0 || x == TrailResolution - 1 || y == TrailResolution - 1) path = 0;
                    _campSurfacePixels[i] = new Color32(path, (byte)(stones * 255), 255, (byte)(turf * 255));
                }
            _campSurfaceMap.SetPixels32(_campSurfacePixels); _campSurfaceMap.Apply(false, false);
            BindCampSurface(_roomMaterial); BindCampSurface(_entranceMaterial); BindCampSurface(_exitMaterial);
            if (_banks != null) BindCampSurface(_banks.GetComponent<MeshRenderer>().sharedMaterial);
            if (_groundFill != null) BindCampSurface(_groundFill.GetComponent<MeshRenderer>().sharedMaterial);
        }

        private void BindCampSurface(Material material)
        {
            material.SetTexture("_SurfaceMap", _campSurfaceMap);
            material.SetVector("_SurfaceBounds", _trailBounds);
        }
    }
}
