using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Приводит исходные флипбуки из ART/vfx к тому виду, который ждёт игра.
///
/// Листы приходят из генераторов и со стоков, и у них две беды, повторяющиеся
/// из раза в раз:
///
/// 1. ФОН БЕЛЫЙ, А НЕ ПРОЗРАЧНЫЙ. В «dus 8x8.png» полностью прозрачных
///    пикселей было 0.5%, а 95.6% — полупрозрачные с белым цветом. Такой лист
///    рисуется белыми квадратами. Ровно на этом в проекте уже отклоняли
///    ItemIcons_Transparent.png.
///
/// 2. РАЗМЕР НЕ КРАТЕН СЕТКЕ. 1254 на 8 колонок даёт 156.75 пикселя на ячейку,
///    и кадры разъезжаются с накоплением ошибки.
///
/// Конвертер решает обе: вынимает каждую ячейку по её дробным границам
/// отдельно (ошибка не накапливается), масштабирует до степени двойки и
/// восстанавливает альфу из белизны.
///
/// Меню: Разлом → VFX → Импортировать флипбуки из ART/vfx
/// </summary>
public static class RazlomVfxSheetImport
{
    private const string SourceFolder = "ART/vfx";
    private const string TargetFolder = "Assets/Resources/VFX/Pelag/Textures";

    /// <summary>
    /// Сторона готового листа. ВСЕГДА 2048, при любой сетке.
    ///
    /// Сборщик VFX проверяет размер жёстко: texture.width != 2048 означает
    /// отказ собирать библиотеку целиком. Лист 4×4 по 256 давал 1024, проверка
    /// падала, и в игре не оставалось ни одного эффекта — только белый квадрат
    /// от материала без текстуры.
    /// </summary>
    private const int SheetSize = 2048;

    /// <summary>
    /// Сетка по умолчанию, если в имени файла её нет.
    ///
    /// ЕДИНИЦА, А НЕ ЧЕТЫРЁХ. Без сетки в имени файл — не флипбук, а цельная
    /// картинка: полоса следа, маска, градиент. Резать её на 16 ячеек значит
    /// гарантированно её испортить, причём молча.
    /// </summary>
    private const int DefaultTiles = 1;

    /// <summary>
    /// Отступ внутрь ячейки в пикселях ИСХОДНИКА.
    ///
    /// Срезает разделительные линии, которые генератор рисует между кадрами.
    /// Больше трёх брать не стоит: при ячейке около 156 пикселей это уже
    /// заметно подрезает сам эффект по краям.
    /// </summary>
    private const float SourceInset = 2.5f;

    /// <summary>
    /// Альфа ниже этого порога обнуляется.
    ///
    /// Генератор оставляет вокруг рисунка белёсую дымку с альфой 1–15. На
    /// маленькой иконке её не видно, а растянутая на два метра в игре она даёт
    /// светлый прямоугольник вокруг эффекта — те самые «белые края и углы».
    /// </summary>
    private const float AlphaFloor = 0.075f;

    /// <summary>
    /// Ширина принудительно прозрачной каймы по краю ячейки, в пикселях
    /// результата.
    ///
    /// Билинейная фильтрация на границе кадра подмешивает соседнюю ячейку;
    /// прозрачная кайма делает это невозможным.
    /// </summary>
    private const int EdgeFade = 6;

    [MenuItem("Разлом/VFX/Импортировать флипбуки из ART/vfx")]
    public static void ImportAll()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", SourceFolder));
        if (!Directory.Exists(source))
        {
            Debug.LogWarning("[vfx sheet] нет папки " + source);
            return;
        }

        Directory.CreateDirectory(Path.GetFullPath(TargetFolder));
        var done = new List<string>();
        foreach (string file in Directory.GetFiles(source, "*.png"))
        {
            string result = Convert(file);
            if (result != null) done.Add(result);
        }

        AssetDatabase.Refresh();
        foreach (string path in done) ApplyImportSettings(path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[vfx sheet] обработано листов: {done.Count}");
    }

    /// <summary>
    /// Сетка берётся из имени файла: «dus 8x8.png» → 8. Так лист сам говорит,
    /// как его резать, и не нужно держать таблицу соответствий.
    /// </summary>
    private static int TilesFromName(string name)
    {
        Match m = Regex.Match(name, @"(\d+)\s*[xX]\s*(\d+)");
        if (!m.Success) return DefaultTiles;
        int columns = int.Parse(m.Groups[1].Value);
        int rows = int.Parse(m.Groups[2].Value);
        if (columns != rows)
            Debug.LogWarning($"[vfx sheet] {name}: сетка {columns}x{rows} не квадратная, беру {columns}");
        return Mathf.Max(1, columns);
    }

    /// <summary>Как у листа устроена прозрачность.</summary>
    private enum Backing
    {
        /// <summary>Альфа настоящая, трогать нельзя.</summary>
        Alpha,
        /// <summary>Фон белый: альфа = 1 − белизна.</summary>
        White,
        /// <summary>Фон чёрный: альфа = яркость, как у аддитивных эффектов.</summary>
        Black
    }

    /// <summary>
    /// Определяет, откуда брать прозрачность.
    ///
    /// ЛИСТ С НАСТОЯЩЕЙ АЛЬФОЙ НЕЛЬЗЯ КЛЮЧИТЬ ПОВТОРНО. У вспышки ядро белое и
    /// непрозрачное; вычисленная из белизны альфа обнулила бы именно его —
    /// пропала бы самая яркая часть эффекта.
    ///
    /// Генераторы отдают три варианта, и все три встретились в одной пачке:
    /// прозрачный фон, белый фон и чёрный фон под аддитивное наложение.
    /// </summary>
    private static Backing DetectBacking(Texture2D source)
    {
        Color32[] pixels = source.GetPixels32();
        int transparent = 0, bright = 0, dark = 0, opaque = 0;
        for (int i = 0; i < pixels.Length; i += 37)
        {
            Color32 c = pixels[i];
            if (c.a <= 4) { transparent++; continue; }
            if (c.a >= 250) opaque++;
            int low = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            int high = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (low >= 240) bright++;
            if (high <= 30) dark++;
        }

        int sampled = pixels.Length / 37 + 1;
        // Десятой части прозрачных хватает: у настоящего листа фон занимает
        // большую часть кадра, у ключуемого прозрачных нет вовсе.
        if (transparent * 10 > sampled) return Backing.Alpha;
        return dark > bright ? Backing.Black : Backing.White;
    }

    private static string Convert(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        int tiles = TilesFromName(name);

        var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!raw.LoadImage(File.ReadAllBytes(file)))
        {
            Debug.LogWarning("[vfx sheet] не читается: " + file);
            Object.DestroyImmediate(raw);
            return null;
        }

        // Цельная картинка сохраняет свои пропорции: полоса следа не квадрат,
        // и вписывать её в квадрат значит растянуть.
        bool single = tiles == 1;
        int cellSize = single ? 0 : SheetSize / tiles;
        int width = single ? NearestPowerOfTwo(raw.width) : cellSize * tiles;
        int height = single ? NearestPowerOfTwo(raw.height) : cellSize * tiles;
        int side = width;
        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];

        Backing backing = DetectBacking(raw);

        // Ячейки вынимаются по дробным границам исходника, каждая независимо:
        // при 156.75 пикселя на ячейку общий ресайз копил бы сдвиг до кадра.
        //
        // ОТСТУП ОТ КРАЁВ ОБЯЗАТЕЛЕН. Генератор рисует между ячейками
        // разделительные линии: в «dus 8x8» на границах средняя альфа 214, а
        // внутри 85. Без отступа эти линии попадают в кадры и дают светлую
        // сетку поверх каждого эффекта.
        float cellSource = raw.width / (float)tiles;
        float usable = cellSource - 2f * SourceInset;
        if (single)
        {
            // Ни нарезки, ни каймы: у полосы плотный конец должен доходить до
            // самого края, иначе след обрывается прозрачностью.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color c = raw.GetPixelBilinear((x + 0.5f) / width, 1f - (y + 0.5f) / height);
                    pixels[(height - 1 - y) * width + x] = Key(c, backing);
                }
            result.SetPixels32(pixels);
            result.Apply();
            string strip = Path.Combine(TargetFolder, "Pelag_FX_" + Clean(name) + ".png").Replace('\\', '/');
            File.WriteAllBytes(Path.GetFullPath(strip), result.EncodeToPNG());
            Object.DestroyImmediate(raw);
            Object.DestroyImmediate(result);
            Debug.Log($"[vfx sheet] {Path.GetFileName(file)} → {strip}, цельная картинка {width}x{height}, фон={backing}");
            return strip;
        }

        for (int ty = 0; ty < tiles; ty++)
            for (int tx = 0; tx < tiles; tx++)
                for (int y = 0; y < cellSize; y++)
                    for (int x = 0; x < cellSize; x++)
                    {
                        float u = tx * cellSource + SourceInset + (x + 0.5f) / cellSize * usable;
                        float v = ty * cellSource + SourceInset + (y + 0.5f) / cellSize * usable;
                        Color c = raw.GetPixelBilinear(u / raw.width, 1f - v / raw.height);
                        Color32 keyed = Key(c, backing);

                        // Кайма по краю ячейки гасится до нуля: и от подмешивания
                        // соседнего кадра фильтрацией, и от белёсой рамки, которую
                        // генератор оставляет по периметру рисунка.
                        int edge = Mathf.Min(Mathf.Min(x, cellSize - 1 - x), Mathf.Min(y, cellSize - 1 - y));
                        if (edge < EdgeFade)
                            keyed.a = (byte)(keyed.a * edge / (float)EdgeFade);

                        pixels[(side - 1 - (ty * cellSize + y)) * side + tx * cellSize + x] = keyed;
                    }

        result.SetPixels32(pixels);
        result.Apply();

        string target = Path.Combine(TargetFolder, "Pelag_FX_" + Clean(name) + ".png").Replace('\\', '/');
        File.WriteAllBytes(Path.GetFullPath(target), result.EncodeToPNG());
        Object.DestroyImmediate(raw);
        Object.DestroyImmediate(result);
        Debug.Log($"[vfx sheet] {Path.GetFileName(file)} → {target}, " +
                  $"сетка {tiles}x{tiles}, лист {side}px, ячейка {cellSize}px, фон={backing}");
        return target;
    }

    private static Color32 Key(Color c, Backing backing)
    {
        if (backing == Backing.Alpha)
        {
            // Даже у листа с настоящей альфой фон не идеально прозрачен:
            // в «dus 8x8» он держит альфу 1–4. Растянутая на два метра, эта
            // дымка читается как светлая рамка вокруг эффекта.
            if (c.a < AlphaFloor) return new Color32(0, 0, 0, 0);
            // Порог срезан снизу — оставшееся растягиваем обратно на весь
            // диапазон, иначе эффект в целом станет бледнее.
            c.a = Mathf.Clamp01((c.a - AlphaFloor) / (1f - AlphaFloor));
            return c;
        }

        if (backing == Backing.Black)
        {
            // Чёрный фон — соглашение аддитивных эффектов: чем ярче, тем
            // плотнее. Цвет остаётся как есть, гасить его нечем.
            float bright = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return new Color(c.r, c.g, c.b, Mathf.Clamp01(bright));
        }

        // Белый фон. Нейтральное и светлое считаем фоном, цветное оставляем
        // плотным: иначе золотая вспышка пропала бы вместе с белизной.
        float low = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        float high = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float alpha = Mathf.Clamp01(1f - low + (high - low));
        if (alpha <= 0.004f) return new Color32(0, 0, 0, 0);

        // Цвет делится на альфу: иначе полупрозрачные места выцветают в белый
        // при наложении, потому что белизна фона остаётся в самом цвете.
        float k = 1f / alpha;
        return new Color(
            Mathf.Clamp01((c.r - (1f - alpha)) * k),
            Mathf.Clamp01((c.g - (1f - alpha)) * k),
            Mathf.Clamp01((c.b - (1f - alpha)) * k),
            alpha);
    }

    /// <summary>Ближайшая степень двойки, не крупнее 2048.</summary>
    private static int NearestPowerOfTwo(int value)
    {
        int result = 1;
        while (result * 2 <= value && result < 2048) result *= 2;
        return Mathf.Max(64, result);
    }

    private static string Clean(string name)
    {
        string cleaned = Regex.Replace(name, @"[^A-Za-z0-9]+", "_").Trim('_');
        return string.IsNullOrEmpty(cleaned) ? "Sheet" : cleaned;
    }

    private static void ApplyImportSettings(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }
}
