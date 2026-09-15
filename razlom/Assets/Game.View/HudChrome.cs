using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    // Контуры строятся один раз для каждого размера. Полосы обрезаются при
    // отрисовке: изменение здоровья не создаёт новые текстуры каждый кадр.
    internal sealed class HudChrome
    {
        readonly Dictionary<(Texture2D, Rect), Texture2D> _art = new Dictionary<(Texture2D, Rect), Texture2D>();
        readonly Dictionary<Texture2D, Texture2D> _emptyIcons = new Dictionary<Texture2D, Texture2D>();
        Texture2D _sheen;

        public void Shape(Rect rect, Color color, float cut = 6f, float stroke = 0f)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            // Контур считается в экранном пространстве, без растяжения растровой рамки.
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true,
                0f, color, stroke, cut);
        }

        public void Progress(Rect rect, float value, Color color, float cut = 4f)
        {
            float width = rect.width * Mathf.Clamp01(value);
            if (width <= 0f) return;
            GUI.BeginGroup(new Rect(rect.x, rect.y, width, rect.height));
            Shape(new Rect(0f, 0f, rect.width, rect.height), color, cut);
            GUI.EndGroup();
        }

        public void Meter(Rect rect, float value, Color fill, float radius)
        {
            Shape(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), new Color(.08f, .07f, .04f, .55f), radius);
            Shape(rect, new Color(.39f, .35f, .25f, .95f), radius);
            Rect lip = Inset(rect, .65f);
            Shape(lip, new Color(.89f, .82f, .65f, 1f), Mathf.Max(0f, radius - .65f));
            Rect well = Inset(rect, 1.7f);
            Shape(well, new Color(.15f, .15f, .11f, .9f), Mathf.Max(0f, radius - 1.7f));
            Rect liquid = Inset(rect, 2.5f);
            Progress(liquid, value, fill, Mathf.Max(0f, radius - 2.5f));
            if (_sheen == null)
            {
                _sheen = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++)
                    _sheen.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Lerp(.01f, .17f, y / 31f)));
                _sheen.Apply(false, true);
            }
            GUI.DrawTexture(well, _sheen, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, Mathf.Max(0f, radius - 1.7f));
        }

        public void EmptyIcon(Rect rect, Texture2D source)
        {
            if (source == null || !source.isReadable) return;
            if (!_emptyIcons.TryGetValue(source, out var grey))
            {
                var pixels = source.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    float luminance = pixels[i].grayscale * .65f;
                    pixels[i] = new Color(luminance, luminance, luminance, pixels[i].a * .65f);
                }
                grey = NewTexture(source.width, source.height, "HUD empty " + source.name);
                grey.SetPixels(pixels); grey.Apply(false, true);
                _emptyIcons.Add(source, grey);
            }
            GUI.DrawTexture(rect, grey, ScaleMode.ScaleToFit);
        }

        static Rect Inset(Rect rect, float amount) => new Rect(rect.x + amount, rect.y + amount,
            rect.width - amount * 2f, rect.height - amount * 2f);

        public void Portrait(Rect rect, Texture2D source)
        {
            if (source == null) return;
            GUI.DrawTexture(rect, source, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 13f);
        }

        // Рамка и сама иллюстрация имеют один радиус; исходный арт не меняется.
        public void Art(Rect rect, Texture2D source, Rect uv)
        {
            if (source == null || !source.isReadable) return;
            var key = (source, uv);
            if (!_art.TryGetValue(key, out var texture))
            {
                const int size = 384;
                texture = new Texture2D(size,size,TextureFormat.RGBA32,true)
                { name="HUD filtered "+source.name,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u=uv.x+(x+.5f)/size*uv.width,v=uv.y+(y+.5f)/size*uv.height;
                        float du=uv.width/size*.25f,dv=uv.height/size*.25f;
                        Color pixel=(source.GetPixelBilinear(u-du,v-dv)+source.GetPixelBilinear(u+du,v-dv)
                            +source.GetPixelBilinear(u-du,v+dv)+source.GetPixelBilinear(u+du,v+dv))*.25f;

                        pixels[y * size + x] = pixel;
                    }
                texture.SetPixels(pixels);
                texture.Apply(true, true);
                _art.Add(key, texture);
            }
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, rect.width * .125f);
        }

        public void Forget(Texture2D source)
        {
            var key = (source, new Rect(0f, 0f, 1f, 1f));
            if (_art.TryGetValue(key, out var texture))
            {
                Object.Destroy(texture);
                _art.Remove(key);
            }
        }

        public void Dispose()
        {
            foreach (var texture in _art.Values) Object.Destroy(texture);
            _art.Clear();
            foreach (var texture in _emptyIcons.Values) Object.Destroy(texture);
            _emptyIcons.Clear();
            if (_sheen != null) Object.Destroy(_sheen);
            _sheen = null;
        }

        static Texture2D NewTexture(int width, int height, string name)
            => new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
    }
}

