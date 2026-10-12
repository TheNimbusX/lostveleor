namespace Game.Sim
{
    /// <summary>
    /// Бросок якоря: только чтение для вида (контракт AGENTS/specs/anchor-throw-view-contract.md).
    /// Ничего не пишет, в хеш не идёт; те же формулы, что ведут Sim (голова — AnchorThrowHead,
    /// путь тела — кривая Водоворота MaelstromPullProgress), чтобы вид не держал копий правил.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Чем задет враг в этом броске: −1 — не задет; 0 — железный якорь; 1 — призрак Веера +25°;
        /// 2 — призрак −25°; 3 — сеть Невода. Живёт от попадания до следующего каста (как
        /// AnchorThrowLandingSpot) — вид может собрать «кого зацепило» и без событий.
        /// </summary>
        public int AnchorThrowHitLane(int id)
        {
            if (_throwHit == null || (uint)id >= (uint)_throwHit.Length || _throwHit[id] == 0) return -1;
            return (_throwHit[id] & ~ThrowHitPull) - 1;
        }

        /// <summary>Задетый помечен «на тягу» (лёгкий; у Гарпуна — любой с весом > 0, кроме босса).</summary>
        public bool AnchorThrowHitPulls(int id)
            => _throwHit != null && (uint)id < (uint)_throwHit.Length && (_throwHit[id] & ThrowHitPull) != 0;

        /// <summary>
        /// Список «на крюке» этого броска по возрастанию id — без событий (вид включился посреди
        /// броска, съёмка, HUD): все задетые или только помеченные «на тягу» (pulledOnly). Пишет
        /// в ids не больше ids.Length штук (ids == null — только счёт) и возвращает, сколько их
        /// всего. Живёт от попадания до следующего каста, как AnchorThrowHitLane; кто доехал
        /// и кто ещё в тяге — AnchorThrowReeled. Без аллокаций.
        /// </summary>
        public int AnchorThrowHooks(int[] ids, bool pulledOnly = false)
        {
            if (_throwHit == null) return 0;
            int last = System.Math.Min(Entities.Count, _throwHit.Length), count = 0;
            for (int i = PlayerId + 1; i < last; i++)
            {
                byte hit = _throwHit[i];
                if (hit == 0 || pulledOnly && (hit & ThrowHitPull) == 0) continue;
                if (ids != null && count < ids.Length) ids[count] = i;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Начало пути тела в натяг (где оно стояло, когда тяга началась). Имеет смысл для тех,
        /// кто попал на полукольцо (AnchorThrowReeled в натяг…возврат); живёт до следующего каста.
        /// </summary>
        public FixVec2 AnchorThrowPullFrom(int id)
            => _throwPullFrom != null && (uint)id < (uint)_throwPullFrom.Length ? _throwPullFrom[id] : FixVec2.Zero;

        /// <summary>Длина пути тела по плану (от AnchorThrowPullFrom до AnchorThrowLandingSpot), м.</summary>
        public Fix64 AnchorThrowPullLength(int id)
            => _throwPullLength != null && (uint)id < (uint)_throwPullLength.Length ? _throwPullLength[id] : Fix64.Zero;

        /// <summary>
        /// Плановая точка тела на тике tick (конец шага тика): from + (spot − from) · P(tick − натяг, R),
        /// P — MaelstromPullProgress; весь натяг (до рывка TautTick включительно) — from, с ловли — spot. Та же кривая, что у головы
        /// на возврате. Настоящая позиция — Entities.Position: стена может задержать тело, догон
        /// (×1,35 плана тика) его потом подтягивает.
        /// </summary>
        public static FixVec2 AnchorThrowPullAt(in AnchorThrowState s, FixVec2 from, FixVec2 spot, int tick)
        {
            if (s.Serial == 0 || tick <= s.TautTick) return from;
            if (tick >= s.CatchTick) return spot;
            return from + (spot - from) * MaelstromPullProgress(tick - s.TautTick, s.ReturnTicks);
        }

        /// <summary>
        /// Углы сети Невода на полу (по оси главной полосы, без тела врага — попадание считает
        /// ещё + радиус тела): ближний край — рука (Center + Dir · 0,5), дальний — конец главной
        /// полосы (Center + Dir · Reach0); left — +90° от Dir (против часовой). False — сети нет
        /// (не Невод или бросок в стену). Сеть ловит один раз, в тик натяга (AnchorThrowYank).
        /// </summary>
        public static bool AnchorThrowNetCorners(in AnchorThrowState s, out FixVec2 nearLeft, out FixVec2 nearRight,
            out FixVec2 farLeft, out FixVec2 farRight)
        {
            nearLeft = nearRight = farLeft = farRight = FixVec2.Zero;
            if (!s.HasNet) return false;
            var side = new FixVec2(-s.Dir.Y, s.Dir.X) * s.NetHalfWidth;
            FixVec2 near = s.Center + s.Dir * AbordageHandReach, far = s.Center + s.Dir * s.Reach0;
            nearLeft = near + side;
            nearRight = near - side;
            farLeft = far + side;
            farRight = far - side;
            return true;
        }
    }
}
