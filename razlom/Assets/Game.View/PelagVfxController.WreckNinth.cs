using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение · ДЕВЯТЫЙ ВАЛ (форма 12; 07.10; ассеты — Editor/PelagWreckComboVfxSetup.Ninth, числа — PelagWreckNinthLook;
    /// целевой кадр v4-form-ninthwave.png). Заряды Sim (WreckNinthSwingLanded: мах, задевший хоть одного врага, — заряд,
    /// один за мах) вид выводит из тех же событий в тик показа: первый задетый махом этапа 0 или 1 серии этой формы —
    /// заряд → индиговая вспышка и кольцо с лучами на голове якоря; голова дальше светится индиго (ореол позади головы,
    /// искорки за ней), с двумя зарядами — крупнее и гуще; в кадр выпада ореол лопается. Выпад: доля урона Sim (DamagePercent)
    /// — правда о зарядах; без зарядов выпад и знаки задетых рисуются ровно как у базы (форма снимается с очереди), с
    /// зарядами — стек выпада базы в индиго плюс свой веер гребней (.WreckNinthLunge). Знаки задетых — в индиго.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private int _wnSerial = -1, _wnLanded, _wnCharges;
        private bool _wnSeries;
        private int _wnGlowFx = -1, _wnChargeFx = -1;
        private GameObject _wnGlow, _wnCharge;
        private ParticleSystem _wnMotes;
        private float _wnGlowBorn, _wnGlowOutAt = -1f, _wnMoteCarry, _wnChargeBorn;
        private Vector3 _wnHeadLast;
        private bool _wnHasHead;

        private bool WreckNinthReady => _pools != null && (int)PelagVfxId.WreckNinthGlow < _pools.Length
            && _pools[(int)PelagVfxId.WreckNinthLunge] != null && _pools[(int)PelagVfxId.WreckNinthCharge] != null
            && _pools[(int)PelagVfxId.WreckNinthGlow] != null;

        /// <summary>
        /// Шаг очереди Крушения перед разбором (RunWreckCue): серия и её форма, заряды махами, выпад без зарядов — к базе
        /// (Look/Form снимаются, тогда его стек и знаки задетых в точности базовые), ореол лопается в кадр выпада.
        /// </summary>
        private void NoteWreckNinthCue(ref WkPending p)
        {
            switch (p.Kind)
            {
                case WkKind.Stage:
                    if (p.Serial != _wnSerial)
                    {
                        _wnSerial = p.Serial;
                        _wnLanded = _wnCharges = 0;
                        EndWreckNinthGlow();
                    }
                    _wnSeries = p.Form == PelagForm.WreckNinthWave;
                    break;
                case WkKind.Hit:
                    if (p.Hit == WreckVfxHit.Swing && _wnSeries)
                    {
                        // Правда о зарядах — Sim (WreckState.NinthCharges; он на 2 тика впереди показа): задетый махом в тик
                        // показа добавляет один заряд, пока счёт вида меньше счёта Sim. Этап по ходу маха вид угадывает не
                        // всегда (окна махов 1 и 2 перекрываются — раунд 1: Sim 2 заряда, вид 1).
                        Simulation sim = _driver.Sim;
                        int truth = sim != null && sim.Wreck.Serial == _wnSerial
                            ? Mathf.Min(sim.Wreck.NinthCharges, Simulation.WreckNinthMaxCharges) : 0;
                        if (_wnCharges >= truth) break;
                        int stage = p.Stage >= 0 ? p.Stage : _wsLastStage;
                        if (stage >= 0 && stage < 8) _wnLanded |= 1 << stage;
                        _wnCharges++;
                        PlayWreckNinthCharge();
                    }
                    else if ((p.Hit == WreckVfxHit.Circle || p.Hit == WreckVfxHit.Wave) && WreckNinthUncharged(p.Wave)) ToWreckBase(ref p);
                    break;
                case WkKind.Slam:
                    if (WreckNinthUncharged(p.Wave)) ToWreckBase(ref p);
                    if (p.Serial == _wnSerial) EndWreckNinthGlow();
                    break;
                case WkKind.End:
                    if (p.Serial == _wnSerial) EndWreckNinthGlow();
                    break;
            }
            if (CaptureRig.HasEnemyOverride && (p.Kind == WkKind.Slam && p.Wave.Look == PelagForm.WreckNinthWave || p.Kind == WkKind.Hit && p.Hit == WreckVfxHit.Swing && _wnSeries))
                Debug.Log($"[wreck-ninth] cue {p.Kind} tick={p.Tick} stage={p.Stage} charges={_wnCharges} landed={_wnLanded} percent={p.Wave.DamagePercent}");
        }

        private static bool WreckNinthUncharged(in WkWave wave)
            => wave.Look == PelagForm.WreckNinthWave && PelagWreckNinthLook.ChargesFromPercent(wave.DamagePercent) == 0;

        private static void ToWreckBase(ref WkPending p)
        {
            p.Wave.Look = PelagForm.None;
            p.Wave.Form = PelagForm.None;
            p.Form = PelagForm.None;
        }

        /// <summary>Заряд (тик показа задетого махом): вспышка и кольцо на голове якоря; ореол головы — по числу зарядов.</summary>
        private void PlayWreckNinthCharge()
        {
            if (!WreckNinthReady || CaptureRig.NoVfx || !WreckHeadPoint(out Vector3 head)) return;
            if (_wnCharge != null && WkStill(_wnChargeFx, _wnCharge)) Release(_wnChargeFx);
            if (!TryAcquire(PelagVfxId.WreckNinthCharge, out GameObject go, out PelagVfxElement element)) return;
            int index = ReserveActive();
            element.Begin(head, Quaternion.identity);
            go.transform.localScale = Vector3.one * PelagWreckNinthLook.FlareSize(_wnCharges);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WreckNinthCharge, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = head, End = head, Motion = Motion.Static, FollowIndex = -1
            };
            _wnChargeFx = index;
            _wnCharge = go;
            _wnChargeBorn = Time.time;
            if (_wnGlow == null || !WkStill(_wnGlowFx, _wnGlow))
            {
                if (!TryAcquire(PelagVfxId.WreckNinthGlow, out GameObject glow, out PelagVfxElement glowElement)) return;
                int g = ReserveActive();
                glowElement.Begin(head, Quaternion.identity);
                _active[g] = new ActiveFx
                {
                    Active = true, Id = PelagVfxId.WreckNinthGlow, Object = glow, Element = glowElement,
                    Duration = PelagWreckNinthLook.AuraMaxSeconds + 1f, Start = head, End = head, Motion = Motion.Static, FollowIndex = -1
                };
                _wnGlowFx = g;
                _wnGlow = glow;
                _wnMotes = glow.transform.Find("Motes")?.GetComponent<ParticleSystem>();
                _wnMoteCarry = 0f;
            }
            _wnGlowBorn = Time.time;
            _wnGlowOutAt = -1f;
            _wnHeadLast = head;
            _wnHasHead = true;
            _wnGlow.transform.localScale = Vector3.one * PelagWreckNinthLook.AuraScale(_wnCharges) * 1.3f;
            if (CaptureRig.HasEnemyOverride) Debug.Log($"[wreck-ninth] charge {_wnCharges} head={head.ToString("F2")}");
        }

        /// <summary>Ореол лопается: короткий перелёт и сжатие в ноль (жёстко, без мягкого таяния).</summary>
        private void EndWreckNinthGlow()
        {
            if (_wnGlow != null && WkStill(_wnGlowFx, _wnGlow) && _wnGlowOutAt < 0f) _wnGlowOutAt = Time.time;
        }

        /// <summary>Поздний кадр (после рига якоря): заряд и ореол — на голове якоря, искорки за ней.</summary>
        private void LateWreckNinth()
        {
            bool charge = _wnCharge != null && WkStill(_wnChargeFx, _wnCharge);
            bool glow = _wnGlow != null && WkStill(_wnGlowFx, _wnGlow);
            if (!charge && !glow) return;
            bool hasHead = WreckHeadPoint(out Vector3 head);
            if (!hasHead) head = _wnHeadLast;
            if (charge) _wnCharge.transform.position = head;
            if (!glow) { _wnHeadLast = head; return; }
            float now = Time.time;
            if (_wnGlowOutAt < 0f && now - _wnGlowBorn > PelagWreckNinthLook.AuraMaxSeconds) _wnGlowOutAt = now;
            float scale = PelagWreckNinthLook.AuraScale(_wnCharges);
            // Хлопок нового заряда: 1,3 → 1 за 0,08 с; дальше лёгкое дыхание.
            float since = now - _wnGlowBorn;
            scale *= 1f + .3f * Mathf.Clamp01(1f - since / .08f) + .04f * Mathf.Sin(since * 22f);
            if (_wnGlowOutAt >= 0f)
            {
                float k = (now - _wnGlowOutAt) / PelagWreckNinthLook.AuraOutSeconds;
                if (k >= 1f) { ReleaseWreckNinthGlow(); _wnHeadLast = head; return; }
                scale *= k < .35f ? 1f + .35f * k / .35f : 1.35f * (1f - (k - .35f) / .65f);
            }
            _wnGlow.transform.position = head;
            _wnGlow.transform.localScale = Vector3.one * Mathf.Max(.001f, scale);
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            Vector3 velocity = _wnHasHead ? (head - _wnHeadLast) / dt : Vector3.zero;
            _wnHeadLast = head;
            _wnHasHead = hasHead;
            if (_wnMotes == null || _wnGlowOutAt >= 0f || CaptureRig.NoVfx) return;
            _wnMoteCarry += PelagWreckNinthLook.MotesPerSecond(_wnCharges) * dt;
            while (_wnMoteCarry >= 1f)
            {
                _wnMoteCarry -= 1f;
                Vector3 at = head + Random.insideUnitSphere * .18f;
                Vector3 v = -velocity * .12f + Vector3.up * Random.Range(.3f, 1.1f) + Random.insideUnitSphere * .9f;
                WclEmit(_wnMotes, at, v, Random.value < .5f ? WnPale : WnViolet, Random.Range(.03f, .055f), Random.Range(.18f, .34f), 0f);
            }
        }

        private void ReleaseWreckNinthGlow()
        {
            if (_wnGlow != null && WkStill(_wnGlowFx, _wnGlow)) Release(_wnGlowFx);
            _wnGlow = null;
            _wnGlowFx = -1;
            _wnMotes = null;
            _wnGlowOutAt = -1f;
        }

        private void ForgetWreckNinth()
        {
            ReleaseWreckNinthGlow();
            if (_wnCharge != null && WkStill(_wnChargeFx, _wnCharge)) Release(_wnChargeFx);
            _wnCharge = null;
            _wnChargeFx = -1;
            _wnSerial = -1;
            _wnLanded = _wnCharges = 0;
            _wnSeries = false;
            ForgetWreckNinthLunge();
        }

        // ---- краска индиго на кусках общего стека (удар выпада, знаки задетых)

        // Вершинные цвета частиц — линейные; цвета материала (тень, ядро, контур) — sRGB, как у материалов Ninth.
        private static readonly Color WnViolet = new Color(.30f, .25f, 1.0f, 1f), WnPale = new Color(.72f, .68f, 1.15f, 1f),
            WnMarkIndigo = new Color(.19f, .14f, .92f, 1f);
        private static readonly Color WnShade = new Color(.62f, .54f, 1f, 1f), WnCore = new Color(1.0f, .97f, 1.32f, 1f),
            WnContour = new Color(.11f, .035f, .25f, .9f);
        private static readonly string[] WnLungeParts = { "Half", "Star", "StarEcho", "GroundStar", "Glow", "Sparks", "LaneSparks", "EndHalf" };
        private static readonly string[] WnMarkParts = { "Burst", "Sparks" };
        private MaterialPropertyBlock _wnTintBlock;
        private readonly System.Collections.Generic.HashSet<Renderer> _wnTinted = new System.Collections.Generic.HashSet<Renderer>();

        /// <summary>
        /// Девятый вал: у кусков общего стека тень у края — фиолетовая, ядро — бело-фиолетовое, контур — тёмное индиго (у базы
        /// синяя тень и чернила: индиго выходило синим). Объекты пула общие: кусок, который красил Девятый вал, у другой формы
        /// получает значения материала обратно — кроме кусков, которые только что покрасил Призрачный якорь (его краска
        /// зовётся раньше и своё не трогать). Куски, которых Девятый вал не касался, не трогаются вовсе.
        /// </summary>
        private void TintWreckNinthBits(Transform root, PelagForm form, bool mark)
        {
            bool ninth = form == PelagForm.WreckNinthWave;
            if (root == null) return;
            if (_wnTintBlock == null) _wnTintBlock = new MaterialPropertyBlock();
            // Знак задетого: основной тон звезды — индиго (у прочих форм цвет ставит TintWreckComboHit каждый раз сам).
            ParticleSystem burst = ninth && mark ? root.Find("Burst")?.GetComponent<ParticleSystem>() : null;
            if (burst != null) { var main = burst.main; main.startColor = WnMarkIndigo; }
            // Раунд 1: знак маха выходил почти весь белым — у этой формы белое только в сердцевине (блок звезды каждый раз
            // заново ставит/снимает стек базы, у прочих форм значение не протекает).
            Renderer star = burst != null ? burst.GetComponent<Renderer>() : null;
            if (star != null)
            {
                star.GetPropertyBlock(_wnTintBlock);
                _wnTintBlock.SetFloat(WclCoreWidthId, .62f);
                star.SetPropertyBlock(_wnTintBlock);
            }
            foreach (string name in mark ? WnMarkParts : WnLungeParts)
            {
                Renderer r = root.Find(name)?.GetComponent<Renderer>();
                Material m = r != null ? r.sharedMaterial : null;
                if (m == null || !m.HasProperty(WgShadeId) || !m.HasProperty(WgCoreId) || !m.HasProperty(WgOutlineId)) continue;
                if (!ninth)
                {
                    if (!_wnTinted.Remove(r)) continue;
                    if (form == PelagForm.WreckGhostAnchor && System.Array.IndexOf(mark ? WgMarkParts : WgLungeParts, name) >= 0) continue;
                }
                else _wnTinted.Add(r);
                r.GetPropertyBlock(_wnTintBlock);
                _wnTintBlock.SetColor(WgShadeId, ninth ? WnShade : m.GetColor(WgShadeId));
                _wnTintBlock.SetColor(WgCoreId, ninth ? WnCore : m.GetColor(WgCoreId));
                _wnTintBlock.SetColor(WgOutlineId, ninth ? WnContour : m.GetColor(WgOutlineId));
                r.SetPropertyBlock(_wnTintBlock);
            }
        }
    }
}
