using UnityEditor;
using UnityEngine;

/// <summary>
/// Импорт арта главного меню.
///
/// Настройки те же, что у инвентаря, и по той же причине: это экранная
/// графика, а не текстура на модели. Мипмапы ей не нужны и только мылят
/// картинку, сжатие даёт полосы на градиенте неба, а подложка 1672 px должна
/// дойти до экрана в исходном размере.
///
/// Размер — всегда свой, без подгонки к степени двойки: у обычной текстуры Unity по умолчанию тянет
/// её к ближайшей степени (фон 4096×2292 стал бы 4096×2048, рассвет 2688×1520 — 2048×1024), и фон
/// меню, который подгоняется под экран по пропорциям текстуры, растягивался вширь.
///
/// Исключение — лого (logo_stone_hd и его огонь, tools/ui-kit/make-menu-logo.py): оно всегда
/// рисуется в 2–2,5 раза мельче своей текстуры, и без мипов край букв рябил лесенкой
/// (владелец, 26 сентября: «сильно поднять бы качество»). Мипы — фильтр Кайзера (держит
/// резкость лучше обычного бокса), трилинейная выборка и сдвиг к более чёткому уровню.
///
/// Фон «Дым» (menu_smoke, 4096 px) — сжат в BC7 с наилучшим качеством: без сжатия он занимал бы
/// ~38 МБ видеопамяти, BC7 — ~9 МБ, а на мягком дыме без ровных градиентов неба полос нет. BC7
/// требует сторон, кратных 4, — высоту подрезает tools/ui-kit/make-menu-seam.py. Свет его шва
/// (menu_smoke_glow/halo) — без сжатия, как всё в папке: тусклые хвосты зарева на тёмном дали бы ступени.
/// </summary>
public sealed class MainMenuArtImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/MainMenu/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        // Панорама меню пака (3840x2160) — единственный полноэкранный арт: на 1440p и 4K
        // ужатая до 2048 она заметно мылится.
        // Ночь меню М1, светлые фоны (рассвет, сумерки; 2688 px) и дым (4096 px) тоже во весь экран — тот же предел.
        importer.maxTextureSize = assetPath.EndsWith("/menu_panorama.png") || assetPath.EndsWith("/menu_night.png")
            || assetPath.EndsWith("/menu_dawn.png") || assetPath.EndsWith("/menu_twilight.png")
            || assetPath.EndsWith("/menu_smoke.png") ? 4096 : 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        if (assetPath.EndsWith("/menu_smoke.png"))
        {
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
        }

        if (assetPath.EndsWith("/logo_stone_hd.png") || assetPath.EndsWith("/logo_stone_glow.png"))
        {
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.filterMode = FilterMode.Trilinear;
            // На 1080p лого 820 единиц из 2048 px: без сдвига трилинейка брала бы наполовину
            // уровень 512 px и мылила; −0,5 держит его на 1024 px, где рябь уже ушла.
            importer.mipMapBias = -.5f;
        }

        // Маски анимации — это веса, а не цвет. В sRGB гамма исказила бы их:
        // половина силы колыхания превратилась бы примерно в пятую часть.
        bool mask = assetPath.EndsWith("_Mask.png");
        importer.sRGBTexture = !mask;
        importer.alphaIsTransparency = !mask;
    }
}
