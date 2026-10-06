using Game.Sim;
using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    /// <summary>
    /// Абордаж v2 — очередь до тика показа и разовые знаки: всплеск укуса и корона
    /// натяга у ноги (зацеп), всплеск удара на теле, стоп-кадр цели, толчок камеры и
    /// корона под ставящейся левой стопой (удар), знаки задетых волной, струёй,
    /// падением воды и «На абордаж!». Рождение объектов — свои пулы Абордажа
    /// (принятые префабы семьи под своими id: всплеск серии сабли, корона рывка,
    /// корона Вихря), цвет — PelagAbordageFormLook.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Всплеск на теле: укус, удар, сосед «На абордаж!», задетый струёй, прочее.</summary>
        private const float AbBiteSplash = .8f, AbPunchSplash = 1.2f, AbSweepSplash = .85f, AbBreachSplash = .9f, AbOtherSplash = .8f;
        /// <summary>Корона у ноги: натяг на зацепе, удар, промах.</summary>
        private const float AbHookCrown = .7f, AbPunchCrown = 1f, AbMissCrown = .8f;
        /// <summary>Стоп-кадр цели, с: удар кулаком и волна Обвала (сбит с ног).</summary>
        private const float AbPunchHold = .07f, AbQuakeHold = .05f;

        /// <summary>Кадр Абордажа v2 (LateUpdate после Шквала, до UpdateActive).</summary>
        private void UpdateAbordageVfx()
        {
            Simulation sim = _driver.Sim;
            if (sim != _abSim)
            {
                // Арену сменили: тел прежнего боя нет — очередь и всё живое Абордажа отпускаются.
                ResetAbordageVfx();
                _abSim = sim;
            }
            if (sim == null) return;
            float shown = PelagAbordageVfxRules.ShownTick(sim.Tick, _driver.Alpha);
            float dt = Time.deltaTime;
            while (_abQueued > 0 && _abQueue[0].Tick <= shown) RunAbordageCue(0, false);
            UpdateAbordageAnchor(sim, shown, dt);
            UpdateAbordageWakes(dt);
            UpdateAbordageForms(sim, shown, dt);
            UpdateAbordageGeysers(sim, shown, dt);
        }

        private void ResetAbordageVfx()
        {
            _abQueued = 0;
            _abLastCue = AbordageVfxCue.None;
            _abLastCueTick = -1;
            _abWaveCue = AbordageVfxCue.None;
            _abWaveStart = -1;
            if (_abRun.Active) FinishAbordageRun(false);
            ForgetAbordageForms();
            ForgetAbordageGeysers();
        }

        /// <summary>Показать шаг очереди <paramref name="index"/> и убрать его.</summary>
        private void RunAbordageCue(int index, bool force)
        {
            AbPending p = _abQueue[index];
            for (int i = index + 1; i < _abQueued; i++) _abQueue[i - 1] = _abQueue[i];
            _abQueued--;
            if (CaptureRig.HasEnemyOverride)
            {
                // shown — тик показа в миг рождения знака (приёмка: shown ≥ tick и shown − tick < 1, иначе знак раньше тела).
                Simulation sim = _driver.Sim;
                float shown = sim != null ? PelagAbordageVfxRules.ShownTick(sim.Tick, _driver.Alpha) : -1f;
                Debug.Log($"[abordage-vfx] cue {p.Kind} tick={p.Tick} shown={shown:F2} target={p.Target} serial={p.Serial} hit={p.Hit} force={force}");
            }
            switch (p.Kind)
            {
                case AbKind.Bite: PlayAbordageBite(p); break;
                case AbKind.Punch: PlayAbordagePunch(p); break;
                case AbKind.Quake: BeginAbordageQuake(p); break;
                case AbKind.Breach: BeginAbordageBreach(p); break;
                case AbKind.GeyserLift: BeginAbordageGeyser(p); break;
                case AbKind.GeyserFall: FallAbordageGeyser(p); break;
                case AbKind.Hit: PlayAbordageHit(p); break;
                case AbKind.End: EndAbordageCue(p); break;
            }
        }

        /// <summary>Зацеп: тело ещё стоит, цепь прямая — всплеск на укусе, корона натяга у ноги, капли с цепи.</summary>
        private void PlayAbordageBite(in AbPending p)
        {
            bool ours = _abRun.Active && _abRun.Serial == p.Serial;
            PelagForm form = ours ? _abRun.Form : PelagForm.None;
            if (ours)
            {
                _abRun.Bitten = true;
                StartAbordageWake();
                SnapAbordageChain();
            }
            if (CaptureRig.NoVfx) return;
            Vector3 hero = PlayerPosition();
            Vector3 bite = ours && _abRun.HasBite ? _abRun.LastBite : EntityPosition(p.Target, hero) + Vector3.up;
            Vector3 toHero = FlatDirection(bite, hero);
            AbordageBodySplash(bite, toHero + Vector3.up * .35f, AbBiteSplash, form);
            Vector3 forward = FlatDirection(hero, bite);
            AbordageCrown(AbordageFoot(hero, forward), forward, AbHookCrown, form);
            _juice?.PunchCamera(.10f, .02f);
        }

        /// <summary>Удар в тик прибытия: всплеск на теле по ходу тяги, стоп-кадр, корона под левой стопой; цепь сматывается.</summary>
        private void PlayAbordagePunch(in AbPending p)
        {
            bool ours = _abRun.Active && _abRun.Serial == p.Serial;
            Vector3 hero = PlayerPosition();
            Vector3 dir = ours && (_abRun.To - _abRun.From).sqrMagnitude > .01f ? FlatDirection(_abRun.From, _abRun.To)
                : p.Target >= 0 ? FlatDirection(hero, EntityPosition(p.Target, hero + PlayerFacing())) : PlayerFacing();
            if (ours)
            {
                EndAbordageWake();
                if (_abRun.RetractStart < 0f) StartAbordageRetract(p.Tick);
            }
            if (CaptureRig.NoVfx) return;
            if (p.Flag && p.Target >= 0)
            {
                Vector3 body = EntityPosition(p.Target, p.At);
                AbordageBodySplash(body + Vector3.up * .9f, dir + Vector3.up * .6f, AbPunchSplash, p.Form);
                _arena.HoldEntityPose(p.Target, AbPunchHold);
                _juice?.PunchCamera(.24f, .05f);
                PulseCombatLight(.5f);
            }
            AbordageCrown(AbordageFoot(hero, dir), dir, p.Flag ? AbPunchCrown : AbMissCrown, p.Form);
        }

        /// <summary>Задетый не кулаком: волна, струя, падение воды, сосед «На абордаж!».</summary>
        private void PlayAbordageHit(in AbPending p)
        {
            if (CaptureRig.NoVfx) return;
            Vector3 body = EntityPosition(p.Target, p.At);
            Vector3 hero = PlayerPosition();
            PelagForm form = _abRun.Active ? _abRun.Form : PelagForm.None;
            switch (p.Hit)
            {
                case AbordageVfxHit.Quake:
                {
                    Vector3 centre = _abQuake.Active ? _abQuake.Centre : hero;
                    Vector3 outward = FlatDirection(centre, body);
                    AbordageKnock(p.Target, body, outward, PelagForm.AbordageQuake);
                    AbordageBodySplash(body + Vector3.up * .8f, outward + Vector3.up * .8f, .7f, PelagForm.AbordageQuake);
                    _arena.HoldEntityPose(p.Target, AbQuakeHold);
                    break;
                }
                case AbordageVfxHit.Breach:
                {
                    Vector3 dir = _abBreach.Active ? _abBreach.Dir : FlatDirection(hero, body);
                    AbordageBodySplash(body + Vector3.up * .9f, dir + Vector3.up * .4f, AbBreachSplash, PelagForm.AbordageBreach);
                    StartAbordageDrag(p.Target, body, dir);
                    break;
                }
                case AbordageVfxHit.Fall:
                    // Круг 5: пена у ног слитыми клубами Гейзера, не корона Вихря («яйца»); подброшенной — без короны.
                    AbordageGeyserFallKnock(p.Target, body);
                    break;
                case AbordageVfxHit.Sweep:
                    AbordageBodySplash(body + Vector3.up * .9f, FlatDirection(hero, body) + Vector3.up * .5f, AbSweepSplash, form);
                    break;
                default:
                    AbordageBodySplash(body + Vector3.up * .9f, FlatDirection(hero, body) + Vector3.up * .5f, AbOtherSplash, form);
                    break;
            }
        }

        /// <summary>Конец каста: срыв — цепь сматывается с места; доигран — уже смотана (с удара).</summary>
        private void EndAbordageCue(in AbPending p)
        {
            if (!_abRun.Active || _abRun.Serial != p.Serial)
            {
                // Сорван до выпуска (рывок, оглушение) или цель пропала в замахе: якорь не летал —
                // только снять хват, если новый Абордаж не начат.
                Simulation sim = _driver.Sim;
                if (sim != null && sim.Abordage.Serial == p.Serial && !sim.AbordageActive) _arena?.EndPlayerAnchorUse();
                return;
            }
            _abRun.Ended = true;
            _abRun.EndReason = (AbordageEnd)p.Amount;
            EndAbordageWake();
            if (!_abRun.Released || _abRun.HandedOff) { FinishAbordageRun(true); return; }
            if (_abRun.RetractStart < 0f) StartAbordageRetract(p.Tick);
        }

        // ---- рождение объектов Абордажа

        /// <summary>Объект пула в точке, цвет формы; масштаб — доля авторского. −1 — эффектов нет (NoVfx) или пула нет.</summary>
        private int AbSpawn(PelagVfxId id, Vector3 at, Quaternion rotation, float scale, float duration, PelagForm form, out GameObject go)
        {
            go = null;
            if (CaptureRig.NoVfx || !TryAcquire(id, out go, out PelagVfxElement element)) return -1;
            int index = ReserveActive();
            element.Begin(at, rotation);
            if (scale > 0f && !Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            PelagAbordageFormLook.Apply(go, form);
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = duration > 0f ? duration : Mathf.Max(.05f, element.DefaultLifetime),
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            return index;
        }

        /// <summary>Всплеск пены на теле — префаб всплеска серии сабли; перед поверхностью тела, к камере.</summary>
        private void AbordageBodySplash(Vector3 at, Vector3 along, float scale, PelagForm form)
        {
            Camera camera = Camera.main;
            if (camera != null) at += (camera.transform.position - at).normalized * .45f;
            if (along.sqrMagnitude < 1e-4f) along = Vector3.up;
            AbSpawn(PelagVfxId.AbordageSplash, at, Quaternion.LookRotation(along.normalized), scale, 0f, form, out _);
        }

        /// <summary>Корона пены у ноги — префаб короны рывка (А5).</summary>
        private void AbordageCrown(Vector3 foot, Vector3 forward, float scale, PelagForm form)
            => AbSpawn(PelagVfxId.AbordageCrown, foot, Quaternion.LookRotation(forward, Vector3.up), scale, 0f, form, out _);

        /// <summary>Сбит с ног / задет падением: корона брызг Вихря у ног, клонится наружу; размер — по телу.</summary>
        private void AbordageKnock(int target, Vector3 body, Vector3 outward, PelagForm form)
        {
            Simulation sim = _driver.Sim;
            float radius = sim != null && (uint)target < (uint)sim.Entities.Count
                ? sim.Entities.BodyRadius[target].ToFloat() : EntityStore.DefaultBodyRadius.ToFloat();
            _abGroundBase = PlayerPosition().y;
            var at = new Vector3(body.x, AbordageGroundAt(body.x, body.z) + .02f, body.z);
            float scale = Mathf.Clamp(.6f + .8f * radius, .8f, 1.6f);
            AbSpawn(PelagVfxId.AbordageKnock, at, Quaternion.LookRotation(outward, Vector3.up), scale, 0f, form, out _);
        }

        private Transform _abFootBody, _abFoot, _abFootScale;

        /// <summary>Под передней (левой) ногой: кость левой стопы, иначе её точка числом (как корона рывка).</summary>
        private Vector3 AbordageFoot(Vector3 root, Vector3 forward)
        {
            if (_arena.TryGetEntityView(Simulation.PlayerId, out Transform body) && body != _abFootBody)
            {
                _abFootBody = body;
                _abFoot = null;
                foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "mixamorig:LeftFoot") { _abFoot = bone; break; }
                Animator animator = body.GetComponentInChildren<Animator>(true);
                _abFootScale = animator != null ? animator.transform : body;
            }
            _abGroundBase = root.y;
            Vector3 foot;
            if (_abFoot != null && _abFoot.gameObject.activeInHierarchy) foot = _abFoot.position;
            else
            {
                float scale = _abFootScale != null ? _abFootScale.lossyScale.y : 1f;
                foot = root + (forward * .244f + Vector3.Cross(forward, Vector3.up) * .072f) * scale;
            }
            foot.y = AbordageGroundAt(foot.x, foot.z) + .02f;
            return foot;
        }

        private float _abGroundBase;
        private System.Func<float, float, float> _abGround;

        /// <summary>Земля в точке: лагерь — навигация, разлом — пол показанной арены с уступами; иначе высота ног.</summary>
        private float AbordageGroundAt(float x, float z)
        {
            GameSession session = _driver.Session;
            if (session != null && session.Mode == GameMode.Camp)
            {
                if (NavMesh.SamplePosition(new Vector3(x, _abGroundBase + 1f, z), out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                    return Mathf.Clamp(hit.position.y, _abGroundBase - .6f, _abGroundBase + .8f);
                return _abGroundBase;
            }
            LayoutView layout = LayoutView.Shown;
            return layout != null ? layout.WeaponGroundHeight(x, z) : _abGroundBase;
        }

        /// <summary>Сетка земли под водой формы радиуса <paramref name="radius"/>; центр — на земле.</summary>
        private Vector3 SampleAbordageGround(FormGroundGrid grid, Vector3 centre, float radius)
        {
            _abGroundBase = centre.y;
            if (_abGround == null) _abGround = AbordageGroundAt;
            grid.Sample(centre, radius, _abGround);
            return new Vector3(centre.x, grid.Centre, centre.z);
        }

        private bool AbStill(int fx, GameObject go)
            => go != null && fx >= 0 && fx < _active.Length && _active[fx].Active && _active[fx].Object == go;
    }
}
