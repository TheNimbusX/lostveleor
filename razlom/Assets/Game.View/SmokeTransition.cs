using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Дымная завеса перехода (владелец 26 сентября: «переход между лагерем и забегом бы тоже сделать уже
    /// в стиле такой дымки, как комбат-худ, — красиво анимировано»). Лагерь ↔ разлом, «Повторить» на
    /// итогах, выход из паузы в лагерь и смена арены: вместо белой вспышки глубокий дым растекается из
    /// центра и накрывает экран целиком, под клубами — сплошная основа (у дыма своё дыхание и просветы,
    /// сквозь них мир мелькал бы во время сборки арены). Потом дым расходится от центра к краям.
    ///
    /// Первый кадр в игре (владелец 26 сентября, «успокоить огонь»): накат читался ржавчиной — тлеющая
    /// кромка основы шла по всему экрану мелкой рыжей сыпью, а в закрытом экране висело красное «яйцо»
    /// отсвета. Теперь завеса — тёмный сланцевый дым: кромки не горят, фронт широкий и мягкий, в глубине
    /// едва заметная холодная кремовая дымка, углей мало, а клубы, пока завеса на экране, медленно плывут —
    /// закрытый экран не стоит картинкой.
    ///
    /// Префаб Resources/UI/Prefabs/SmokeTransition.prefab собирает SmokeTransitionBuilder. Сама завеса
    /// времени не ведёт: <see cref="CampTransition"/> своими часами двигает <see cref="SetCover"/> (накат
    /// 0 → 1) и <see cref="SetOpen"/> (рассеивание 0 → 1) — на склейке арены кадр длится секунды, и
    /// «закрыто целиком» должен решать один хозяин.
    ///
    /// В покое холст выключен: полноэкранный дым не рисуется ни одного лишнего кадра.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SmokeTransition : MonoBehaviour
    {
        /// <summary>Клуб дыма завесы и его место в очереди.</summary>
        [Serializable]
        public struct Puff
        {
            public UiInkReveal Ink;
            [Tooltip("Очередь: 0 — встаёт первым и первым уходит (центр), 1 — последним (край экрана)")]
            [Range(0f, 1f)] public float Order;
            [Tooltip("Откуда клуб растекается на накате, доли клуба: сторона к центру экрана")]
            public Vector2 Inner;
            [Tooltip("Куда клуб отступает на рассеивании, доли клуба: сторона к краю экрана. Не угол — " +
                     "от угла фронт не успевает закрыть дальний угол целиком")]
            public Vector2 Outer;
            [Tooltip("Направление от центра экрана, единичное: клуб наплывает с него и уплывает по нему")]
            public Vector2 Away;
        }

        public Canvas Canvas;
        [Tooltip("Ловит мышь, пока завеса на экране: клик сквозь дым не должен выбрать карточку или кнопку")]
        public GraphicRaycaster Raycaster;
        [Tooltip("Сплошная основа (UiInkPlain, SmokeDeep): закрывает мир без просветов")]
        public UiInkReveal Base;
        public Puff[] Puffs = new Puff[0];
        [Tooltip("Бледная дымка света в глубине завесы (свет): холодная кремовая, едва заметная")]
        public UiInkReveal Glow;
        public UiEmbers Embers;

        [Header("Накат")]
        [Tooltip("Доля наката, за которую клубы встают лесенкой от центра к краям")]
        [Range(0f, .8f)] public float Spread = .4f;
        [Tooltip("Когда начинает растекаться основа, доля наката: клубы ведут, основа догоняет под ними")]
        [Range(0f, .6f)] public float BaseStart = .2f;
        [Tooltip("Доля проявления основы, при которой её неровный фронт уже дошёл до углов экрана. Основа растекается " +
                 "до неё весь накат, а не закрывает экран на двух третях и дальше стоит")]
        [Range(.5f, 1f)] public float BaseReach = .75f;
        [Tooltip("Тлеющая кромка основы на накате. 0: кромка основы идёт по всему экрану, и даже слабая " +
                 "читалась ржавой сыпью (владелец 26 сентября)")]
        [Range(0f, 1f)] public float BaseBurn = 0f;
        [Tooltip("Тлеющая кромка клубов на накате. 0: завеса — дым, а не огонь")]
        [Range(0f, 1f)] public float PuffBurn = 0f;
        [Tooltip("Насколько клубы наплывают с краёв и уплывают к краям, единиц холста")]
        public float Drift = 70f;
        [Tooltip("Дымка в глубине, сила света. Не больше .08: ярче — снова светящееся пятно посреди экрана " +
                 "(красное «яйцо» владелец отверг)")]
        [Range(0f, .08f)] public float GlowStrength = .04f;

        [Header("Течение")]
        [Tooltip("Насколько клубы бродят, пока завеса на экране, единиц холста")]
        public float Wander = 34f;
        [Tooltip("Скорость блуждания клубов, оборотов в секунду: у каждого клуба своя, слои дыма сдвигаются друг " +
                 "относительно друга")]
        public float WanderSpeed = .07f;

        [Header("Рассеивание")]
        [Tooltip("Доля рассеивания, за которую гаснет сплошная основа; дальше мир виден сквозь уходящие клубы")]
        [Range(.1f, 1f)] public float BaseFade = .5f;

        [Header("Угли")]
        [Tooltip("Искр в секунду, пока завеса закрыта: редкие, дым главный")] public float EmberRate = 1.5f;
        [Tooltip("Искр разом в миг полного закрытия")] public int EmberBurst = 4;

        Graphic _base, _glow;
        Graphic[] _puffGraphics = new Graphic[0];
        Vector2[] _home = new Vector2[0];
        float[] _offset = new float[0];
        bool _remembered, _active, _burst;
        int _warmFrames;
        float _wander, _wanderLast = -1f;

        /// <summary>Завеса закрыла экран целиком: основа проявлена до конца.</summary>
        public bool Covered => _active && (Base == null || Base.Hidden <= 0f);

        void Awake()
        {
            Remember();
            UiInkClock.Ensure();
            if (Canvas == null) Canvas = GetComponent<Canvas>();
            // Каналы uv1/uv2: без них шейдер «Дыма и света» не знает, что элемент ещё скрыт.
            if (Canvas != null)
                Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            Hide();
        }

        void Remember()
        {
            if (_remembered) return;
            _remembered = true;
            _base = Base != null ? Base.GetComponent<Graphic>() : null;
            _glow = Glow != null ? Glow.GetComponent<Graphic>() : null;
            _puffGraphics = new Graphic[Puffs.Length];
            _home = new Vector2[Puffs.Length];
            _offset = new float[Puffs.Length];
            for (int i = 0; i < Puffs.Length; i++)
            {
                if (Puffs[i].Ink == null) continue;
                _puffGraphics[i] = Puffs[i].Ink.GetComponent<Graphic>();
                _home[i] = ((RectTransform)Puffs[i].Ink.transform).anchoredPosition;
            }
        }

        /// <summary>
        /// Прогрев при входе в лагерь: пару кадров холст рисует полностью скрытую завесу, чтобы первый
        /// показ шейдера и сетки не лёг на сам накат.
        /// </summary>
        public void Warm()
        {
            if (_active) return;
            Remember();
            SetAll(1f);
            SetCanvas(true, false);
            _warmFrames = 2;
        }

        void LateUpdate()
        {
            if (_warmFrames > 0 && --_warmFrames == 0 && !_active) SetCanvas(false, false);
            if (!_active || Wander <= 0f) return;
            // Часы блуждания — свои, шаг не больше .1 с: кадр сборки арены длится секунды, клубы не должны
            // перепрыгнуть. Накат и рассеивание по-прежнему ведёт CampTransition, здесь только течение.
            float now = UiMotion.Now;
            _wander += _wanderLast < 0f ? 0f : Mathf.Clamp(now - _wanderLast, 0f, .1f);
            _wanderLast = now;
            for (int i = 0; i < Puffs.Length; i++) Place(i, i < _offset.Length ? _offset[i] : 0f);
        }

        /// <summary>Начало наката: всё скрыто, чернила готовы растекаться от центра.</summary>
        public void BeginCover()
        {
            Remember();
            _active = true;
            _burst = false;
            _warmFrames = 0;
            _wander = 0f;
            _wanderLast = -1f;
            if (Base != null) { Base.Origin = new Vector2(.5f, .5f); Base.Burn = BaseBurn; }
            // Дымка проявляется без кромки: огненное кольцо по свету читалось бы вспышкой.
            if (Glow != null) Glow.Burn = 0f;
            for (int i = 0; i < Puffs.Length; i++)
            {
                if (Puffs[i].Ink == null) continue;
                Puffs[i].Ink.Origin = Puffs[i].Inner;
                Puffs[i].Ink.Burn = PuffBurn;
                Alpha(_puffGraphics[i], 1f);
            }
            Alpha(_base, 1f);
            Alpha(_glow, GlowStrength > 0f ? 1f : 0f);
            if (Embers != null) { Embers.Rate = 0f; Alpha(Embers, 1f); }
            SetAll(1f);
            Dirty();
            SetCanvas(true, true);
            SetCover(0f);
        }

        /// <summary>
        /// Накат, <paramref name="k"/> 0..1. При 1 экран закрыт целиком (у всех «скрыто» ровно 0,
        /// основа непрозрачна).
        /// </summary>
        public void SetCover(float k)
        {
            k = Mathf.Clamp01(k);
            float length = 1f - Spread;
            for (int i = 0; i < Puffs.Length; i++)
            {
                if (Puffs[i].Ink == null) continue;
                float p = Smooth(Phase(k, Puffs[i].Order * Spread, length));
                Puffs[i].Ink.Hidden = 1f - p;
                // Клуб наплывает с края к своему месту, пока растекается.
                Place(i, Drift * .6f * (1f - p));
            }
            // Шейдер закрывает весь элемент задолго до «скрыто 0» (фронт шире шума): растягиваем
            // накат основы до BaseReach, а ровный ноль ставим в самом конце — там он уже не виден.
            if (Base != null) Base.Hidden = k >= 1f ? 0f : 1f - BaseReach * Smooth(Phase(k, BaseStart, 1f - BaseStart));
            if (Glow != null) Glow.Hidden = 1f - Smooth(Phase(k, .45f, .55f));
            if (Embers != null) Embers.Rate = EmberRate * k;
            if (k >= 1f && !_burst)
            {
                _burst = true;
                if (Embers != null) Embers.Burst(EmberBurst);
            }
        }

        /// <summary>
        /// Начало рассеивания: клубы отступают к краям экрана (своя сторона у края), без тлеющей кромки —
        /// огонь на уходе читался бы вспышкой.
        /// </summary>
        public void BeginOpen()
        {
            Remember();
            if (Base != null) Base.Burn = 0f;
            if (Glow != null) Glow.Burn = 0f;
            for (int i = 0; i < Puffs.Length; i++)
            {
                if (Puffs[i].Ink == null) continue;
                Puffs[i].Ink.Origin = Puffs[i].Outer;
                Puffs[i].Ink.Burn = 0f;
            }
            Dirty();
        }

        /// <summary>
        /// Рассеивание, <paramref name="k"/> 0..1: основа гаснет первой, клубы расходятся от центра к краям
        /// и уплывают. При 1 не видно ничего — дальше <see cref="Hide"/>.
        /// </summary>
        public void SetOpen(float k)
        {
            k = Mathf.Clamp01(k);
            float length = 1f - Spread;
            for (int i = 0; i < Puffs.Length; i++)
            {
                if (Puffs[i].Ink == null) continue;
                float p = Smooth(Phase(k, Puffs[i].Order * Spread, length));
                Puffs[i].Ink.Hidden = p;
                Alpha(_puffGraphics[i], 1f - p * p);
                Place(i, Drift * p);
            }
            Alpha(_base, 1f - Smooth(Mathf.Clamp01(k / BaseFade)));
            Alpha(_glow, 1f - Smooth(Mathf.Clamp01(k / (BaseFade * .8f))));
            if (Embers != null)
            {
                Embers.Rate = 0f;
                Alpha(Embers, 1f - Smooth(k));
            }
        }

        /// <summary>Всё скрыто, холст выключен, клубы на своих местах.</summary>
        public void Hide()
        {
            Remember();
            _active = false;
            _warmFrames = 0;
            _wander = 0f;
            _wanderLast = -1f;
            SetAll(1f);
            for (int i = 0; i < Puffs.Length; i++) Place(i, 0f);
            if (Embers != null) Embers.Rate = 0f;
            SetCanvas(false, false);
        }

        void SetAll(float hidden)
        {
            if (Base != null) Base.Hidden = hidden;
            if (Glow != null) Glow.Hidden = hidden;
            foreach (Puff puff in Puffs)
                if (puff.Ink != null) puff.Ink.Hidden = hidden;
        }

        /// <summary>Начало и кромку UiInkReveal пишет в сетку: без пересборки новые значения ждали бы первой смены «скрыто».</summary>
        void Dirty()
        {
            if (_base != null) _base.SetVerticesDirty();
            if (_glow != null) _glow.SetVerticesDirty();
            foreach (Graphic graphic in _puffGraphics)
                if (graphic != null) graphic.SetVerticesDirty();
        }

        void SetCanvas(bool shown, bool blocking)
        {
            if (Canvas != null) Canvas.enabled = shown;
            if (Raycaster != null) Raycaster.enabled = shown && blocking;
        }

        void Place(int i, float offset)
        {
            if (Puffs[i].Ink == null || i >= _home.Length) return;
            if (i < _offset.Length) _offset[i] = offset;
            ((RectTransform)Puffs[i].Ink.transform).anchoredPosition = _home[i] + Puffs[i].Away * offset + Wandering(i);
        }

        /// <summary>
        /// Сдвиг клуба течением: медленная вытянутая петля, по горизонтали шире — дым стелется, а не кружит.
        /// У каждого клуба своя скорость и фаза; в начале наката сдвиг ровно ноль (клуб на своём месте).
        /// </summary>
        Vector2 Wandering(int i)
        {
            if (Wander <= 0f || _wander <= 0f) return Vector2.zero;
            float speed = WanderSpeed * (.75f + .5f * Mathf.Repeat(i * .618f, 1f)) * 2f * Mathf.PI;
            float phase = i * 1.9f;
            float a = _wander * speed;
            float x = Mathf.Sin(a + phase) - Mathf.Sin(phase);
            float y = Mathf.Sin(a * .6f + phase * 1.3f) - Mathf.Sin(phase * 1.3f);
            return new Vector2(x, y * .45f) * Wander;
        }

        void Alpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;
            // Потолок и для старого префаба (там сила .16 — красное «яйцо»).
            if (graphic == _glow) alpha *= Mathf.Min(GlowStrength, .08f);
            graphic.canvasRenderer.SetAlpha(Mathf.Clamp01(alpha));
        }

        static float Phase(float k, float start, float length) => Mathf.Clamp01((k - start) / Mathf.Max(length, .0001f));

        static float Smooth(float k) => k * k * (3f - 2f * k);
    }
}
