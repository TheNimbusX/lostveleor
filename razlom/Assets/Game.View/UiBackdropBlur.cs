using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Размытый фон под меню паузы: ОДИН снимок кадра игры в момент открытия,
    /// уменьшенный и размытый несколькими проходами, показывается в RawImage.
    ///
    /// Игра в паузе стоит, поэтому снимок не устаревает, а рендер и URP-ассет
    /// не трогаются: ни одного лишнего прохода каждый кадр (картинку ради
    /// меню не меняем — см. память о настройках рендера).
    /// </summary>
    public sealed class UiBackdropBlur : MonoBehaviour
    {
        public RawImage Target;
        [Tooltip("Во сколько раз уменьшить снимок перед размытием")] [Range(1, 8)] public int Downsample = 4;
        [Tooltip("Проходов размытия (каждый — по горизонтали и по вертикали)")] [Range(0, 8)] public int Passes = 3;
        [Tooltip("Шаг выборки в пикселях уменьшенного снимка")] [Range(0.5f, 4f)] public float Spread = 1.5f;

        RenderTexture _result, _swap;
        Material _material;

        /// <summary>Снять кадр в конце текущего кадра и размыть. Вызывать, пока меню ещё не показано.</summary>
        public IEnumerator Capture()
        {
            yield return new WaitForEndOfFrame();
            if (Target == null) yield break;
            if (_material == null)
            {
                Shader shader = Resources.Load<Shader>("UI/Shaders/UiBlur");
                if (shader == null) { Target.enabled = false; yield break; }
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            int width = Mathf.Max(64, Screen.width / Downsample), height = Mathf.Max(64, Screen.height / Downsample);
            Ensure(ref _result, width, height);
            Ensure(ref _swap, width, height);

            RenderTexture full = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
            // Снимок экрана на DirectX и Metal перевёрнут относительно UV.
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(full, _result, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(full, _result);
            RenderTexture.ReleaseTemporary(full);

            for (int i = 0; i < Passes; i++)
            {
                _material.SetVector("_Direction", new Vector4(Spread / width, 0f, 0f, 0f));
                Graphics.Blit(_result, _swap, _material);
                _material.SetVector("_Direction", new Vector4(0f, Spread / height, 0f, 0f));
                Graphics.Blit(_swap, _result, _material);
            }
            Target.texture = _result;
            Target.enabled = true;
        }

        static void Ensure(ref RenderTexture texture, int width, int height)
        {
            if (texture != null && texture.width == width && texture.height == height) return;
            if (texture != null) { texture.Release(); Destroy(texture); }
            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                { name = "Pause backdrop blur", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.Create();
        }

        void OnDestroy()
        {
            if (_result != null) { _result.Release(); Destroy(_result); }
            if (_swap != null) { _swap.Release(); Destroy(_swap); }
            if (_material != null) Destroy(_material);
        }
    }
}
