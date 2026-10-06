using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Абордаж v2 — якорь, цепь и вода на ней (кадр A-base). Якорь летит ПРЯМО от
    /// правой руки к точке укуса текущей цели (самонаведение, как якорь Sim) и
    /// приходит ровно в тик зацепа показа; цепь из левого кулака всегда натянута
    /// (прямая от хвата этого кадра до кольца головы, без провиса и дуги).
    /// Вдоль цепи — тонкая лента воды (чуть ниже звеньев по экрану, чтобы не
    /// закрывать металл), в натяг она шире и вода бежит по ней к герою, с цепи
    /// сыплются капли; за головой в полёте — короткий росчерк воды (жизнь ~0,15 с).
    /// После удара (или срыва) якорь за 0,2 с возвращается к руке, лента рвётся на
    /// капли, голову принимает пояс (PelagAnchorSlamView.ReturnFlyingAnchor, как у
    /// прежнего броска). Цель пропала до зацепа (Recall в снимке) — якорь назад за
    /// AbordageRecallTicks. Голова и цепь — те же префабы, что у прежнего броска, но
    /// свои пулы (AbordageAnchor/AbordageChain): прежний путь и Шквал их не трогают.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class AbordageRun
        {
            public bool Active, Released, HasHook, Bitten, Punched, Landed, Hull, Ended, Recall, HandedOff, HasBite, StreakDone;
            public int Serial = -1, Slot = -1, Target = -1, ReleaseTick, BiteTick, ArriveTick;
            public PelagForm Form;
            public AbordageEnd EndReason;
            public Simulation Sim;
            public Vector3 From, To, HandStart, LastBite, Head, StreakTail;
            public float RetractStart = -1f, SleeveFlow, StreakFlow, DropCarry, StreakCarry;
            public Vector3 RetractFrom;
            public int HeadFx = -1, ChainFx = -1, SleeveFx = -1, StreakFx = -1, WakeSlot = -1;
            public GameObject HeadObject, ChainObject, SleeveObject, StreakObject;
            public PelagVfxElement HeadElement, ChainElement;
            public MeshFilter SleeveFilter, StreakFilter;
            public ParticleSystem SleeveDrops, SleeveFoam, StreakDrops;
            public readonly PelagAbordageRibbon Sleeve = new PelagAbordageRibbon(), Streak = new PelagAbordageRibbon();
        }

        private readonly AbordageRun _abRun = new AbordageRun();
        /// <summary>Хват и кольцо головы — две точки прямой цепи (SetCurvePoints).</summary>
        private readonly Vector3[] _abChainLine = new Vector3[2];

        /// <summary>Лента воды ниже оси цепи по экрану, м: звенья видны над водой (вода рисуется поверх всего, кроме тел).</summary>
        private const float AbSleeveDrop = .07f;
        /// <summary>Вода бежит по ленте к герою, м/с: в полёте, в натяг (тяга), после удара.</summary>
        private const float AbSleeveFlowFlight = 1.5f, AbSleeveFlowTaut = 4.5f, AbSleeveFlowSlack = 2f;
        /// <summary>Росчерк: вода бежит к хвосту, м/с; капли с головы, штук в секунду.</summary>
        private const float AbStreakFlow = 6f, AbStreakDropRate = 70f;

        /// <summary>Сколько голов Абордажа сейчас в воздухе (VisibleFlyingAnchors: пояс не рисует второй якорь).</summary>
        private int AbordageFlyingAnchors
            => _abRun.Active && AbStill(_abRun.HeadFx, _abRun.HeadObject) && _abRun.HeadObject.activeInHierarchy ? 1 : 0;

        /// <summary>Выпуск якоря (AbordageThrow): голова появится в тик показа выпуска.</summary>
        private void BeginAbordageRun(Simulation sim, int serial, int slot, int target, int releaseTick, int biteTick, Vector3 hero)
        {
            if (_abRun.Active) FinishAbordageRun(false);
            AbordageRun run = _abRun;
            run.Active = true;
            run.Released = run.HasHook = run.Bitten = run.Punched = run.Landed = run.Hull = false;
            run.Ended = run.Recall = run.HandedOff = run.HasBite = run.StreakDone = false;
            run.Sim = sim;
            run.Serial = serial;
            run.Slot = slot;
            run.Target = target;
            run.ReleaseTick = releaseTick;
            run.BiteTick = biteTick;
            run.ArriveTick = biteTick + 1;
            run.Form = (uint)slot < Simulation.AbilitySlots ? sim.FormAt(slot) : PelagForm.None;
            run.From = run.To = run.HandStart = run.Head = hero;
            run.RetractStart = -1f;
            run.SleeveFlow = run.StreakFlow = run.DropCarry = run.StreakCarry = 0f;
            run.HeadFx = run.ChainFx = run.SleeveFx = run.StreakFx = run.WakeSlot = -1;
            run.HeadObject = run.ChainObject = run.SleeveObject = run.StreakObject = null;
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[abordage-vfx] throw serial={serial} slot={slot} form={run.Form} target={target} release={releaseTick} bite={biteTick}");
        }

        /// <summary>Тик показа выпуска: голова отрывается от руки, цепь, лента воды и росчерк — от неё.</summary>
        private void ReleaseAbordageAnchor(AbordageRun run, Simulation sim)
        {
            run.Released = true;
            Vector3 hand = _arena.PlayerAnchorHeadPosition;
            if (hand == Vector3.zero) hand = ChainHandPosition();
            run.HandStart = run.Head = run.StreakTail = hand;
            Vector3 dir = FlatDirection(hand, AbordageBitePoint(run, sim, false, hand));
            // Голова и цепь — снаряжение: видны и в съёмке без эффектов (как у прежнего броска).
            run.HeadFx = AbSpawnPart(PelagVfxId.AbordageAnchor, hand, Quaternion.LookRotation(dir, Vector3.up), out run.HeadObject, out run.HeadElement);
            run.HeadElement?.SetTrailEmission(false);
            run.ChainFx = AbSpawnPart(PelagVfxId.AbordageChain, hand, Quaternion.identity, out run.ChainObject, out run.ChainElement);
            run.SleeveFx = AbSpawnRibbon(run, hand, out run.SleeveObject, out run.SleeveFilter, out run.SleeveDrops, out run.SleeveFoam);
            run.Sleeve.Begin();
            run.StreakFx = AbSpawnRibbon(run, hand, out run.StreakObject, out run.StreakFilter, out run.StreakDrops, out _);
            run.Streak.Begin();
            EmitAbordageDrops(run.SleeveDrops, run.Form, hand, hand, dir, 6, 1.4f);
        }

        /// <summary>Голова или цепь из своего пула, без цвета формы и без запрета NoVfx.</summary>
        private int AbSpawnPart(PelagVfxId id, Vector3 at, Quaternion rotation, out GameObject go, out PelagVfxElement element)
        {
            if (!TryAcquire(id, out go, out element)) { go = null; element = null; return -1; }
            int index = ReserveActive();
            element.Begin(at, rotation);
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element, Duration = 6f,
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            return index;
        }

        private int AbSpawnRibbon(AbordageRun run, Vector3 at, out GameObject go, out MeshFilter filter, out ParticleSystem drops, out ParticleSystem foam)
        {
            filter = null; drops = foam = null;
            int fx = AbSpawn(PelagVfxId.AbordageRibbon, at, Quaternion.identity, 0f, 6f, run.Form, out go);
            if (fx < 0) return -1;
            go.transform.localScale = Vector3.one;
            filter = go.transform.Find("Water")?.GetComponent<MeshFilter>();
            drops = go.transform.Find("Drops")?.GetComponent<ParticleSystem>();
            foam = go.transform.Find("Foam")?.GetComponent<ParticleSystem>();
            return fx;
        }

        /// <summary>Якорь назад к руке с тика <paramref name="tick"/> (удар, срыв, цель пропала).</summary>
        private void StartAbordageRetract(float tick)
        {
            if (!_abRun.Active || _abRun.RetractStart >= 0f) return;
            _abRun.RetractStart = tick;
            _abRun.RetractFrom = _abRun.Head;
            if (_abRun.Released && !CaptureRig.NoVfx)
                EmitAbordageDrops(_abRun.SleeveDrops, _abRun.Form, ChainHandPosition(), _abRun.Head, Vector3.up, 10, 1.2f);
        }

        /// <summary>Зацеп: цепь рывком выпрямилась — с неё срывается горсть капель.</summary>
        private void SnapAbordageChain()
        {
            if (!_abRun.Released || CaptureRig.NoVfx) return;
            Vector3 hand = ChainHandPosition();
            EmitAbordageDrops(_abRun.SleeveDrops, _abRun.Form, hand, _abRun.Head, Vector3.up, 18, 2f);
        }

        /// <summary>Точка укуса на теле цели (грудь, видимая поверхность со стороны героя; босс — точка корпуса из Sim).</summary>
        private Vector3 AbordageBitePoint(AbordageRun run, Simulation sim, bool live, Vector3 hand)
        {
            EntityStore e = sim.Entities;
            int target = run.Target;
            if ((uint)target < (uint)e.Count && (e.Alive[target] || _arena.TryGetEntityView(target, out _)))
            {
                Vector3 body = EntityPosition(target, e.Position[target]);
                float r = e.BodyRadius[target].ToFloat();
                Vector3 offset;
                if (run.Hull && live && run.HasHook)
                {
                    FixVec2 rel = sim.Abordage.AnchorAt - e.Position[target];
                    offset = new Vector3(rel.X.ToFloat(), 0f, rel.Y.ToFloat());
                }
                // Край тела Sim (r) шире модели: голова встаёт на видимую грудь, а не в воздух перед ней.
                else offset = -FlatDirection(PlayerPosition(), body) * PelagAbordageVfxRules.VisibleBodyRadius(r);
                run.LastBite = body + offset + Vector3.up * PelagAbordageVfxRules.BiteHeight(r);
                run.HasBite = true;
                return run.LastBite;
            }
            return run.HasBite ? run.LastBite : hand + PlayerFacing() * 2f;
        }

        /// <summary>Кадр якоря: где голова, натяжение цепи, вода на ней, росчерк, возврат и передача поясу.</summary>
        private void UpdateAbordageAnchor(Simulation sim, float shown, float dt)
        {
            AbordageRun run = _abRun;
            if (!run.Active) return;
            if (run.Sim != sim) { FinishAbordageRun(false); return; }
            AbordageState state = sim.Abordage;
            bool live = state.Serial == run.Serial;
            if (live && !run.HasHook && state.Phase == AbordagePhase.Hook)
            {
                // Перевыбор цели в полёте (Sim): якорь доворачивает к новой, зацеп может сдвинуться.
                run.BiteTick = state.BiteTick;
                if (state.Target >= 0) run.Target = state.Target;
            }
            if (live && state.Phase == AbordagePhase.Recall && !run.Recall)
            {
                run.Recall = true;
                if (run.Released) StartAbordageRetract(shown);
            }
            if (!run.Released)
            {
                if (run.Recall || shown < run.ReleaseTick) return;
                ReleaseAbordageAnchor(run, sim);
            }

            Vector3 hand = ChainHandPosition();
            Vector3 bite = AbordageBitePoint(run, sim, live, hand);
            int retractTicks = run.Recall ? Simulation.AbordageRecallTicks : PelagAbordageVfxRules.RetractTicks;
            float back = run.RetractStart >= 0f ? PelagAbordageVfxRules.Retract(shown, run.RetractStart, retractTicks) : 0f;
            float travel = PelagAbordageVfxRules.AnchorTravel(shown, run.ReleaseTick, run.BiteTick);
            Vector3 head = run.RetractStart >= 0f ? Vector3.Lerp(run.RetractFrom, hand, back)
                : shown < run.BiteTick ? Vector3.Lerp(run.HandStart, bite, travel) : bite;
            run.Head = head;
            Vector3 outward = FlatDirection(hand, head);

            Vector3 ring = head;
            if (AbStill(run.HeadFx, run.HeadObject))
            {
                run.HeadObject.transform.SetPositionAndRotation(head, Quaternion.LookRotation(outward, Vector3.up));
                Transform spinner = run.HeadElement.Spinner;
                float roll = run.RetractStart >= 0f ? Mathf.Lerp(0f, 25f, back) : shown < run.BiteTick ? Mathf.Lerp(-40f, 0f, travel) : 0f;
                if (spinner != null) spinner.localRotation = Quaternion.Euler(0f, 0f, roll);
                ring = run.HeadElement.AnchorRingPosition;
            }
            // Цепь всегда прямая — от хвата этого кадра до кольца головы (спека 5). Решатель звеньев с
            // запасом выдачи отставал от кулака на резком развороте броска и выгибал цепь дугой мимо
            // ленты воды (ревью 03.10, каст по тяжёлому за спиной).
            if (AbStill(run.ChainFx, run.ChainObject))
            {
                _abChainLine[0] = hand;
                _abChainLine[1] = ring;
                run.ChainElement.SetCurvePoints(_abChainLine);
            }

            UpdateAbordageSleeve(run, shown, dt, hand, ring);
            UpdateAbordageStreak(run, shown, dt, head, outward);

            if (run.RetractStart >= 0f && shown >= run.RetractStart + retractTicks)
            {
                HandOffAbordageAnchor(run);
                if (run.Ended) FinishAbordageRun(true);
            }
        }

        private void UpdateAbordageSleeve(AbordageRun run, float shown, float dt, Vector3 hand, Vector3 ring)
        {
            if (!AbStill(run.SleeveFx, run.SleeveObject)) return;
            float release = run.RetractStart;
            float tension = PelagAbordageVfxRules.Tension(shown, run.BiteTick, release);
            float half = PelagAbordageVfxRules.SleeveHalfWidth(tension);
            float age = PelagAbordageVfxRules.SleeveBreakAge(shown, release);
            run.SleeveFlow += dt * (shown < run.BiteTick ? AbSleeveFlowFlight : release < 0f ? AbSleeveFlowTaut : AbSleeveFlowSlack);
            float appear = PelagAbordageVfxRules.Smooth01((shown - run.ReleaseTick) / 1.2f);
            Camera camera = Camera.main;
            Vector3 eye = camera != null ? camera.transform.position : hand + Vector3.up * 10f;
            Vector3 down = camera != null ? -camera.transform.up : Vector3.down;
            run.SleeveObject.transform.position = hand;
            if (run.SleeveFilter != null)
                run.Sleeve.Build(FormWaterMesh.MeshFor(run.SleeveFilter, PelagAbordageRibbon.MeshName), hand, hand, ring, eye, down, AbSleeveDrop,
                    half * .6f, half, age, age, run.SleeveFlow, .05f + .25f * tension, appear, Time.time);
            // Капли с цепи: в натяг сыплет, вода бежит к герою — капли летят по цепи к руке и в стороны.
            if (release < 0f && dt > 0f && !CaptureRig.NoVfx)
            {
                run.DropCarry += dt * PelagAbordageVfxRules.SleeveDropRate(tension) * Vector3.Distance(hand, ring);
                int count = (int)run.DropCarry;
                run.DropCarry -= count;
                if (count > 0) EmitAbordageDrops(run.SleeveDrops, run.Form, hand, ring, -(ring - hand).normalized * tension, count, 1f);
            }
            if (age > .55f) { Release(run.SleeveFx); run.SleeveFx = -1; run.SleeveObject = null; }
        }

        private void UpdateAbordageStreak(AbordageRun run, float shown, float dt, Vector3 head, Vector3 outward)
        {
            if (run.StreakDone || !AbStill(run.StreakFx, run.StreakObject)) return;
            bool flying = shown < run.BiteTick && run.RetractStart < 0f;
            if (flying) run.StreakTail = head - outward * PelagAbordageVfxRules.StreakLength(Vector3.Distance(run.HandStart, head));
            float ageTail = PelagAbordageVfxRules.StreakAge(shown, run.BiteTick, 0f);
            float ageHead = PelagAbordageVfxRules.StreakAge(shown, run.BiteTick, 1f);
            run.StreakFlow += dt * AbStreakFlow;
            Camera camera = Camera.main;
            Vector3 eye = camera != null ? camera.transform.position : head + Vector3.up * 10f;
            run.StreakObject.transform.position = run.StreakTail;
            if (run.StreakFilter != null)
                run.Streak.Build(FormWaterMesh.MeshFor(run.StreakFilter, PelagAbordageRibbon.MeshName), run.StreakTail, run.StreakTail, head,
                    eye, Vector3.down, 0f, .018f, .07f, ageTail, ageHead, run.StreakFlow, .25f, 1f, Time.time);
            if (flying && dt > 0f && !CaptureRig.NoVfx)
            {
                run.StreakCarry += dt * AbStreakDropRate;
                int count = (int)run.StreakCarry;
                run.StreakCarry -= count;
                if (count > 0) EmitAbordageDrops(run.StreakDrops, run.Form, head - outward * .15f, head, -outward, count, 1.2f);
            }
            if (ageTail > .5f || ageHead > .45f)
            {
                run.StreakDone = true;
                Release(run.StreakFx);
                run.StreakFx = -1;
                run.StreakObject = null;
            }
        }

        /// <summary>
        /// Капли воды с отрезка a–b: вылет вдоль <paramref name="along"/> (к руке по цепи, назад
        /// за головой), в стороны и вверх; падают и тают порогом. Цвет — белая пена с водой формы.
        /// </summary>
        private static void EmitAbordageDrops(ParticleSystem system, PelagForm form, Vector3 a, Vector3 b, Vector3 along, int count, float strength)
        {
            if (system == null || count <= 0) return;
            Vector3 axis = b - a;
            Vector3 side = Vector3.Cross(axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector3.forward, Vector3.up);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = Vector3.Lerp(a, b, Random.value) + Random.insideUnitSphere * .03f;
                float sideways = Random.Range(-1f, 1f);
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = (along * Random.Range(1f, 3f) + side * sideways * Random.Range(.5f, 1.3f)
                                + Vector3.up * Random.Range(.4f, 1.4f)) * strength,
                    startSize = Random.Range(.035f, .075f),
                    startLifetime = Random.Range(.25f, .40f),
                    startColor = PelagAbordageFormLook.DropColor(form, Random.Range(.2f, .8f)),
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>Голова у руки: её принимает пояс (как прежний бросок), голова, цепь и вода уходят в пул.</summary>
        private void HandOffAbordageAnchor(AbordageRun run)
        {
            if (run.HandedOff) return;
            run.HandedOff = true;
            if (AbStill(run.HeadFx, run.HeadObject) && _driver.Sim != null && _driver.Sim.Entities.Alive[Simulation.PlayerId]
                && _arena.TryGetEntityView(Simulation.PlayerId, out Transform body))
            {
                Transform head = run.HeadElement.Spinner ?? run.HeadObject.transform;
                body.GetComponent<PelagAnchorSlamView>()?.ReturnFlyingAnchor(head.position, head.rotation);
            }
            if (AbStill(run.HeadFx, run.HeadObject)) Release(run.HeadFx);
            if (AbStill(run.ChainFx, run.ChainObject)) Release(run.ChainFx);
            if (AbStill(run.StreakFx, run.StreakObject)) Release(run.StreakFx);
            // Лента доживает распадом на капли сама (UpdateAbordageSleeve), если ещё жива — уходит сейчас.
            if (AbStill(run.SleeveFx, run.SleeveObject)) Release(run.SleeveFx);
            run.HeadFx = run.ChainFx = run.StreakFx = run.SleeveFx = -1;
            run.HeadObject = run.ChainObject = run.StreakObject = run.SleeveObject = null;
        }

        /// <summary>Каст кончился (или сменился новым): голову — поясу, след — гаснуть; <paramref name="endUse"/> — снять хват якоря.</summary>
        private void FinishAbordageRun(bool endUse)
        {
            AbordageRun run = _abRun;
            if (!run.Active) return;
            if (run.Released) HandOffAbordageAnchor(run);
            EndAbordageWake();
            run.Active = false;
            if (endUse) _arena?.EndPlayerAnchorUse();
            if (CaptureRig.HasEnemyOverride) Debug.Log($"[abordage-vfx] finish serial={run.Serial} reason={run.EndReason} endUse={endUse}");
        }
    }
}
