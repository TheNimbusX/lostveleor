using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Поза Абордажа на тике показа: клип и кадр, взгляд корня, сдвиг тела от позиции Sim, якорь в руке.</summary>
    public struct PelagAbordagePose
    {
        public PelagAbordageClip Clip;
        public float Frame;

        /// <summary>Рысканье корня, градусы плоскости Sim (atan2(y, x)), влево — плюс.</summary>
        public float Yaw;

        /// <summary>Сдвиг корня, м мира (X, мировая Z): левая лодыжка стоит, пока корень крутится.</summary>
        public float ShiftX, ShiftY;

        /// <summary>Левая стопа стоит (весь бросок, удар, выход).</summary>
        public bool Planted;

        /// <summary>Якорь в ПРАВОЙ руке (лист B): от каста до выпуска; цель пропала до выпуска — и весь возврат.</summary>
        public bool AnchorInHand;

        /// <summary>Показ кончился: тело отдаётся стойке или бегу.</summary>
        public bool Finished;
    }

    /// <summary>
    /// Лента одного каста Абордажа для вида, без Unity. Собирается из снимка Sim в каст
    /// (прогноз выпуска, зацепа, удара, форма слота) и событий AbordageThrow/Hook/Punch/Ended
    /// (тик события = SimulationTick − 1), отвечает позой на любой тик показа. События приходят
    /// минимум за тик до того, как показ до них дойдёт (тело рисуется на тик позже Sim);
    /// опоздавшее событие не рвёт позу: поворот перенацеливается с текущего угла, полёт
    /// якоря дотягивается до кадра 9 с текущего кадра.
    ///
    /// v3 (03.10): тяга P = 4…5 — PullShort (выбор фиксируется в зацеп); прибытие по форме —
    /// Гейзер Uppercut, Обвал Slam (с тика B+P−1, контакт — кадр 1 в тик удара), база и
    /// Пробоина — Punch. Форму знаем со снимка (Simulation.FormAt слота) раньше удара;
    /// событие удара её подтверждает.
    /// </summary>
    public sealed class PelagAbordageTimeline
    {
        private struct Turn { public float Start, Ticks, From, Delta; }

        private readonly Turn[] _turns = new Turn[16];
        private int _turnCount;
        private float _startYaw = float.NaN, _targetYaw;

        // Полёт якоря перестроен (перевыбор цели): с этого тика показа и кадра — к кадру 9; −1 — нет.
        private float _flightFrom = -1f, _flightFromFrame;

        private float _recallFrom = -1f, _recallFromFrame;
        private int _recallEnd;
        private bool _recallInHand;

        private bool _ended;
        private int _endTick;

        public bool Active { get; private set; }
        public int Serial { get; private set; }
        public int CastTick { get; private set; }
        public int ReleaseTick { get; private set; }
        public int BiteTick { get; private set; }
        public int ArriveTick { get; private set; }
        public bool Released { get; private set; }
        public bool Hooked { get; private set; }
        public bool Struck { get; private set; }
        public bool Landed { get; private set; }

        /// <summary>Форма удара: со снимка в каст (ожидаемая), с удара — подтверждённая.</summary>
        public PelagForm Form { get; private set; }

        /// <summary>Клип тяги (Pull или PullShort): выбирается в зацеп по тикам тяги и дальше не меняется.</summary>
        public PelagAbordageClip PullClip { get; private set; } = PelagAbordageClip.Pull;

        /// <summary>В контроллере есть PullShort (вид: состояние собрано). Нет — короткая тяга на Pull.</summary>
        public bool ShortPullAvailable { get; set; } = true;

        /// <summary>В контроллере есть Uppercut и Slam. Нет — формы бьют базовым Punch.</summary>
        public bool FormClipsAvailable { get; set; } = true;

        public bool Recalling => _recallFrom >= 0f;
        public bool Ended => _ended;
        public AbordageEnd EndReason { get; private set; }
        public bool HasStartYaw => !float.IsNaN(_startYaw);
        public int PullTicks => Math.Max(1, ArriveTick - BiteTick);
        public int ExitEndTick => ArriveTick + PelagAbordageClipRules.HoldTicks + PelagAbordageClipRules.ExitTicks;

        /// <summary>Клип прибытия: по форме (Гейзер — Uppercut, Обвал — Slam), иначе Punch.</summary>
        public PelagAbordageClip ArrivalClip => PelagAbordageClipRules.ArrivalClip(Form, FormClipsAvailable);

        /// <summary>Тик, с которого идёт клип прибытия: удар, у Uppercut/Slam — на тик раньше (не раньше зацепа).</summary>
        public int ArrivalStartTick => Math.Max(BiteTick, ArriveTick - PelagAbordageClipRules.ArrivalLeadTicks(ArrivalClip));

        /// <summary>Замах этого каста: 3 тика, цель за спиной — 4 (Sim, AbordageTurnWindupTicks). На него — и поворот корня.</summary>
        public int WindupTicks => ReleaseTick - CastTick;

        /// <summary>Каст по снимку Sim: прогноз выпуска, зацепа и удара, взгляд на цель, форма слота.</summary>
        public void Begin(int serial, int castTick, int releaseTick, int biteTick, int arriveTick, float targetYaw,
            PelagForm form = PelagForm.None)
        {
            Active = true;
            Serial = serial;
            CastTick = castTick;
            ReleaseTick = Math.Max(castTick + 1, releaseTick);
            BiteTick = Math.Max(ReleaseTick + 1, biteTick);
            ArriveTick = Math.Max(BiteTick + 1, arriveTick);
            _targetYaw = targetYaw;
            _startYaw = float.NaN;
            _turnCount = 0;
            _flightFrom = _recallFrom = -1f;
            _recallInHand = false;
            Released = Hooked = Struck = Landed = _ended = false;
            Form = form;
            PullClip = PelagAbordageClipRules.PullClip(PullTicks, ShortPullAvailable);
            EndReason = AbordageEnd.Done;
        }

        public void Stop() => Active = false;

        /// <summary>Форма слота по снимку (сменили в мини-меню посреди каста): до удара — она; удар подтверждает свою.</summary>
        public void ExpectForm(PelagForm form)
        {
            if (!Active || Struck) return;
            Form = form;
        }

        /// <summary>Взгляд тела до каста (показанный в прошлом кадре): с него начинается поворот замаха.</summary>
        public void SetStartYaw(float yaw, float now)
        {
            if (!Active || HasStartYaw) return;
            _startYaw = yaw;
            TurnToTarget(_targetYaw, now);
        }

        /// <summary>Поворот замаха: с тика каста S-кривой ровно на тики замаха (3, цель за спиной — 4).</summary>
        private void TurnToTarget(float targetYaw, float now)
        {
            float ticks = PelagAbordageClipRules.ThrowTurnTicks(WindupTicks);
            TurnTo(CastTick, ticks, ticks, targetYaw, now);
        }

        /// <summary>Выпуск якоря (AbordageThrow): тик выпуска, тиков полёта, взгляд на цель в миг выпуска.</summary>
        public void Throw(int tick, int hookTicks, float targetYaw, float now)
        {
            if (!Active || Hooked || Recalling) return;
            Released = true;
            ReleaseTick = Math.Max(CastTick + 1, tick);
            Retime(ReleaseTick + Math.Max(1, hookTicks), now);
            _targetYaw = targetYaw;
            if (HasStartYaw) TurnToTarget(targetYaw, now);
        }

        /// <summary>
        /// Зацеп сдвинулся без события (перевыбор цели в полёте якоря, снимок Sim): уже
        /// летящий якорь дотягивает кадр до натяга с того кадра, что показан сейчас.
        /// </summary>
        public void Retime(int biteTick, float now)
        {
            if (!Active || Hooked || biteTick == BiteTick) return;
            biteTick = Math.Max(ReleaseTick + 1, biteTick);
            if (now > ReleaseTick && now < BiteTick)
            {
                _flightFromFrame = ThrowFrameAt(now);
                _flightFrom = now;
            }
            BiteTick = biteTick;
            if (ArriveTick <= BiteTick) ArriveTick = BiteTick + 1;
        }

        /// <summary>Зацеп (AbordageHook): тяга pullTicks тиков (без тяги — 1), взгляд по тяге; клип тяги — по ней.</summary>
        public void Hook(int tick, int pullTicks, float pullYaw, float now)
        {
            if (!Active || Hooked || Recalling) return;
            Hooked = true;
            if (tick != BiteTick && now > ReleaseTick && now < tick)
            {
                _flightFromFrame = ThrowFrameAt(now);
                _flightFrom = now;
            }
            BiteTick = Math.Max(ReleaseTick + 1, tick);
            ArriveTick = BiteTick + Math.Max(1, pullTicks);
            PullClip = PelagAbordageClipRules.PullClip(PullTicks, ShortPullAvailable);
            TurnTo(BiteTick, PelagAbordageClipRules.HookTurnTicks, PelagAbordageClipRules.HookTurnMaxTicks, pullYaw, now);
        }

        /// <summary>Удар (AbordagePunch): тик прибытия, дошёл ли, форма; взгляд Sim после удара.</summary>
        public void Punch(int tick, bool landed, PelagForm form, float punchYaw, float now)
        {
            if (!Active || Struck || Recalling) return;
            Struck = true;
            Landed = landed;
            Form = form;
            bool unhooked = !Hooked;
            if (!Hooked) { Hooked = true; BiteTick = Math.Min(BiteTick, tick - 1); }
            ArriveTick = Math.Max(BiteTick + 1, tick);
            // Зацепа не видели (удар без события зацепа): клип тяги — по тикам, что вышли.
            if (unhooked) PullClip = PelagAbordageClipRules.PullClip(PullTicks, ShortPullAvailable);
            TurnTo(Math.Max(BiteTick, ArriveTick - PelagAbordageClipRules.PunchTurnLeadTicks),
                PelagAbordageClipRules.PunchTurnTicks, PelagAbordageClipRules.PunchTurnMaxTicks, punchYaw, now);
        }

        /// <summary>
        /// Цели нет до зацепа (снимок Sim, фаза Recall, или AbordageEnded NoTarget): бросок
        /// идёт назад к стойке до конца возврата. Узнали поздно — от текущего кадра, конец тот же.
        /// </summary>
        public void Recall(int startTick, float now)
        {
            if (!Active || Hooked || Recalling) return;
            float from = Math.Max(startTick, now);
            _recallFromFrame = ThrowFrameAt(from);
            _recallFrom = from;
            _recallEnd = startTick + PelagAbordageClipRules.RecallTicks;
            _recallInHand = startTick < ReleaseTick;
        }

        public void End(int tick, AbordageEnd reason)
        {
            if (!Active || _ended) return;
            _ended = true;
            _endTick = tick;
            EndReason = reason;
        }

        public PelagAbordagePose Sample(float t, float scale)
        {
            var pose = new PelagAbordagePose { Yaw = YawAt(t) };
            if (!Active) { pose.Finished = true; return pose; }
            if (Recalling && t >= _recallFrom)
            {
                float span = Math.Max(.5f, _recallEnd - _recallFrom);
                pose.Clip = PelagAbordageClip.Throw;
                pose.Frame = _recallFromFrame * (1f - PelagSquallClipRules.Smooth((t - _recallFrom) / span));
                pose.Planted = true;
                pose.AnchorInHand = _recallInHand;
                pose.Finished = t >= _recallFrom + span;
                Shift(CastTick, t, scale, 1f, ref pose);
            }
            else if (!Hooked || t < BiteTick)
            {
                pose.Clip = PelagAbordageClip.Throw;
                pose.Frame = ThrowFrameAt(t);
                pose.Planted = true;
                pose.AnchorInHand = t < ReleaseTick;
                Shift(CastTick, t, scale, 1f, ref pose);
            }
            else if (t < ArrivalStartTick)
            {
                float k = t - BiteTick;
                pose.Clip = PullClip;
                pose.Frame = PelagAbordageClipRules.PullClipFrame(PullClip, PullTicks, k);
                pose.Planted = PelagAbordageClipRules.LeftPlanted(pose.Clip, pose.Frame);
                Shift(CastTick, BiteTick, scale, PelagSquallClipRules.ShiftDecay(k, PullTicks), ref pose);
            }
            else if (t < ArriveTick + PelagAbordageClipRules.HoldTicks)
            {
                pose.Clip = ArrivalClip;
                if (pose.Clip == PelagAbordageClip.Punch)
                {
                    pose.Frame = PelagAbordageClipRules.PunchFrame(t - ArriveTick);
                    pose.Planted = true;
                    Shift(ArriveTick, t, scale, 1f, ref pose);
                }
                else
                {
                    // Uppercut/Slam: кадр 0 — последний тик тяги (тело ещё в воздухе), кадр 1 — удар.
                    pose.Frame = PelagAbordageClipRules.FormFrame(t - (ArriveTick - PelagAbordageClipRules.FormLeadTicks));
                    pose.Planted = PelagAbordageClipRules.LeftPlanted(pose.Clip, pose.Frame);
                    if (t < ArriveTick)
                        Shift(CastTick, BiteTick, scale, PelagSquallClipRules.ShiftDecay(t - BiteTick, PullTicks), ref pose);
                    else Shift(ArriveTick, t, scale, 1f, ref pose);
                }
            }
            else
            {
                pose.Clip = PelagAbordageClip.Recover;
                pose.Frame = PelagAbordageClipRules.RecoverFrame(t - ArriveTick - PelagAbordageClipRules.HoldTicks);
                pose.Planted = true;
                pose.Finished = t >= ExitEndTick;
                Shift(ArriveTick, t, scale, 1f, ref pose);
            }
            if (_ended && t >= _endTick) pose.Finished = true;
            return pose;
        }

        /// <summary>
        /// Доля входного сдвига (CharacterAnimatorView.Abordage: левая стопа стоит там, где
        /// стояла в стойке покоя): весь бросок и возврат — 1, тяга гасит его S-кривой (тело в
        /// воздухе), к удару и дальше — 0.
        /// </summary>
        public float EntryWeight(float t)
        {
            if (!Active) return 0f;
            if (Recalling || !Hooked || t < BiteTick) return 1f;
            return t < ArriveTick ? PelagSquallClipRules.ShiftDecay(t - BiteTick, PullTicks) : 0f;
        }

        private float ThrowFrameAt(float t)
        {
            if (_flightFrom >= 0f && t >= _flightFrom)
            {
                float span = Math.Max(.5f, BiteTick - _flightFrom);
                return _flightFromFrame + (PelagAbordageClipRules.BiteFrame - _flightFromFrame)
                       * PelagAbordageClipRules.Clamp((t - _flightFrom) / span, 0f, 1f);
            }
            return PelagAbordageClipRules.ThrowFrame(ReleaseTick - CastTick, BiteTick - ReleaseTick, t - CastTick);
        }

        /// <summary>Сдвиг корня за поворот в окне опоры [plantStart, plantEnd], погашенный decay.</summary>
        private void Shift(float plantStart, float plantEnd, float scale, float decay, ref PelagAbordagePose pose)
        {
            if (decay <= 0f || scale <= 0f) return;
            PelagSquallClipRules.PivotShift(YawAt(plantStart), YawAt(plantEnd), scale, out float x, out float y);
            pose.ShiftX = x * decay;
            pose.ShiftY = y * decay;
        }

        /// <summary>Взгляд корня: последний начавшийся отрезок поворота.</summary>
        public float YawAt(float t)
        {
            float yaw = HasStartYaw ? _startYaw : _targetYaw;
            for (int i = 0; i < _turnCount; i++)
            {
                Turn turn = _turns[i];
                if (t < turn.Start) break;
                yaw = turn.From + turn.Delta * PelagSquallClipRules.Smooth((t - turn.Start) / turn.Ticks);
            }
            return yaw;
        }

        /// <summary>
        /// Поворот к toYaw с тика start кратчайшим путём: S-кривая на baseTicks (больше — по
        /// потолку скорости, не дольше maxTicks). Ещё не начавшийся поворот заменяется;
        /// начавшийся — перенацеливается с текущего угла, без скачка (как у Шквала).
        /// </summary>
        private void TurnTo(float start, float baseTicks, float maxTicks, float toYaw, float now)
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
            float delta = PelagSquallClipRules.SignedTurn(from, toYaw, 0);
            if (Math.Abs(delta) < .05f || _turnCount == _turns.Length) return;
            float min = at > start ? Math.Max(1f, start + baseTicks - at) : baseTicks;
            _turns[_turnCount++] = new Turn
            {
                Start = at, From = from, Delta = delta,
                Ticks = PelagSquallClipRules.TurnTicks(delta, min, Math.Max(min, maxTicks)),
            };
        }
    }
}
