using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// База Броска якоря — борозды волока (кадр base-yank-v1): на каждого тянутого — канава по настоящему пути ног с рывка
    /// («Razlom/Anchor Throw Furrow», единственный плоский слой рывка), у переднего края — комья в стороны и вверх, настоящие
    /// камни (освещённые, инстансинг выключен), клубы пыли. Тело встало — борозда рассыпается с краёв и гаснет (~1,8 с).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private static readonly Color AiRockEarth = new Color(.46f, .38f, .32f, 1f), AiRockStone = new Color(.60f, .57f, .54f, 1f);

        /// <summary>
        /// Hit «на тягу»: тело запомнено; борозда — с рывка, если тело правда поехало. <paramref name="wide"/> — цель Гарпуна:
        /// долгая борозда шире и гуще комьями и камнями (кадр form-harpoon-v2).
        /// </summary>
        private void AiNoteTow(int target, int serial, bool wide)
        {
            AiFurrow free = null;
            foreach (AiFurrow f in AiFurrows)
            {
                if ((f.Noted || f.Started) && f.Target == target && f.Serial == serial) return;
                if (free == null && !f.Noted && !f.Started) free = f;
            }
            if (free == null) return;
            free.Noted = true;
            free.Started = free.Stopped = false;
            free.Target = target;
            free.Serial = serial;
            free.Wide = wide;
        }

        private void StartAiFurrows(int serial)
        {
            foreach (AiFurrow f in AiFurrows) if (f.Noted && !f.Started && f.Serial == serial) StartAiFurrow(f);
        }

        private void StartAiFurrow(AiFurrow f)
        {
            Simulation sim = _driver.Sim;
            if (sim == null || CaptureRig.NoVfx || !sim.AnchorThrowReeled(f.Target)) { f.Noted = false; return; }
            f.Fx = AiSpawn(PelagVfxId.AnchorThrowIronFurrow, Vector3.zero, Quaternion.identity, 0f, 8f, out f.Object);
            if (f.Fx < 0) { f.Noted = false; return; }
            Transform t = f.Object.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            t.localScale = Vector3.one;
            f.Filter = t.Find("Strip")?.GetComponent<MeshFilter>();
            f.Clods = t.Find("Clods")?.GetComponent<ParticleSystem>();
            f.Rocks = t.Find("Rocks")?.GetComponent<ParticleSystem>();
            f.Dust = t.Find("Dust")?.GetComponent<ParticleSystem>();
            Vector3 feet = AtFeet(EntityPosition(f.Target, PlayerPosition()));
            t.Find("Ground")?.SetPositionAndRotation(new Vector3(0f, feet.y - .02f, 0f), Quaternion.identity);
            f.Half = PelagThrowFormLook.FurrowHalf(AtBodyRadius(f.Target), f.Wide);
            f.Strip.Begin();
            f.Strip.Add(feet, Time.time);
            f.Last = feet;
            f.ClodCarry = f.RockCarry = f.DustCarry = f.Clock = 0f;
            f.Started = true;
            f.Stopped = false;
            if (f.Filter != null) FormWaterMesh.MeshFor(f.Filter, AnchorThrowIronFurrow.MeshName).Clear();
        }

        private void UpdateAiFurrows(Simulation sim, float shown, float dt)
        {
            if (_aiFurrows == null) return;
            float now = Time.time;
            foreach (AiFurrow f in _aiFurrows)
            {
                if (!f.Started) continue;
                if (!AtStill(f.Fx, f.Object)) { f.Started = f.Noted = false; continue; }
                f.Clock += dt;
                bool alive = (uint)f.Target < (uint)sim.Entities.Count && sim.Entities.Alive[f.Target];
                Vector3 feet = alive ? AtFeet(EntityPosition(f.Target, f.Last)) : f.Last;
                bool towed = alive && sim.AnchorThrowReeled(f.Target);
                if (!f.Stopped && !towed && (feet - f.Last).sqrMagnitude < 1e-4f) { f.Stopped = true; f.StopTime = now; }
                if (!f.Stopped)
                {
                    float moved = Vector3.Distance(feet, f.Last);
                    if (moved > 1e-4f) AiFurrowSpray(f, feet, feet - f.Last, moved, f.Wide ? PelagThrowFormLook.HarpoonFurrowSpray : 1f);
                    f.Strip.Add(feet, now);
                    f.Last = feet;
                }
                if (f.Filter != null)
                    f.Strip.Build(FormWaterMesh.MeshFor(f.Filter, AnchorThrowIronFurrow.MeshName), f.Stopped ? f.Strip.Last : feet, f.Half, now,
                        f.Clock, 1f);
                if (f.Stopped && now - f.StopTime > PelagAnchorThrowIronLook.FurrowAfterSeconds + .1f) EndAiFurrow(f);
            }
        }

        /// <summary>Передний край борозды: комья в стороны и вверх, камни вперёд-вверх, клубы пыли — на пройденных метрах.</summary>
        private void AiFurrowSpray(AiFurrow f, Vector3 feet, Vector3 step, float moved, float gain)
        {
            step.y = 0f;
            Vector3 dir = step.sqrMagnitude > 1e-6f ? step.normalized : Vector3.forward;
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);
            int clods = PelagAnchorThrowIronLook.Spend(ref f.ClodCarry, moved * gain, PelagAnchorThrowIronLook.ClodEvery);
            for (int i = 0; i < clods && f.Clods != null; i++)
            {
                float s = Random.value < .5f ? -1f : 1f;
                f.Clods.Emit(new ParticleSystem.EmitParams
                {
                    position = feet + side * (s * f.Half * Random.Range(.6f, 1.05f)) + Vector3.up * .08f,
                    velocity = side * (s * Random.Range(.8f, 2.4f)) + Vector3.up * Random.Range(1.6f, 3.2f) + dir * Random.Range(.4f, 1.6f),
                    applyShapeToPosition = false
                }, 1);
            }
            int rocks = PelagAnchorThrowIronLook.Spend(ref f.RockCarry, moved * gain, PelagAnchorThrowIronLook.RockEvery);
            for (int i = 0; i < rocks && f.Rocks != null; i++)
            {
                float s = Random.Range(-1f, 1f);
                f.Rocks.Emit(new ParticleSystem.EmitParams
                {
                    position = feet + side * (s * f.Half * .8f) + dir * .15f + Vector3.up * .25f,
                    velocity = dir * Random.Range(1.2f, 2.6f) + side * (s * Random.Range(.6f, 1.6f)) + Vector3.up * Random.Range(2.2f, 3.8f),
                    startColor = Color.Lerp(AiRockEarth, AiRockStone, Random.value),
                    applyShapeToPosition = false
                }, 1);
            }
            int dust = PelagAnchorThrowIronLook.Spend(ref f.DustCarry, moved * gain, PelagAnchorThrowIronLook.DustEvery);
            for (int i = 0; i < dust && f.Dust != null; i++)
                f.Dust.Emit(new ParticleSystem.EmitParams
                {
                    position = feet + Vector3.up * .18f + side * Random.Range(-f.Half, f.Half) + dir * .1f,
                    velocity = Vector3.up * Random.Range(.2f, .6f) + dir * Random.Range(.2f, .6f),
                    applyShapeToPosition = false
                }, 1);
        }

        /// <summary>Доехавший встал: последняя точка борозды — у ног, комья и камни осыпаются вокруг.</summary>
        private void AiFurrowLanded(int target, Vector3 feet)
        {
            if (_aiFurrows == null) return;
            foreach (AiFurrow f in _aiFurrows)
            {
                if (!f.Started || f.Target != target || f.Stopped) continue;
                f.Strip.Add(feet, Time.time);
                Vector3 step = feet - f.Last;
                if (step.sqrMagnitude < 1e-4f) step = FlatDirection(feet, PlayerPosition());
                AiFurrowSpray(f, feet, step, PelagAnchorThrowIronLook.ClodEvery * PelagAnchorThrowIronLook.LandClods, 1f);
                f.Last = feet;
                f.Stopped = true;
                f.StopTime = Time.time;
            }
        }

        private void EndAiFurrow(AiFurrow f)
        {
            if (AtStill(f.Fx, f.Object))
            {
                if (f.Filter != null) FormWaterMesh.MeshFor(f.Filter, AnchorThrowIronFurrow.MeshName).Clear();
                Release(f.Fx);
            }
            f.Fx = -1;
            f.Object = null;
            f.Started = f.Noted = false;
        }

        private void EndAiFurrows()
        {
            if (_aiFurrows == null) return;
            foreach (AiFurrow f in _aiFurrows) EndAiFurrow(f);
        }
    }
}
