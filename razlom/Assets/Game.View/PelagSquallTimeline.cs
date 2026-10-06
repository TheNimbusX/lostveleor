using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Поза Шквала на тике показа: клип и кадр, взгляд корня и сдвиг тела от позиции Sim.</summary>
    public struct PelagSquallPose
    {
        public PelagSquallClip Clip;
        public float Frame;

        /// <summary>Рысканье корня, градусы плоскости Sim (atan2(y, x)), влево — плюс.</summary>
        public float Yaw;

        /// <summary>Сдвиг корня, м мира (X, мировая Z): лодыжка левой стопы стоит, пока корень крутится.</summary>
        public float ShiftX, ShiftY;

        /// <summary>Левая стопа стоит (замах, опора, финиш, посадка возврата).</summary>
        public bool Planted;

        /// <summary>Вход в этот клип смешиванием, а не стыком (поздний финиш).</summary>
        public bool Crossfade;

        /// <summary>Показ кончился: тело отдаётся стойке или бегу.</summary>
        public bool Finished;
    }

    /// <summary>Что идёт после удара — решает вид по снимку Sim в миг события удара.</summary>
    public enum PelagSquallNext : byte { Unknown = 0, Jump = 1, Return = 2, Finish = 3 }

    /// <summary>
    /// Лента одного каста Шквала для вида, без Unity. Собирается из событий Sim
    /// (SquallJump/Strike/Return/Ended, тик события = SimulationTick − 1), отвечает
    /// позой на любой тик показа. События приходят минимум за тик до того, как тик
    /// показа до них дойдёт (тело рисуется на тик позже Sim), поэтому лента
    /// знает прыжок до его первого кадра. Опоздавшее событие не рвёт позу: поворот
    /// перенацеливается с текущего угла (TurnTo).
    /// </summary>
    public sealed class PelagSquallTimeline
    {
        private struct Hop
        {
            public bool Known, Struck;
            public int Start, Flight, StrikeTick;
            public bool Backhand;
            public float Yaw, StrikeX, StrikeY;
            public PelagSquallNext Next;
            public float LateFrom;
        }

        private struct Turn { public float Start, Ticks, From, Delta; }

        public const int MaxHops = 16;
        private readonly Hop[] _hops = new Hop[MaxHops];
        private readonly Turn[] _turns = new Turn[48];
        private int _hopCount, _turnCount;
        private float _startYaw = float.NaN, _firstYaw, _castX, _castY;

        private bool _hasReturn;
        private int _returnStart, _returnTicks, _legCount;
        private readonly float[] _legYaw = new float[3], _legEnd = new float[3];

        private bool _ended;
        private int _endTick;
        private SquallEnd _endReason;

        public bool Active { get; private set; }
        public int Serial { get; private set; }
        public int CastTick { get; private set; }
        public int HopCount => _hopCount;
        public bool HasReturn => _hasReturn;
        public bool Ended => _ended;
        public SquallEnd EndReason => _endReason;
        public bool HasStartYaw => !float.IsNaN(_startYaw);

        /// <summary>Каст: замах с castTick, первая цель в направлении firstYaw (взгляд Sim при касте).</summary>
        public void Begin(int serial, int castTick, float castX, float castY, float firstYaw)
        {
            Active = true;
            Serial = serial;
            CastTick = castTick;
            _castX = castX;
            _castY = castY;
            _firstYaw = firstYaw;
            _startYaw = float.NaN;
            _hopCount = _turnCount = _legCount = 0;
            Array.Clear(_hops, 0, _hops.Length);
            _hasReturn = _ended = false;
        }

        public void Stop() => Active = false;

        /// <summary>Взгляд тела до каста (показанный в прошлом кадре): с него начинается поворот замаха.</summary>
        public void SetStartYaw(float yaw, float now)
        {
            if (!Active || HasStartYaw) return;
            _startYaw = yaw;
            TurnTo(CastTick, PelagSquallClipRules.LoadTurnTicks, PelagSquallClipRules.LoadTurnTicks + 1f, _firstYaw, 0, now);
        }

        public void Jump(int tick, int index, int flight, bool backhand, float landX, float landY, float fallbackYaw, float now)
        {
            if (!Active || index < 0 || index >= MaxHops || tick < CastTick) return;
            float fromX = _castX, fromY = _castY;
            if (index > 0 && _hops[index - 1].Struck) { fromX = _hops[index - 1].StrikeX; fromY = _hops[index - 1].StrikeY; }
            float dx = landX - fromX, dy = landY - fromY;
            bool fromKnown = index == 0 || _hops[index - 1].Struck;
            float yaw = fromKnown && dx * dx + dy * dy > 1e-6f ? PelagSquallClipRules.YawOf(dx, dy) : fallbackYaw;
            _hops[index] = new Hop
            {
                Known = true, Start = tick, Flight = Math.Max(2, flight), Backhand = backhand, Yaw = yaw, LateFrom = -1f,
            };
            if (index + 1 > _hopCount) _hopCount = index + 1;
            if (index == 0)
            {
                _firstYaw = yaw;
                if (HasStartYaw)
                    TurnTo(CastTick, PelagSquallClipRules.LoadTurnTicks, PelagSquallClipRules.LoadTurnTicks + 1f, yaw, 0, now);
                return;
            }
            _hops[index - 1].Next = PelagSquallNext.Jump;
            _hops[index - 1].LateFrom = -1f;
            TurnTo(StrikeTickOf(index - 1) + PelagSquallClipRules.TurnDelayTicks, PelagSquallClipRules.TurnTicksBase,
                Math.Max(PelagSquallClipRules.TurnTicksBase, _hops[index].Flight), yaw,
                PelagSquallClipRules.FollowThroughSign(_hops[index - 1].Backhand), now);
        }

        /// <summary>Удар прыжка index. next — что дальше по снимку Sim; predictedYaw — куда повернуть в опоре.</summary>
        public void Strike(int tick, int index, float x, float y, PelagSquallNext next, float predictedYaw, float now)
        {
            if (!Active || index < 0 || index >= MaxHops) return;
            ref Hop hop = ref _hops[index];
            if (!hop.Known)
            {
                // Прыжок без события (не должно быть): опора от удара, полёт минимальный.
                hop.Known = true; hop.Start = tick - 2; hop.Flight = 2; hop.LateFrom = -1f;
                hop.Backhand = index > 0 && !_hops[index - 1].Backhand;
                if (index + 1 > _hopCount) _hopCount = index + 1;
            }
            hop.Struck = true;
            hop.StrikeTick = tick;
            hop.StrikeX = x;
            hop.StrikeY = y;
            if (next != PelagSquallNext.Unknown) hop.Next = next;
            if (next == PelagSquallNext.Jump || next == PelagSquallNext.Return)
                TurnTo(tick + PelagSquallClipRules.TurnDelayTicks, PelagSquallClipRules.TurnTicksBase,
                    PelagSquallClipRules.TurnTicksBase + 1f, predictedYaw, PelagSquallClipRules.FollowThroughSign(hop.Backhand), now);
            // Узнали о финише, когда опора уже показывается: финиш догоняет смешиванием.
            if (next == PelagSquallNext.Finish && now > tick + .25f) hop.LateFrom = now;
        }

        /// <summary>
        /// Прыжок назад к точке каста: totalTicks — тиков полёта (событие), отрезки —
        /// взгляд и тики каждого (ReturnLegs). Тики отрезков подгоняются к totalTicks.
        /// </summary>
        public void Return(int tick, int totalTicks, int legCount, float[] legYaw, int[] legTicks, float now)
        {
            if (!Active || legCount <= 0) return;
            _hasReturn = true;
            _returnStart = tick;
            _returnTicks = Math.Max(2, totalTicks);
            _legCount = Math.Min(legCount, _legYaw.Length);
            int sum = 0;
            for (int i = 0; i < _legCount; i++) sum += Math.Max(1, legTicks[i]);
            float scale = sum > 0 ? (float)_returnTicks / sum : 1f;
            float acc = 0f;
            for (int i = 0; i < _legCount; i++)
            {
                acc += Math.Max(1, legTicks[i]) * scale;
                _legEnd[i] = acc;
                _legYaw[i] = legYaw[i];
            }
            int last = _hopCount - 1;
            int strike = last >= 0 ? StrikeTickOf(last) : tick - PelagSquallClipRules.SupportTicks;
            bool backhand = last >= 0 && _hops[last].Backhand;
            if (last >= 0) { _hops[last].Next = PelagSquallNext.Return; _hops[last].LateFrom = -1f; }
            TurnTo(strike + PelagSquallClipRules.TurnDelayTicks, PelagSquallClipRules.TurnTicksBase,
                Math.Max(PelagSquallClipRules.TurnTicksBase, Math.Min(_returnTicks, 4)), _legYaw[0],
                PelagSquallClipRules.FollowThroughSign(backhand), now);
        }

        /// <summary>Следующий прыжок сорвался уже после опоры (Sim ушла в удержание): финиш с кадра 2 смешиванием.</summary>
        public void LateFinish(float from)
        {
            int last = _hopCount - 1;
            if (!Active || last < 0 || _hasReturn) return;
            ref Hop hop = ref _hops[last];
            if (hop.LateFrom >= 0f || (hop.Next == PelagSquallNext.Finish && hop.LateFrom < 0f)) return;
            hop.Next = PelagSquallNext.Finish;
            hop.LateFrom = from;
        }

        public void End(int tick, SquallEnd reason)
        {
            if (!Active || _ended) return;
            _ended = true;
            _endTick = tick;
            _endReason = reason;
        }

        /// <summary>После последнего удара продолжения нет (ни прыжка, ни возврата, ни финиша).</summary>
        public bool AwaitingContinuation(out int strikeTick)
        {
            int last = _hopCount - 1;
            strikeTick = last >= 0 ? StrikeTickOf(last) : CastTick;
            return Active && last >= 0 && _hops[last].Struck && !_hasReturn
                   && _hops[last].Next != PelagSquallNext.Finish;
        }

        public PelagSquallPose Sample(float t, float scale)
        {
            var pose = new PelagSquallPose { Yaw = YawAt(t) + ReturnCornerOffset(t) };
            if (!Active) { pose.Finished = true; return pose; }
            int index = LastHopStartedBy(t);
            if (index < 0)
            {
                pose.Clip = PelagSquallClip.Load;
                pose.Frame = PelagSquallClipRules.LoadFrame(t - CastTick);
                pose.Planted = true;
                Shift(CastTick, t, scale, 1f, ref pose);
            }
            else SampleHop(index, t, scale, ref pose);
            if (_ended && t >= _endTick) pose.Finished = true;
            return pose;
        }

        private void SampleHop(int index, float t, float scale, ref PelagSquallPose pose)
        {
            Hop hop = _hops[index];
            float plantStart = index == 0 ? CastTick : StrikeTickOf(index - 1);
            float k = t - hop.Start;
            if (k <= hop.Flight)
            {
                pose.Clip = PelagSquallClipRules.StrikeClip(hop.Backhand);
                pose.Frame = PelagSquallClipRules.FlightFrame(hop.Flight, k);
                Shift(plantStart, hop.Start, scale, PelagSquallClipRules.ShiftDecay(k, hop.Flight), ref pose);
                return;
            }
            float contact = StrikeTickOf(index);
            float since = t - contact;
            pose.Planted = true;
            if (hop.Next == PelagSquallNext.Finish && hop.LateFrom < 0f)
            {
                pose.Clip = PelagSquallClipRules.FinishClip(hop.Backhand);
                pose.Frame = PelagSquallClipRules.FinishFrame(since);
                pose.Finished = since >= PelagSquallClipRules.FinishTicks;
                Shift(contact, t, scale, 1f, ref pose);
            }
            else if (hop.Next == PelagSquallNext.Return && _hasReturn && t >= _returnStart)
            {
                float rk = t - _returnStart;
                pose.Clip = PelagSquallClipRules.ReturnClip(hop.Backhand);
                pose.Frame = PelagSquallClipRules.ReturnFrame(_returnTicks, rk);
                pose.Planted = rk >= _returnTicks;
                if (rk <= _returnTicks) Shift(contact, _returnStart, scale, PelagSquallClipRules.ShiftDecay(rk, _returnTicks), ref pose);
                pose.Finished = rk >= _returnTicks + PelagSquallClipRules.ReturnRecoveryTicks;
            }
            else if (hop.LateFrom >= 0f && t >= hop.LateFrom)
            {
                pose.Clip = PelagSquallClipRules.FinishClip(hop.Backhand);
                pose.Frame = PelagSquallClipRules.Clamp(PelagSquallClipRules.LateFinishFrame + (t - hop.LateFrom),
                    PelagSquallClipRules.LateFinishFrame, PelagSquallClipRules.FinishTicks);
                pose.Crossfade = true;
                pose.Finished = pose.Frame >= PelagSquallClipRules.FinishTicks;
                // Левая стопа стоит и в финише: начатый в опоре поворот доходит вокруг неё же.
                Shift(contact, t, scale, 1f, ref pose);
            }
            else
            {
                // Опора: кадры 6–8; ждём продолжения на кадре 8, лодыжка держит корень.
                pose.Clip = PelagSquallClipRules.StrikeClip(hop.Backhand);
                pose.Frame = PelagSquallClipRules.SupportFrame(since);
                Shift(contact, t, scale, 1f, ref pose);
            }
        }

        /// <summary>
        /// Доля входного сдвига (CharacterAnimatorView.Squall: левая стопа стоит там, где стояла
        /// в стойке покоя): весь замах — 1, первый полёт гасит его S-кривой, как сдвиг опоры
        /// (ShiftDecay); к первому удару и дальше — 0.
        /// </summary>
        public float EntryWeight(float t)
        {
            if (!Active) return 0f;
            int index = LastHopStartedBy(t);
            if (index < 0) return 1f;
            return index == 0 ? PelagSquallClipRules.ShiftDecay(t - _hops[0].Start, _hops[0].Flight) : 0f;
        }

        /// <summary>Сдвиг корня за поворот в окне опоры [plantStart, plantEnd], погашенный decay.</summary>
        private void Shift(float plantStart, float plantEnd, float scale, float decay, ref PelagSquallPose pose)
        {
            if (decay <= 0f || scale <= 0f) return;
            PelagSquallClipRules.PivotShift(YawAt(plantStart), YawAt(plantEnd), scale, out float x, out float y);
            pose.ShiftX = x * decay;
            pose.ShiftY = y * decay;
        }

        /// <summary>Взгляд корня без углов пути возврата: последний начавшийся отрезок поворота.</summary>
        public float YawAt(float t)
        {
            float yaw = HasStartYaw ? _startYaw : _firstYaw;
            for (int i = 0; i < _turnCount; i++)
            {
                Turn turn = _turns[i];
                if (t < turn.Start) break;
                yaw = turn.From + turn.Delta * PelagSquallClipRules.Smooth((t - turn.Start) / turn.Ticks);
            }
            return yaw;
        }

        /// <summary>Углы ломаной возврата: взгляд по пути, каждый угол — S-кривая ±1 тик.</summary>
        private float ReturnCornerOffset(float t)
        {
            if (!_hasReturn || _legCount < 2) return 0f;
            float k = t - _returnStart, offset = 0f;
            for (int i = 0; i < _legCount - 1; i++)
                offset += PelagSquallClipRules.WrapDeg(_legYaw[i + 1] - _legYaw[i])
                          * PelagSquallClipRules.Smooth((k - (_legEnd[i] - 1f)) / 2f);
            return offset;
        }

        /// <summary>
        /// Поворот к toYaw с тика start: S-кривая на baseTicks (больше — по потолку
        /// скорости, не дольше maxTicks). Ещё не начавшийся поворот заменяется;
        /// начавшийся — перенацеливается с текущего угла, без скачка.
        /// </summary>
        private void TurnTo(float start, float baseTicks, float maxTicks, float toYaw, int prefer, float now)
        {
            while (_turnCount > 0 && _turns[_turnCount - 1].Start > now && _turns[_turnCount - 1].Start >= start - 1e-3f)
                _turnCount--;
            float at = Math.Max(start, now);
            if (_turnCount > 0 && at < _turns[_turnCount - 1].Start) at = _turns[_turnCount - 1].Start;
            if (_turnCount > 0)
            {
                Turn last = _turns[_turnCount - 1];
                if (at < last.Start + last.Ticks
                    && Math.Abs(PelagSquallClipRules.WrapDeg(last.From + last.Delta - toYaw)) < 1f) return;
            }
            float from = YawAt(at);
            float delta = PelagSquallClipRules.SignedTurn(from, toYaw, prefer);
            if (Math.Abs(delta) < .05f || _turnCount == _turns.Length) return;
            float min = at > start ? Math.Max(1f, start + baseTicks - at) : baseTicks;
            _turns[_turnCount++] = new Turn
            {
                Start = at, From = from, Delta = delta,
                Ticks = PelagSquallClipRules.TurnTicks(delta, min, Math.Max(min, maxTicks)),
            };
        }

        private int LastHopStartedBy(float t)
        {
            int found = -1;
            for (int i = 0; i < _hopCount; i++)
                if (_hops[i].Known && _hops[i].Start <= t) found = i;
            return found;
        }

        private int StrikeTickOf(int index)
        {
            Hop hop = _hops[index];
            return hop.Struck ? hop.StrikeTick : hop.Known ? hop.Start + hop.Flight : CastTick;
        }

        /// <summary>Отрезки возврата по снимку Sim (как Simulation.StartSquallReturn): взгляд и тики каждого.</summary>
        public static int ReturnLegs(FixVec2 from, in SquallState s, float[] yaw, int[] ticks)
        {
            int n = 0;
            FixVec2 a = from;
            for (int leg = 0; leg <= s.ViaCount && n < yaw.Length && n < ticks.Length; leg++)
            {
                FixVec2 b = leg >= s.ViaCount ? s.Origin : leg == 0 ? s.Via0 : s.Via1;
                FixVec2 d = b - a;
                yaw[n] = PelagSquallClipRules.YawOf(d.X.ToFloat(), d.Y.ToFloat());
                ticks[n] = PelagSquallClipRules.ReturnLegTicks(a, b);
                n++;
                a = b;
            }
            return n;
        }
    }
}
