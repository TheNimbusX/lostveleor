using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Строка эффектов героя ТОЛЬКО НАД портретом (этап 4, выбор владельца 30.09 — кадр 1a):
    /// корни, оглушение, защита от контроля, замедление, зелья, артефакт, Blaze. Порядок постоянный
    /// (<see cref="HudEffectKind"/>), у каждого вида свой круг (<see cref="Chips"/>), лишние уходят
    /// в круг «+N». Новый круг раздвигает соседей, ушедший — сдвигает обратно (веса
    /// <see cref="HudEffectRowMath"/>), поэтому строка перетекает плавно.
    ///
    /// Угрозы (корни, оглушение, замедление) — приглушённо-красное кольцо, защита от контроля и
    /// «Ясный настой» — холодное, помощь — кремово-золотое. Подсказка при наведении — имя, одна
    /// строка и секунды (Nunito, малая подложка кита без кромки); мышь строка не ловит — клик
    /// проходит в игру. Что показывать, решает CombatHudView (CombatHudView.Effects).
    /// Раскладка и вид — в префабе CombatHudWc (миграция v2), здесь только смысл и движение.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudEffectRow : MonoBehaviour
    {
        [Tooltip("Круги по видам эффектов, в порядке HudEffectKind")]
        public HudEffectChip[] Chips = new HudEffectChip[HudEffectList.KindCount];
        [Tooltip("Круг «+N»: эффекты, что не поместились в строку")] public HudEffectChip More;
        [Tooltip("Значки по видам эффектов, в порядке HudEffectKind (у артефакта — запасной, если нет картинки артефакта)")]
        public Texture[] Icons = new Texture[HudEffectList.KindCount];
        [Tooltip("Шаг кругов в строке, единицы холста")] public float Pitch = 52f;
        [Tooltip("Сколько кругов помещается, считая «+N»")] [Min(1)] public int MaxVisible = 7;
        [Tooltip("За сколько секунд новый круг раздвигает соседей")] public float GrowTime = .2f;
        [Tooltip("За сколько секунд соседи съезжаются на место ушедшего")] public float ShrinkTime = .24f;
        [Tooltip("Появление: клуб дыма и «выпрыгивание»")] public float PopTime = .45f;
        [Tooltip("Уход: вспышка и угасание")] public float LeaveTime = .55f;
        [Tooltip("До какой доли ухода круг держит место (дальше соседи съезжаются)")] [Range(0f, 1f)] public float LeaveHold = .6f;

        [Header("Кольцо-таймер по тону")]
        public Color ThreatRing = new Color(.78f, .36f, .33f, 1f);
        public Color GuardRing = new Color(.62f, .8f, .86f, 1f);
        public Color BoonRing = new Color(1f, .84f, .56f, 1f);

        [Header("Свет появления по тону (аддитивный)")]
        public Color ThreatGlow = new Color(1f, .38f, .3f, 1f);
        public Color GuardGlow = new Color(.62f, .86f, 1f, 1f);
        public Color BoonGlow = new Color(1f, .8f, .5f, 1f);

        [Header("Подсказка")]
        public RectTransform Tooltip;
        public TMP_Text TooltipText;
        [Tooltip("Отступ подсказки над кругом, единицы холста")] public float TooltipGap = 10f;

        readonly float[] _weights = new float[HudEffectList.KindCount + 1];
        readonly float[] _xs = new float[HudEffectList.KindCount + 1];
        readonly HudEffectChip[] _slots = new HudEffectChip[HudEffectList.KindCount + 1];
        static readonly Vector3[] Buffer = new Vector3[4];
        float _last = -1f;

        void Awake()
        {
            HideAll();
        }

        /// <summary>Все круги и подсказку — сразу, без движения (новая симуляция).</summary>
        internal void HideAll()
        {
            foreach (HudEffectChip chip in Chips)
                if (chip != null) chip.Hide();
            if (More != null) More.Hide();
            HideTooltip();
            _last = -1f;
        }

        /// <summary>Значок вида: у артефакта — картинка включённого артефакта (null — запасной камень).</summary>
        internal void SetIcon(HudEffectKind kind, Texture texture)
        {
            int i = (int)kind;
            HudEffectChip chip = (uint)i < (uint)Chips.Length ? Chips[i] : null;
            if (chip == null || chip.Icon == null) return;
            Texture icon = texture != null ? texture : (uint)i < (uint)Icons.Length ? Icons[i] : null;
            if (chip.Icon.texture != icon) chip.Icon.texture = icon;
        }

        Color RingOf(HudEffectTone tone) => tone == HudEffectTone.Threat ? ThreatRing : tone == HudEffectTone.Guard ? GuardRing : BoonRing;
        Color GlowOf(HudEffectTone tone) => tone == HudEffectTone.Threat ? ThreatGlow : tone == HudEffectTone.Guard ? GuardGlow : BoonGlow;

        /// <summary>Кадр строки: кто появился, обновился, ушёл; кольца, секунды, места и движение.</summary>
        internal void Apply(HudEffectList list, float now)
        {
            float dt = _last < 0f ? 0f : Mathf.Clamp(now - _last, 0f, .1f);
            _last = now;
            int count = 0;
            for (int i = 0; i < HudEffectList.KindCount && i < Chips.Length; i++)
            {
                HudEffectChip chip = Chips[i];
                if (chip == null) continue;
                var kind = (HudEffectKind)i;
                bool shown = list.Shown(kind);
                if (shown && !chip.Live)
                {
                    HudEffectTone tone = HudEffectList.ToneOf(kind);
                    chip.Enter(now, RingOf(tone), GlowOf(tone));
                }
                else if (shown && list.Refreshed(kind)) chip.Punch(now);
                else if (!shown && chip.Live) chip.Leave(now, list.Ended(kind));
                if (chip.Live) chip.SetState(list.Fill(kind), list.Seconds(kind), list.Stacks(kind));
                Track(chip, now, dt, ref count);
            }
            if (More != null)
            {
                bool shown = list.Overflow > 0;
                if (shown && !More.Live) More.Enter(now, BoonRing, BoonGlow);
                else if (!shown && More.Live) More.Leave(now, false);
                if (More.Live) More.SetCount(list.Overflow);
                Track(More, now, dt, ref count);
            }

            HudEffectRowMath.Place(_weights, count, Pitch, _xs);
            for (int i = 0; i < count; i++)
            {
                HudEffectChip chip = _slots[i];
                RectTransform rect = chip.Rect;
                var place = new Vector2(_xs[i] + Pitch * .5f, 0f);
                if (rect.anchoredPosition != place) rect.anchoredPosition = place;
            }
        }

        /// <summary>Вес места круга, движение и выключение отжившего; живые и уходящие — в раскладку.</summary>
        void Track(HudEffectChip chip, float now, float dt, ref int count)
        {
            bool hold = chip.Live || chip.Leaving && chip.LeaveProgress(now, LeaveTime) < LeaveHold;
            chip.Weight = HudEffectRowMath.StepWeight(chip.Weight, hold, dt, GrowTime, ShrinkTime);
            // Первый кадр после скрытия: вес с нуля растёт на следующем шаге — место под круг уже есть.
            if (chip.Live && dt <= 0f) chip.Weight = 1f;
            bool needed = chip.gameObject.activeSelf && chip.Animate(now, PopTime, LeaveTime);
            if (!needed)
            {
                if (chip.gameObject.activeSelf) chip.Hide();
                return;
            }
            _weights[count] = chip.Weight;
            _slots[count] = chip;
            count++;
        }

        /// <summary>
        /// Круг под точкой экрана (снизу слева): номер вида (HudEffectKind), <see cref="HudEffectList.KindCount"/>
        /// — круг «+N», −1 — ни одного. Только живые круги: гаснущий подсказку не открывает.
        /// </summary>
        internal int HitChip(Vector2 screen)
        {
            for (int i = 0; i < Chips.Length; i++)
                if (Hit(Chips[i], screen)) return i;
            return Hit(More, screen) ? HudEffectList.KindCount : -1;
        }

        static bool Hit(HudEffectChip chip, Vector2 screen)
            => chip != null && chip.Live && chip.gameObject.activeInHierarchy
               && RectTransformUtility.RectangleContainsScreenPoint(chip.Rect, screen, null);

        internal RectTransform ChipRect(int index)
        {
            HudEffectChip chip = index == HudEffectList.KindCount ? More : (uint)index < (uint)Chips.Length ? Chips[index] : null;
            return chip != null ? chip.Rect : null;
        }

        /// <summary>
        /// Подсказка над кругом <paramref name="index"/>. <paramref name="text"/> — null, если текст
        /// не менялся (раскладка не пересчитывается). Подсказка встаёт низом над кругом, по X прижата
        /// к экрану; <paramref name="scale"/> — масштаб холста.
        /// </summary>
        internal void ShowTooltip(int index, string text, float scale)
        {
            RectTransform chip = ChipRect(index);
            if (Tooltip == null || chip == null) return;
            bool opened = !Tooltip.gameObject.activeSelf;
            if (opened)
            {
                // Поверх всего HUD — только в миг появления: смена порядка перестраивает холст.
                Tooltip.SetAsLastSibling();
                Tooltip.gameObject.SetActive(true);
            }
            if (text != null && TooltipText != null) TooltipText.text = text;
            if (text != null || opened) LayoutRebuilder.ForceRebuildLayoutImmediate(Tooltip);
            chip.GetWorldCorners(Buffer);
            float centre = (Buffer[0].x + Buffer[2].x) * .5f, top = Buffer[1].y;
            float half = Tooltip.rect.width * .5f * scale;
            float x = Mathf.Clamp(centre, half + 16f * scale, Screen.width - half - 16f * scale);
            Tooltip.pivot = new Vector2(.5f, 0f);
            Tooltip.position = new Vector3(x, top + TooltipGap * scale, 0f);
        }

        internal void HideTooltip()
        {
            if (Tooltip != null && Tooltip.gameObject.activeSelf) Tooltip.gameObject.SetActive(false);
        }
    }
}
