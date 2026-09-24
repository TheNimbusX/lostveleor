using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Готовность способности в боевом HUD (вариант B владельца, 23 сентября): вместо яркого
    /// свечения вокруг плитки — камень на верхней кромке. Готова — камень горит и изредка
    /// тихо мерцает искрой; на перезарядке — тусклый. В момент, когда способность стала
    /// готова, — одна короткая вспышка искры и отблеск рамки. Показ HUD вспышку не вызывает:
    /// её даёт только переход «перезарядка → готово». Время неигровое.
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudReadyGem : MonoBehaviour
    {
        public Image Gem;
        [Tooltip("Четырёхлучевая искра над камнем")] public Image Spark;
        [Tooltip("Необязательно: рамка плитки — коротко светлеет во вспышке")] public Image Frame;
        public Color ReadyColour = new Color32(0x3B, 0xF0, 0xF5, 0xFF);
        public Color IdleColour = new Color(.55f, .6f, .68f, .45f);
        [Tooltip("Пауза между мерцаниями готовой, секунды (с разбросом)")] public float TwinkleEvery = 4.5f;
        public float TwinkleTime = .7f;
        [Range(0f, 1f)] public float TwinkleAlpha = .55f;
        public float FlashTime = .5f;
        [Tooltip("Размер искры во вспышке относительно обычного")] public float FlashScale = 1.9f;

        bool _known, _ready;
        float _flashAt = -100f, _twinkleAt;
        Color _frameColour;

        void Awake()
        {
            if (Frame != null) _frameColour = Frame.color;
            _twinkleAt = Time.unscaledTime + Random.Range(1f, TwinkleEvery);
        }

        public void SetReady(bool ready)
        {
            if (_known && ready && !_ready) _flashAt = Time.unscaledTime;
            _known = true;
            _ready = ready;
        }

        void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (Gem != null)
            {
                Color gem = _ready ? ReadyColour : IdleColour;
                Gem.color = gem;
            }
            float flash = 1f - Mathf.Clamp01((now - _flashAt) / Mathf.Max(.05f, FlashTime));
            float spark = 0f, scale = 1f;
            if (flash > 0f)
            {
                spark = flash;
                scale = Mathf.Lerp(.6f, FlashScale, Mathf.Sin((1f - flash) * Mathf.PI * .5f + .3f));
            }
            else if (_ready)
            {
                if (now >= _twinkleAt + TwinkleTime) _twinkleAt = now + TwinkleEvery * Random.Range(.7f, 1.4f);
                float t = (now - _twinkleAt) / Mathf.Max(.05f, TwinkleTime);
                if (t >= 0f && t <= 1f)
                {
                    spark = Mathf.Sin(t * Mathf.PI) * TwinkleAlpha;
                    scale = .75f + .35f * Mathf.Sin(t * Mathf.PI);
                }
            }
            if (Spark != null)
            {
                Color c = ReadyColour;
                c.a = spark;
                Spark.color = Color.Lerp(c, new Color(1f, 1f, 1f, spark), .45f);
                Spark.rectTransform.localScale = Vector3.one * scale;
                Spark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (1f - scale) * 25f);
            }
            if (Frame != null) Frame.color = Color.Lerp(_frameColour, ReadyColour, flash * .8f);
        }
    }
}
