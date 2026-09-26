using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    /// <summary>
    /// Лента когтей вендиго по референсу claw-sync-r03: три ВЛОЖЕННЫХ тонких
    /// серпа (когти сидят на разных радиусах от плеча), кремовое ядро и янтарные
    /// кромки, хвосты сужаются и растворяются по возрасту семплов. Каждый кадр
    /// маха сюда приходит точка головы серпа и ось «поперёк» (радиально наружу от
    /// зверя); лента строится по истории точек, как лента сабли героя.
    /// Материал — плёнка меча CFXR (ForestWendigoVfxSetup, M_Wendigo_ClawRibbon).
    /// Спиральные дуги CFXR (V1–V4) и лента по кости кисти (V5–V6: наша
    /// анимация бьёт сверху вниз, выходил вертикальный столб) не подошли.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WendigoClawRibbon : MonoBehaviour
    {
        private const int Samples = 28;
        private const int Claws = 3;
        private const int Across = 4;
        /// <summary>Сколько живёт семпл, с: хвост стирается за это время.</summary>
        private const float Fade = .34f;
        /// <summary>Смещение каждого серпа поперёк (наружу +), его ширина и доля жизни хвоста.</summary>
        private static readonly float[] ClawOffset = { .50f, 0f, -.48f };
        private static readonly float[] ClawWidth = { .40f, .34f, .28f };
        private static readonly float[] ClawLife = { 1f, .85f, .7f };
        private static readonly float[] AcrossV = { 0f, .3f, .7f, 1f };
        private static readonly float[] AcrossT = { -.5f, -.2f, .2f, .5f };

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private readonly Vector3[] _head = new Vector3[Samples], _across = new Vector3[Samples];
        private readonly float[] _time = new float[Samples];
        private int _count;
        private Vector3[] _vertices;
        private Color[] _colors;
        private Vector2[] _uvs;
        private int[] _triangles;
        private static readonly Color Ivory = new Color(2.7f, 2.45f, 1.9f, 1f);
        private static readonly Color Amber = new Color(1.6f, 1.0f, .45f, 1f);

        public bool Active { get; private set; }

        private void Awake()
        {
            _mesh = new Mesh { name = "Wendigo claw ribbon" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            var pack = Resources.Load<Material>("VFX/Wendigo/Materials/M_Wendigo_ClawRibbon");
            Material material;
            if (pack != null) material = new Material(pack) { name = "Wendigo claw ribbon" };
            else
            {
                Shader shader = Shader.Find("Razlom/SwordTrail");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                material = new Material(shader) { name = "Wendigo claw ribbon" };
                if (material.HasProperty("_Glow")) material.SetFloat("_Glow", 1.7f);
                if (material.HasProperty("_Hard")) material.SetFloat("_Hard", 1f);
                if (material.HasProperty("_Brush")) material.SetFloat("_Brush", .3f);
            }
            material.renderQueue = 3100;
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            int verts = Claws * Samples * Across;
            _vertices = new Vector3[verts]; _colors = new Color[verts]; _uvs = new Vector2[verts];
            _triangles = new int[Claws * (Samples - 1) * (Across - 1) * 6];
            int t = 0;
            for (int c = 0; c < Claws; c++)
                for (int i = 0; i < Samples - 1; i++)
                    for (int k = 0; k < Across - 1; k++)
                    {
                        int v = (c * Samples + i) * Across + k, w = v + Across;
                        _triangles[t++] = v; _triangles[t++] = w; _triangles[t++] = v + 1;
                        _triangles[t++] = v + 1; _triangles[t++] = w; _triangles[t++] = w + 1;
                    }
            _renderer.enabled = false;
        }

        public void Begin()
        {
            _count = 0; Active = true; _renderer.enabled = false;
        }

        /// <summary>Точка головы серпа и единичная ось поперёк (наружу от зверя) — один семпл маха.</summary>
        public void Sample(Vector3 head, Vector3 across, float time)
        {
            if (!Active) return;
            if (_count == Samples)
            {
                for (int i = 1; i < Samples; i++) { _head[i - 1] = _head[i]; _across[i - 1] = _across[i]; _time[i - 1] = _time[i]; }
                _count--;
            }
            _head[_count] = head;
            _across[_count] = across;
            _time[_count] = time;
            _count++;
        }

        /// <summary>Перестраивает ленту по возрасту семплов; возвращает false, когда всё растворилось.</summary>
        public bool Rebuild(float now)
        {
            if (!Active) return false;
            if (_count < 2) { _renderer.enabled = false; return true; }
            bool alive = false;
            for (int c = 0; c < Claws; c++)
            {
                float fade = Fade * ClawLife[c];
                for (int i = 0; i < Samples; i++)
                {
                    int s = Mathf.Min(i, _count - 1);
                    float age = now - _time[s];
                    float alpha = Mathf.Clamp01(1f - age / fade);
                    if (alpha > 0f && i < _count) alive = true;
                    // Хвост сужается: у головы полная ширина, к растворению — треть.
                    float width = ClawWidth[c] * (.35f + .65f * alpha);
                    Vector3 middle = _head[s] + _across[s] * ClawOffset[c];
                    // u: 0 у хвоста (старые семплы), 1 у головы.
                    float u = _count > 1 ? s / (float)(_count - 1) : 1f;
                    float a = alpha * (i < _count ? 1f : 0f);
                    for (int k = 0; k < Across; k++)
                    {
                        int v = (c * Samples + i) * Across + k;
                        _vertices[v] = middle + _across[s] * (AcrossT[k] * width);
                        _uvs[v] = new Vector2(u, AcrossV[k]);
                        var color = k == 0 || k == Across - 1 ? Amber : Ivory;
                        color.a = a * (k == 0 || k == Across - 1 ? .8f : 1f);
                        _colors[v] = color;
                    }
                }
            }
            _mesh.Clear();
            _mesh.vertices = _vertices; _mesh.colors = _colors; _mesh.uv = _uvs; _mesh.triangles = _triangles;
            _mesh.RecalculateBounds();
            _renderer.enabled = alive;
            if (!alive) Active = false;
            return alive;
        }

        public void End() { Active = false; _renderer.enabled = false; _count = 0; }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_renderer != null && _renderer.sharedMaterial != null) Destroy(_renderer.sharedMaterial);
        }
    }
}
