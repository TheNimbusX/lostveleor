using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Поза Крушения на тике показа: клип базового слоя и кадр, вход в клип, корень.</summary>
    public struct PelagWreckPose
    {
        public PelagWreckClip Clip;
        public float Frame;

        /// <summary>v4: верхнего и нижнего слоёв нет (Wait/Braced v3) — всегда None.</summary>
        public PelagWreckClip Upper;
        public float UpperFrame;
        public PelagWreckClip Lower;
        public float LowerFrame;

        /// <summary>Вход в Clip, если он сменился: 0 — стык (та же поза, без смешивания), иначе тиков смешивания.</summary>
        public float EntryBlendTicks;

        /// <summary>Этап 0…3; −1 — уборка якоря на спину (Stow) после конца серии.</summary>
        public int Stage;

        /// <summary>Sim отпускает ноги (окно, хвост выхода, уборка): идёт — ноги бегут слоем Recovery Footwork.</summary>
        public bool LegsFree;

        /// <summary>Корнем правит лента (поворот к направлению этапа за TurnTicks, без сдвига); false — взгляд ArenaView.</summary>
        public bool RootOwned;

        /// <summary>Рысканье корня, градусы плоскости Sim (atan2(y, x)), влево — плюс; сдвиг тела, м мира (v4 — ноль).</summary>
        public float Yaw, ShiftX, ShiftY;

        /// <summary>Следующее нажатие уже было: голова маха идёт дальше по запечке (риг не уходит в живой маятник).</summary>
        public bool NextStarted;

        /// <summary>Кадров клипа на тик Sim сейчас (догон, сжатое снятие, темп) — для скоростей запечки рига.</summary>
        public float Rate;

        public bool Finished;
    }

    /// <summary>
    /// Лента одной серии Крушения v4 для вида, без Unity. Собирается из снимка WreckState (PelagWreckFeed.Track:
    /// этапы, контакт, заряд Девятого вала, выход) и события WreckEnded, отвечает позой на любой тик показа
    /// (Tick − 2 + Alpha). Этапы хранятся историей: показ отстаёт от снимка, хвост прошлого этапа доигрывается,
    /// пока показ не дошёл до нажатия следующего.
    ///
    /// Клипы v4 (PelagWreckClipRules): Draw → Swing1 → Swing2 → Lunge → Stow. Снятие сжато в замах этапа 0. Нажатие
    /// раньше стыка (удар + 1 из буфера) — «догон»: хвост прошлого клипа до стыка и замах нового одним равномерным
    /// ходом за замах, контакт клипа — в тик удара Sim, позы не смешиваются. Нажатие после стыка — смешивание 2 тика.
    /// Выпад последнего этапа в кадре 16 переходит в Stow без смешивания (тело Stow = хвост Lunge 16–24); окно без
    /// нажатия (WindowExpired) и сорванный ходьбой выход — Stow со смешиванием; срыв — конец показа.
    /// Поворот к направлению этапа — за первые TurnTicks тиков замаха, без сдвига тела (как серия сабли).
    /// Девятый вал: в заряде тело держит Lunge@4, после отпускания — 4 → 8 к удару (клипа цикла у v4 нет).
    /// </summary>
    public sealed partial class PelagWreckTimeline
    {
        private const int MaxStages = 6, MaxChargeYaw = 48;

        private struct Stage
        {
            public int Index, Start, Contact, Overhead, ChargeStart, ReleaseStart;
            public int ExitWalk, ExitEnd;
            public bool Last;
            public float DirYaw, StartYaw;
        }

        private readonly Stage[] _stages = new Stage[MaxStages];
        private int _count;
        private readonly int[] _chargeTick = new int[MaxChargeYaw];
        private readonly float[] _chargeYaw = new float[MaxChargeYaw];
        private int _chargeCount;
        private float _replantAt = -1f;
        private int _replantStage = -1;

        public bool Active { get; private set; }
        public int Serial { get; private set; }
        public int CastTick { get; private set; }
        public bool Ended { get; private set; }
        public int EndTick { get; private set; }
        public WreckEnd EndReason { get; private set; }
        public int StageCount => _count;

        public void Begin(int serial, int castTick, bool pinned = true)
        {
            Active = true;
            Serial = serial;
            CastTick = castTick;
            Ended = false;
            EndTick = int.MaxValue;
            EndReason = WreckEnd.Done;
            _count = 0;
            _chargeCount = 0;
            _replantAt = -1f;
            _replantStage = -1;
        }

        public void Stop() => Active = false;

        /// <summary>
        /// Этап из снимка: новый StageStartTick — новая запись, тот же — поправка сроков (отпускание заряда двигает удар).
        /// dirYaw — направление этапа в тик нажатия; last — после удара этого этапа серия кончается (нет «Четвёртого удара»).
        /// </summary>
        public void Track(int index, int start, int contact, int overhead, float dirYaw, bool last)
        {
            if (!Active || Ended && start >= EndTick) return;
            int at = Find(start);
            if (at < 0)
            {
                if (_count == MaxStages || (_count > 0 && start <= _stages[_count - 1].Start)) return;
                at = _count++;
                _stages[at] = new Stage
                {
                    Index = index, Start = start, Contact = contact, Overhead = overhead, ChargeStart = -1, ReleaseStart = -1,
                    DirYaw = dirYaw, StartYaw = float.NaN, Last = last,
                };
                return;
            }
            ref Stage s = ref _stages[at];
            s.Contact = contact;
            s.Last = last;
            if (s.ChargeStart >= 0 && s.ReleaseStart >= 0) s.ReleaseStart = contact - Math.Max(1, Simulation.WreckSlamDownTicks);
        }

        /// <summary>Девятый вал: заряд с тика «держат» (WreckChargeStarted или ChargeStartTick снимка).</summary>
        public void Charge(int start, int chargeStart)
        {
            int at = Find(start);
            if (at < 0 || chargeStart < 0) return;
            _stages[at].ChargeStart = chargeStart;
        }

        /// <summary>Отпустили: удар — contact, тело идёт к удару с кадра заряда за тики спуска.</summary>
        public void Release(int start, int contact)
        {
            int at = Find(start);
            if (at < 0 || _stages[at].ChargeStart < 0) return;
            _stages[at].Contact = contact;
            _stages[at].ReleaseStart = contact - Math.Max(1, Simulation.WreckSlamDownTicks);
        }

        /// <summary>Взгляд Sim в заряде (за курсором) — по тикам, показ берёт между ними.</summary>
        public void ChargeYaw(int tick, float yaw)
        {
            if (_chargeCount > 0 && tick <= _chargeTick[_chargeCount - 1])
            {
                if (tick == _chargeTick[_chargeCount - 1]) _chargeYaw[_chargeCount - 1] = yaw;
                return;
            }
            if (_chargeCount == MaxChargeYaw) return;
            _chargeTick[_chargeCount] = tick;
            _chargeYaw[_chargeCount++] = yaw;
        }

        /// <summary>Выход выпада: ходьба срывает с exitWalk, конец — exitEnd.</summary>
        public void Exit(int start, int exitWalk, int exitEnd)
        {
            int at = Find(start);
            if (at < 0) return;
            if (exitWalk > 0) _stages[at].ExitWalk = exitWalk;
            if (exitEnd > 0) _stages[at].ExitEnd = exitEnd;
        }

        /// <summary>WreckEnded (Amount — причина): дальше уборка якоря Stow или (срыв) конец показа.</summary>
        public void End(int tick, WreckEnd reason)
        {
            if (!Active || Ended) return;
            Ended = true;
            EndTick = tick;
            EndReason = reason;
        }

        /// <summary>Герой пошёл в окне: корень до следующего нажатия — у ArenaView.</summary>
        public void Replant(float now)
        {
            int at = At(now);
            if (at < 0 || _replantStage == at) return;
            _replantStage = at;
            _replantAt = now;
        }

        /// <summary>Взгляд, показанный до нажатия этапа (прошлый кадр): с него начинается поворот замаха.</summary>
        public void SetStageStartYaw(float yaw, float now)
        {
            int at = _count > 0 ? Math.Max(0, At(now)) : -1;
            if (at < 0 || !float.IsNaN(_stages[at].StartYaw)) return;
            _stages[at].StartYaw = yaw;
        }

        public bool NeedsStartYaw(float now)
        {
            int at = _count > 0 ? Math.Max(0, At(now)) : -1;
            return at >= 0 && float.IsNaN(_stages[at].StartYaw);
        }

        /// <summary>Последний этап с началом не позже now; −1 — до первого.</summary>
        private int At(float now)
        {
            for (int i = _count - 1; i >= 0; i--) if (_stages[i].Start <= now) return i;
            return -1;
        }

        private int Find(int start)
        {
            for (int i = _count - 1; i >= 0; i--) if (_stages[i].Start == start) return i;
            return -1;
        }
    }
}
