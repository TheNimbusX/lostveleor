using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Настройки импорта UI-пака (Assets/UI/Kit).
    ///
    /// ПОЧЕМУ PPU 200 У ХРОМА. Панели и кнопки нарисованы в 2× (tools/ui-kit/
    /// build-chrome.ps1). При 200 пикселях на единицу их рамки в Canvas
    /// получают естественную толщину, а 9-slice тянет середину без искажения
    /// углов. Границы берутся из slices.txt рядом со спрайтами — их пишет тот
    /// же скрипт, что рисует хром, поэтому они не разъезжаются с картинкой.
    ///
    /// Сжатие выключено: рамки тонкие, и блочные артефакты на них видны сразу.
    ///
    /// Mip-уровни включены: HUD уменьшен до 65%, хром нарисован в 2×, иконки
    /// ~400 px идут на 20–30 px экрана. Без mip-уровней края рвались.
    /// </summary>
    public sealed class UiKitImport : AssetPostprocessor
    {
        public const string KitRoot = "Assets/UI/Kit";
        const string ChromeFolder = KitRoot + "/Chrome";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(KitRoot + "/")) return;
            Configure((TextureImporter)assetImporter, assetPath);
        }

        /// <summary>Применяет настройки к уже лежащему файлу; true — если что-то поменялось.</summary>
        public static bool Ensure(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return false;
            if (!Configure(importer, path)) return false;
            importer.SaveAndReimport();
            return true;
        }

        static bool Configure(TextureImporter importer, string path)
        {
            // slices.txt рядом со спрайтами задаёт границы 9-slice и, пятым числом,
            // пиксели на единицу: нарисованные листы нарезаны в разном масштабе.
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            bool chrome = path.StartsWith(ChromeFolder + "/");
            Slice slice = Find(folder, Path.GetFileNameWithoutExtension(path));
            float ppu = slice.Ppu > 0f ? slice.Ppu : chrome ? 200f : 100f;
            Vector4 border = slice.Border;
            bool changed = importer.textureType != TextureImporterType.Sprite
                || importer.spriteImportMode != SpriteImportMode.Single
                || !Mathf.Approximately(importer.spritePixelsPerUnit, ppu)
                || importer.spriteBorder != border
                || !importer.mipmapEnabled
                || importer.mipmapFilter != TextureImporterMipFilter.KaiserFilter
                || importer.textureCompression != TextureImporterCompression.Uncompressed
                || !importer.alphaIsTransparency;
            if (!changed) return false;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.spriteBorder = border;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.mipMapBias = -.3f;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            return true;
        }

        struct Slice
        {
            public Vector4 Border;
            public float Ppu;
        }

        /// <summary>
        /// Строка slices.txt: «имя лево низ право верх [ppu]». Граница в порядке Unity.
        /// Файл маленький и читается на каждый спрайт без кэша: после перенарезки
        /// листа новые границы применяются без перезапуска редактора.
        /// </summary>
        static Slice Find(string folder, string name)
        {
            string file = Path.Combine(folder, "slices.txt");
            if (!File.Exists(file)) return default;
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string line in File.ReadAllLines(file))
            {
                string[] part = line.Split(' ');
                if (part.Length < 5 || part[0] != name) continue;
                return new Slice
                {
                    Border = new Vector4(float.Parse(part[1], invariant), float.Parse(part[2], invariant),
                        float.Parse(part[3], invariant), float.Parse(part[4], invariant)),
                    Ppu = part.Length > 5 ? float.Parse(part[5], invariant) : 0f,
                };
            }
            return default;
        }
    }
}
