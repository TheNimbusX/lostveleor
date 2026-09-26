using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Переход лагерь ↔ разлом и между аренами (владелец 24 сентября: «вход в разлом сейчас мгновенный;
    /// нужен момент — камера подаётся к арке, вспышка, звук; то же при возвращении»; 26 сентября: «переход
    /// между лагерем и забегом бы тоже сделать уже в стиле такой дымки, как комбат-худ»).
    ///
    /// Вместо тёплой вспышки — дымная завеса <see cref="SmokeTransition"/>. Порядок у всех путей один:
    ///  1. накат: дым растекается от центра, мир стоит (<see cref="Busy"/>: ни ввода, ни тиков);
    ///  2. экран закрыт целиком — и ещё один кадр показан закрытым: кадр сборки арены длится секунды
    ///     и застывает на последнем показанном кадре, а в нём дым должен быть уже сплошным;
    ///  3. смена — из корутины, после Update: сборка идёт в LateUpdate этого же кадра, под завесой;
    ///  4. завеса держится хотя бы кадр после смены и пока камера не встанет;
    ///  5. рассеивание от центра к краям; с <see cref="ReleaseAt"/> мир снова живой.
    ///
    /// Пути: арка (<see cref="EnterRift"/>: сначала камера подаётся к арке, дым накатывает к концу подачи),
    /// итоги «Повторить» / «В лагерь» и пауза «В лагерь» (<see cref="Swap"/>), выбор следующей арены
    /// (<see cref="BetweenArenas"/>). Без префаба завесы — прежняя тёплая вспышка у арки и сразу у
    /// остальных. Съёмочные сценарии переходят сразу (<see cref="Bypass"/>). Время неигровое, шаг часов
    /// не больше 0,1 с.
    /// </summary>
    public sealed class CampTransition : MonoBehaviour
    {
        /// <summary>Переход держит мир: ввод в лагере закрыт, тики стоят (TickDriver).</summary>
        public static bool Busy { get; private set; }

        /// <summary>
        /// Завеса закрывает мир: ввод не собирается, пауза не открывается. Шире <see cref="Busy"/> на смене
        /// арены — там тики идут под завесой, чтобы команда маршрута дошла до симуляции.
        /// </summary>
        public static bool Covering { get; private set; }

        /// <summary>Идёт переход (от начала наката до конца рассеивания).</summary>
        public static bool Running => _instance != null && _instance._running;

        [Header("Подача к арке")]
        [Tooltip("Секунд на подачу камеры к арке")] public float PushTime = .95f;
        [Tooltip("Во сколько раз приближается камера у арки")] public float PushZoom = .7f;

        [Header("Дымная завеса")]
        [Tooltip("Секунд на накат дыма; у арки накат кончается вместе с подачей камеры")] public float RollTime = .6f;
        [Tooltip("Секунд держать завесу после смены, не меньше (кадр сборки арены — сверх этого)")] public float HoldTime = .15f;
        [Tooltip("Дольше не ждём под завесой, пока камера встанет, с (между аренами она едет со старого места)")]
        public float SettleMax = 1f;
        [Tooltip("Дольше не ждём новую арену после выбора маршрута, с")] public float ArriveMax = 3f;
        [Tooltip("Секунд на рассеивание")] public float OpenTime = .85f;
        [Tooltip("Доля рассеивания, после которой мир оживает: ввод, тики, пауза")]
        [Range(0f, 1f)] public float ReleaseAt = .45f;

        [Header("Возвращение в лагерь")]
        [Tooltip("Приближение камеры лагеря в миг смены: лагерь проявляется чуть ближе и отъезжает")] public float ReturnZoom = .82f;
        [Tooltip("Секунд на отъезд камеры лагеря к обычному плану")] public float ReturnTime = 1.2f;

        [Header("Запасная вспышка (префаба завесы нет)")]
        [Tooltip("Секунд на угасание вспышки в разломе")] public float FlashOutTime = .7f;
        public Color FlashColour = new Color(1f, .86f, .64f, 1f);

        const string SmokePrefab = "UI/Prefabs/SmokeTransition";

        /// <summary>
        /// Съёмка самого перехода: флаг -capture-smoke оставляет завесу и в съёмочных сценариях
        /// (иначе они переходят сразу — им нужен предсказуемый кадр).
        /// </summary>
        static readonly bool SmokeInCapture = Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-smoke") >= 0;

        /// <summary>Съёмочный сценарий: переход сразу, без камеры, завесы и вспышки.</summary>
        public static bool Bypass => !SmokeInCapture && (CampIntegrationCapture.IsRunning || CaptureRig.AutoEnterRift);

        static CampTransition _instance;
        Image _flash;
        SmokeTransition _smoke;
        bool _smokeLoaded, _running;
        float _lastNow;

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

        /// <summary>Завеса из префаба (создаётся один раз); null — префаб не собран.</summary>
        SmokeTransition Smoke
        {
            get
            {
                if (_smokeLoaded) return _smoke;
                _smokeLoaded = true;
                var prefab = Resources.Load<GameObject>(SmokePrefab);
                if (prefab == null) return null;
                var go = Instantiate(prefab);
                go.name = "Дымная завеса перехода";
                _smoke = go.GetComponent<SmokeTransition>();
                if (_smoke == null) Destroy(go);
                return _smoke;
            }
        }

        /// <summary>Завеса для этого перехода; null — съёмка или префаба нет.</summary>
        static SmokeTransition Veil
        {
            get
            {
                if (Bypass) return null;
                SmokeTransition smoke = Instance.Smoke;
                return smoke != null ? smoke : null;
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

        /// <summary>
        /// Завеса готова заранее (вход в лагерь): префаб создан и прогрет, первый показ шейдера
        /// и сетки не ложится на сам накат.
        /// </summary>
        public static void Prewarm()
        {
            SmokeTransition veil = Veil;
            if (veil != null) veil.Warm();
        }

        /// <summary>Вход в забег через арку. enter вызывается под завесой (без префаба — под вспышкой).</summary>
        public static void EnterRift(CampRiftEntrance arch, Vector3 archPosition, Action enter)
        {
            if (Busy || !Free()) return;
            SmokeTransition veil = Veil;
            Instance.StartCoroutine(Instance.Enter(arch, archPosition, enter, veil));
        }

        /// <summary>
        /// Смена под завесой: итоги «Повторить» / «В лагерь», пауза «В лагерь». <paramref name="swap"/>
        /// зовётся, когда экран закрыт целиком; <paramref name="toCamp"/> — лагерь проявляется с отъездом камеры.
        /// false — завесы нет (съёмка, префаб не собран): вызывающий меняет сам, как раньше.
        /// true — смена взята, или уже идёт другой переход и этот запрос лишний.
        /// </summary>
        public static bool Swap(Action swap, bool toCamp)
        {
            if (!Free()) return true;
            SmokeTransition veil = Veil;
            if (veil == null) return false;
            _instance.StartCoroutine(_instance.Cover(veil, () => { swap?.Invoke(); return true; }, null, toCamp));
            return true;
        }

        /// <summary>
        /// Смена арены под завесой. <paramref name="queue"/> ставит команду маршрута в обычный тик, когда
        /// экран закрыт (false — ставить уже нечего: завеса просто расходится); <paramref name="arrived"/> —
        /// новая арена в симуляции (сменилась глубина). Возврат — как у <see cref="Swap"/>.
        /// </summary>
        public static bool BetweenArenas(Func<bool> queue, Func<bool> arrived)
        {
            if (!Free()) return true;
            SmokeTransition veil = Veil;
            if (veil == null) return false;
            _instance.StartCoroutine(_instance.Cover(veil, queue, arrived ?? (() => true), false));
            return true;
        }

        /// <summary>
        /// Вернулись из забега мимо завесы (пути разработчика, съёмка звука): лагерь проявляется из тёплой
        /// пелены, как раньше. Смена под завесой рассеивается сама — тогда здесь ничего.
        /// </summary>
        public static void ReturnToCamp()
        {
            if (Running) return;
            Instance.StopAllCoroutines();
            Busy = false;
            Covering = false;
            Instance.StartCoroutine(Instance.Return());
        }

        /// <summary>
        /// Можно начинать новый переход. Пока завеса закрывает мир, второй запрос лишний; на хвосте
        /// рассеивания мир уже живой (пауза открыта, «В лагерь» нажато) — хвост обрывается, новый
        /// переход начинается с чистой завесы, а не глотает нажатие.
        /// </summary>
        static bool Free()
        {
            if (!Running) return true;
            if (Covering) return false;
            _instance.Interrupt();
            return true;
        }

        void Interrupt()
        {
            StopAllCoroutines();
            if (_smoke != null) _smoke.Hide();
            SetFlash(0f);
            CameraFollow.CampZoom = 1f;
            CameraFollow.CampFocus = Vector3.zero;
            Finish();
        }

        /// <summary>Шаг часов: время интерфейса, не больше 0,1 с — кадр сборки арены длится секунды.</summary>
        float Step()
        {
            float now = UiMotion.Now;
            float dt = Mathf.Clamp(now - _lastNow, 0f, .1f);
            _lastNow = now;
            return dt;
        }

        void Begin()
        {
            _running = true;
            Busy = true;
            Covering = true;
            _lastNow = UiMotion.Now;
        }

        void Finish()
        {
            _running = false;
            Busy = false;
            Covering = false;
        }

        /// <summary>Смена не должна оставить мир навсегда стоящим: исключение пишется в лог, переход доходит до конца.</summary>
        static bool Call(Func<bool> action)
        {
            try { return action == null || action(); }
            catch (Exception e) { Debug.LogException(e); return false; }
        }

        static bool Arrived(Func<bool> arrived)
        {
            try { return arrived(); }
            catch (Exception e) { Debug.LogException(e); return true; }
        }

        IEnumerator Enter(CampRiftEntrance arch, Vector3 archPosition, Action enter, SmokeTransition veil)
        {
            Begin();
            var hero = CampPlayerView.Instance;
            Vector3 toArch = hero != null ? archPosition - hero.Position : Vector3.zero;
            toArch.y = 0f;
            GameSound.Play("rift_awaken", .7f, .02f, 2f);
            bool whoosh = false, rolling = false;
            // Дым накатывает к концу подачи: камера успевает показать арку, потом её затягивает.
            float rollFrom = Mathf.Max(0f, PushTime - RollTime);
            float rollTime = Mathf.Max(PushTime - rollFrom, .01f);
            float t = 0f;
            while (t < PushTime)
            {
                t += Step();
                float k = Ease(t / PushTime);
                CameraFollow.CampZoom = Mathf.Lerp(1f, PushZoom, k);
                CameraFollow.CampFocus = toArch * .55f * k;
                if (arch != null) arch.Surge = k;
                if (veil != null)
                {
                    if (!rolling && t >= rollFrom) { rolling = true; veil.BeginCover(); }
                    if (rolling) veil.SetCover((t - rollFrom) / rollTime);
                }
                else SetFlash(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1f, t / PushTime)));
                if (!whoosh && t > PushTime * .35f) { whoosh = true; GameSound.Play("rift_whoosh", .8f); }
                if (t < PushTime) yield return null;
            }
            if (veil != null)
            {
                if (!rolling) veil.BeginCover();
                veil.SetCover(1f);
            }
            else SetFlash(1f);
            // Кадр сборки застывает на последнем показанном кадре: сначала показать экран закрытым целиком.
            yield return null;
            Call(() => { enter?.Invoke(); return true; });
            CameraFollow.CampZoom = 1f;
            CameraFollow.CampFocus = Vector3.zero;
            if (arch != null) arch.Surge = 0f;
            GameSound.Play("rift_portal", .9f);
            if (veil != null)
            {
                yield return Hold();
                yield return Open(veil, false);
            }
            else
            {
                t = 0f;
                while (t < FlashOutTime)
                {
                    // Кадр сборки арены длится секунды: по настоящему шагу вспышка гасла за один кадр.
                    t += Step();
                    SetFlash(1f - Ease(t / FlashOutTime));
                    yield return null;
                }
                SetFlash(0f);
            }
            Finish();
        }

        IEnumerator Cover(SmokeTransition veil, Func<bool> swap, Func<bool> arrived, bool toCamp)
        {
            Begin();
            bool arena = arrived != null;
            GameSound.Play("rift_whoosh", arena ? .55f : .75f);
            veil.BeginCover();
            float t = 0f;
            while (true)
            {
                t += Step();
                float k = t / Mathf.Max(RollTime, .01f);
                veil.SetCover(k);
                if (k >= 1f) break;
                yield return null;
            }
            // Кадр сборки застывает на последнем показанном кадре: сначала показать экран закрытым целиком.
            yield return null;
            bool swapped = Call(swap);
            if (!arena)
            {
                if (toCamp) CameraFollow.CampZoom = ReturnZoom;
                GameSound.Play("rift_portal", toCamp ? .45f : .9f);
            }
            else if (swapped)
            {
                // Команда маршрута идёт обычным тиком: тики пускаем, ввод — нет (Covering).
                Busy = false;
                float waited = 0f;
                while (!Arrived(arrived) && waited < ArriveMax)
                {
                    waited += Step();
                    yield return null;
                }
                // Новая арена уже в симуляции, её сборка — в LateUpdate этого кадра. Мир снова стоит,
                // пока завеса: иначе бой начался бы до того, как игрок его увидел.
                Busy = true;
                GameSound.Play("rift_portal", .6f);
            }
            yield return Hold();
            yield return Open(veil, toCamp);
            Finish();
        }

        /// <summary>
        /// Держим закрытым: кадр сборки уже прошёл под завесой; дальше не меньше <see cref="HoldTime"/> и
        /// пока камера не встанет (на смене арены CameraFollow не прыгает, а едет со старого места).
        /// </summary>
        IEnumerator Hold()
        {
            float held = 0f;
            int still = 0;
            Vector3 last = CameraPosition();
            do
            {
                yield return null;
                held += Step();
                Vector3 now = CameraPosition();
                still = (now - last).sqrMagnitude < .0004f ? still + 1 : 0;
                last = now;
            }
            while (held < HoldTime || (still < 2 && held < SettleMax));
        }

        static Vector3 CameraPosition()
        {
            Camera camera = Camera.main;
            return camera != null ? camera.transform.position : Vector3.zero;
        }

        IEnumerator Open(SmokeTransition veil, bool toCamp)
        {
            veil.BeginOpen();
            float length = Mathf.Max(OpenTime, toCamp ? ReturnTime : 0f);
            float t = 0f;
            bool released = false, hidden = false;
            while (true)
            {
                t += Step();
                float k = Mathf.Clamp01(t / Mathf.Max(OpenTime, .01f));
                if (!hidden)
                {
                    veil.SetOpen(k);
                    if (k >= 1f) { veil.Hide(); hidden = true; }
                }
                if (toCamp) CameraFollow.CampZoom = Mathf.Lerp(ReturnZoom, 1f, Ease(t / ReturnTime));
                if (!released && k >= ReleaseAt)
                {
                    // Дым ещё расходится, но мир уже виден: ввод и тики возвращаются, не дожидаясь краёв.
                    released = true;
                    Busy = false;
                    Covering = false;
                }
                if (t >= length) break;
                yield return null;
            }
            if (!hidden) veil.Hide();
            if (toCamp) CameraFollow.CampZoom = 1f;
        }

        IEnumerator Return()
        {
            GameSound.Play("rift_portal", .45f);
            float t = 0f;
            _lastNow = UiMotion.Now;
            while (t < ReturnTime)
            {
                t += Step();
                float k = Ease(t / ReturnTime);
                SetFlash(.85f * (1f - Ease(t / (ReturnTime * .7f))));
                CameraFollow.CampZoom = Mathf.Lerp(ReturnZoom, 1f, k);
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
            Covering = false;
            CameraFollow.CampZoom = 1f;
            CameraFollow.CampFocus = Vector3.zero;
            if (_smoke != null) Destroy(_smoke.gameObject);
        }
    }
}
