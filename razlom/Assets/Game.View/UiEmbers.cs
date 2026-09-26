using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Угли над интерфейсом «Дыма и света»: редкие искры рождаются в своём прямоугольнике,
    /// всплывают, покачиваются и гаснут. Один графический элемент — все искры одной сеткой,
    /// без системы частиц (в экранном холсте она не рисуется).
    ///
    /// <see cref="Burst"/> — вспышка искр разом (появление окна, новый уровень).
    /// Часы свои, шаг не больше 0,1 с; в паузе угли тоже летят (время интерфейса).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiEmbers : MaskableGraphic
    {
        [Tooltip("Искра (свет на чёрном: материал UiAdditive)")] public Sprite Sprite;
        [Tooltip("Искр в секунду в покое")] public float Rate = 3f;
        [Tooltip("Не больше искр разом")] public int Max = 40;
        [Tooltip("Жизнь искры, с (от–до)")] public Vector2 Life = new Vector2(1.4f, 2.8f);
        [Tooltip("Размер, единицы Canvas (от–до)")] public Vector2 Size = new Vector2(5f, 11f);
        [Tooltip("Скорость подъёма, единиц в секунду (от–до)")] public Vector2 Speed = new Vector2(18f, 46f);
        [Tooltip("Размах покачивания, единиц")] public float Sway = 10f;
        [Tooltip("Рождаются только в нижней доле прямоугольника (0..1)")] [Range(0f, 1f)] public float SpawnBand = .45f;

        struct Spark
        {
            public Vector2 Position;
            public float Age, Life, Size, Speed, Phase;
        }

        Spark[] _sparks = new Spark[0];
        int _count;
        float _debt, _lastNow = -1f;

        public override Texture mainTexture => Sprite != null ? Sprite.texture : s_WhiteTexture;

        /// <summary>Вспышка: <paramref name="count"/> искр разом из нижней части прямоугольника.</summary>
        public void Burst(int count)
        {
            // Массив искр растёт в Update; вспышка сразу после включения иначе молча ничего не рождала.
            if (_sparks.Length != Max) System.Array.Resize(ref _sparks, Mathf.Max(0, Max));
            for (int i = 0; i < count; i++) Spawn(1.6f);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _lastNow = -1f;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            float now = UiMotion.Now;
            float dt = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            if (_sparks.Length != Max) System.Array.Resize(ref _sparks, Mathf.Max(0, Max));

            _debt += Rate * dt;
            while (_debt >= 1f) { _debt -= 1f; Spawn(1f); }

            for (int i = _count - 1; i >= 0; i--)
            {
                Spark s = _sparks[i];
                s.Age += dt;
                if (s.Age >= s.Life) { _sparks[i] = _sparks[--_count]; continue; }
                s.Position.y += s.Speed * dt;
                s.Position.x += Mathf.Sin(s.Age * 2.1f + s.Phase) * Sway * dt;
                _sparks[i] = s;
            }
            if (_count > 0 || _debt > 0f) SetVerticesDirty();
        }

        void Spawn(float speedScale)
        {
            if (_count >= _sparks.Length) return;
            Rect r = rectTransform.rect;
            _sparks[_count++] = new Spark
            {
                Position = new Vector2(Random.Range(r.xMin, r.xMax), Random.Range(r.yMin, r.yMin + r.height * SpawnBand)),
                Life = Random.Range(Life.x, Life.y),
                Size = Random.Range(Size.x, Size.y),
                Speed = Random.Range(Speed.x, Speed.y) * speedScale,
                Phase = Random.value * 6.28f,
            };
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector4 uv = Sprite != null ? DataUtility.GetOuterUV(Sprite) : new Vector4(0f, 0f, 1f, 1f);
            for (int i = 0; i < _count; i++)
            {
                Spark s = _sparks[i];
                float k = s.Age / s.Life;
                // Вспыхивает за первую пятую жизни, дальше гаснет и мерцает.
                float alpha = Mathf.Min(k * 5f, 1f) * (1f - k) * (.75f + .25f * Mathf.Sin(s.Age * 23f + s.Phase));
                Color32 c = color;
                c.a = (byte)(c.a * Mathf.Clamp01(alpha));
                float half = s.Size * (1f - k * .4f) * .5f;
                int start = vh.currentVertCount;
                vh.AddVert(new Vector3(s.Position.x - half, s.Position.y - half), c, new Vector2(uv.x, uv.y));
                vh.AddVert(new Vector3(s.Position.x - half, s.Position.y + half), c, new Vector2(uv.x, uv.w));
                vh.AddVert(new Vector3(s.Position.x + half, s.Position.y + half), c, new Vector2(uv.z, uv.w));
                vh.AddVert(new Vector3(s.Position.x + half, s.Position.y - half), c, new Vector2(uv.z, uv.y));
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }
    }
}
