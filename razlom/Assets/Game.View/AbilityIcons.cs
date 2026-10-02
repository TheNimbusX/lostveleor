using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Иконки способностей для всех мест вида — плитки HUD, подсказка, карточки и экраны забега, мини-меню
    /// добычи, IMGUI: одно правило <see cref="AbilityIconRules"/> и один кеш по (способность, форма).
    ///
    /// Форма без своего арта (Resources/UI/Abilities/Icon_Навык_Форма.png) получает базовую иконку с меткой
    /// формы: картинка копируется видеокартой в 512 px (исходник может быть сжатым и нечитаемым) и метка
    /// рисуется поверх её угла (<see cref="AbilityIconRules.Paint"/>). Собирается раз за запуск на пару
    /// (способность, форма) — в бою это одна-две картинки.
    /// </summary>
    public static class AbilityIcons
    {
        static readonly Dictionary<long, Texture2D> Cache = new Dictionary<long, Texture2D>();

        /// <summary>Иконка способности в этой форме; без иконки у способности — null.</summary>
        public static Texture2D Get(int definitionId, PelagForm form = PelagForm.None)
        {
            long key = AbilityIconRules.CacheKey(definitionId, form);
            // Собранная в Play картинка гибнет на выходе из него — тогда собрать заново (Unity-null).
            if (Cache.TryGetValue(key, out Texture2D cached) && (cached != null || (object)cached == null)) return cached;
            Texture2D texture = Load(definitionId, form);
            Cache[key] = texture;
            return texture;
        }

        static Texture2D Load(int definitionId, PelagForm form)
        {
            string file = AbilityIconRules.BaseFile(definitionId);
            if (file == null) return null;
            if (form != PelagForm.None)
            {
                // Свой арт формы — когда владелец его примет; до тех пор — база с меткой.
                string formFile = AbilityIconRules.FormFile(definitionId, form);
                Texture2D art = formFile != null ? Resources.Load<Texture2D>(AbilityIconRules.ResourceFolder + formFile) : null;
                if (art != null) return art;
            }
            Texture2D icon = Resources.Load<Texture2D>(AbilityIconRules.ResourceFolder + file);
            if (form == PelagForm.None || icon == null) return icon;
            return Marked(icon, form) ?? icon;
        }

        /// <summary>Базовая иконка с меткой формы; видеокарты нет (съёмка без графики) — null, остаётся база.</summary>
        static Texture2D Marked(Texture2D source, PelagForm form)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            int size = AbilityIconRules.MarkedIconSize;
            RenderTexture target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = source.name + "_" + AbilityIconRules.FormSuffix(form) + " (метка формы)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            Color32[] pixels = texture.GetPixels32();
            AbilityIconRules.MarkPixels(size, out int x0, out int y0, out int x1, out int y1);
            float pixel = 1f / size;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = y * size + x;
                Color32 c = pixels[i];
                AbilityIconRules.Paint(ref c.r, ref c.g, ref c.b, (x + .5f) * pixel, (y + .5f) * pixel, pixel);
                c.a = 255;
                pixels[i] = c;
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
