using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря — разовые знаки в тик показа (спека §5.2–5.3, кадры A–D): всплеск на теле задетого
    /// (лёгкому — корона «петли» у ног и струйка к цепи, тяжёлому — всплеск крупнее и стоп-кадр),
    /// укус Гарпуна (маджентовый веер), натяг (щелчок капель по цепи, корона у ноги, толчок камеры),
    /// ловля (шлепок у руки, корона, вырванный гарпун), приземление каждого доехавшего (корона у ног,
    /// стоп-кадр), конец (срыв — голова к руке за 4 тика). Объекты — пулы Броска (префабы семьи под
    /// своими id), цвет — PelagAnchorThrowFormLook.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Всплеск на теле: лёгкий, тяжёлый (не тянется), сеть Невода, укус Гарпуна, стена.</summary>
        private const float AtHitSplash = .9f, AtHeavySplash = 1.1f, AtNetSplash = .7f, AtBiteSplash = 1.15f, AtWallSplash = .8f;
        /// <summary>Корона у ног: петля лёгкого, узел сети, натяг у ноги героя, ловля.</summary>
        private const float AtPullCrown = .5f, AtNetCrown = .45f, AtYankCrown = .8f, AtCatchCrown = .9f;
        /// <summary>Стоп-кадр, с: укус, тяжёлый, приземление (2 кадра).</summary>
        private const float AtBiteHold = .07f, AtHeavyHold = .05f, AtLandHold = .07f;
        /// <summary>Корень веера укуса сдвинут к камере, м (как веер удара кольца Пенных волн).</summary>
        private const float AtBurstCameraPush = .6f;

        /// <summary>Голова или цепь из своего пула — снаряжение: без цвета формы, видна и в съёмке без эффектов.</summary>
        private int AtSpawnPart(PelagVfxId id, Vector3 at, Quaternion rotation, out GameObject go, out PelagVfxElement element)
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

        private bool AtOurs(int serial) => _atRun.Active && _atRun.Serial == serial;

        private void PlayAnchorThrowHit(in AtPending p)
        {
            bool ours = AtOurs(p.Serial);
            PelagForm form = ours ? _atRun.Form : PelagForm.None;
            bool bite = form == PelagForm.AnchorThrowHarpoon && p.Lane == 0;
            if (bite) _atRun.HarpoonPulled = p.Flag;
            // Лёгкий пойдёт на тягу: струйка воды от пояса к своей цепи (у Гарпуна голова и так в теле, у сети — узел) и
            // борозда в натяг; сеть Невода ловит в самый натяг — её борозды стартуют сразу (событие Yank уже прошло).
            if (ours && p.Flag)
            {
                AnchorThrowTow tow = BeginAnchorThrowTow(p.Target, !bite && p.Lane != 3, p.Lane);
                if (tow != null && p.Lane == 3) StartAtDrag(tow);
            }
            if (CaptureRig.NoVfx) return;
            Vector3 hero = PlayerPosition();
            Vector3 body = EntityPosition(p.Target, p.At);
            Vector3 dir = ours ? AtDir(_atRun.Snap, Mathf.Clamp(p.Lane, 0, 2)) : FlatDirection(hero, body);
            if (p.Lane == 3) dir = FlatDirection(hero, body);
            if (bite)
            {
                // Укус (кадр D): веер маджентовых брызг от ног цели наружу по ходу броска (веер Пенных волн — как удар
                // кольца по врагу: корень у ног, сдвинут к камере, чтобы тело его не закрывало), всплеск на укусе.
                Vector3 point = AtBitePoint(p.Target, body);
                Vector3 back = FlatDirection(body, hero);
                Vector3 root = AtFeet(body);
                Camera camera = Camera.main;
                if (camera != null) root += (camera.transform.position - root).normalized * AtBurstCameraPush;
                AtSpawn(PelagVfxId.AnchorThrowBurst, root, Quaternion.LookRotation(-back, Vector3.up), .8f, 0f, form, true, out _);
                AtBodySplash(point, back + Vector3.up * .45f, AtBiteSplash, form);
                _arena.HoldEntityPose(p.Target, AtBiteHold);
                _juice?.PunchCamera(.16f, .03f);
                return;
            }
            float r = AtBodyRadius(p.Target);
            Vector3 chest = body + Vector3.up * PelagAbordageVfxRules.BiteHeight(r);
            AtBodySplash(chest, dir + Vector3.up * .5f, p.Lane == 3 ? AtNetSplash : p.Flag ? AtHitSplash : AtHeavySplash, form);
            if (p.Flag)
                AtSpawn(PelagVfxId.AnchorThrowKnock, AtFeet(body), Quaternion.LookRotation(dir, Vector3.up),
                    p.Lane == 3 ? AtNetCrown : AtPullCrown, 0f, form, true, out _);
            else _arena.HoldEntityPose(p.Target, AtHeavyHold);
            if (p.Lane == 3) KnotAnchorThrowNet(body);
        }

        private void PlayAnchorThrowYank(in AtPending p)
        {
            bool ours = AtOurs(p.Serial);
            if (!ours) return;
            AnchorThrowRun run = _atRun;
            StartAnchorThrowDrags();
            StartAnchorThrowHeadWake();
            if (CaptureRig.NoVfx) return;
            Vector3 dir = AtDir(run.Snap, 0);
            // Натяг: «щелчок» капель вдоль цепи от головы к руке, корона у ноги героя, толчок камеры.
            ParticleSystem drops = AtStill(run.SleeveFx, run.SleeveObject) ? run.SleeveDrops : null;
            EmitAnchorThrowDrops(drops, run.Form, run.Ring, run.Grip, Vector3.up * .6f - dir * .4f, 22, 1.7f);
            Vector3 hero = PlayerPosition();
            AtSpawn(PelagVfxId.AnchorThrowCrown, AtFoot(hero, dir), Quaternion.LookRotation(dir, Vector3.up), AtYankCrown, 0f, run.Form, true, out _);
            _juice?.PunchCamera(.15f, .02f);
            // Стена или корпус босса оборвали полёт: брызги об преграду там, где встала голова.
            if (p.Flag && p.Target < 0) AtBodySplash(run.Head, -dir + Vector3.up * .6f, AtWallSplash, run.Form);
        }

        private void PlayAnchorThrowCatch(in AtPending p)
        {
            bool ours = AtOurs(p.Serial);
            if (!ours) return;
            AnchorThrowRun run = _atRun;
            run.Caught = true;
            CatchAnchorThrowForms();
            CatchAnchorThrowTows();
            if (CaptureRig.NoVfx) return;
            Vector3 hero = PlayerPosition();
            Vector3 dir = AtDir(run.Snap, 0);
            Vector3 fist = AnchorThrowFist();
            // «Шлепок» в ладонь: брызги у правой руки навстречу якорю, корона у ног, толчок по числу доехавших.
            ParticleSystem drops = AtStill(run.SleeveFx, run.SleeveObject) ? run.SleeveDrops : null;
            EmitAnchorThrowDrops(drops, run.Form, fist, fist + dir * .2f, dir + Vector3.up * .5f, 16, 1.5f);
            AtSpawn(PelagVfxId.AnchorThrowCrown, AtFoot(hero, dir), Quaternion.LookRotation(dir, Vector3.up), AtCatchCrown, 0f, run.Form, true, out _);
            _juice?.PunchCamera(.10f + .03f * Mathf.Min(4, p.Amount), .03f);
            // Гарпун: в ловлю якорь вырывается из цели брызгами.
            int target = run.Snap.HarpoonTarget;
            if (run.Snap.Stuck && run.HarpoonPulled && target >= 0 && _arena.TryGetEntityView(target, out Transform body))
            {
                AtBodySplash(AtBitePoint(target, body.position), FlatDirection(body.position, hero) + Vector3.up * .4f, AtHitSplash, run.Form);
                // Цель Гарпуна тоже доехала (её Stun не повторяется в ловлю — оглушена с укуса): корона приземления.
                PlayAnchorThrowLand(new AtPending { Kind = AtKind.Land, Tick = p.Tick, Target = target, Serial = p.Serial, At = body.position });
            }
        }

        /// <summary>Stun следом за ловлей: тело доехало в своей тяге — корона у ног и стоп-кадр 2 кадра.</summary>
        private void PlayAnchorThrowLand(in AtPending p)
        {
            if (CaptureRig.NoVfx) return;
            PelagForm form = AtOurs(p.Serial) ? _atRun.Form : PelagForm.None;
            Vector3 body = EntityPosition(p.Target, p.At);
            float scale = Mathf.Clamp(.55f + .6f * AtBodyRadius(p.Target), .7f, 1.3f);
            AtSpawn(PelagVfxId.AnchorThrowKnock, AtFeet(body), Quaternion.LookRotation(FlatDirection(PlayerPosition(), body), Vector3.up),
                scale, 0f, form, true, out _);
            _arena.HoldEntityPose(p.Target, AtLandHold);
        }

        /// <summary>Конец (76): доигран — всё уже у руки; срыв до ловли — тяги рвутся, голова к руке за 4 тика.</summary>
        private void EndAnchorThrowCue(in AtPending p)
        {
            AnchorThrowRun run = _atRun;
            if (!AtOurs(p.Serial))
            {
                Simulation sim = _driver.Sim;
                if (!run.Active && sim != null && sim.AnchorThrow.Serial == p.Serial && !sim.AnchorThrowActive) _arena?.EndPlayerAnchorUse();
                return;
            }
            run.Ended = true;
            run.EndReason = (AnchorThrowEnd)p.Amount;
            // Не выпущен — показывать нечего; пойман или уже у руки — вода на цепи доломается сама (UpdateAnchorThrowRun).
            if (!run.Released) { FinishAnchorThrowRun(true); return; }
            if (run.Caught || run.HandedOff || run.RetractStart >= 0f) return;
            run.RetractStart = p.Tick;
            run.RetractFrom = run.Head;
            DropAnchorThrowForms();
            DropAnchorThrowTows();
            if (!CaptureRig.NoVfx && AtStill(run.SleeveFx, run.SleeveObject))
                EmitAnchorThrowDrops(run.SleeveDrops, run.Form, run.Grip, run.Head, Vector3.up, 12, 1.2f);
        }

        /// <summary>Голова у руки: временный путь отдаёт её поясу (как Абордаж); голова, цепь и росчерк — в пул.</summary>
        private void HandOffAnchorThrowAnchor(AnchorThrowRun run)
        {
            if (run.HandedOff) return;
            run.HandedOff = true;
            if (!run.Rig && AtStill(run.HeadFx, run.HeadObject) && _driver.Sim != null && _driver.Sim.Entities.Alive[Simulation.PlayerId]
                && _arena.TryGetEntityView(Simulation.PlayerId, out Transform body))
            {
                Transform spinner = run.HeadElement.Spinner;
                Transform head = spinner != null ? spinner : run.HeadObject.transform;
                PelagAnchorSlamView belt = body.GetComponentInChildren<PelagAnchorSlamView>(true);
                if (belt != null) belt.ReturnFlyingAnchor(head.position, head.rotation);
            }
            if (AtStill(run.HeadFx, run.HeadObject)) Release(run.HeadFx);
            if (AtStill(run.ChainFx, run.ChainObject)) Release(run.ChainFx);
            if (AtStill(run.StreakFx, run.StreakObject)) Release(run.StreakFx);
            run.HeadFx = run.ChainFx = run.StreakFx = -1;
            run.HeadObject = run.ChainObject = run.StreakObject = null;
        }

        /// <summary>Каст кончился (или сменился новым): голову — поясу, воду — в пул; <paramref name="endUse"/> — снять хват якоря.</summary>
        private void FinishAnchorThrowRun(bool endUse)
        {
            AnchorThrowRun run = _atRun;
            if (!run.Active) return;
            HandOffAnchorThrowAnchor(run);
            if (AtStill(run.SleeveFx, run.SleeveObject)) Release(run.SleeveFx);
            if (AtStill(run.ShadowFx, run.ShadowObject)) Release(run.ShadowFx);
            run.SleeveFx = run.ShadowFx = -1;
            run.SleeveObject = run.ShadowObject = null;
            EndAnchorThrowForms();
            EndAnchorThrowTows();
            if (run.Rig)
            {
                // Риг: последняя фаза — поймал (маятник и уборка) или ничего (срыв — уборка).
                bool driven = false;
                Vector3 grip = Vector3.zero, ring = Vector3.zero;
                byte phase = (byte)(run.Caught ? AnchorThrowRigPhase.Done : AnchorThrowRigPhase.None);
                AnchorThrowRigDrive(run.Serial, phase, run.Head, AtDir(run.Snap, 0), 0f, 0f, true, ref driven, ref grip, ref ring);
            }
            run.Active = false;
            if (endUse && !run.Rig) _arena?.EndPlayerAnchorUse();
            if (CaptureRig.HasEnemyOverride) Debug.Log($"[anchor-throw-vfx] finish serial={run.Serial} reason={run.EndReason} endUse={endUse} rig={run.Rig}");
        }

        // ---- общие мелочи

        private float AtBodyRadius(int target)
        {
            Simulation sim = _driver.Sim;
            return sim != null && (uint)target < (uint)sim.Entities.Count ? sim.Entities.BodyRadius[target].ToFloat()
                : EntityStore.DefaultBodyRadius.ToFloat();
        }

        private float AtGround(Vector3 p)
        {
            _atGroundBase = PlayerPosition().y;
            return AnchorThrowGroundAt(p.x, p.z);
        }

        private Vector3 AtFeet(Vector3 body) => new Vector3(body.x, AtGround(body) + .02f, body.z);

        /// <summary>Всплеск пены на теле — префаб всплеска серии сабли; перед поверхностью тела, к камере.</summary>
        private void AtBodySplash(Vector3 at, Vector3 along, float scale, PelagForm form)
        {
            Camera camera = Camera.main;
            if (camera != null) at += (camera.transform.position - at).normalized * .45f;
            if (along.sqrMagnitude < 1e-4f) along = Vector3.up;
            AtSpawn(PelagVfxId.AnchorThrowSplash, at, Quaternion.LookRotation(along.normalized), scale, 0f, form, true, out _);
        }

        private Transform _atFootBody, _atFootBone;

        /// <summary>Под передней (левой) ногой героя: кость стопы, иначе точка числом (как корона рывка).</summary>
        private Vector3 AtFoot(Vector3 root, Vector3 forward)
        {
            if (_arena.TryGetEntityView(Simulation.PlayerId, out Transform body) && body != _atFootBody)
            {
                _atFootBody = body;
                _atFootBone = null;
                foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "mixamorig:LeftFoot") { _atFootBone = bone; break; }
            }
            Vector3 foot = _atFootBone != null && _atFootBone.gameObject.activeInHierarchy ? _atFootBone.position
                : root + forward * .244f + Vector3.Cross(forward, Vector3.up) * .072f;
            foot.y = AtGround(foot) + .02f;
            return foot;
        }

        /// <summary>Капли воды с отрезка a–b: вылет вдоль <paramref name="along"/>, в стороны и вверх; цвет — пена с водой формы.</summary>
        private static void EmitAnchorThrowDrops(ParticleSystem system, PelagForm form, Vector3 a, Vector3 b, Vector3 along, int count, float strength)
        {
            if (system == null || count <= 0) return;
            Vector3 axis = b - a;
            Vector3 side = Vector3.Cross(axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector3.forward, Vector3.up);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = Vector3.Lerp(a, b, Random.value) + Random.insideUnitSphere * .03f;
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = (along * Random.Range(1f, 3f) + side * Random.Range(-1f, 1f) * Random.Range(.5f, 1.3f)
                                + Vector3.up * Random.Range(.4f, 1.4f)) * strength,
                    startSize = Random.Range(.035f, .075f),
                    startLifetime = Random.Range(.25f, .40f),
                    startColor = PelagAnchorThrowFormLook.DropColor(form, Random.Range(.2f, .8f)),
                    applyShapeToPosition = false
                }, 1);
            }
        }
    }
}
