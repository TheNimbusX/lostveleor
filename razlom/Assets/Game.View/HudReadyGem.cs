using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Камень над способностью в боевом HUD. Показывает две вещи сразу.
    ///
    /// Усиления (владелец, 24 сентября: ступени камня «нечитаемы» — выбран камень + 8 насечек;
    /// потом: ряд «показывать только при наведении»). Над камнем ряд из 8 насечек: сколько горит —
    /// столько усилений. Ряд виден только под мышью, в покое — один камень.
    /// Камень меняет материал ступенями: 0 пустой, 1–2 сталь, 3–5 серебро, 6–7 золото, 8 сияющий
    /// кристалл с мягким дыханием. Взятое усиление — новая насечка вспыхивает, камень вздрагивает.
    ///
    /// Готовность: готова — камень горит в полную силу, на перезарядке тусклый. В момент
    /// «перезарядка → готово» по плитке проходит одна волна света. Постоянного мерцания нет —
    /// старая «искра» спорила с камнем и убрана. Время неигровое.
    /// Свой файл обязателен: компонент стоит в префабе CombatHudWc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudReadyGem : MonoBehaviour
    {
        public Image Gem;
        [Tooltip("Рисунки камня по числу усилений: 0…8 (сборщик ставит 4 материала по ступеням)")]
        public Sprite[] Grades = new Sprite[0];
        [Tooltip("Насечки усилений слева направо; горят первые N")] public Image[] Notches = new Image[0];
        [Tooltip("Ряд насечек целиком: виден только при наведении на плитку")] public CanvasGroup NotchGroup;
        [Tooltip("Секунд на появление и угасание ряда")] public float NotchFade = .12f;
        [Tooltip("Цвет горящей насечки по материалу: сталь, серебро, золото, кристалл")]
        public Color[] TierColours =
        {
            new Color(.78f, .8f, .86f, 1f), new Color(.93f, .96f, 1f, 1f), new Color(1f, .8f, .38f, 1f), new Color(1f, .96f, .82f, 1f),
        };
        [Tooltip("Негорящая насечка")] public Color NotchOff = new Color(.08f, .09f, .12f, .9f);
        [Tooltip("Необязательно: сияние камня (аддитивное); дышит на восьми усилениях и вспыхивает на новом")]
        public Image Halo;
        [Tooltip("Необязательно: волна света по плитке в момент готовности (аддитивная)")] public Image Burst;
        [Tooltip("Необязательно: рамка плитки — коротко светлеет в момент готовности")] public Image Frame;
        public Color ReadyColour = new Color(1f, .9f, .72f, 1f);
        [Tooltip("Камень на перезарядке")] public Color IdleColour = new Color(.62f, .62f, .66f, .55f);
        public float FlashTime = .5f;

        bool _known, _ready, _hover;
        int _count = -1;
        float _flashAt = -100f, _haloFlashUntil;
        Color _frameColour;

        void Awake()
        {
            if (Frame != null) _frameColour = Frame.color;
            if (Burst != null) Burst.enabled = false;
        }

        public static int TierOf(int count) => count >= 8 ? 3 : count >= 6 ? 2 : count >= 3 ? 1 : 0;

        /// <summary>Сколько усилений у способности (0…8): насечки и материал камня.</summary>
        public void SetUpgrades(int count)
        {
            count = Mathf.Clamp(count, 0, Mathf.Max(Notches.Length, Grades.Length - 1));
            if (count == _count) return;
            bool grew = _count >= 0 && count > _count;
            int from = Mathf.Max(0, _count);
            _count = count;
            if (Gem != null && Grades != null && count < Grades.Length && Grades[count] != null) Gem.sprite = Grades[count];
            Color lit = TierColours.Length > 0 ? TierColours[Mathf.Min(TierOf(count), TierColours.Length - 1)] : Color.white;
            for (int i = 0; i < Notches.Length; i++)
                if (Notches[i] != null) Notches[i].color = i < count ? lit : NotchOff;
            if (!grew) return;
            for (int i = from; i < count && i < Notches.Length; i++) HudFx.Punch(Notches[i].transform, 2.1f, .45f);
            if (Gem != null) HudFx.Punch(Gem.transform, 1.35f, .4f);
            if (Halo != null)
            {
                _haloFlashUntil = Time.unscaledTime + .7f;
                HudFx.Flash(Halo, .9f, .7f);
            }
        }

        /// <summary>Мышь над плиткой — показать ряд насечек.</summary>
        public void SetHover(bool hover) => _hover = hover;

        public void SetReady(bool ready)
        {
            if (_known && ready && !_ready)
            {
                _flashAt = Time.unscaledTime;
                if (Burst != null) HudFx.Burst(Burst, .85f, .8f, 1.35f, .5f);
            }
            _known = true;
            _ready = ready;
        }

        void LateUpdate()
        {
            if (Gem != null)
            {
                // Рисованный камень несёт свой материал — готовый не красится, только тускнеет в откате.
                bool painted = Grades != null && Grades.Length > 0;
                Gem.color = _ready ? (painted ? Color.white : ReadyColour) : IdleColour;
            }
            // Кристалл (8 усилений) тихо дышит светом; вспышку нового усиления ведёт HudFx.
            bool flashing = Time.unscaledTime < _haloFlashUntil;
            if (Halo != null && _count >= 8 && !flashing)
            {
                Halo.enabled = true;
                float breath = .5f + .5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 2.6f);
                HudFx.SetAlpha(Halo, (_ready ? .32f : .14f) + .18f * breath);
            }
            else if (Halo != null && _count < 8 && Halo.enabled && !flashing) Halo.enabled = false;
            float flash = 1f - Mathf.Clamp01((Time.unscaledTime - _flashAt) / Mathf.Max(.05f, FlashTime));
            if (Frame != null) Frame.color = Color.Lerp(_frameColour, ReadyColour, flash * .8f);
            if (NotchGroup != null)
                NotchGroup.alpha = Mathf.MoveTowards(NotchGroup.alpha, _hover ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.02f, NotchFade));
        }

        /// <summary>Кадр редактора: число усилений, готовность и наведение без анимаций.</summary>
        public void Preview(int count, bool ready, bool hover = false)
        {
            _hover = hover;
            if (NotchGroup != null) NotchGroup.alpha = hover ? 1f : 0f;
            _count = -1;
            SetUpgrades(count);
            _ready = ready;
            if (Gem != null) Gem.color = ready ? Color.white : IdleColour;
            if (Halo != null)
            {
                Halo.enabled = count >= 8;
                HudFx.SetAlpha(Halo, ready ? .42f : .2f);
            }
        }
    }
}
