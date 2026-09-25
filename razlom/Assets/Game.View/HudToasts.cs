using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Всплывашки над портретом — концепт 2Б (владелец, 25 сентября): тёмная «пилюля» с серебряной
    /// кромкой, слева круг значка с подсветкой цвета события (редкость вещи, медь золота, заказ
    /// жителя), справа имя и строка. Новая встаёт снизу и выезжает слева, старые поднимаются;
    /// каждая живёт несколько секунд и гаснет. Одинаковые подряд (золото) складываются в одну.
    /// Часы свои, шаг не больше 0,1 с за кадр: кадр сборки арены длится секунды.
    /// </summary>
    public sealed class HudToasts : MonoBehaviour
    {
        [Tooltip("Образец всплывашки: «Круг/Кольцо», «Круг/Свет», «Круг/Значок», «Имя», «Строка»")]
        public RectTransform Template;
        [Tooltip("Сколько видно разом")] public int MaxShown = 4;
        [Tooltip("Шаг столбика по высоте")] public float Pitch = 54f;
        [Tooltip("Сколько секунд держится")] public float Life = 3.4f;
        public float InTime = .25f, OutTime = .45f;
        [Tooltip("Откуда выезжает, по X")] public float SlideFrom = -40f;

        sealed class Toast
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public ThemeColor Ring, Light;
            public RawImage Icon;
            public TMP_Text Title, Line;
            public float Age;
            public string Key;
            public int Amount;
            public Vector2 TitleRest;
        }

        readonly List<Toast> _shown = new List<Toast>();
        readonly Stack<Toast> _free = new Stack<Toast>();
        float _lastNow = -1f;

        /// <summary>
        /// Новая всплывашка. <paramref name="mergeKey"/> — одинаковые подряд складываются:
        /// «+45 золота» и «+30 золота» за пару секунд становятся «+75 золота».
        /// </summary>
        public void Push(Texture icon, UiTheme.Role ring, string title, string line, UiTheme.Role lineRole,
            string mergeKey = null, int amount = 0, System.Func<int, string> titleFor = null)
        {
            if (Template == null) return;
            if (mergeKey != null && _shown.Count > 0)
            {
                Toast last = _shown[_shown.Count - 1];
                if (last.Key == mergeKey && last.Age < Life - OutTime)
                {
                    last.Amount += amount;
                    if (titleFor != null) last.Title.text = titleFor(last.Amount);
                    last.Age = Mathf.Min(last.Age, InTime);
                    HudFx.Punch(last.Root, 1.08f, .25f);
                    return;
                }
            }
            Toast toast = _free.Count > 0 ? _free.Pop() : Create();
            toast.Age = 0f;
            toast.Key = mergeKey;
            toast.Amount = amount;
            toast.Title.text = titleFor != null ? titleFor(amount) : title;
            toast.Line.text = line ?? string.Empty;
            bool hasLine = !string.IsNullOrEmpty(line);
            toast.Line.gameObject.SetActive(hasLine);
            // Без второй строки («+45 золота») имя встаёт по середине пилюли.
            toast.Title.rectTransform.anchoredPosition = toast.TitleRest + new Vector2(0f, hasLine ? 0f : -10f);
            var lineTint = toast.Line.GetComponent<ThemeColor>();
            if (lineTint != null) lineTint.SetRole(lineRole);
            if (toast.Ring != null) toast.Ring.SetRole(ring);
            if (toast.Light != null) toast.Light.SetRole(ring);
            toast.Icon.texture = icon;
            toast.Icon.enabled = icon != null;
            toast.Root.SetAsLastSibling();
            toast.Root.anchoredPosition = new Vector2(SlideFrom, 0f);
            toast.Group.alpha = 0f;
            toast.Root.gameObject.SetActive(true);
            _shown.Add(toast);
            while (_shown.Count > MaxShown) Release(0);
        }

        Toast Create()
        {
            RectTransform root = Instantiate(Template, Template.parent);
            root.name = "Всплывашка";
            var toast = new Toast
            {
                Root = root,
                Group = root.GetComponent<CanvasGroup>(),
                Ring = root.Find("Круг/Кольцо")?.GetComponent<ThemeColor>(),
                Light = root.Find("Круг/Свет")?.GetComponent<ThemeColor>(),
                Icon = root.Find("Круг/Значок").GetComponent<RawImage>(),
                Title = root.Find("Имя").GetComponent<TMP_Text>(),
                Line = root.Find("Строка").GetComponent<TMP_Text>(),
            };
            toast.TitleRest = toast.Title.rectTransform.anchoredPosition;
            if (toast.Group == null) toast.Group = root.gameObject.AddComponent<CanvasGroup>();
            return toast;
        }

        void Release(int index)
        {
            Toast toast = _shown[index];
            _shown.RemoveAt(index);
            toast.Root.gameObject.SetActive(false);
            _free.Push(toast);
        }

        void LateUpdate()
        {
            float now = UiMotion.Now;
            float step = _lastNow < 0f ? 0f : Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            for (int i = _shown.Count - 1; i >= 0; i--)
            {
                Toast toast = _shown[i];
                toast.Age += step;
                if (toast.Age >= Life) { Release(i); continue; }
            }
            // Снизу вверх: последняя — у портрета; старые поднимаются мягко, без рывка.
            for (int i = 0; i < _shown.Count; i++)
            {
                Toast toast = _shown[i];
                float target = (_shown.Count - 1 - i) * Pitch;
                float y = Mathf.Lerp(toast.Root.anchoredPosition.y, target, 1f - Mathf.Exp(-step * 14f));
                float enter = Mathf.Clamp01(toast.Age / InTime);
                float x = Mathf.LerpUnclamped(SlideFrom, 0f, UiMotion.EaseOut.Evaluate(enter));
                toast.Root.anchoredPosition = new Vector2(x, y);
                float fadeOut = Mathf.Clamp01((Life - toast.Age) / OutTime);
                toast.Group.alpha = enter * fadeOut;
            }
        }

        /// <summary>Кадр редактора: всплывашки на месте, без анимации.</summary>
        public void Preview(params (Texture icon, UiTheme.Role ring, string title, string line, UiTheme.Role lineRole)[] items)
        {
            foreach (var item in items) Push(item.icon, item.ring, item.title, item.line, item.lineRole);
            for (int i = 0; i < _shown.Count; i++)
            {
                Toast toast = _shown[i];
                toast.Age = InTime;
                toast.Root.anchoredPosition = new Vector2(0f, (_shown.Count - 1 - i) * Pitch);
                toast.Group.alpha = 1f;
            }
        }
    }
}
