using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Абордаж v2 — пенные следы на земле кодом следа рывка (PelagDashWake): след
    /// тяги героя (кадр A: «низкий рывок с пенным следом, как рывок») — от места
    /// зацепа по настоящему пути тела, с заносом у ног, гаснет фронтом после
    /// удара; и след волока за врагами, которых отбросила струя Пробоины (как
    /// следы тяги Водоворота: появляется, только если тело правда поехало).
    /// Префабы — следа рывка и следа Водоворота под своими id (AbordageWake,
    /// AbordageDrag), цвет — PelagAbordageFormLook.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private const int AbordageWakeSlots = 2, AbordageDragSlots = 6;
        /// <summary>Корень следа волока позади тела, м: голова следа (HeadLead) встаёт у ног, а не впереди тела.</summary>
        private const float AbordageDragBack = PelagDashWake.HeadLead - .20f;

        private sealed class AbordageWakeRun
        {
            public readonly PelagDashWake Wake = new PelagDashWake();
            public bool Active, Started;
            public int Fx = -1, Target = -1, StillFrames;
            public GameObject Object;
            public MeshFilter Filter;
            public ParticleSystem SkidFoam, SkidDrops, TailDrops;
            public Vector3 From, Direction, Start;
            public float Ground, SkidAlong, FoamCarry, DropCarry, LastShift, Born;
            public PelagForm Form;
        }

        private readonly AbordageWakeRun[] _abWakes = { new AbordageWakeRun(), new AbordageWakeRun() };
        private readonly AbordageWakeRun[] _abDrags =
        {
            new AbordageWakeRun(), new AbordageWakeRun(), new AbordageWakeRun(),
            new AbordageWakeRun(), new AbordageWakeRun(), new AbordageWakeRun()
        };

        /// <summary>Зацеп: след ложится от места героя к точке посадки и растёт за телом.</summary>
        private void StartAbordageWake()
        {
            AbordageRun run = _abRun;
            if (CaptureRig.NoVfx || !run.HasHook || run.WakeSlot >= 0) return;
            Vector3 path = run.To - run.From;
            path.y = 0f;
            if (path.magnitude < .3f) return;
            int slot = 0;
            for (int i = 0; i < _abWakes.Length; i++)
                if (!_abWakes[i].Active) { slot = i; break; }
                else if (_abWakes[i].Wake.Age > _abWakes[slot].Wake.Age) slot = i;
            AbordageWakeRun w = _abWakes[slot];
            ReleaseAbordageWake(w);
            float ground = PlayerPosition().y;
            var from = new Vector3(run.From.x, ground, run.From.z);
            Vector3 dir = path.normalized;
            if (!BindAbordageWake(w, PelagVfxId.AbordageWake, from, dir, run.Form)) return;
            w.From = from;
            w.Direction = dir;
            w.Ground = ground;
            w.Wake.Begin(path.magnitude, Random.Range(0f, 8f), GameUserSettings.FlashScale);
            if (w.Filter != null) w.Wake.Build(PelagDashWake.MeshFor(w.Filter));
            if (w.TailDrops != null) PelagDashWake.EmitTailDrops(w.TailDrops, from, dir, ground);
            run.WakeSlot = slot;
        }

        /// <summary>Удар или конец: след дальше не растёт (длина — пройденный путь) и гаснет фронтом.</summary>
        private void EndAbordageWake()
        {
            AbordageRun run = _abRun;
            if (run.WakeSlot < 0) return;
            AbordageWakeRun w = _abWakes[run.WakeSlot];
            run.WakeSlot = -1;
            if (!w.Active || w.Wake.Ended) return;
            float along = Vector3.Dot(PlayerPosition() - w.From, w.Direction);
            w.Wake.End(Mathf.Max(w.Wake.Length, along));
        }

        /// <summary>Струя Пробоины задела тело: если его правда понесёт — за ним пенный след волока.</summary>
        private void StartAbordageDrag(int target, Vector3 body, Vector3 dir)
        {
            if (CaptureRig.NoVfx) return;
            AbordageWakeRun free = null;
            foreach (AbordageWakeRun d in _abDrags)
            {
                if (d.Active && d.Target == target) return;
                if (free == null && !d.Active) free = d;
            }
            if (free == null) return;
            ReleaseAbordageWake(free);
            free.Active = true;
            free.Started = false;
            free.Target = target;
            free.Start = body;
            free.Direction = dir;
            free.StillFrames = 0;
            free.LastShift = 0f;
            free.Born = Time.time;
            _abGroundBase = PlayerPosition().y;
            free.Ground = AbordageGroundAt(body.x, body.z);
        }

        private bool BindAbordageWake(AbordageWakeRun w, PelagVfxId id, Vector3 root, Vector3 dir, PelagForm form)
        {
            int fx = AbSpawn(id, root, Quaternion.LookRotation(dir, Vector3.up), 0f, PelagDashWake.MaxLife + .2f, form, out GameObject go);
            if (fx < 0) return false;
            go.transform.localScale = Vector3.one;
            Transform t = go.transform;
            w.Active = true;
            w.Fx = fx;
            w.Object = go;
            w.Form = form;
            w.Filter = t.Find("Wake")?.GetComponent<MeshFilter>();
            w.SkidFoam = t.Find("SkidFoam")?.GetComponent<ParticleSystem>();
            w.SkidDrops = t.Find("SkidDrops")?.GetComponent<ParticleSystem>();
            w.TailDrops = t.Find("TailDrops")?.GetComponent<ParticleSystem>();
            w.SkidAlong = 0f;
            w.FoamCarry = w.DropCarry = .6f;
            return true;
        }

        private void ReleaseAbordageWake(AbordageWakeRun w)
        {
            if (w.Active && AbStill(w.Fx, w.Object)) Release(w.Fx);
            w.Active = w.Started = false;
            w.Fx = -1;
            w.Object = null;
        }

        /// <summary>Кадр следов: рост за телом героя / отброшенного, занос у ног, распад, возврат в пул.</summary>
        private void UpdateAbordageWakes(float dt)
        {
            Vector3 hero = PlayerPosition();
            foreach (AbordageWakeRun w in _abWakes)
            {
                if (!w.Active) continue;
                if (!AbStill(w.Fx, w.Object)) { w.Active = false; continue; }
                AdvanceAbordageWake(w, dt, Vector3.Dot(hero - w.From, w.Direction));
            }
            foreach (AbordageWakeRun d in _abDrags)
            {
                if (!d.Active) continue;
                Vector3 body = EntityPosition(d.Target, d.Start);
                Vector3 moved = body - d.Start;
                moved.y = 0f;
                float shift = Mathf.Max(0f, Vector3.Dot(moved, d.Direction));
                if (!d.Started)
                {
                    // След — только если тело правда понесло (элиты, тяжёлые, босс стоят — следа нет).
                    if (shift >= .06f)
                    {
                        Vector3 root = d.Start - d.Direction * AbordageDragBack;
                        root.y = d.Ground;
                        if (!BindAbordageWake(d, PelagVfxId.AbordageDrag, root, d.Direction, PelagForm.AbordageBreach)) { d.Active = false; continue; }
                        // Занос — у ног тела (как борозда Водоворота), а не у корня меша позади него.
                        d.From = new Vector3(d.Start.x, d.Ground, d.Start.z);
                        d.Started = true;
                        d.Wake.Begin(Simulation.AbordageBreachKnockback.ToFloat() + .3f, Random.Range(0f, 8f), GameUserSettings.FlashScale);
                        if (d.TailDrops != null) PelagDashWake.EmitTailDrops(d.TailDrops, root, d.Direction, d.Ground);
                    }
                    else if (Time.time - d.Born > .25f) { d.Active = false; continue; }
                    if (!d.Started) continue;
                }
                if (!AbStill(d.Fx, d.Object)) { d.Active = false; continue; }
                if (!d.Wake.Ended)
                {
                    d.StillFrames = shift - d.LastShift < .003f ? d.StillFrames + 1 : 0;
                    if (d.StillFrames >= 3 || Time.time - d.Born > .45f) d.Wake.End(Mathf.Max(shift, d.Wake.Length));
                }
                d.LastShift = shift;
                AdvanceAbordageWake(d, dt, shift);
            }
        }

        private void AdvanceAbordageWake(AbordageWakeRun w, float dt, float along)
        {
            w.Wake.Advance(dt, along);
            if (w.SkidFoam != null && w.SkidDrops != null && w.Wake.Length > w.SkidAlong)
            {
                PelagDashWake.EmitSkid(w.SkidFoam, w.SkidDrops, w.From, w.Direction, w.Ground,
                    w.SkidAlong, w.Wake.Length, ref w.FoamCarry, ref w.DropCarry);
                w.SkidAlong = w.Wake.Length;
            }
            if (w.Filter != null) w.Wake.Build(PelagDashWake.MeshFor(w.Filter));
            if (w.Wake.Done) ReleaseAbordageWake(w);
        }
    }
}
