using System;

namespace Game.View
{
    /// <summary>
    /// Кривые отклика на удар по герою (этап 4, п. 4; выбор владельца 30.09 — кадр 1a доски
    /// ART/UI/concepts-2026-09-30-hud-polish): мягкая красная кромка экрана со стороны удара,
    /// виньетка при низком здоровье и микростоп на сильных ударах. Без UnityEngine — их проверяют
    /// тесты вне Unity (tools/Combat.Presentation.Tests/HeroHitFeedbackTests.cs). Время — секунды
    /// интерфейса (<see cref="UiMotion.Now"/>), экран — пиксели с осью y вверх, как у WorldToScreenPoint.
    /// Кромку мазком туши из 1b владелец не взял («похожа на кровь»): здесь только мягкий свет.
    /// </summary>
    public static class HeroHitFeedbackCurves
    {
        public const int EdgeLeft = 0, EdgeRight = 1, EdgeBottom = 2, EdgeTop = 3, EdgeCount = 4;

        const float Tau = (float)(Math.PI * 2.0);

        /// <summary>
        /// Вес края <paramref name="edge"/> для удара, пришедшего с экранного направления
        /// (<paramref name="dx"/>, <paramref name="dy"/>) — от героя к бьющему: косинус к наружной нормали
        /// края в степени <paramref name="sharpness"/>. Удар ровно слева — горит только левый край, по
        /// диагонали — два соседних поровну, с обратной стороны — ноль. Нулевое направление — ноль всем.
        /// </summary>
        public static float EdgeWeight(float dx, float dy, int edge, float sharpness = 1.5f)
        {
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (!(length > 1e-4f)) return 0f;
            dx /= length;
            dy /= length;
            float cos = edge switch
            {
                EdgeLeft => -dx,
                EdgeRight => dx,
                EdgeBottom => -dy,
                EdgeTop => dy,
                _ => 0f,
            };
            if (cos <= 0f) return 0f;
            return (float)Math.Pow(cos, Math.Max(.1f, sharpness));
        }

        /// <summary>
        /// Где на краю <paramref name="edge"/> загорается кромка, 0..1 вдоль края (у левого и правого —
        /// снизу вверх, у нижнего и верхнего — слева направо): точка, где луч от героя
        /// (<paramref name="hx"/>, <paramref name="hy"/> — доли экрана) в сторону бьющего пересекает
        /// линию края. Луч к этому краю не идёт — место героя по этой оси. <paramref name="aspect"/> —
        /// ширина экрана / высота: 16:9 и 16:10 дают разные точки при одном угле.
        /// </summary>
        public static float EdgeAlong(float hx, float hy, float dx, float dy, float aspect, int edge)
        {
            aspect = Math.Max(.1f, aspect);
            // Считается в единицах высоты экрана: x — 0..aspect, y — 0..1, направление в пикселях
            // уже в этих единицах (оба деления на высоту сокращаются).
            float x = hx * aspect, y = hy;
            switch (edge)
            {
                case EdgeLeft:
                case EdgeRight:
                {
                    float wall = edge == EdgeLeft ? 0f : aspect;
                    float toward = wall - x;
                    if (Math.Abs(dx) < 1e-5f || toward * dx <= 0f) return Clamp01(hy);
                    return Clamp01(y + dy * (toward / dx));
                }
                case EdgeBottom:
                case EdgeTop:
                {
                    float wall = edge == EdgeBottom ? 0f : 1f;
                    float toward = wall - y;
                    if (Math.Abs(dy) < 1e-5f || toward * dy <= 0f) return Clamp01(hx);
                    return Clamp01((x + dx * (toward / dy)) / aspect);
                }
                default:
                    return .5f;
            }
        }

        /// <summary>
        /// Сила кромки 0..1 по доле здоровья, которую снял удар: даже мелкий удар виден
        /// (<paramref name="min"/>), удар в <paramref name="full"/> здоровья и больше — полная.
        /// Урона нет или здоровья нет — ноль.
        /// </summary>
        public static float HitStrength(int amount, int maxHealth, float min = .4f, float full = .2f)
        {
            if (amount <= 0 || maxHealth <= 0) return 0f;
            float share = amount / (float)maxHealth;
            return Clamp01(min + (1f - min) * share / Math.Max(1e-4f, full));
        }

        /// <summary>
        /// Огибающая вспышки кромки 0..1: мягко вверх за <paramref name="rise"/>, полная
        /// <paramref name="hold"/>, гаснет за <paramref name="fade"/>. До удара и после — ноль.
        /// </summary>
        public static float Flash(float age, float rise, float hold, float fade)
        {
            if (!(age >= 0f)) return 0f;
            if (age < rise) return Smooth(age / rise);
            age -= Math.Max(0f, rise);
            if (age <= hold) return 1f;
            age -= Math.Max(0f, hold);
            if (!(fade > 0f) || age >= fade) return 0f;
            return 1f - Smooth(age / fade);
        }

        /// <summary>
        /// Новый удар в край, который ещё светит от прошлого: ярче из двух пиков, и яркость не
        /// проваливается — огибающая продолжается с нынешнего уровня (возраст ставится на подъёме
        /// там, где она уже есть). Возвращает новый пик и возраст.
        /// </summary>
        public static void Retrigger(float peak, float age, float strength, float rise, float hold, float fade,
            out float newPeak, out float newAge)
        {
            float current = Math.Max(0f, peak) * Flash(age, rise, hold, fade);
            newPeak = Math.Max(strength, current);
            if (!(newPeak > 0f)) { newAge = 0f; return; }
            // Где на подъёме уже стоит нынешняя яркость: Smooth(k) = current / newPeak.
            newAge = Math.Max(0f, rise) * InverseSmooth(current / newPeak);
        }

        /// <summary>
        /// Вес виньетки низкого здоровья 0..1: ноль при доле здоровья <paramref name="start"/> и выше,
        /// полная при <paramref name="full"/> и ниже, между — мягко. Мёртвый (доля ≤ 0) — ноль: смерть
        /// показывает RunEndBeat.
        /// </summary>
        public static float LowHealth(float ratio, float start = .3f, float full = .1f)
        {
            if (!(ratio > 0f) || ratio >= start) return 0f;
            if (ratio <= full) return 1f;
            return Smooth((start - ratio) / Math.Max(1e-4f, start - full));
        }

        /// <summary>
        /// Вдох виньетки 0..1 — вдвое медленнее красной дымки портрета (HudPulse, период
        /// <paramref name="portraitPeriod"/>, вдох 0,5 + 0,5·sin(t·2π/период)) и с тех же часов:
        /// каждый вдох виньетки совпадает с каждым вторым вдохом дымки, два ритма не спорят.
        /// </summary>
        public static float VignetteBreath(float now, float portraitPeriod)
        {
            float period = Math.Max(.1f, portraitPeriod);
            // Пики дымки — в t = период/4 + k·период; у виньетки — в t = период/4 + 2k·период.
            return .5f + .5f * (float)Math.Cos((now - period * .25f) * Tau / (2f * period));
        }

        /// <summary>
        /// Микростоп на ударе по герою: удар снял не меньше <paramref name="share"/> здоровья или
        /// героя оглушили (разбег Камнекопыта), и прошлый стоп был не раньше
        /// <paramref name="cooldown"/> назад — толпа, бьющая чаще, не держит кадр застывшим
        /// (та же беда, что описана в CombatJuiceView: «ВХОДЯЩИЙ УДАР НЕ ОСТАНАВЛИВАЕТ ВРЕМЯ»).
        /// </summary>
        public static bool MicroStop(int amount, int maxHealth, bool stun, float sinceLast, float share = .15f, float cooldown = .6f)
        {
            if (!(sinceLast >= cooldown)) return false;
            if (stun) return true;
            return amount > 0 && maxHealth > 0 && amount >= share * maxHealth;
        }

        /// <summary>Вес к цели: вверх за <paramref name="rise"/> секунд, вниз за <paramref name="fall"/>.</summary>
        public static float Approach(float current, float target, float dt, float rise, float fall)
        {
            if (!(dt > 0f)) return current;
            float time = target > current ? rise : fall;
            if (!(time > 0f)) return target;
            float step = dt / time;
            return target > current ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }

        internal static float Smooth(float k)
        {
            k = Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        /// <summary>Обратная к Smooth на 0..1 (бисекция: вызывается раз на удар, не в кадре).</summary>
        internal static float InverseSmooth(float value)
        {
            value = Clamp01(value);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * .5f;
                if (Smooth(mid) < value) lo = mid; else hi = mid;
            }
            return (lo + hi) * .5f;
        }

        internal static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>Состояние следа урона одной полоски: доля здоровья, доля следа и пауза до стекания.</summary>
    public struct BarTrailState
    {
        public float Value;
        public float Trail;
        public float Hold;
        public bool Ready;
    }

    /// <summary>
    /// След урона на полосках врагов и элиты (этап 4, п. 1; кадр 1a): светлый участок держит прежнее
    /// здоровье, через паузу стекает к новому — как след полосы героя (HudBarAnim) и босса
    /// (WcBar.Trail). Лечение следа не даёт. Без UnityEngine — проверяется тестами вне Unity.
    /// </summary>
    public static class HealthBarTrail
    {
        /// <summary>
        /// Шаг следа за <paramref name="dt"/>: новая потеря — след держит значение до удара и паузу
        /// <paramref name="delay"/> заново, потом стекает к здоровью со скоростью <paramref name="speed"/>
        /// долей полоски в секунду. Лечение поднимает здоровье поверх следа — след не растёт вверх.
        /// Первый шаг (полоска ещё не видела эту сущность) следа не рисует.
        /// </summary>
        public static void Step(ref BarTrailState s, float value, float dt, float delay, float speed)
        {
            value = HeroHitFeedbackCurves.Clamp01(value);
            if (!s.Ready)
            {
                s.Value = s.Trail = value;
                s.Hold = 0f;
                s.Ready = true;
                return;
            }
            if (value < s.Value - 1e-5f)
            {
                // След держит то, что было до удара (или ещё выше, если прошлый след не стёк).
                if (s.Trail < s.Value) s.Trail = s.Value;
                s.Hold = Math.Max(0f, delay);
            }
            s.Value = value;
            if (s.Hold > 0f) s.Hold = Math.Max(0f, s.Hold - Math.Max(0f, dt));
            else if (s.Trail > value) s.Trail = Math.Max(value, s.Trail - Math.Max(0f, speed) * Math.Max(0f, dt));
            if (s.Trail < value) s.Trail = value;
        }

        /// <summary>След виден: он длиннее заливки хоть на пятисотую полоски.</summary>
        public static bool Visible(in BarTrailState s) => s.Ready && s.Trail - s.Value > .002f;
    }

    /// <summary>
    /// Правила цифр урона по кадру 1a («42 · 37 · крит 126 · слитое 211 · по герою −38»): когда
    /// попадание вливается в висящую цифру, насколько слитая цифра выросла и потеплела, как цифры
    /// одной цели расходятся лесенкой, появление и толчок. Без UnityEngine — проверяется тестами.
    /// </summary>
    public static class DamageNumberRules
    {
        /// <summary>
        /// Вливается ли попадание в висящую цифру цели: после прошлого попадания прошло меньше окна.
        /// Крит всегда встаёт своей цифрой — он должен читаться отдельно, а не растворяться в сумме.
        /// </summary>
        public static bool Merges(float sinceHit, float window, bool crit)
            => !crit && sinceHit >= 0f && sinceHit < window;

        /// <summary>
        /// Во сколько раз слитая цифра крупнее одиночной после <paramref name="hits"/> попаданий:
        /// каждое следующее прибавляет <paramref name="step"/>, не больше <paramref name="max"/>.
        /// </summary>
        public static float MergedScale(int hits, float step = .14f, float max = 1.6f)
        {
            if (hits <= 1) return 1f;
            return Math.Min(Math.Max(1f, max), 1f + step * (hits - 1));
        }

        /// <summary>Насколько слитая цифра потеплела к акценту: 0 у одиночной, не больше 0,45.</summary>
        public static float MergedWarmth(int hits, float step = .12f, float max = .45f)
        {
            if (hits <= 1) return 0f;
            return Math.Min(max, step * (hits - 1));
        }

        /// <summary>
        /// Лесенка цифр одной цели, чтобы не ложились друг на друга: <paramref name="index"/>-я живая
        /// цифра цели встаёт на ступень выше и чуть в сторону (попеременно вправо и влево). Лесенка
        /// короткая: после <paramref name="steps"/> ступеней начинается снова снизу. Сдвиги — в долях
        /// <paramref name="step"/>: y — вверх по экрану, x — вправо.
        /// </summary>
        public static void Stack(int index, float step, out float x, out float y, int steps = 3)
        {
            if (steps < 1) steps = 1;
            int k = index <= 0 ? 0 : index % (steps + 1);
            y = k * step;
            x = k == 0 ? 0f : (k % 2 == 1 ? .35f : -.35f) * step;
        }

        /// <summary>
        /// Появление: масштаб из нуля с перелётом (EaseOutBack). <paramref name="overshoot"/> — сила
        /// перелёта: 1,70158 — обычная цифра (пик ≈ 1,1), 3,2 — крит с коротким «хлопком» (пик ≈ 1,28).
        /// </summary>
        public static float Pop(float age, float duration, float overshoot = 1.70158f)
        {
            if (!(duration > 0f) || age >= duration) return 1f;
            if (!(age > 0f)) return 0f;
            float t = age / duration;
            float c3 = overshoot + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + overshoot * u * u;
        }

        /// <summary>Толчок слитой цифры от нового попадания: +<paramref name="amount"/> сразу и назад к нулю за <paramref name="duration"/>.</summary>
        public static float Bump(float age, float duration, float amount = .22f)
        {
            if (!(age >= 0f) || !(duration > 0f) || age >= duration) return 0f;
            float k = 1f - age / duration;
            return amount * k * k;
        }

        /// <summary>Прозрачность по остатку жизни: полная, пока остаток не меньше <paramref name="fade"/>, потом мягко в ноль.</summary>
        public static float Fade(float remaining, float fade)
        {
            if (!(remaining > 0f)) return 0f;
            if (!(fade > 0f) || remaining >= fade) return 1f;
            return HeroHitFeedbackCurves.Smooth(remaining / fade);
        }
    }
}
