using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;

// Картинки портрета боевого HUD (владелец 29.09: «кайма вокруг волос, рваный край круга, шов
// посередине»). Вырез без каймы и мягкую маску рисует tools/ui-kit/make-hud-portrait.py; здесь —
// что они такие и остались такими. Раскладка маски — как в скрипте и в CombatHudWcBuilder.Portrait:
// круг 132 с началом в левом нижнем углу узла «Портрет», рисунок 155,76 с центром (66; 82).
public sealed class HudPortraitArtTests
{
    const string Hud = "razlom/Assets/Resources/UI/HUD/";
    const double CircleX = 66, CircleY = 66, Radius = 66, ArtX = 66, ArtY = 82, ArtSize = 155.76;

    static Rgba Load(string name) => Png.Read(Path.Combine(RepoRoot.Path, Hud + name));

    /// <summary>Альфа маски в точке холста (единицы от угла узла «Портрет», y вверх).</summary>
    static int MaskAt(Rgba mask, double x, double y)
    {
        int u = (int)Math.Floor((x - (ArtX - ArtSize / 2)) / ArtSize * mask.Width);
        int v = (int)Math.Floor((y - (ArtY - ArtSize / 2)) / ArtSize * mask.Height);
        Assert.That(u, Is.InRange(0, mask.Width - 1));
        Assert.That(v, Is.InRange(0, mask.Height - 1));
        // Строки PNG идут сверху вниз.
        return mask.A(u, mask.Height - 1 - v);
    }

    [Test]
    public void MaskKeepsTheBustInsideTheCircleAndLetsTheHeadOut()
    {
        Rgba mask = Load("PelagPortraitMask.png");
        Assert.That(MaskAt(mask, CircleX, 20), Is.EqualTo(255), "грудь внутри круга");
        Assert.That(MaskAt(mask, 6, 6), Is.EqualTo(0), "угол под кругом — плечи обрезаны");
        Assert.That(MaskAt(mask, 138, 10), Is.EqualTo(0), "правый нижний угол — тоже");
        Assert.That(MaskAt(mask, -8, 120), Is.EqualTo(255), "выше середины круга голова выходит за кромку");
        Assert.That(MaskAt(mask, 140, 150), Is.EqualTo(255));
    }

    [Test]
    public void MaskEdgeIsSmoothNotAStaircase()
    {
        // Поперёк края круга ниже середины — несколько промежуточных значений и ровный спуск, без ступеней 0/255.
        Rgba mask = Load("PelagPortraitMask.png");
        foreach (double y in new[] { 12.0, 30.0, 48.0 })
        {
            double edge = CircleX - Math.Sqrt(Radius * Radius - (CircleY - y) * (CircleY - y));
            var run = new List<int>();
            for (double x = edge - 3; x <= edge + 3; x += ArtSize / mask.Width) run.Add(MaskAt(mask, x, y));
            int soft = run.FindAll(a => a > 0 && a < 255).Count;
            Assert.That(soft, Is.GreaterThanOrEqualTo(2), "край на высоте " + y + " без сглаживания: " + string.Join(",", run));
            for (int i = 1; i < run.Count; i++) Assert.That(run[i], Is.GreaterThanOrEqualTo(run[i - 1]), "край не монотонный на высоте " + y);
        }
    }

    [Test]
    public void MaskHasNoSeamAtTheCircleMiddle()
    {
        // Раньше две копии рисунка стыковались на середине круга (высота челюсти) — «шов». Маска одна:
        // внутри круга от низа до головы — сплошная единица, без провала на середине.
        Rgba mask = Load("PelagPortraitMask.png");
        foreach (double x in new[] { 10.0, 30.0, 66.0, 120.0 })
        {
            double half = Math.Sqrt(Radius * Radius - (CircleX - x) * (CircleX - x));
            // Рисунок начинается на 4 единицы выше низа круга — ниже маски нет.
            for (double y = Math.Max(CircleY - half + 3, ArtY - ArtSize / 2 + 1); y <= ArtY + ArtSize / 2 - 1; y += 1)
                Assert.That(MaskAt(mask, x, y), Is.EqualTo(255), "провал маски в точке (" + x + "; " + y + ")");
        }
    }

    [Test]
    public void CutoutBodyIsFullyOpaque()
    {
        // Тело старого выреза было почти непрозрачным (альфа 250–254): сквозь него просвечивал фон.
        Rgba art = Load("PelagPortraitPaintedCutout.png");
        int nearOpaque = 0;
        for (int i = 0; i < art.Pixels; i++)
        {
            byte a = art.Data[i * 4 + 3];
            if (a >= 250 && a < 255) nearOpaque++;
        }
        Assert.That(nearOpaque, Is.EqualTo(0));
    }

    [Test]
    public void CutoutHasNoHaloCrumbs()
    {
        // Отдельные крошки красного ореола вокруг выреза — одна связная фигура (8-связность).
        Rgba art = Load("PelagPortraitPaintedCutout.png");
        var seen = new bool[art.Pixels];
        int shapes = 0;
        var stack = new Stack<int>();
        for (int start = 0; start < art.Pixels; start++)
        {
            if (seen[start] || art.Data[start * 4 + 3] == 0) continue;
            shapes++;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), px = p % art.Width, py = p / art.Width;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = px + dx, ny = py + dy;
                    if (nx < 0 || ny < 0 || nx >= art.Width || ny >= art.Height) continue;
                    int n = ny * art.Width + nx;
                    if (seen[n] || art.Data[n * 4 + 3] == 0) continue;
                    seen[n] = true;
                    stack.Push(n);
                }
            }
        }
        Assert.That(shapes, Is.EqualTo(1));
    }

    [Test]
    public void CutoutEdgeCarriesNoOldDarkBackground()
    {
        // Кайма: полупрозрачный край и прозрачные пиксели у силуэта несли тёмно-сине-красный фон старого
        // рисунка (было: две трети бледного края тёмные). Теперь край — цвета соседних волос и повязки,
        // прозрачное у края — цвета ближайшего края (мип-уровни и билинейный фильтр не тянут тёмное).
        Rgba art = Load("PelagPortraitPaintedCutout.png");
        int faint = 0, faintDark = 0, near = 0, nearDark = 0;
        for (int y = 0; y < art.Height; y++)
        for (int x = 0; x < art.Width; x++)
        {
            int i = (y * art.Width + x) * 4;
            byte a = art.Data[i + 3];
            bool dark = art.Data[i] + art.Data[i + 1] + art.Data[i + 2] < 90;
            if (a > 0 && a < 64)
            {
                faint++;
                if (dark) faintDark++;
            }
            else if (a == 0 && TouchesShape(art, x, y, 2))
            {
                near++;
                if (dark) nearDark++;
            }
        }
        Assert.That(faint, Is.GreaterThan(1000), "у выреза должен быть мягкий край");
        Assert.That(faintDark / (double)faint, Is.LessThan(.2), "бледный край тёмный — вернулась кайма");
        Assert.That(nearDark / (double)near, Is.LessThan(.2), "прозрачное у силуэта тёмное — мипы потянут кайму");
    }

    static bool TouchesShape(Rgba art, int x, int y, int reach)
    {
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= art.Width || ny >= art.Height) continue;
            if (art.A(nx, ny) > 0) return true;
        }
        return false;
    }

    /// <summary>Картинка RGBA 8 бит, строки сверху вниз.</summary>
    sealed class Rgba
    {
        public int Width, Height;
        public byte[] Data;
        public int Pixels => Width * Height;
        public byte A(int x, int y) => Data[(y * Width + x) * 4 + 3];
    }

    /// <summary>Чтение PNG без библиотек: только то, что пишет скрипт (RGBA 8 бит, без чересстрочности).</summary>
    static class Png
    {
        public static Rgba Read(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            Assert.That(file.Length, Is.GreaterThan(8));
            Assert.That(file[1] == 'P' && file[2] == 'N' && file[3] == 'G', "не PNG (указатель LFS без git lfs pull?): " + path);
            int width = 0, height = 0;
            var idat = new MemoryStream();
            for (int at = 8; at + 8 <= file.Length;)
            {
                int length = BigEndian(file, at);
                string type = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
                if (type == "IHDR")
                {
                    width = BigEndian(file, at + 8);
                    height = BigEndian(file, at + 12);
                    Assert.That(file[at + 16], Is.EqualTo(8), "глубина цвета");
                    Assert.That(file[at + 17], Is.EqualTo(6), "тип цвета RGBA");
                    Assert.That(file[at + 20], Is.EqualTo(0), "чересстрочность");
                }
                else if (type == "IDAT") idat.Write(file, at + 8, length);
                else if (type == "IEND") break;
                at += 12 + length;
            }
            idat.Position = 0;
            var raw = new MemoryStream();
            using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
            byte[] packed = raw.ToArray();
            int stride = width * 4;
            Assert.That(packed.Length, Is.EqualTo((stride + 1) * height));
            var data = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int filter = packed[y * (stride + 1)];
                int src = y * (stride + 1) + 1, dst = y * stride;
                for (int i = 0; i < stride; i++)
                {
                    int left = i >= 4 ? data[dst + i - 4] : 0;
                    int up = y > 0 ? data[dst - stride + i] : 0;
                    int corner = y > 0 && i >= 4 ? data[dst - stride + i - 4] : 0;
                    int value = packed[src + i];
                    switch (filter)
                    {
                        case 1: value += left; break;
                        case 2: value += up; break;
                        case 3: value += (left + up) / 2; break;
                        case 4: value += Paeth(left, up, corner); break;
                    }
                    data[dst + i] = (byte)value;
                }
            }
            return new Rgba { Width = width, Height = height, Data = data };
        }

        static int BigEndian(byte[] b, int at) => b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3];

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
