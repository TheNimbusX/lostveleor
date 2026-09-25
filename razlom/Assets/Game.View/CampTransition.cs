using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Переход лагерь ↔ разлом (владелец 24 сентября: «вход в разлом сейчас мгновенный; нужен
    /// момент — камера подаётся к арке, вспышка, звук; то же при возвращении»).
    ///
    /// Вход: камера за ~1 с подаётся к арке и чуть приближается (CameraFollow.CampZoom/CampFocus),
    /// арка разгорается (CampRiftEntrance.Surge), шорох разлома, тёплая вспышка на весь экран —
    /// под ней сессия уходит в забег, и вспышка гаснет уже в разломе.
    /// Возвращение: экран проявляется из тёплой пелены, камера мягко отъезжает к обычному плану.
    /// Пока идёт вход, ввод в лагере закрыт (CampPlayerView.InputBlocked). Время неигровое.
    /// </summary>
    public sealed class CampTransition : MonoBehaviour
    {
        public static bool Busy { get; private set; }

        [Tooltip("Секунд на подачу камеры к арке")] public float PushTime = .95f;
        [Tooltip("Во сколько раз приближается камера у арки")] public float PushZoom = .7f;
        [Tooltip("Секунд на угасание вспышки в разломе")] public float FlashOutTime = .7f;
        [Tooltip("Секунд на проявление лагеря при возвращении")] public float ReturnTime = 1.2f;
        public Color FlashColour = new Color(1f, .86f, .64f, 1f);

        static CampTransition _instance;
        Image _flash;

        static CampTransition Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Переход лагерь — разлом");
                _instance = go.AddComponent<CampTransition>();
                _instance.Build();
                return _instance;
            }
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Поверх всего, включая паузу: вспышку видно долю секунды.
            canvas.sortingOrder = 900;
            var flash = new GameObject("Вспышка", typeof(RectTransform));
            flash.transform.SetParent(transform, false);
            var rect = (RectTransform)flash.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _flash = flash.AddComponent<Image>();
            _flash.raycastTarget = false;
            SetFlash(0f);
        }

        void SetFlash(float alpha)
        {
            if (_flash == null) return;
            Color c = FlashColour;
            c.a = Mathf.Clamp01(alpha);
            _flash.color = c;
            _flash.enabled = c.a > .001f;
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Вход в забег через арку. enter вызывается под вспышкой.</summary>
        public static void EnterRift(CampRiftEntrance arch, Vector3 archPosition, Action enter)
        {
            if (Busy) return;
            Instance.StartCoroutine(Instance.Enter(arch, archPosition, enter));
        }

        /// <summary>Вернулись из забега: лагерь проявляется из тёплой пелены.</summary>
        public static void ReturnToCamp()
        {
            Instance.StopAllCoroutines();
            Busy = false;
            Instance.StartCoroutine(Instance.Return());
        }

        IEnumerator Enter(CampRiftEntrance arch, Vector3 archPosition, Action enter)
        {
            Busy = true;
            var hero = CampPlayerView.Instance;
            Vector3 toArch = hero != null ? archPosition - hero.Position : Vector3.zero;
            toArch.y = 0f;
            GameSound.Play("rift_awaken", .7f, .02f, 2f);
            bool whoosh = false;
            float t = 0f;
            while (t < PushTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Ease(t / PushTime);
                CameraFollow.CampZoom = Mathf.Lerp(1f, PushZoom, k);
                CameraFollow.CampFocus = toArch * .55f * k;
                if (arch != null) arch.Surge = k;
                SetFlash(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1f, t / PushTime)));
                if (!whoosh && t > PushTime * .35f) { whoosh = true; GameSound.Play("rift_whoosh", .8f); }
                yield return null;
            }
            SetFlash(1f);
            try { enter?.Invoke(); }
            finally
            {
                CameraFollow.CampZoom = 1f;
                CameraFollow.CampFocus = Vector3.zero;
                if (arch != null) arch.Surge = 0f;
            }
            GameSound.Play("rift_portal", .9f);
            t = 0f;
            while (t < FlashOutTime)
            {
                // Кадр сборки арены длится секунды: по настоящему шагу вспышка гасла за один кадр.
                t += Mathf.Min(Time.unscaledDeltaTime, .1f);
                SetFlash(1f - Ease(t / FlashOutTime));
                yield return null;
            }
            SetFlash(0f);
            Busy = false;
        }

        IEnumerator Return()
        {
            GameSound.Play("rift_portal", .45f);
            float t = 0f;
            while (t < ReturnTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Ease(t / ReturnTime);
                SetFlash(.85f * (1f - Ease(t / (ReturnTime * .7f))));
                CameraFollow.CampZoom = Mathf.Lerp(.82f, 1f, k);
                yield return null;
            }
            CameraFollow.CampZoom = 1f;
            SetFlash(0f);
        }

        void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;
            Busy = false;
            CameraFollow.CampZoom = 1f;
            CameraFollow.CampFocus = Vector3.zero;
        }
    }
}
