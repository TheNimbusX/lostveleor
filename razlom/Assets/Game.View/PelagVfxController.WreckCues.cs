using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v2 — очередь до тика показа и знаки на телах: всплеск маха по касательной с
    /// короткой отдачей тела (вес удара — отдачей тела цели, спека 2.3), тяжёлый всплеск круга,
    /// корона сбитого валом и обрушением, всплеск подхваченного стеной (несомый приподнят и
    /// откинут назад от хода стены, кадр B), капли взрыва Панциря.
    /// Рождение объектов — свои пулы Крушения (принятые префабы семьи под своими id: всплеск
    /// серии сабли, корона рывка, корона Вихря, веер Пенных волн), цвет — PelagWreckFormLook.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Кадр Крушения v2 (LateUpdate после Шквала, до UpdateActive). Дуга — в позднем кадре.</summary>
        private void UpdateWreckVfx()
        {
            Simulation sim = _driver.Sim;
            if (sim != _wkSim)
            {
                // Арену сменили: тел прежнего боя нет — очередь и всё живое Крушения отпускаются.
                ResetWreckVfx();
                _wkSim = sim;
            }
            if (sim == null || !WreckVfxReady) return;
            float shown = PelagWreckVfxRules.ShownTick(sim.Tick, _driver.Alpha);
            float dt = Time.deltaTime;
            while (_wkQueued > 0 && _wkQueue[0].Tick <= shown) RunWreckCue(0, false);
            UpdateWreckBodies(shown);
            UpdateWreckWaves(sim, shown, dt);
            UpdateWreckCraters(shown);
            UpdateWreckShell(sim, shown, dt);
            UpdateWreckIron(shown);
        }

        /// <summary>Поздний кадр (PelagWreckLateHook, после рига якоря): путь головы и дуга.</summary>
        private void LateWreckVfx()
        {
            if (!isActiveAndEnabled || _driver == null || _arena == null || (_driver.GameplayPaused && !ShowcaseRunning)) return;
            Simulation sim = _driver.Sim;
            if (sim == null || sim != _wkSim || !WreckVfxReady) return;
            UpdateWreckArcs(sim, PelagWreckVfxRules.ShownTick(sim.Tick, _driver.Alpha), Time.deltaTime);
            UpdateWreckSwings(sim, PelagWreckVfxRules.ShownTick(sim.Tick, _driver.Alpha), Time.deltaTime);
        }

        private void ResetWreckVfx()
        {
            _wkQueued = 0;
            _wkLastCue = WreckVfxCue.None;
            _wkLastCueTick = -1;
            _wkHasWave = false;
            ForgetWreckArcs();
            ForgetWreckWaves();
            ForgetWreckShell();
            ForgetWreckBodies();
            ForgetWreckIron();
            ForgetWreckSwings();
        }

        private void RunWreckCue(int index, bool force)
        {
            WkPending p = _wkQueue[index];
            for (int i = index + 1; i < _wkQueued; i++) _wkQueue[i - 1] = _wkQueue[i];
            _wkQueued--;
            if (CaptureRig.HasEnemyOverride)
            {
                // shown — тик показа в миг рождения знака (приёмка: shown ≥ tick и shown − tick < 1, иначе знак раньше тела).
                Simulation sim = _driver.Sim;
                float shown = sim != null ? PelagWreckVfxRules.ShownTick(sim.Tick, _driver.Alpha) : -1f;
                Debug.Log($"[wreck-vfx] cue {p.Kind} tick={p.Tick} shown={shown:F2} stage={p.Stage} target={p.Target} hit={p.Hit} form={p.Form} force={force}");
            }
            switch (p.Kind)
            {
                case WkKind.Stage: BeginWreckStageVfx(p); break;
                case WkKind.Contact: PlayWreckContact(p); break;
                case WkKind.Slam: PlayWreckSlam(p); break;
                case WkKind.ChargeStart: BeginWreckCharge(p); break;
                case WkKind.ChargeRelease: ReleaseWreckCharge(p); break;
                case WkKind.Catch: PlayWreckCatch(p); break;
                case WkKind.Crash: PlayWreckCrash(p); break;
                case WkKind.ShellHit: PlayWreckShellHit(p); break;
                case WkKind.Burst: BurstWreckShell(p); break;
                case WkKind.Hit: PlayWreckHit(p); break;
                case WkKind.End: EndWreckVfx(p); break;
            }
        }

        /// <summary>Задетый: всплеск на теле по силе удара, стоп-кадр, корона сбитого, отдача маха.</summary>
        private void PlayWreckHit(in WkPending p)
        {
            if (CaptureRig.NoVfx) return;
            Simulation sim = _driver.Sim;
            Vector3 hero = PlayerPosition();
            Vector3 body = EntityPosition(p.Target, p.At);
            Vector3 dir;
            switch (p.Hit)
            {
                case WreckVfxHit.Swing:
                    dir = WreckSwingDirection(hero, body);
                    WreckRecoil(p.Target, dir);
                    break;
                case WreckVfxHit.Wave:
                case WreckVfxHit.Wall:
                    dir = p.Wave.Dir.sqrMagnitude > .01f ? p.Wave.Dir : FlatDirection(hero, body);
                    break;
                case WreckVfxHit.Circle:
                    dir = FlatDirection(p.Wave.Impact, body);
                    break;
                case WreckVfxHit.Crash:
                    dir = FlatDirection(p.Wave.Origin + p.Wave.Dir * p.Wave.End, body);
                    break;
                default:
                    dir = FlatDirection(hero, body);
                    break;
            }
            // «Холодное железо» (база): махи и четвёртый — по форме серии, круг и вал — по форме удара; стена,
            // обрушение и взрыв Панциря — прежняя пена до переделки форм.
            bool iron = WreckIronReady && (p.Hit == WreckVfxHit.Swing || p.Hit == WreckVfxHit.Fourth || p.Hit == WreckVfxHit.Other
                ? _wkIronSeries : (p.Hit == WreckVfxHit.Circle || p.Hit == WreckVfxHit.Wave) && p.Form == PelagForm.None);
            // Махи и «Четвёртый» — сколы, вспышка, пыль, вмятина, отброс по весу (.WreckSwingHit); прочее — искра «железа».
            // 06.10: удачный знак маха раньше проваливался в ветку пены ниже — на каждом задетом махом рос всплеск пены
            // серии сабли (M_Sabre_Foam). У «железа» пены нет: знак маха или искра «железа».
            // 06.10 поздно: задетые выпадом (круг, вал, стена) — знак стека серии сабли крупнее (.WreckComboLungeBits).
            bool lungeMark = PlayWreckComboLungeMark(p, body, dir);
            if (!lungeMark && iron)
            {
                if (!PlayWreckSwingHit(p, body, dir)) PlayWreckIronHit(p, body, dir);
            }
            else if (!lungeMark)
            {
                WreckBodySplash(body + Vector3.up * .9f, dir + Vector3.up * .5f, PelagWreckVfxRules.HitSplashScale(p.Hit), p.Form);
                if (PelagWreckVfxRules.Knocks(p.Hit)) WreckKnock(sim, p.Target, body, dir, p.Form);
            }
            float hold = PelagWreckVfxRules.HitHoldSeconds(p.Hit);
            if (hold > 0f) _arena.HoldEntityPose(p.Target, hold);
        }

        /// <summary>Конец серии: якорь на спину — капли с цепи; оболочка тает без взрыва.</summary>
        private void EndWreckVfx(in WkPending p)
        {
            EndWreckArcs(p);
            DropWreckHumps(p.Serial);
            MeltWreckShell(p.Serial);
        }

        // ---- рождение объектов Крушения

        /// <summary>Объект пула в точке, цвет формы; масштаб — доля авторского. −1 — эффектов нет (NoVfx) или пула нет.</summary>
        private int WkSpawn(PelagVfxId id, Vector3 at, Quaternion rotation, float scale, float duration, PelagForm form, out GameObject go)
        {
            go = null;
            if (CaptureRig.NoVfx || !TryAcquire(id, out go, out PelagVfxElement element)) return -1;
            int index = ReserveActive();
            // Капли принятых префабов — до Begin: вспышка рождается уже в цвете формы.
            PelagWreckFormLook.ApplyDrops(go, form);
            element.Begin(at, rotation);
            if (scale > 0f && !Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            PelagWreckFormLook.Apply(go, form);
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = duration > 0f ? duration : Mathf.Max(.05f, element.DefaultLifetime),
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            return index;
        }

        private bool WkStill(int fx, GameObject go)
            => go != null && fx >= 0 && fx < _active.Length && _active[fx].Active && _active[fx].Object == go;

        /// <summary>Всплеск пены на теле — префаб всплеска серии сабли; перед поверхностью тела, к камере.</summary>
        private void WreckBodySplash(Vector3 at, Vector3 along, float scale, PelagForm form)
        {
            Camera camera = Camera.main;
            if (camera != null) at += (camera.transform.position - at).normalized * .45f;
            if (along.sqrMagnitude < 1e-4f) along = Vector3.up;
            WkSpawn(PelagVfxId.WreckSplash, at, Quaternion.LookRotation(along.normalized), scale, 0f, form, out _);
        }

        /// <summary>Сбит с ног: корона брызг Вихря у ног, клонится по удару; размер — по телу.</summary>
        private void WreckKnock(Simulation sim, int target, Vector3 body, Vector3 outward, PelagForm form)
        {
            float radius = sim != null && (uint)target < (uint)sim.Entities.Count
                ? sim.Entities.BodyRadius[target].ToFloat() : EntityStore.DefaultBodyRadius.ToFloat();
            var at = new Vector3(body.x, WreckGroundAt(body) + .02f, body.z);
            float scale = Mathf.Clamp(.6f + .8f * radius, .8f, 1.6f);
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            WkSpawn(PelagVfxId.WreckKnock, at, Quaternion.LookRotation(outward.normalized, Vector3.up), scale, 0f, form, out _);
        }

        /// <summary>Корона пены (префаб короны рывка) — у точки удара и у конца стены.</summary>
        private void WreckCrown(Vector3 foot, Vector3 forward, float scale, PelagForm form)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            WkSpawn(PelagVfxId.WreckCrown, foot, Quaternion.LookRotation(forward.normalized, Vector3.up), scale, 0f, form, out _);
        }

        /// <summary>Земля в точке (уступы арены; лагерь — навигация): тот же расчёт, что у воды Абордажа.</summary>
        private float WreckGroundAt(Vector3 near)
        {
            _abGroundBase = near.y;
            return AbordageGroundAt(near.x, near.z);
        }

        // ---- тела: отдача маха, подъём и наклон несомых стеной (только вид — SetPresentationOffset и поворот
        //      корня тела после ArenaView; Sim не меняется)

        private struct WkBody
        {
            public bool Active, Carried, Tilted;
            public int Target, CatchTick, CrashTick;
            public float RecoilAt;
            public Vector3 RecoilDir, TiltAxis;
            public Quaternion TiltBase, TiltWritten;
        }

        private readonly WkBody[] _wkBodies = new WkBody[16];

        private int WreckBodySlot(int target)
        {
            int free = -1;
            for (int i = 0; i < _wkBodies.Length; i++)
            {
                if (_wkBodies[i].Active && _wkBodies[i].Target == target) return i;
                if (!_wkBodies[i].Active && free < 0) free = i;
            }
            if (free < 0) { ReleaseWreckBody(ref _wkBodies[0]); free = 0; }
            _wkBodies[free] = new WkBody { Active = true, Target = target, RecoilAt = -100f, CatchTick = -1, CrashTick = -1 };
            return free;
        }

        private void WreckRecoil(int target, Vector3 dir)
        {
            if (target < 0) return;
            int i = WreckBodySlot(target);
            dir.y = 0f;
            _wkBodies[i].RecoilDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
            _wkBodies[i].RecoilAt = Time.time;
        }

        /// <param name="along">Ход стены: несомый откидывается назад от него (кадр B).</param>
        private void WreckCarry(int target, int catchTick, int crashTick, Vector3 along)
        {
            if (target < 0) return;
            int i = WreckBodySlot(target);
            _wkBodies[i].Carried = true;
            _wkBodies[i].CatchTick = catchTick;
            _wkBodies[i].CrashTick = crashTick;
            Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(along.x, 0f, along.z));
            _wkBodies[i].TiltAxis = axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector3.zero;
        }

        private void UpdateWreckBodies(float shown)
        {
            for (int i = 0; i < _wkBodies.Length; i++)
            {
                ref WkBody b = ref _wkBodies[i];
                if (!b.Active) continue;
                float since = Time.time - b.RecoilAt;
                float lift = b.Carried ? PelagWreckVfxRules.CarriedLiftAt(shown, b.CatchTick, b.CrashTick) : 0f;
                bool recoiling = since < PelagWreckVfxRules.RecoilSeconds;
                bool carrying = b.Carried && shown < b.CrashTick + PelagWreckVfxRules.CarriedDropTicks;
                _arena.SetPresentationOffset(b.Target, b.RecoilDir * PelagWreckVfxRules.Recoil(since) + Vector3.up * lift);
                if (b.Carried) WreckTilt(ref b, PelagWreckVfxRules.CarriedTiltAt(lift));
                if (recoiling || carrying) continue;
                ReleaseWreckBody(ref b);
            }
        }

        /// <summary>
        /// Наклон несомого: поворот корня тела вокруг оси поперёк хода стены (назад от хода). ArenaView пишет
        /// поворот тела заново каждый кадр раньше этого (LateUpdate без порядка против 1010); не переписал
        /// (тело скрыто) — основа прошлого кадра, наклон не копится.
        /// </summary>
        private void WreckTilt(ref WkBody b, float degrees)
        {
            if (_arena == null || b.TiltAxis == Vector3.zero || !_arena.TryGetEntityView(b.Target, out Transform view) || view == null)
            {
                b.Tilted = false;
                return;
            }
            Quaternion current = view.rotation;
            Quaternion basis = b.Tilted && Quaternion.Angle(current, b.TiltWritten) < .01f ? b.TiltBase : current;
            if (degrees <= .01f)
            {
                if (b.Tilted) view.rotation = basis;
                b.Tilted = false;
                return;
            }
            view.rotation = Quaternion.AngleAxis(-degrees, b.TiltAxis) * basis;
            b.TiltBase = basis;
            b.TiltWritten = view.rotation;
            b.Tilted = true;
        }

        private void ReleaseWreckBody(ref WkBody b)
        {
            if (b.Active && _arena != null)
            {
                _arena.SetPresentationOffset(b.Target, Vector3.zero);
                if (b.Tilted) WreckTilt(ref b, 0f);
            }
            b.Active = b.Tilted = false;
        }

        private void ForgetWreckBodies()
        {
            for (int i = 0; i < _wkBodies.Length; i++) ReleaseWreckBody(ref _wkBodies[i]);
        }
    }
}
