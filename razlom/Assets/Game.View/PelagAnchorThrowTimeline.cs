using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Поза Броска якоря на тике показа: клип и кадр, взгляд корня, сдвиг тела от позиции Sim.</summary>
    public struct PelagAnchorThrowPose
    {
        public PelagAnchorThrowClip Clip;
        public float Frame;

        /// <summary>Точка цепочки клипов (PelagAnchorThrowClipRules): для проверок и журнала.</summary>
        public float Chain;

        /// <summary>Рысканье корня, градусы плоскости Sim (atan2(y, x)), влево — плюс.</summary>
        public float Yaw;

        /// <summary>Сдвиг корня, м мира (X, мировая Z): левая лодыжка стоит весь бросок.</summary>
        public float ShiftX, ShiftY;

        /// <summary>Левая стопа стоит (весь бросок: клипы держат её в одной точке, timing.json planted_ankles).</summary>
        public bool Planted;

        /// <summary>Клип ловли дошёл до кадра «якорь на спину» (Catch 3): риг кончает маятник и убирает голову.</summary>
        public bool StowCue;

        /// <summary>Показ кончился: тело отдаётся стойке или бегу.</summary>
        public bool Finished;
    }

    /// <summary>
    /// Лента одного каста Броска якоря для вида, без Unity. Собирается из снимка Sim в каст
    /// (AnchorThrowState: каст, замах, выпуск, F, натяг, R, ловля — прогноз со стенами) и событий
    /// AnchorThrowRelease/Yank/Catch/Ended (тик события = SimulationTick − 1); отвечает позой на
    /// любой тик показа (sim.Tick − 2 + Alpha). Полёт короче прогноза (корпус босса, укус Гарпуна)
    /// виден в снимке тиком раньше показа: уже идущий полёт дотягивается от показанного кадра до
    /// Yank 0 в новый тик натяга — без скачка позы (как перевыбор цели Абордажа).
    /// </summary>
    public sealed class PelagAnchorThrowTimeline
    {
        private float _startYaw = float.NaN, _targetYaw;
        private float _flightFrom = -1f, _flightFromChain;
        private bool _ended;
        private int _endTick;

        public bool Active { get; private set; }
        public int Serial { get; private set; }
        public int CastTick { get; private set; }
        public int ReleaseTick { get; private set; }
        public int FlightTicks { get; private set; }
        public int TautTick { get; private set; }
        public int ReturnTicks { get; private set; }
        public int CatchTick { get; private set; }
        public bool Released { get; private set; }
        public bool Yanked { get; private set; }
        public bool Caught { get; private set; }
        public bool Ended => _ended;
        public int EndTick => _endTick;
        public AnchorThrowEnd EndReason { get; private set; }
        public bool HasStartYaw => !float.IsNaN(_startYaw);
        public float TargetYaw => _targetYaw;

        /// <summary>Замах этого каста: 2 тика, курсор за спиной — 3 (Sim). На него — и поворот корня.</summary>
        public int WindupTicks => ReleaseTick - CastTick;

        /// <summary>Тик конца стоячей фазы: ловля + удержание + выход (Sim: PhaseEndTick выхода).</summary>
        public int ExitEndTick => CatchTick + PelagAnchorThrowClipRules.HoldTicks + PelagAnchorThrowClipRules.ExitTicks;

        /// <summary>Каст по снимку Sim: расписание (прогноз) и взгляд на Dir.</summary>
        public void Begin(int serial, int castTick, int releaseTick, int flightTicks, int tautTick, int returnTicks,
            int catchTick, float targetYaw)
        {
            Active = true;
            Serial = serial;
            CastTick = castTick;
            ReleaseTick = Math.Max(castTick + 1, releaseTick);
            FlightTicks = Math.Max(1, flightTicks);
            TautTick = Math.Max(ReleaseTick + 1, tautTick);
            ReturnTicks = Math.Max(4, Math.Min(8, returnTicks));
            CatchTick = Math.Max(TautTick + 1, catchTick);
            _targetYaw = targetYaw;
            _startYaw = float.NaN;
            _flightFrom = -1f;
            Released = Yanked = Caught = _ended = false;
            EndReason = AnchorThrowEnd.Done;
        }

        public void Stop() => Active = false;

        /// <summary>Взгляд тела до каста (показанный в прошлом кадре): с него начинается поворот замаха.</summary>
        public void SetStartYaw(float yaw)
        {
            if (!Active || HasStartYaw) return;
            _startYaw = yaw;
        }

        /// <summary>
        /// Расписание полёта и возврата из снимка или события (выпуск, натяг). Полёт, который уже
        /// показывается, дотягивается от текущей точки цепочки до Yank 0 в новый тик натяга.
        /// </summary>
        public void Schedule(int releaseTick, int flightTicks, int tautTick, int returnTicks, int catchTick, float now)
        {
            if (!Active || Yanked) return;
            releaseTick = Math.Max(CastTick + 1, releaseTick);
            flightTicks = Math.Max(1, flightTicks);
            tautTick = Math.Max(releaseTick + 1, tautTick);
            returnTicks = Math.Max(4, Math.Min(8, returnTicks));
            catchTick = Math.Max(tautTick + 1, catchTick);
            bool flightMoved = releaseTick != ReleaseTick || flightTicks != FlightTicks || tautTick != TautTick;
            if (flightMoved && now > ReleaseTick && now < TautTick)
            {
                _flightFromChain = ChainAt(now);
                _flightFrom = now;
            }
            ReleaseTick = releaseTick;
            FlightTicks = flightTicks;
            TautTick = tautTick;
            ReturnTicks = returnTicks;
            CatchTick = catchTick;
        }

        /// <summary>Выпуск (AnchorThrowRelease): тик выпуска подтверждён.</summary>
        public void Release(int tick, float now)
        {
            if (!Active || Released) return;
            Released = true;
            if (tick != ReleaseTick)
                Schedule(tick, FlightTicks, tick + FlightTicks + 1, ReturnTicks, tick + FlightTicks + 1 + ReturnTicks, now);
        }

        /// <summary>Натяг (AnchorThrowYank): тик натяга и тики возврата — окончательные.</summary>
        public void Yank(int tick, int returnTicks, float now)
        {
            if (!Active || Yanked) return;
            Schedule(ReleaseTick, Math.Max(1, tick - ReleaseTick - 1), tick, returnTicks, tick + Math.Max(4, returnTicks), now);
            Yanked = true;
        }

        /// <summary>Ловля (AnchorThrowCatch).</summary>
        public void Catch(int tick)
        {
            if (!Active || Caught) return;
            Caught = true;
            if (tick > TautTick) CatchTick = tick;
        }

        public void End(int tick, AnchorThrowEnd reason)
        {
            if (!Active || _ended) return;
            _ended = true;
            _endTick = tick;
            EndReason = reason;
        }

        /// <summary>Точка цепочки клипов в дробный тик показа: по прямой между ключами целых тиков.</summary>
        public float ChainAt(float t)
        {
            if (_flightFrom >= 0f && t >= _flightFrom && t < TautTick)
            {
                float span = Math.Max(.5f, TautTick - _flightFrom);
                return _flightFromChain + (PelagAnchorThrowClipRules.YankStart - _flightFromChain)
                       * PelagAnchorThrowClipRules.Clamp((t - _flightFrom) / span, 0f, 1f);
            }
            int a = (int)Math.Floor(t);
            float u = t - a;
            float from = Key(a), to = Key(a + 1);
            return from + (to - from) * u;
        }

        private float Key(int n) => PelagAnchorThrowClipRules.KeyChain(CastTick, WindupTicks, ReleaseTick, FlightTicks,
            TautTick, ReturnTicks, CatchTick, n);

        public PelagAnchorThrowPose Sample(float t, float scale)
        {
            var pose = new PelagAnchorThrowPose { Yaw = YawAt(t), Planted = true };
            if (!Active) { pose.Finished = true; return pose; }
            float c = ChainAt(t);
            pose.Chain = c;
            pose.Clip = PelagAnchorThrowClipRules.ClipAt(c, ReturnTicks, out pose.Frame);
            pose.StowCue = pose.Clip == PelagAnchorThrowClip.Catch && pose.Frame >= PelagAnchorThrowClipRules.StowHandoffFrame;
            if (scale > 0f)
            {
                PelagSquallClipRules.PivotShift(YawAt(CastTick), pose.Yaw, scale, out float x, out float y);
                pose.ShiftX = x;
                pose.ShiftY = y;
            }
            pose.Finished = t >= ExitEndTick || (_ended && t >= _endTick);
            return pose;
        }

        /// <summary>
        /// Взгляд корня: с тика каста S-кривой ровно за тики замаха к Dir (к выпуску правая бросает
        /// точно по линии), кратчайшим путём; дальше — Dir весь бросок (Sim взгляд не меняет).
        /// </summary>
        public float YawAt(float t)
        {
            if (!HasStartYaw) return _targetYaw;
            float delta = PelagSquallClipRules.SignedTurn(_startYaw, _targetYaw, 0);
            float ticks = Math.Max(1, WindupTicks);
            return _startYaw + delta * PelagSquallClipRules.Smooth((t - CastTick) / ticks);
        }
    }
}
