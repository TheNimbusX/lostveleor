using System;
using Game.Sim;
using R = Game.View.PelagWreckClipRules;

namespace Game.View
{
    public sealed partial class PelagWreckTimeline
    {
        /// <summary>Пошёл в окне — корень отдаётся ArenaView.</summary>
        public const float ReplantTicks = 3f;

        /// <summary>Шаг для темпа кадра (кадров на тик), тиков.</summary>
        private const float RateProbe = .1f;

        /// <summary>Поза на тик показа t и темп кадра (Rate — по соседнему тику той же ленты).</summary>
        public PelagWreckPose Sample(float t, float scale)
        {
            PelagWreckPose pose = SampleCore(t);
            if (pose.Finished || pose.Clip == PelagWreckClip.None) { pose.Rate = 0f; return pose; }
            PelagWreckPose next = SampleCore(t + RateProbe);
            float frames = next.Clip == pose.Clip ? next.Frame - pose.Frame
                : Math.Max(0f, R.SeamFrame(pose.Clip) - pose.Frame) + next.Frame;
            pose.Rate = R.Clamp(frames / RateProbe, 0f, 4f);
            return pose;
        }

        /// <summary>Тик начала уборки (Stow): выпад последнего этапа — со стыка 16 (или раньше, если выход сорван ходьбой),
        /// иначе — конец серии; срыв и серия без конца — нет (float.MaxValue).</summary>
        public float StowStart(out bool seam)
        {
            seam = false;
            if (_count == 0 || Ended && EndReason == WreckEnd.Interrupted) return float.MaxValue;
            Stage last = _stages[_count - 1];
            if (last.Index >= 2 && last.Last && last.Contact > last.Start)
            {
                float at = last.Contact + (R.LungeSeam - R.LungeContact);
                if (Ended && EndTick < at) return EndTick;
                seam = true;
                return at;
            }
            return Ended ? EndTick : float.MaxValue;
        }

        private PelagWreckPose SampleCore(float t)
        {
            var pose = new PelagWreckPose { Stage = -1 };
            if (!Active || _count == 0) { pose.Finished = true; return pose; }
            if (Ended && EndReason == WreckEnd.Interrupted && t >= EndTick)
            {
                pose.Yaw = PelagSquallClipRules.WrapDeg(FinalYaw(_count - 1));
                pose.Finished = true;
                return pose;
            }
            float stow = StowStart(out bool seam);
            if (t >= stow)
            {
                // Якорь на спину: тело Stow = хвост Lunge 16–24 (стык), из маха (окно прошло) и сорванного выхода — смешивание.
                float since = t - stow;
                pose.Clip = PelagWreckClip.Stow;
                pose.Frame = R.StowFrame(since);
                pose.EntryBlendTicks = seam ? 0f : R.SeamBlendTicks;
                pose.LegsFree = true;
                pose.Finished = Ended && since >= R.StowLast;
                pose.RootOwned = true;
                Root(_count - 1, t, ref pose);
                return pose;
            }
            int at = Math.Max(0, At(t));
            Stage s = _stages[at];
            pose.Stage = s.Index;
            pose.RootOwned = true;
            // Следующее нажатие видно ленте, когда Sim уже начал этап (показ отстаёт на 1–2 тика) — та же граница и задним числом.
            pose.NextStarted = at < _count - 1 && _stages[at + 1].Start <= t + 2f;
            if (s.Index >= 2 && s.ChargeStart >= 0) SampleCharged(s, t, ref pose);
            else SampleStage(at, t, ref pose);
            Root(at, t, ref pose);
            return pose;
        }

        /// <summary>Замах этапа (с догоном хвоста прошлого или со снятием у первого), после удара — 1:1.</summary>
        private void SampleStage(int at, float t, ref PelagWreckPose pose)
        {
            Stage s = _stages[at];
            PelagWreckClip clip = R.StageClip(s.Index);
            int windup = Math.Max(1, s.Contact - s.Start);
            if (t < s.Contact)
            {
                float u = R.Clamp((t - s.Start) / windup, 0f, 1f);
                if (at == 0 && s.Index == 0)
                {
                    // Снятие со спины и замах маха 1 — одним ходом за замах этапа 0 (Sim времени на снятие не даёт).
                    float v = R.DrawEntryFrame + u * (R.DrawLast - R.DrawEntryFrame + R.ContactFrame(clip));
                    if (v < R.DrawLast) { pose.Clip = PelagWreckClip.Draw; pose.Frame = v; pose.EntryBlendTicks = R.CastBlendTicks; }
                    else { pose.Clip = clip; pose.Frame = v - R.DrawLast; pose.EntryBlendTicks = 0f; }
                    return;
                }
                if (at > 0)
                {
                    Stage prev = _stages[at - 1];
                    PelagWreckClip prevClip = R.StageClip(prev.Index);
                    float f0 = prev.Contact > prev.Start && s.Start >= prev.Contact
                        ? R.ContactFrame(prevClip) + (s.Start - prev.Contact) : float.MaxValue;
                    int seam = R.SeamFrame(prevClip);
                    if (prev.Index <= 1 && f0 < seam - .01f)
                    {
                        // Догон: хвост прошлого клипа до стыка и замах этого — равномерно, контакт в тик удара Sim.
                        float v = f0 + u * (seam - f0 + R.ContactFrame(clip));
                        pose.EntryBlendTicks = 0f;
                        if (v < seam) { pose.Clip = prevClip; pose.Frame = v; pose.NextStarted = true; }
                        else { pose.Clip = clip; pose.Frame = v - seam; }
                        return;
                    }
                }
                pose.Clip = clip;
                pose.Frame = R.WindupFrame(clip, windup, t - s.Start);
                pose.EntryBlendTicks = R.SeamBlendTicks;
                return;
            }
            float since = t - s.Contact;
            pose.Clip = clip;
            pose.Frame = R.AfterContactFrame(clip, since);
            pose.EntryBlendTicks = R.SeamBlendTicks;
            pose.LegsFree = s.Index <= 1 ? since >= Simulation.WreckFollowTicks : ExitLegs(s, t);
        }

        /// <summary>Девятый вал: до тика заряда — замах к Lunge@4, в заряде держит его, после отпускания — 4 → 8 к удару.</summary>
        private void SampleCharged(in Stage s, float t, ref PelagWreckPose pose)
        {
            pose.Clip = PelagWreckClip.Lunge;
            pose.EntryBlendTicks = R.SeamBlendTicks;
            int hold = s.ChargeStart > s.Start ? s.ChargeStart : s.Start + Simulation.WreckSlamUpTicks;
            if (t < hold) { pose.Frame = R.ChargeHoldFrame * R.Clamp((t - s.Start) / Math.Max(1, hold - s.Start), 0f, 1f); return; }
            if (s.ReleaseStart < 0 || t < s.ReleaseStart) { pose.Frame = R.ChargeHoldFrame; return; }
            if (t < s.Contact)
            {
                float u = R.Clamp((t - s.ReleaseStart) / Math.Max(1, s.Contact - s.ReleaseStart), 0f, 1f);
                pose.Frame = R.ChargeHoldFrame + u * (R.LungeContact - R.ChargeHoldFrame);
                return;
            }
            pose.Frame = R.AfterContactFrame(PelagWreckClip.Lunge, t - s.Contact);
            pose.LegsFree = ExitLegs(s, t);
        }

        private static bool ExitLegs(in Stage s, float t)
            => (s.ExitWalk > 0 && t >= s.ExitWalk) || (s.ExitEnd > 0 && t >= s.ExitEnd);

        // ---- корень: поворот S-кривой к направлению этапа за первые тики замаха, без сдвига тела ----

        private void Root(int at, float t, ref PelagWreckPose pose)
        {
            Stage s = _stages[at];
            float start = !float.IsNaN(s.StartYaw) ? s.StartYaw : at > 0 ? FinalYaw(at - 1) : s.DirYaw;
            float turn = Math.Max(1f, Math.Min(R.TurnTicks, s.Contact - s.Start));
            float yaw = start + PelagSquallClipRules.SignedTurn(start, s.DirYaw, 0) * PelagSquallClipRules.Smooth((t - s.Start) / turn);
            if (s.Index >= 2 && s.ChargeStart >= 0 && t >= s.ChargeStart) yaw = ChargeYawAt(t, yaw);
            pose.Yaw = PelagSquallClipRules.WrapDeg(yaw);
            pose.ShiftX = pose.ShiftY = 0f;
            if (_replantStage == at && t >= _replantAt) pose.RootOwned = false;
        }

        private float ChargeYawAt(float t, float fallback)
        {
            if (_chargeCount == 0) return fallback;
            if (t <= _chargeTick[0]) return _chargeYaw[0];
            for (int i = 1; i < _chargeCount; i++)
            {
                if (t > _chargeTick[i]) continue;
                float u = (t - _chargeTick[i - 1]) / Math.Max(1, _chargeTick[i] - _chargeTick[i - 1]);
                return _chargeYaw[i - 1] + PelagSquallClipRules.SignedTurn(_chargeYaw[i - 1], _chargeYaw[i], 0) * u;
            }
            return _chargeYaw[_chargeCount - 1];
        }

        /// <summary>Взгляд в конце этапа: направление этапа, у заряженного — последнее за курсором.</summary>
        private float FinalYaw(int at)
        {
            Stage s = _stages[at];
            return s.Index >= 2 && s.ChargeStart >= 0 && _chargeCount > 0 ? _chargeYaw[_chargeCount - 1] : s.DirYaw;
        }
    }
}
