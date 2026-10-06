using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Печать «Дыма и света» палатки (06.10): клятва на доске, место в ряду слотов, артефакт атласа, большая печать карточки.
    /// Круглый рисунок в тёмном кольце (эталон — плитка способности боевого HUD): покой — тихое кремовое кольцо; наведение —
    /// тонкая тлеющая кромка (UiHoverMotion сам); куплено — огонь light_ring тлеет; в силе — горит; выбрано рукой — толстое
    /// кольцо акцента. Собирает CampTentWcBuilder (части CampInkParts.IconMedallion); что показать — решает CampInventoryView.
    /// Отдельный файл обязателен: компонент стоит в префабе.
    /// </summary>
    public sealed class CampOathSeal : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Вид печати.</summary>
        public enum Look : byte
        {
            /// <summary>Не куплена — тёмный диск, знак приглушён.</summary>
            Dim = 0,
            /// <summary>Не куплена и не по карману — ещё тусклее.</summary>
            Faint = 1,
            /// <summary>Куплена, выключена; найденный артефакт без огня — знак в полную силу, огонь тлеет.</summary>
            Owned = 2,
            /// <summary>В силе; найденный артефакт — огонь горит.</summary>
            Active = 3,
            /// <summary>Неизвестный артефакт — тёмный силуэт рисунка.</summary>
            Unknown = 4,
            /// <summary>Пустое место (открытый слот без клятвы, место акта II) — только тихое кольцо.</summary>
            Empty = 5,
        }

        [Tooltip("Номер: клятва (OathId − 1), слот или место атласа — его читает окно")] public int Index;
        public Image Disc;
        [Tooltip("Рисунок под круглой маской: белая маска знака (краска темы) или расписная картинка")] public RawImage Art;
        [Tooltip("Тихое кремовое кольцо покоя")] public Image Ring;
        [Tooltip("Огненное кольцо light_ring: тлеет у купленной, горит у клятвы в силе")] public Image Fire;
        [Tooltip("Толстое кольцо акцента: выбрано рукой")] public Image Picked;
        [Tooltip("Слабое сияние цвета группы внутри диска")] public Image Glow;
        [Tooltip("Закрытый слот: тень, замок, подпись «Ур. N»")] public GameObject Locked;
        public TMP_Text LockLabel;
        [Tooltip("Цена под печатью (число) и значок пепла рядом")] public TMP_Text Price;
        public RawImage PriceIcon;
        [Tooltip("Ступени клятвы героя: огоньки на нижней дуге кольца")] public Image[] Pips = new Image[0];
        [Tooltip("«?» неизвестного артефакта")] public TMP_Text Question;
        [Tooltip("Подпись, видная только при наведении («Акт II»)")] public TMP_Text HoverCaption;

        [Header("Сила света")]
        [Range(0f, 1f)] public float FireOwned = .32f;
        [Range(0f, 1f)] public float FireActive = 1f;
        [Range(0f, 1f)] public float ArtDim = .45f;
        [Range(0f, 1f)] public float ArtFaint = .28f;
        [Range(0f, 1f)] public float RingRest = .45f;
        [Range(0f, 1f)] public float RingDim = .28f;
        [Tooltip("Силуэт неизвестного артефакта: фигура читается, рисунок — нет")] public Color Silhouette = new Color(.16f, .18f, .22f, 1f);

        /// <summary>Наведение (true — вошла мышь).</summary>
        public Action<CampOathSeal, bool> Hovered;
        /// <summary>Клик: false — левая кнопка, true — правая.</summary>
        public Action<CampOathSeal, bool> Clicked;

        Look _look = Look.Dim;
        bool _hovered;

        public Look Current => _look;

        /// <summary>Картинка печати; null прячет рисунок (пустая RawImage рисует белый квадрат).</summary>
        public void SetArt(Texture texture)
        {
            if (Art == null) return;
            Art.texture = texture;
            Art.enabled = texture != null;
        }

        /// <summary>Вид печати и выбор рукой.</summary>
        public void Show(Look look, bool selected)
        {
            _look = look;
            switch (look)
            {
                case Look.Dim: Paint(Art, ArtDim); break;
                case Look.Faint: Paint(Art, ArtFaint); break;
                case Look.Unknown:
                    // Белая маска — тусклее; расписной рисунок — тёмным силуэтом (фигура читается, рисунок — нет).
                    if (Art != null && Art.GetComponent<ThemeColor>() == null) Art.color = Silhouette;
                    else Paint(Art, ArtFaint);
                    break;
                default: Paint(Art, 1f); break;
            }
            if (Art != null && look == Look.Empty) Art.enabled = false;
            bool quiet = look == Look.Dim || look == Look.Faint || look == Look.Unknown || look == Look.Empty;
            Paint(Ring, quiet ? RingDim : RingRest);
            if (Fire != null)
            {
                bool lit = look == Look.Owned || look == Look.Active;
                Fire.gameObject.SetActive(lit);
                if (lit) Fire.color = new Color(1f, 1f, 1f, look == Look.Active ? FireActive : FireOwned);
            }
            if (Picked != null) Picked.gameObject.SetActive(selected);
            if (Glow != null) Glow.enabled = look != Look.Empty && look != Look.Unknown;
            RefreshQuestion();
        }

        /// <summary>«?» — у неизвестного и пустого места атласа; при наведении его сменяет подпись («Акт II»), если она есть.</summary>
        void RefreshQuestion()
        {
            bool caption = _hovered && HoverCaption != null && HoverCaption.text.Length > 0;
            if (HoverCaption != null) HoverCaption.gameObject.SetActive(caption);
            if (Question != null) Question.gameObject.SetActive((_look == Look.Unknown || _look == Look.Empty) && !caption);
        }

        /// <summary>Яркость рисунка поверх вида (большая печать карточки не тускнеет вместе с печатью доски).</summary>
        public void SetArtAlpha(float alpha) => Paint(Art, alpha);

        /// <summary>Ступени клятвы героя: max огоньков, первые rank горят. max ≤ 1 — без огоньков.</summary>
        public void SetRank(int rank, int max)
        {
            if (Pips == null) return;
            for (int i = 0; i < Pips.Length; i++)
            {
                if (Pips[i] == null) continue;
                bool shown = max > 1 && i < max;
                Pips[i].gameObject.SetActive(shown);
                if (shown) Pips[i].color = new Color(1f, 1f, 1f, i < rank ? 1f : .22f);
            }
        }

        /// <summary>Цена под печатью; null — без цены (клятва на потолке). Не по карману — красным.</summary>
        public void SetPrice(string text, bool affordable)
        {
            if (Price == null) return;
            Transform row = Price.transform.parent;
            bool shown = !string.IsNullOrEmpty(text);
            if (row != null && row != transform) row.gameObject.SetActive(shown);
            else Price.gameObject.SetActive(shown);
            if (!shown) return;
            Price.text = text;
            var tint = Price.GetComponent<ThemeColor>();
            if (tint != null) tint.SetRole(affordable ? UiTheme.Role.TextMuted : UiTheme.Role.Bad, 1f);
        }

        /// <summary>Закрытый слот: замок и подпись («Ур. 12»).</summary>
        public void SetLocked(bool locked, string caption)
        {
            if (Locked != null) Locked.SetActive(locked);
            if (LockLabel != null) LockLabel.text = locked ? caption ?? "" : "";
        }

        /// <summary>Подпись при наведении (место акта II); пусто — нет подписи.</summary>
        public void SetHoverCaption(string text)
        {
            if (HoverCaption == null) return;
            HoverCaption.text = text ?? "";
            RefreshQuestion();
        }

        /// <summary>Вспышка огня: клятва встала в слот или куплена ступень. Кольцо вспыхивает шире и оседает к своей силе.</summary>
        public void Flash()
        {
            if (Fire == null) return;
            Fire.gameObject.SetActive(true);
            float rest = _look == Look.Active ? FireActive : _look == Look.Owned ? FireOwned : 0f;
            RectTransform rect = Fire.rectTransform;
            UiMotion.Play(Fire, 52, .45f, t =>
            {
                if (Fire == null) return;
                Fire.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, rest, t));
                rect.localScale = Vector3.one * Mathf.Lerp(1.22f, 1f, t);
            }, UiMotion.EaseOut, () =>
            {
                if (Fire == null) return;
                rect.localScale = Vector3.one;
                if (rest <= 0f) Fire.gameObject.SetActive(false);
            });
        }

        /// <summary>
        /// Альфа через тему, если элемент крашен ролью (ThemeColor): прямую запись цвета следующая правка темы затёрла бы.
        /// Расписная картинка без роли — белая с этой альфой.
        /// </summary>
        static void Paint(Graphic graphic, float alpha)
        {
            if (graphic == null) return;
            var tint = graphic.GetComponent<ThemeColor>();
            if (tint != null) { tint.SetRole(tint.Role, alpha); return; }
            graphic.color = new Color(1f, 1f, 1f, alpha);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Middle) return;
            Clicked?.Invoke(this, eventData.button == PointerEventData.InputButton.Right);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            RefreshQuestion();
            Hovered?.Invoke(this, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            RefreshQuestion();
            Hovered?.Invoke(this, false);
        }

        // Без колбэка и без SetActive: печать выключается вместе со страницей или палаткой, а менять активность детей
        // посреди выключения Unity не даёт. Наведение окно сбрасывает само (смена страницы, открытие палатки).
        void OnDisable() => _hovered = false;
    }
}
