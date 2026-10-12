using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Махи Крушения на стеке сабли — СРОКИ (07.10; разбор: мах 2 шёл «мимо цели» — вспышка на враге, а полумесяц справа от
    /// Пелага; вспышка маха 1 — на 3–4 кадра позже, чем волна дошла до цели). Sim бьёт по ходу головы: голова входит в
    /// сектор за 3 тика до контакта и бьёт каждого в тик, когда проходит его угол (Simulation.Wreck.Sweep). Поэтому:
    ///  • ЗАХОД: волна маха рождается не в контакт, а в тик входа головы Sim (нажатие этапа даёт контакт и направление,
    ///    тик входа — как WreckSweepTick: сдвиги до контакта сжимаются темпом замаха), и голова полумесяца идёт от края
    ///    входа к середине ровно к контакту (шейдер: _LeadSeconds), дальше раскадровка прежняя. Тех, кто стоит на стороне
    ///    входа, волна проходит в тот тик, когда Sim их бьёт;
    ///  • ЗНАК ПО ГОЛОВЕ ВОЛНЫ: удар маха (Damage → WreckEnqueueHit) ждёт не тика показа, а мига, когда голова полумесяца
    ///    проходит угол цели (PelagWreckComboLook.ArrivalSeconds), — как всплеск сабли в тик её волны; не раньше, чем удар
    ///    известен Sim (тик показа − 2), и не позже тика показа + 1.
    /// Мах сорван до контакта (рывок, оглушение) — волна снимается в тик контакта: Sim кончил мах там, где была голова.
    /// Секторы махов вид запоминает каждый кадр (NoteWreckSweep): серп, который делал это раньше, со стеком сабли не рисуется.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private struct WcArm
        {
            public bool Active;
            public int Serial, Stage, Contact, Start;
            public Vector3 Dir;
        }

        private struct WcWave
        {
            public bool Active, Confirmed;
            public int Fx, Serial, Stage, Contact, Side;
            public GameObject Object;
            /// <summary>Тик показа рождения частицы (заход мог начаться раньше кадра).</summary>
            public float Born;
            public Vector3 Center, Forward, HeadSide;
            public float HeadFrom, HeadSeconds;
            /// <summary>Въезд захода (V13, материал _LeadSlide): поза корня в контакт и поворот назад в рождение, град (0 — нет).</summary>
            public Quaternion Rotation;
            public float Slide;
        }

        private readonly WcArm[] _wcArms = new WcArm[2];
        private readonly WcWave[] _wcWaves = new WcWave[3];
        private readonly List<WkPending> _wcHits = new List<WkPending>(16);
        private static readonly int WcHeadFromId = Shader.PropertyToID("_HeadFrom"), WcHeadSecondsId = Shader.PropertyToID("_HeadSeconds");

        /// <summary>Нажатие маха 1 или 2 (тик показа, из BeginWreckSwingVfx): контакт и направление этапа — волна родится в тик входа головы Sim.</summary>
        private void ArmWreckComboSwing(in WkPending p)
        {
            if (!WreckComboReady || CaptureRig.NoVfx || p.Serial < 0 || (p.Stage != 0 && p.Stage != 1)) return;
            Simulation sim = _driver.Sim;
            Vector3 dir = sim != null && sim.Wreck.Serial == p.Serial && sim.Wreck.Stage == p.Stage
                ? WreckWorld(sim.Wreck.Direction, 0f) : PlayerFacing();
            dir.y = 0f;
            dir = dir.sqrMagnitude > .01f ? dir.normalized : PlayerFacing();
            int windup = Mathf.Max(0, p.Contact - p.Tick);
            int baseWindup = p.Stage == 0 ? Simulation.WreckSwingWindupTicks : Simulation.WreckBackswingWindupTicks;
            int lead = Mathf.Min(PelagWreckComboLook.LeadTicks, (PelagWreckComboLook.LeadTicks * windup + baseWindup / 2) / baseWindup);
            _wcArms[p.Stage] = new WcArm { Active = true, Serial = p.Serial, Stage = p.Stage, Contact = p.Contact, Start = p.Contact - lead, Dir = dir };
        }

        /// <summary>Кадр (из UpdateWreckVfx, после очереди): заходы махов, снятие сорванных, знаки по голове волны.</summary>
        private void UpdateWreckCombo(float shown)
        {
            // Сектор каждого маха — пока он последний у Sim (кадр за кадром, как делал серп): иначе удар маха 1 в стык с
            // махом 2 (Sim добивает его остаток в тик входа маха 2) записывался махом 2 — знак ждал не ту волну.
            NoteWreckSweep(_driver.Sim);
            for (int i = 0; i < _wcArms.Length; i++)
            {
                WcArm arm = _wcArms[i];
                if (!arm.Active || shown < arm.Start) continue;
                _wcArms[i].Active = false;
                // Контакт уже показан (длинный кадр) — волну родил он.
                if (shown >= arm.Contact || WreckComboWaveAt(arm.Serial, arm.Stage) >= 0) continue;
                float skip = Mathf.Max(0f, shown - (arm.Contact - PelagWreckComboLook.LeadTicks)) / Simulation.TicksPerSecond;
                SpawnWreckComboSwing(arm.Stage, arm.Serial, arm.Contact, arm.Dir, PlayerPosition(), skip, false);
            }
            for (int i = 0; i < _wcWaves.Length; i++)
            {
                if (!_wcWaves[i].Active) continue;
                if (!WkStill(_wcWaves[i].Fx, _wcWaves[i].Object)) { _wcWaves[i].Active = false; continue; }
                SlideWreckComboWave(i, shown);
                if (_wcWaves[i].Confirmed || shown < _wcWaves[i].Contact + 1f) continue;
                Release(_wcWaves[i].Fx);
                _wcWaves[i].Active = false;
            }
            for (int i = 0; i < _wcHits.Count;)
            {
                WkPending hit = _wcHits[i];
                float due = WreckComboHitDue(hit, out float azimuth);
                if (shown < due) { i++; continue; }
                _wcHits.RemoveAt(i);
                if (CaptureRig.HasEnemyOverride)
                    Debug.Log($"[wreck-combo-hit] stage={hit.Stage} tick={hit.Tick} target={hit.Target} due={due:F2} shown={shown:F2} azimuth={azimuth:F1}");
                // Тот же путь, что у очереди (заряды Девятого вала, журнал, PlayWreckHit), — в свой миг.
                WreckEnqueue(hit);
                RunWreckCue(_wkQueued - 1, false);
            }
        }

        /// <summary>Damage слота Крушения (TakeWreckDamage): удар маха 1–2 ждёт головы волны; прочее — в очередь, как было.</summary>
        private void WreckEnqueueHit(in WkPending p)
        {
            if (p.Hit != WreckVfxHit.Swing || (p.Stage != 0 && p.Stage != 1) || !WreckComboReady || CaptureRig.NoVfx)
            {
                WreckEnqueue(p);
                return;
            }
            WkPending hit = p;
            hit.Serial = _wsSweepSerial;
            _wcHits.Add(hit);
        }

        /// <summary>
        /// Тик показа знака: миг, когда голова волны этого маха проходит угол цели. Волны ещё нет, но заход впереди — ждать
        /// (не дольше тика показа + 1); волны не будет — тик показа, как было.
        /// </summary>
        private float WreckComboHitDue(in WkPending hit, out float azimuth)
        {
            azimuth = float.NaN;
            int i = WreckComboWaveAt(hit.Serial, hit.Stage);
            if (i < 0) return WreckComboArmed(hit.Serial, hit.Stage) ? hit.Tick + 1f : hit.Tick;
            WcWave w = _wcWaves[i];
            Vector3 d = EntityPosition(hit.Target, hit.At) - w.Center;
            azimuth = Mathf.Atan2(d.x * w.HeadSide.x + d.z * w.HeadSide.z, d.x * w.Forward.x + d.z * w.Forward.z) * Mathf.Rad2Deg;
            // Голова касается тела раньше, чем проходит его середину: ближний край круга тела.
            Simulation sim = _driver.Sim;
            float body = sim != null && (uint)hit.Target < (uint)sim.Entities.Count ? sim.Entities.BodyRadius[hit.Target].ToFloat() : .5f;
            float distance = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            if (distance > .01f) azimuth -= Mathf.Asin(Mathf.Min(.9f, body / distance)) * Mathf.Rad2Deg;
            float t = PelagWreckComboLook.ArrivalSeconds(azimuth, PelagWreckComboLook.LeadSeconds, PelagWreckComboLook.LeadFrom,
                w.HeadFrom, w.HeadSeconds, PelagWreckComboLook.SpinDegrees[w.Side]);
            return Mathf.Clamp(w.Born + t * Simulation.TicksPerSecond, hit.Tick - 2f, hit.Tick + 1f);
        }

        /// <summary>Запомнить рождённую волну маха; возвращает тик показа этого кадра.</summary>
        private float NoteWreckComboWave(int fx, GameObject go, int serial, int stage, int contact, int side, Vector3 center,
            Vector3 forward, Vector3 headSide, float skip, bool confirmed)
        {
            Simulation sim = _driver.Sim;
            float shown = sim != null ? PelagWreckVfxRules.ShownTick(sim.Tick, _driver.Alpha) : contact;
            int slot = 0;
            for (int i = 0; i < _wcWaves.Length; i++)
            {
                if (!_wcWaves[i].Active) { slot = i; break; }
                if (_wcWaves[i].Born < _wcWaves[slot].Born) slot = i;
            }
            Material m = go.transform.Find("Wave")?.GetComponent<Renderer>()?.sharedMaterial;
            headSide.y = 0f;
            _wcWaves[slot] = new WcWave
            {
                Active = true, Confirmed = confirmed, Fx = fx, Object = go, Serial = serial, Stage = stage, Contact = contact, Side = side,
                Born = shown - skip * Simulation.TicksPerSecond, Center = center, Forward = forward, HeadSide = headSide.normalized,
                HeadFrom = m != null && m.HasProperty(WcHeadFromId) ? m.GetFloat(WcHeadFromId) : .40f,
                HeadSeconds = m != null && m.HasProperty(WcHeadSecondsId) ? m.GetFloat(WcHeadSecondsId) : .10f,
                Rotation = go.transform.rotation
            };
            if (m != null && m.HasProperty(WcLeadSlideId) && m.GetFloat(WcLeadSlideId) > .5f)
                _wcWaves[slot].Slide = (_wcWaves[slot].HeadFrom - PelagWreckComboLook.LeadFrom) * PelagWreckComboLook.SpanDegrees;
            SlideWreckComboWave(slot, shown);
            return shown;
        }

        private static readonly int WcLeadSlideId = Shader.PropertyToID("_LeadSlide");

        /// <summary>
        /// Въезд захода (V13): весь полумесяц повёрнут назад к краю входа на Slide·(1 − доля захода) вокруг своей нормали
        /// (+ — к хвосту) и к контакту встаёт в свою позу; голова по азимуту — та же прямая, что у шейдера без въезда
        /// (PelagWreckComboLook.HeadAzimuth), — сроки знаков не меняются. Хвост за краем входа срезает шейдер.
        /// </summary>
        private void SlideWreckComboWave(int i, float shown)
        {
            if (_wcWaves[i].Slide <= 0f || _wcWaves[i].Object == null) return;
            float p = Mathf.Clamp01((shown - _wcWaves[i].Born) / Mathf.Max(.001f, PelagWreckComboLook.LeadTicks));
            Transform root = _wcWaves[i].Object.transform;
            root.rotation = _wcWaves[i].Rotation * Quaternion.AngleAxis(_wcWaves[i].Slide * (1f - p), Vector3.forward);
            if (p >= 1f) _wcWaves[i].Slide = 0f;
        }

        private int WreckComboWaveAt(int serial, int stage)
        {
            for (int i = 0; i < _wcWaves.Length; i++)
                if (_wcWaves[i].Active && _wcWaves[i].Serial == serial && _wcWaves[i].Stage == stage && WkStill(_wcWaves[i].Fx, _wcWaves[i].Object))
                    return i;
            return -1;
        }

        private bool WreckComboArmed(int serial, int stage)
        {
            foreach (WcArm arm in _wcArms)
                if (arm.Active && arm.Serial == serial && arm.Stage == stage) return true;
            return false;
        }

        /// <summary>Контакт маха: волна этого маха уже идёт с захода — отметить; заход, не успевший родить волну, снять.</summary>
        private bool ConfirmWreckComboWave(int serial, int stage)
        {
            for (int i = 0; i < _wcArms.Length; i++)
                if (_wcArms[i].Serial == serial && _wcArms[i].Stage == stage) _wcArms[i].Active = false;
            int at = WreckComboWaveAt(serial, stage);
            if (at < 0) return false;
            _wcWaves[at].Confirmed = true;
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-combo-swing] contact stage={stage} serial={serial} born={_wcWaves[at].Born:F2}");
            return true;
        }

        private void ForgetWreckCombo()
        {
            for (int i = 0; i < _wcArms.Length; i++) _wcArms[i].Active = false;
            for (int i = 0; i < _wcWaves.Length; i++) _wcWaves[i].Active = false;
            _wcHits.Clear();
        }
    }
}
