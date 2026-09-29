using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Дорога карты тушью (<see cref="SmokeRouteMap"/>): мазок кистью вдоль S-кривой между двумя узлами.
    /// Лента из сегментов, вдоль неё тянется кусок мазка из набора «Дым и свет» (brush_stroke_*): волокна
    /// кисти идут по дороге, а не штампуются.
    ///
    /// Кисть рисует дорогу по-настоящему: <see cref="Head"/> — докуда дошла краска, сетка кончается ровно
    /// там. У головы краска мокрая (<see cref="Wet"/>) — гуще, светлее и чуть шире, кончик мягкий; когда кисть
    /// ушла, дорога «сохнет». Загоревшийся узел греет конец дороги (<see cref="Glow"/>).
    ///
    /// Шейдер — «Дым и свет» (материал UiInkStroke): данные элемента пишет сама лента, UiInkReveal ей не
    /// нужен. uv1 = (скрыто, зерно, начало), uv2 = (доля длины, сторона ленты, кромка 0, ширина фронта) —
    /// проявление и растворение идут шумом вдоль дороги, течение и светлые края — как у мазков HUD.
    /// Сетка пересобирается только при смене головы, мокроты, скрытости или пути, без выделений памяти.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SmokeRouteStroke : MaskableGraphic
    {
        [Tooltip("Мазок кистью из набора «Дым и свет» (brush_stroke_1/2): белая маска, цвет — Color")]
        public Sprite Brush;
        [Tooltip("Толщина ленты, единиц холста. Краска — около трёх четвертей: у спрайта поля сверху и снизу")]
        public float Width = 36f;
        [Tooltip("Кусок мазка вдоль дороги, доли ширины спрайта: без рваного начала и без хвоста щетины")]
        public Vector2 Span = new Vector2(.08f, .8f);
        [Tooltip("Полоса краски поперёк, доли высоты спрайта (снизу вверх)")]
        public Vector2 Band = new Vector2(.2f, .8f);
        [Tooltip("Сегментов кривой")] [Range(8, 64)] public int Segments = 36;
        [Tooltip("Рука дрожит: изгиб поперёк дороги, единиц (к концам — ноль, мазок входит в узел ровно)")]
        public float Wobble = 4f;
        [Tooltip("Мокрая голова: на сколько единиц за кистью краска гуще")] public float WetLength = 34f;
        [Tooltip("Насколько шире мокрая голова (доля толщины)")] public float Swell = .3f;
        [Tooltip("Цвет мокрой краски у головы")] public Color WetTint = new Color(1f, .98f, .95f, 1f);
        [Tooltip("Мягкий кончик кисти, единиц: краска сходит на нет")] public float Tip = 9f;
        [Tooltip("Тёплый отсвет загоревшегося узла на конце дороги: на сколько единиц от конца")] public float GlowLength = 120f;
        [Tooltip("Цвет отсвета узла (спокойный тёплый)")] public Color GlowTint = new Color(1f, .64f, .4f, 1f);
        [Tooltip("Ширина фронта проявления, как UiInkReveal.EdgeScale")] [Range(.5f, 3f)] public float EdgeScale = 2.4f;
        [Tooltip("Сдвиг шума: у соседних дорог краска проявляется по-разному")] public float Seed;
        [Tooltip("Откуда проявляется и куда растворяется дорога: (доля длины, сторона)")]
        public Vector2 Origin = new Vector2(0f, .5f);

        float _head = 1f, _hidden, _wet, _glow;
        float[] _xs = new float[0], _ys = new float[0], _length = new float[0], _nx = new float[0], _ny = new float[0];
        int _count;
        float _total;

        public override Texture mainTexture => Brush != null ? Brush.texture : s_WhiteTexture;

        /// <summary>Докуда дошла кисть, доля длины (0 — ничего, 1 — вся дорога).</summary>
        public float Head
        {
            get => _head;
            set => Set(ref _head, Mathf.Clamp01(value));
        }

        /// <summary>1 — дорога не видна, 0 — видна целиком (фронт шейдера с шумом).</summary>
        public float Hidden
        {
            get => _hidden;
            set => Set(ref _hidden, Mathf.Clamp01(value));
        }

        /// <summary>Мокрота головы, 0..1: пока кисть идёт — 1, потом дорога сохнет.</summary>
        public float Wet
        {
            get => _wet;
            set => Set(ref _wet, Mathf.Clamp01(value));
        }

        /// <summary>Отсвет загоревшегося узла на конце дороги, 0..1.</summary>
        public float Glow
        {
            get => _glow;
            set => Set(ref _glow, Mathf.Clamp01(value));
        }

        /// <summary>Длина дороги, единиц холста.</summary>
        public float Length => _total;

        void Set(ref float field, float value)
        {
            if (Mathf.Abs(field - value) < .0005f) return;
            field = value;
            SetVerticesDirty();
        }

        /// <summary>
        /// Путь от <paramref name="from"/> к <paramref name="to"/> — в координатах ленты (её середина —
        /// начало координат, как у раскладки карты). Массивы растут только при смене числа сегментов.
        /// </summary>
        public void SetPath(Vector2 from, Vector2 to)
        {
            int n = Mathf.Clamp(Segments, 8, 64) + 1;
            if (_xs.Length != n)
            {
                _xs = new float[n];
                _ys = new float[n];
                _length = new float[n];
                _nx = new float[n];
                _ny = new float[n];
            }
            _count = n;
            SmokeRouteMapLayout.Sample(from.x, from.y, to.x, to.y, _xs, _ys, _length);
            Normals();
            if (Wobble != 0f)
            {
                // Дрожь руки поперёк дороги: ноль на концах, одна-полторы волны на дорогу.
                for (int i = 1; i < n - 1; i++)
                {
                    float s = (float)i / (n - 1);
                    float w = Wobble * Mathf.Sin(s * Mathf.PI * 2.3f + Seed * 3f) * Mathf.Sin(s * Mathf.PI);
                    _xs[i] += _nx[i] * w;
                    _ys[i] += _ny[i] * w;
                }
                // Длина и нормали — уже по дрожащей линии.
                float total = 0f;
                for (int i = 1; i < n; i++)
                {
                    float dx = _xs[i] - _xs[i - 1], dy = _ys[i] - _ys[i - 1];
                    total += Mathf.Sqrt(dx * dx + dy * dy);
                    _length[i] = total;
                }
                Normals();
            }
            _total = _length[n - 1];
            SetVerticesDirty();
        }

        /// <summary>Путь убран: лента ничего не рисует.</summary>
        public void ClearPath()
        {
            if (_count == 0) return;
            _count = 0;
            _total = 0f;
            SetVerticesDirty();
        }

        /// <summary>Где сейчас кисть — точка головы в координатах ленты.</summary>
        public Vector2 HeadPoint => PointAt(_head * _total);

        void Normals()
        {
            int n = _count;
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
                float tx = _xs[b] - _xs[a], ty = _ys[b] - _ys[a];
                float m = Mathf.Sqrt(tx * tx + ty * ty);
                if (m < 1e-4f) { _nx[i] = 0f; _ny[i] = 1f; continue; }
                // Левая нормаль к ходу кисти.
                _nx[i] = -ty / m;
                _ny[i] = tx / m;
            }
        }

        Vector2 PointAt(float along)
        {
            if (_count < 2) return Vector2.zero;
            if (along <= 0f) return new Vector2(_xs[0], _ys[0]);
            for (int i = 1; i < _count; i++)
            {
                if (_length[i] < along) continue;
                float span = Mathf.Max(_length[i] - _length[i - 1], 1e-4f);
                float k = (along - _length[i - 1]) / span;
                return new Vector2(Mathf.Lerp(_xs[i - 1], _xs[i], k), Mathf.Lerp(_ys[i - 1], _ys[i], k));
            }
            return new Vector2(_xs[_count - 1], _ys[_count - 1]);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_count < 2 || _total <= .01f || _head <= 0f) return;
            float head = _head * _total;
            bool painting = _head < .999f;
            Vector4 outer = Brush != null ? DataUtility.GetOuterUV(Brush) : new Vector4(0f, 0f, 1f, 1f);
            var data = new Vector4(_hidden, Seed, Origin.x, Origin.y);
            float edge = Mathf.Clamp(EdgeScale, .5f, 3f);
            float v0 = Mathf.Lerp(outer.y, outer.w, Band.x), v1 = Mathf.Lerp(outer.y, outer.w, Band.y);

            int emitted = 0;
            for (int i = 0; i < _count && _length[i] < head; i++)
            {
                Emit(vh, _xs[i], _ys[i], _nx[i], _ny[i], _length[i], head, painting, outer, v0, v1, data, edge);
                emitted++;
            }
            // Последняя пара — ровно на голове кисти.
            if (emitted > 0 && emitted < _count)
            {
                int i = emitted;
                float span = Mathf.Max(_length[i] - _length[i - 1], 1e-4f);
                float k = Mathf.Clamp01((head - _length[i - 1]) / span);
                float nx = Mathf.Lerp(_nx[i - 1], _nx[i], k), ny = Mathf.Lerp(_ny[i - 1], _ny[i], k);
                float m = Mathf.Sqrt(nx * nx + ny * ny);
                if (m > 1e-4f) { nx /= m; ny /= m; }
                Emit(vh, Mathf.Lerp(_xs[i - 1], _xs[i], k), Mathf.Lerp(_ys[i - 1], _ys[i], k), nx, ny, head, head, painting,
                    outer, v0, v1, data, edge);
                emitted++;
            }
            else if (emitted == 0) return;
            for (int i = 0; i < emitted - 1; i++)
            {
                int a = i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a, a + 3, a + 2);
            }
        }

        void Emit(VertexHelper vh, float x, float y, float nx, float ny, float along, float head, bool painting,
            Vector4 outer, float v0, float v1, Vector4 data, float edge)
        {
            float s = along / _total;
            float behind = head - along;
            // Мокрая голова: гуще и шире, спадает за кистью.
            float wet = _wet * Mathf.Exp(-behind / Mathf.Max(WetLength, 1f));
            float half = Width * .5f * SmokeRouteMapLayout.Pressure(s, Seed) * (1f + Swell * wet);
            Color c = color;
            float alpha = c.a;
            if (painting) alpha *= Mathf.Clamp01(behind / Mathf.Max(Tip, .01f));
            c = Color.Lerp(c, WetTint, wet * .55f);
            if (_glow > 0f)
            {
                float warm = _glow * Mathf.Exp(-(_total - along) / Mathf.Max(GlowLength, 1f));
                c = Color.Lerp(c, GlowTint, warm * .7f);
            }
            c.a = alpha;
            float u = Mathf.Lerp(outer.x, outer.z, Mathf.Lerp(Span.x, Span.y, s));
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = c;
            vertex.uv1 = data;

            vertex.position = new Vector3(x + nx * half, y + ny * half, 0f);
            vertex.uv0 = new Vector4(u, v1, 0f, 0f);
            vertex.uv2 = new Vector4(s, 1f, 0f, edge);
            vh.AddVert(vertex);

            vertex.position = new Vector3(x - nx * half, y - ny * half, 0f);
            vertex.uv0 = new Vector4(u, v0, 0f, 0f);
            vertex.uv2 = new Vector4(s, 0f, 0f, edge);
            vh.AddVert(vertex);
        }
    }
}
