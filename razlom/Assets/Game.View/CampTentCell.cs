using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Ячейка палатки: рамка и свечение редкости, камень, значок предмета,
    /// уровень, отметки выбора и «беречь». Мышь и перетаскивание обрабатывает
    /// CampInventoryCell, который игра вешает на ту же ячейку; здесь только
    /// внешний вид. Свой файл обязателен: компонент стоит в префабе.
    ///
    /// Редкость видна сразу (владелец 16 сентября: «нечитаемая»): подкраска-свечение
    /// за вещью, камень в углу, у эпической и уникальной свечение дышит, по
    /// уникальной время от времени пробегает блик.
    ///
    /// «Дым и свет» (26 сентября, CampTentWcBuilder): ячейка — UiInkKit.Cell или круглый SlotOrb,
    /// рамку и свет редкости ведёт их WcSlotState (<see cref="State"/>), камень — малый светящийся
    /// шарик (<see cref="Gem"/> и <see cref="GemGlow"/>), а не гранёный ромб.
    /// </summary>
    public sealed class CampTentCell : MonoBehaviour
    {
        [Tooltip("Круглый слот снаряжения: кольцо не меняется, а красится цветом редкости")]
        public bool Round;
        public Image Frame;
        [Tooltip("Одна рамка и цветной фон редкости; если задан, выбор и наведение рисует он")] public WcSlotState State;
        public Image Icon;
        [Tooltip("Бледный значок пустого слота снаряжения")] public Image Placeholder;
        public TMP_Text Level;
        [Tooltip("Отметка выбранной ячейки")] public GameObject Selection;
        [Tooltip("Замок: вещь отмечена «беречь» и не уйдёт в разбор")] public GameObject Lock;
        [Tooltip("Ячейка, скрытая фильтром, тускнеет")] public CanvasGroup Group;
        [Range(0f, 1f)] public float FilteredAlpha = .28f;

        [Header("Редкость")]
        [Tooltip("Мягкое свечение за вещью, красится в цвет редкости")] public Image RarityGlow;
        [Tooltip("Камень в углу; у обычной скрыт")] public Image Gem;
        [Tooltip("«Дым и свет»: сияние вокруг камня-шарика, цвета редкости; живёт вместе с камнем")] public Image GemGlow;
        [Range(0f, 1f)] public float GemGlowAlpha = .6f;
        [Tooltip("Полоса блика под маской; пробегает по уникальной")] public RectTransform Shine;
        [Range(0f, 1f)] public float GlowAlpha = .55f;
        [Range(0f, 1f)] public float BreathMin = .3f;
        [Tooltip("Секунд на вдох-выдох свечения")] public float BreathPeriod = 2.4f;
        [Tooltip("Пауза между бликами уникальной, секунд")] public float ShineEvery = 3.5f;
        public float ShineDuration = .55f;

        /// <summary>Вещь в полёте: значок в ячейке ждёт, пока копия долетит.</summary>
        [System.NonSerialized] public bool Hold;

        int _rarity = -1;
        Color _colour = Color.white;
        float _phase, _nextShine, _flashUntil, _flashStart;

        void OnEnable()
        {
            // Разные фазы: одинаково дышащая сетка выглядит как мигание.
            _phase = (transform.GetSiblingIndex() * .37f) % 1f;
            _nextShine = UiMotion.Now + ShineEvery * (1f + _phase);
            if (Shine != null) Shine.gameObject.SetActive(false);
        }

        public void Show(Sprite frame, Color tint, Sprite icon, string level, bool selected, bool filtered, bool kept,
            int rarity, Color rarityColour)
        {
            if (State != null) State.Set(icon == null ? WcSlotState.Empty : rarity, selected);
            else if (Frame != null)
            {
                if (Round) Frame.color = tint;
                else if (frame != null && Frame.sprite != frame) Frame.sprite = frame;
            }
            bool visible = icon != null && !Hold;
            if (Icon != null)
            {
                Icon.sprite = icon;
                Icon.enabled = visible;
            }
            if (Placeholder != null) Placeholder.enabled = icon == null && Placeholder.sprite != null;
            if (Level != null && Level.text != level) Level.text = level;
            if (Selection != null && Selection.activeSelf != selected) Selection.SetActive(selected);
            if (Lock != null && Lock.activeSelf != kept) Lock.SetActive(kept);
            if (Group != null) Group.alpha = filtered ? FilteredAlpha : 1f;

            _rarity = icon == null ? -1 : rarity;
            _colour = rarityColour;
            if (Gem != null)
            {
                Gem.enabled = _rarity >= 1;
                Gem.color = rarityColour;
                Gem.rectTransform.localScale = Vector3.one * (_rarity >= 3 ? 1.25f : _rarity == 2 ? 1.1f : 1f);
                if (GemGlow != null)
                {
                    GemGlow.enabled = Gem.enabled;
                    Color glow = rarityColour;
                    glow.a = GemGlowAlpha;
                    GemGlow.color = glow;
                    GemGlow.rectTransform.localScale = Gem.rectTransform.localScale;
                }
            }
            if (RarityGlow != null && UiMotion.Now >= _flashUntil)
            {
                // С цветным фоном (State) свечение только вспыхивает при надевании.
                RarityGlow.enabled = State == null && _rarity >= 1;
                ApplyGlow(GlowAlpha);
            }
        }

        /// <summary>Вспышка при надевании: свечение в полную силу и затухание.</summary>
        public void Flash(Color colour, float duration = .5f)
        {
            _colour = colour;
            _flashStart = UiMotion.Now;
            _flashUntil = _flashStart + duration;
            if (RarityGlow != null) RarityGlow.enabled = true;
        }

        void Update()
        {
            if (RarityGlow == null) return;
            float now = UiMotion.Now;
            if (now < _flashUntil)
            {
                float t = (now - _flashStart) / Mathf.Max(.01f, _flashUntil - _flashStart);
                ApplyGlow(Mathf.Lerp(1f, GlowAlpha, t));
                RarityGlow.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, t);
                return;
            }
            RarityGlow.rectTransform.localScale = Vector3.one;
            if (_rarity < 0 || State != null) RarityGlow.enabled = false;
            if (_rarity >= 2 && State == null)
            {
                float wave = .5f + .5f * Mathf.Sin((now / Mathf.Max(.2f, BreathPeriod) + _phase) * Mathf.PI * 2f);
                ApplyGlow(Mathf.Lerp(BreathMin, 1f, wave) * GlowAlpha * 1.4f);
            }
            if (_rarity >= 3 && Shine != null && now >= _nextShine && !Hold)
            {
                _nextShine = now + ShineEvery;
                RunShine();
            }
        }

        void ApplyGlow(float alpha)
        {
            Color c = _colour;
            c.a = Mathf.Clamp01(alpha);
            RarityGlow.color = c;
        }

        void RunShine()
        {
            if (!(Shine.parent is RectTransform lane)) return;
            Shine.gameObject.SetActive(true);
            float width = lane.rect.width + Shine.rect.width;
            UiMotion.Play(Shine, 21, ShineDuration,
                t => Shine.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-width * .5f, width * .5f, t), 0f),
                AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                () => { if (Shine != null) Shine.gameObject.SetActive(false); });
        }
    }
}
