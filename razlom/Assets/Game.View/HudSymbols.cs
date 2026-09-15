using UnityEngine;

namespace Game.View
{
    // Собственный атлас: рамки ячеек никогда не попадают в маленькие символы HUD.
    internal static class HudSymbols
    {
        static Texture2D _map;
        static Rect[] _mapRects;
        static float _retryMap;
        // Собственные контуры в координатах 20×20. Нет зависимости от альфы растрового атласа.
        static readonly Vector2[][] StatPaths =
        {
            new[] { new Vector2(10,17), new Vector2(3,10), new Vector2(2,6), new Vector2(4,3), new Vector2(7,3), new Vector2(10,6), new Vector2(13,3), new Vector2(16,3), new Vector2(18,6), new Vector2(17,10), new Vector2(10,17) },
            new[] { new Vector2(11,2), new Vector2(10,7), new Vector2(14,5), new Vector2(17,10), new Vector2(16,15), new Vector2(12,18), new Vector2(7,17), new Vector2(3,13), new Vector2(4,8), new Vector2(7,10), new Vector2(8,5), new Vector2(11,2) },
            new[] { new Vector2(4,3), new Vector2(16,3), new Vector2(15,6), new Vector2(10,10), new Vector2(15,14), new Vector2(16,17), new Vector2(4,17), new Vector2(5,14), new Vector2(10,10), new Vector2(5,6), new Vector2(4,3) },
            new[] { new Vector2(5,14), new Vector2(14,3), new Vector2(18,2), new Vector2(17,6), new Vector2(8,16) },
            new[] { new Vector2(3,10), new Vector2(17,10), new Vector2(12,5), new Vector2(17,10), new Vector2(12,15) },
            new[] { new Vector2(10,10), new Vector2(16,5), new Vector2(12,5), new Vector2(16,5), new Vector2(16,9) }
        };
        static readonly Texture2D[] StatTextures = new Texture2D[6];
        static Texture2D _dash;
        static readonly Vector2[][] DashParts =
        {
            new[] { new Vector2(69,15),new Vector2(76,15),new Vector2(75,10),new Vector2(84,13),new Vector2(90,20),new Vector2(88,27),new Vector2(82,31),new Vector2(74,27),new Vector2(72,21) },
            new[] { new Vector2(67,31),new Vector2(78,34),new Vector2(76,45),new Vector2(66,60),new Vector2(54,57),new Vector2(58,44) },
            new[] { new Vector2(68,32),new Vector2(50,30),new Vector2(33,45),new Vector2(36,50),new Vector2(52,40),new Vector2(64,43) },
            new[] { new Vector2(75,34),new Vector2(82,47),new Vector2(90,40),new Vector2(95,43),new Vector2(93,49),new Vector2(82,56),new Vector2(76,54),new Vector2(67,40) },
            new[] { new Vector2(62,53),new Vector2(76,65),new Vector2(69,83),new Vector2(70,88),new Vector2(65,90),new Vector2(60,83),new Vector2(64,70),new Vector2(52,62) },
            new[] { new Vector2(58,54),new Vector2(63,62),new Vector2(47,71),new Vector2(34,85),new Vector2(24,91),new Vector2(19,89),new Vector2(22,85),new Vector2(32,79),new Vector2(40,65),new Vector2(50,55) },
            new[] { new Vector2(8,42),new Vector2(40,34),new Vector2(31,42) },
            new[] { new Vector2(4,59),new Vector2(39,49),new Vector2(27,59) },
            new[] { new Vector2(8,75),new Vector2(31,65),new Vector2(21,75) }
        };
        public static void Dash(Rect rect)
        {
            if(_dash==null)
            {
                const int size=192;
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    Vector2 p=new Vector2((x+.5f)*100f/size,100f-(y+.5f)*100f/size);
                    float coverage=0f;
                    foreach(var polygon in DashParts)
                    {
                        float distance=float.MaxValue;
                        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
                        {
                            Vector2 delta=polygon[i]-polygon[j];
                            float t=Mathf.Clamp01(Vector2.Dot(p-polygon[j],delta)/delta.sqrMagnitude);
                            distance=Mathf.Min(distance,Vector2.Distance(p,polygon[j]+delta*t));
                        }
                        coverage=Mathf.Max(coverage,Mathf.Clamp01((Inside(p,polygon)?distance:-distance)*size/100f+.5f));
                    }
                    pixels[y*size+x]=new Color(1f,.94f,.77f,coverage);
                }
                _dash=new Texture2D(size,size,TextureFormat.RGBA32,false){ name="HUD clean dash silhouette",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
                _dash.SetPixels(pixels); _dash.Apply(false,true);
            }
            GUI.DrawTexture(rect,_dash,ScaleMode.ScaleToFit,true);
        }
        public static void Stat(Rect rect, int index)
        {
            if (index < 0 || index >= StatTextures.Length) return;
            if (StatTextures[index] == null) StatTextures[index] = BakeGlyph(index);
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, StatTextures[index], ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        // Собственная векторная форма растрируется один раз с аналитическим сглаживанием.
        // Это устраняет ступеньки и утолщённые стыки десятков отдельных GUI-линий.
        static Texture2D BakeGlyph(int index)
        {
            const int size = 128;
            var edges = new System.Collections.Generic.List<Vector2>();
            Vector2[] path = StatPaths[index];
            if (index == 0)
            {
                var curve = new System.Collections.Generic.List<Vector2>();
                AddCurve(curve, new Vector2(10,6), new Vector2(3,-1), new Vector2(0,5), new Vector2(4,10));
                AddCurve(curve, new Vector2(4,10), new Vector2(6,13), new Vector2(8,15), new Vector2(10,18));
                AddCurve(curve, new Vector2(10,18), new Vector2(12,15), new Vector2(14,13), new Vector2(16,10));
                AddCurve(curve, new Vector2(16,10), new Vector2(20,5), new Vector2(17,-1), new Vector2(10,6));
                path = curve.ToArray();
            }
            for (int i = 1; i < path.Length; i++) { edges.Add(path[i-1]); edges.Add(path[i]); }
            if (index == 2) { edges.Add(new Vector2(7,15)); edges.Add(new Vector2(13,15)); }
            if (index == 3)
            {
                edges.Add(new Vector2(3,12)); edges.Add(new Vector2(10,18));
                edges.Add(new Vector2(6,15)); edges.Add(new Vector2(3,18));
            }
            if (index == 4) { edges.Add(new Vector2(3,7)); edges.Add(new Vector2(3,13)); }
            if (index == 5)
                for (int i = 0; i < 64; i++)
                {
                    float a = i * Mathf.PI / 32f, b = (i+1) * Mathf.PI / 32f;
                    edges.Add(new Vector2(10+Mathf.Cos(a)*8,10+Mathf.Sin(a)*8));
                    edges.Add(new Vector2(10+Mathf.Cos(b)*8,10+Mathf.Sin(b)*8));
                }
            var pixels = new Color[size*size];
            Color ink = index == 0 ? new Color(.91f,.30f,.26f) : index == 1 ? new Color(.94f,.61f,.22f) : new Color(.43f,.36f,.24f);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x+.5f)*20f/size,20f-(y+.5f)*20f/size);
                float distance = float.MaxValue;
                for (int e = 0; e < edges.Count; e+=2)
                {
                    Vector2 delta = edges[e+1]-edges[e];
                    float t = Mathf.Clamp01(Vector2.Dot(p-edges[e],delta)/Mathf.Max(.0001f,delta.sqrMagnitude));
                    distance = Mathf.Min(distance,Vector2.Distance(p,edges[e]+delta*t));
                }
                bool fill = index <= 1 && Inside(p,path);
                float signed = index <= 1 ? (fill ? distance : -distance) : .8f-distance;
                float alpha = Mathf.Clamp01(signed*size/20f+.5f);
                Color color = ink;
                if (index <= 1)
                {
                    color = Color.Lerp(ink*.83f, ink, Mathf.Clamp01((20f-p.y)/15f));
                }
                color.a = alpha;
                pixels[y*size+x]=color;
            }
            var texture = new Texture2D(size,size,TextureFormat.RGBA32,false)
                { name="HUD vector glyph "+index,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave };
            texture.SetPixels(pixels); texture.Apply(false,true);
            return texture;
        }
        static bool Inside(Vector2 p, Vector2[] polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                Vector2 a=polygon[i],b=polygon[j];
                if((a.y>p.y)!=(b.y>p.y) && p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
            }
            return inside;
        }
        static void AddCurve(System.Collections.Generic.List<Vector2> into, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            for(int i=0;i<=16;i++)
            {
                float t=i/16f,u=1f-t;
                into.Add(u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d);
            }
        }
        public static void Map(Rect rect, int index)
        {
            Load(ref _map, ref _mapRects, ref _retryMap, "UI/HUD/MapSymbols", 4, 2);
            Draw(rect, _map, _mapRects, index);
        }
        static void Load(ref Texture2D atlas, ref Rect[] rects, ref float retry, string path, int columns, int rows)
        {
            if (atlas != null || Time.unscaledTime < retry) return;
            retry = Time.unscaledTime + 1f;
            atlas = Resources.Load<Texture2D>(path);
            if (atlas == null) return;
            rects = new Rect[columns * rows];
            Color32[] pixels = atlas.isReadable ? atlas.GetPixels32() : null;
            for (int i = 0; i < rects.Length; i++)
            {
                int left = i % columns * atlas.width / columns;
                int right = (i % columns + 1) * atlas.width / columns;
                int bottom = (rows - 1 - i / columns) * atlas.height / rows;
                int top = (rows - i / columns) * atlas.height / rows;
                int minX = right, minY = top, maxX = left, maxY = bottom;
                if (pixels != null)
                    for (int y = bottom; y < top; y++) for (int x = left; x < right; x++)
                    {
                        if (pixels[y * atlas.width + x].a < 8) continue;
                        minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x + 1); maxY = Mathf.Max(maxY, y + 1);
                    }
                if (minX >= maxX || minY >= maxY) { minX = left; minY = bottom; maxX = right; maxY = top; }
                minX = Mathf.Max(left, minX - 3); minY = Mathf.Max(bottom, minY - 3);
                maxX = Mathf.Min(right, maxX + 3); maxY = Mathf.Min(top, maxY + 3);
                rects[i] = new Rect(minX / (float)atlas.width, minY / (float)atlas.height,
                    (maxX - minX) / (float)atlas.width, (maxY - minY) / (float)atlas.height);
            }
        }
        static void Draw(Rect rect, Texture2D atlas, Rect[] cells, int index)
        {
            if (atlas == null || cells == null || index < 0 || index >= cells.Length) return;
            Rect uv = cells[index];
            float aspect = uv.width * atlas.width / (uv.height * atlas.height);
            float width = Mathf.Min(rect.width, rect.height * aspect), height = width / aspect;
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(new Rect(rect.center.x - width * .5f, rect.center.y - height * .5f, width, height), atlas, uv);
            GUI.color = previous;
        }
    }
}
