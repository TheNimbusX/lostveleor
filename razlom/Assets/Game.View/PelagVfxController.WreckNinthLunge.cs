using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Девятый вал — выпад с зарядами (из PlayWreckSlam после стека выпада базы, который рисует удар в индиго). Гребень базы
    /// прячется, вместо него — свой ВЕЕР лент (PelagWreckNinthCrest, шейдер «Razlom/Wreck Ninth Crest»): центр выше базы
    /// по зарядам, эхо, второе эхо у двух зарядов, два гребня по краям полосы Sim (полуширина Sim у точки удара, вперёд
    /// сужаются, верх наклонён наружу) — голова каждой на фронте Sim. У точки удара — стоячий веер-полузвезда по ширине
    /// полосы. Под веером — вал по земле (PelagWreckNinthCrest.BuildGround, шейдер «Razlom/Wreck Ninth Ground»: шипастый,
    /// шире у удара, острый вперёд, свет внутри). На шагах фронта: светящиеся трещины, искры с краёв, крупные освещённые
    /// камни (по зарядам); индиговый свет идёт с фронтом. Без зарядов этого слоя нет — выпад базы.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckNinthRun
        {
            public bool Active, Ended;
            public int Fx = -1, Charges, StepsDone, Frame;
            public float FarSide, LastShown;
            public GameObject Object;
            public PelagWreckNinthCrest.Lane Lane;
            public readonly MeshFilter[] Ribbons = new MeshFilter[5];
            public MeshFilter Wave;
            public readonly PelagWreckNinthCrest.Ribbon[] Specs = new PelagWreckNinthCrest.Ribbon[5];
            public ParticleSystem Fan, Stars, Cracks, Sparks, Rocks;
            public Light Light;
        }

        private static readonly string[] WnRibbonNames = { "CrestFar", "CrestEcho2", "CrestEcho", "Crest", "CrestNear" };
        private static readonly string[] WnRibbonMeshes = { "WreckNinthFar", "WreckNinthEcho2", "WreckNinthEcho", "WreckNinthCrest", "WreckNinthNear" };
        private const string WnWaveMesh = "WreckNinthWave";
        private readonly WreckNinthRun[] _wnRuns = { new WreckNinthRun(), new WreckNinthRun() };

        /// <summary>Удар выпада Девятого вала с зарядами (тик показа). Без зарядов, без полосы или без префаба — ничего.</summary>
        private void PlayWreckNinthLunge(in WkPending p)
        {
            WkWave wave = p.Wave;
            if (wave.Look != PelagForm.WreckNinthWave || CaptureRig.NoVfx || !WreckNinthReady) return;
            int charges = PelagWreckNinthLook.ChargesFromPercent(wave.DamagePercent);
            if (charges <= 0 || (wave.Travel <= 0 && !wave.Stopped)) return;
            // Гребень базы этого удара — прячется: его место занимает веер.
            foreach (WreckComboLungeRun baseRun in _wclRuns)
                if (baseRun.Active && baseRun.Tick == p.Tick)
                    foreach (MeshFilter f in new[] { baseRun.Crest, baseRun.Echo })
                        if (f != null && f.GetComponent<Renderer>() is Renderer r) r.enabled = false;
            WreckNinthRun run = _wnRuns[0].Active && (!_wnRuns[1].Active || _wnRuns[1].Lane.Tick < _wnRuns[0].Lane.Tick) ? _wnRuns[1] : _wnRuns[0];
            ReleaseWreckNinthRun(run);
            System.Func<float, float, float> ground = WreckIronGroundAt(wave.Impact.y);
            var root = new Vector3(wave.Impact.x, ground(wave.Impact.x, wave.Impact.z), wave.Impact.z);
            Vector3 dir = wave.Dir.sqrMagnitude > .01f ? new Vector3(wave.Dir.x, 0f, wave.Dir.z).normalized : PlayerFacing();
            if (!TryAcquire(PelagVfxId.WreckNinthLunge, out GameObject go, out PelagVfxElement element)) return;
            int travel = Mathf.Max(1, wave.Travel);
            int index = ReserveActive();
            element.Begin(root, Quaternion.LookRotation(dir, Vector3.up));
            go.transform.localScale = Vector3.one;
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WreckNinthLunge, Object = go, Element = element,
                Duration = Mathf.Max(PelagWreckComboLungeLook.AfterSeconds + 1.3f, PelagWreckVfxRules.Seconds(travel) + 1.5f),
                Start = root, End = root, Motion = Motion.Static, FollowIndex = -1
            };
            run.Active = true;
            run.Ended = false;
            run.Fx = index;
            run.Object = go;
            run.Charges = charges;
            run.StepsDone = 0;
            run.Frame = Time.frameCount;
            run.Lane = new PelagWreckNinthCrest.Lane
            {
                Root = root, Origin = wave.Origin, Dir = dir, Start = wave.Start, End = Mathf.Max(wave.Start + .1f, wave.End),
                Step = Mathf.Max(.05f, wave.Step), HalfWidth = Mathf.Max(.3f, wave.HalfWidth), Tick = p.Tick, Travel = travel
            };
            Transform t = go.transform;
            for (int i = 0; i < WnRibbonNames.Length; i++)
            {
                run.Ribbons[i] = t.Find(WnRibbonNames[i])?.GetComponent<MeshFilter>();
                if (run.Ribbons[i] == null) continue;
                FormWaterMesh.MeshFor(run.Ribbons[i], WnRibbonMeshes[i]).Clear();
                run.Ribbons[i].transform.SetPositionAndRotation(root, Quaternion.identity);
                run.Ribbons[i].transform.localScale = Vector3.one;
                Renderer renderer = run.Ribbons[i].GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = i != 1 || charges >= 2;
            }
            run.Wave = t.Find("Wave")?.GetComponent<MeshFilter>();
            if (run.Wave != null)
            {
                FormWaterMesh.MeshFor(run.Wave, WnWaveMesh).Clear();
                run.Wave.transform.SetPositionAndRotation(root, Quaternion.identity);
                run.Wave.transform.localScale = Vector3.one;
                if (run.Wave.GetComponent<Renderer>() is Renderer waveRenderer) waveRenderer.enabled = true;
            }
            run.Fan = t.Find("Fan")?.GetComponent<ParticleSystem>();
            run.Stars = t.Find("GroundStars")?.GetComponent<ParticleSystem>();
            run.Cracks = t.Find("Cracks")?.GetComponent<ParticleSystem>();
            run.Sparks = t.Find("Sparks")?.GetComponent<ParticleSystem>();
            run.Rocks = t.Find("Rocks")?.GetComponent<ParticleSystem>();
            run.Light = t.Find("Light")?.GetComponent<Light>();
            Camera camera = Camera.main;
            Vector3 away = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up) : Vector3.forward;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            var left = new Vector3(-dir.z, 0f, dir.x);
            run.FarSide = Vector3.Dot(left, away) >= 0f ? 1f : -1f;
            float center = PelagWreckNinthLook.CenterHeight(charges);
            float flank = center * PelagWreckNinthLook.FlankHeight;
            run.Specs[0] = new PelagWreckNinthCrest.Ribbon { Side = run.FarSide * PelagWreckNinthLook.FlankInset, Tilt = PelagWreckNinthLook.FlankTiltFar, Height = flank, DelayTicks = PelagWreckNinthLook.FlankDelayTicks, USeed = .23f };
            run.Specs[1] = new PelagWreckNinthCrest.Ribbon { Height = center * PelagWreckNinthLook.Echo2Height, DelayTicks = PelagWreckNinthLook.Echo2DelayTicks, Offset = away * PelagWreckNinthLook.Echo2Back, USeed = .77f };
            run.Specs[2] = new PelagWreckNinthCrest.Ribbon { Height = center * PelagWreckNinthLook.EchoHeight, DelayTicks = PelagWreckNinthLook.EchoDelayTicks, Offset = away * PelagWreckNinthLook.EchoBack, USeed = .41f };
            run.Specs[3] = new PelagWreckNinthCrest.Ribbon { Height = center };
            run.Specs[4] = new PelagWreckNinthCrest.Ribbon { Side = -run.FarSide * PelagWreckNinthLook.FlankInset, Tilt = PelagWreckNinthLook.FlankTiltNear, Height = flank, DelayTicks = PelagWreckNinthLook.FlankDelayTicks, USeed = .59f };
            // Веер у точки удара — по ширине полосы Sim (круг выпада не растёт: звезда базы того же размера).
            float fan = PelagWreckNinthLook.FanWidth(run.Lane.HalfWidth);
            if (run.Fan != null)
                run.Fan.Emit(new ParticleSystem.EmitParams
                {
                    position = root + dir * .2f, startColor = WnViolet, startLifetime = .36f, startSize3D = new Vector3(fan, fan * .52f, 1f),
                    rotation = 0f, applyShapeToPosition = false
                }, 1);
            WclDecal(run.Stars, root + Vector3.up * .03f, WclDecalAngle(dir), fan * 1.15f, fan * 1.15f, WnIndigo);
            if (run.Light != null) run.Light.intensity = 0f;
            UpdateWreckNinthRun(run, p.Tick);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-ninth-lunge] tick={p.Tick} charges={charges} half={run.Lane.HalfWidth:F2} start={run.Lane.Start:F2} end={run.Lane.End:F2} travel={travel} center={center:F2}");
        }

        private static readonly Color WnIndigo = new Color(.16f, .12f, .86f, 1f);

        /// <summary>Кадр (UpdateWreckVfx, тик показа): ленты веера, куски на шагах фронта, свет у фронта.</summary>
        private void UpdateWreckNinthLunge(float shown)
        {
            foreach (WreckNinthRun run in _wnRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Object = null; continue; }
                UpdateWreckNinthRun(run, shown);
            }
        }

        private void UpdateWreckNinthRun(WreckNinthRun run, float shown)
        {
            run.LastShown = shown;
            // Кадр рождения: залп удара базы выходит на следующем обновлении частиц — веер ждёт его (как гребень базы).
            if (Time.frameCount == run.Frame) return;
            Camera camera = Camera.main;
            Vector3 view = camera != null ? camera.transform.forward : new Vector3(0f, -.8f, .6f);
            Vector3 lift = PelagWreckNinthCrest.Lift(view, run.Lane.Dir);
            System.Func<float, float, float> ground = WreckIronGroundAt(run.Lane.Root.y);
            float last = PelagWreckVfxRules.WaveEndTick(run.Lane.Tick, run.Lane.Travel);
            bool over = PelagWreckVfxRules.Seconds(shown - last) > PelagWreckComboLungeLook.AfterSeconds + .05f;
            for (int i = 0; i < run.Ribbons.Length; i++)
            {
                MeshFilter filter = run.Ribbons[i];
                if (filter == null) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled) continue;
                if (over) { renderer.enabled = false; continue; }
                PelagWreckNinthCrest.Build(FormWaterMesh.MeshFor(filter, WnRibbonMeshes[i]), run.Lane, run.Specs[i], shown, lift, ground);
            }
            if (run.Wave != null && run.Wave.GetComponent<Renderer>() is Renderer wave && wave.enabled)
            {
                if (over) wave.enabled = false;
                else PelagWreckNinthCrest.BuildGround(FormWaterMesh.MeshFor(run.Wave, WnWaveMesh), run.Lane, shown, PelagWreckNinthLook.GroundLift, ground);
            }
            EmitWreckNinthFront(run, shown, lift, ground);
            if (run.Light != null)
            {
                float front = PelagWreckVfxRules.WaveFront(shown, run.Lane.Tick, run.Lane.Start, run.Lane.Step, run.Lane.Travel, run.Lane.End);
                Vector3 at = run.Lane.Origin + run.Lane.Dir * Mathf.Max(run.Lane.Start, front - .4f);
                at.y = ground(at.x, at.z) + 1.2f;
                run.Light.transform.position = at;
                float after = PelagWreckVfxRules.Seconds(shown - last);
                float peak = run.Charges >= 2 ? 3.4f : 2.4f;
                run.Light.intensity = after <= 0f ? peak : peak * Mathf.Clamp01(1f - after / .25f);
            }
        }

        /// <summary>Фронт дошёл до новых шагов Sim: шипы-звёзды и трещины поперёк вала, искры с краёв, крупные камни.</summary>
        private void EmitWreckNinthFront(WreckNinthRun run, float shown, Vector3 lift, System.Func<float, float, float> ground)
        {
            PelagWreckNinthCrest.Lane lane = run.Lane;
            int steps = Mathf.Clamp(Mathf.FloorToInt(shown - lane.Tick + 1f + 1e-3f), 0, lane.Travel);
            var side = new Vector3(-lane.Dir.z, 0f, lane.Dir.x);
            float decal = WclDecalAngle(lane.Dir);
            float center = PelagWreckNinthLook.CenterHeight(run.Charges);
            while (run.StepsDone < steps)
            {
                run.StepsDone++;
                float s = Mathf.Min(lane.End, lane.Start + lane.Step * run.StepsDone);
                float t = PelagWreckNinthLook.PathFraction(s, lane.Start, lane.End);
                float half = lane.HalfWidth * PelagWreckNinthLook.LateralTaper(t);
                Vector3 axis = lane.Origin + lane.Dir * s;
                axis.y = ground(axis.x, axis.z);
                Vector3 mark = lane.Origin + lane.Dir * (s - lane.Step * .5f);
                mark.y = ground(mark.x, mark.z) + .03f;
                WclDecal(run.Cracks, mark, decal + Random.Range(-14f, 14f) + (Random.value < .5f ? 180f : 0f), half * 1.3f, lane.Step * 2.4f, WnViolet);
                float h = center * PelagWreckNinthLook.FlankHeight * PelagWreckNinthLook.HeightTaper(t);
                for (int k = 0; k < PelagWreckNinthLook.FlankSparks(run.Charges) * 2; k++)
                {
                    float sign = (k & 1) == 0 ? 1f : -1f;
                    Vector3 from = axis + side * (sign * half) + lift * (h * Random.Range(.5f, 1.05f)) - lane.Dir * Random.Range(0f, .4f);
                    Vector3 v = side * sign * Random.Range(1.8f, 5f) + Vector3.up * Random.Range(1.4f, 3.6f) + lane.Dir * Random.Range(1f, 4.5f);
                    WclEmit(run.Sparks, from, v, k % 3 == 0 ? WnPale : WnViolet, Random.Range(.035f, .065f), Random.Range(.14f, .28f), 0f);
                }
                for (int k = 0; k < PelagWreckNinthLook.BigRocksAt(run.Charges, run.StepsDone); k++)
                {
                    float off = Random.Range(-.85f, .85f) * half;
                    Vector3 v = Vector3.up * Random.Range(3.2f, 5.6f) + side * (Mathf.Sign(off) * Random.Range(.5f, 1.9f)) + lane.Dir * Random.Range(.3f, 1.6f);
                    WnRock(run.Rocks, axis + side * off + Vector3.up * .3f, v, Random.Range(PelagWreckNinthLook.RockMin(run.Charges), PelagWreckNinthLook.RockMax(run.Charges)));
                }
            }
            if (!run.Ended && run.StepsDone >= lane.Travel)
            {
                run.Ended = true;
                Vector3 end = lane.Origin + lane.Dir * lane.End;
                end.y = ground(end.x, end.z);
                for (int k = 0; k < run.Charges + 1; k++)
                    WnRock(run.Rocks, end + side * Random.Range(-.3f, .3f) + Vector3.up * .3f,
                        Vector3.up * Random.Range(3.4f, 5f) + lane.Dir * Random.Range(.6f, 1.8f) + side * Random.Range(-1.4f, 1.4f),
                        Random.Range(PelagWreckNinthLook.RockMin(run.Charges), PelagWreckNinthLook.RockMax(run.Charges)) * .85f);
            }
        }

        // Раунд 1: камни под индиговым светом выходили светло-сиреневыми; раунд 2 (темнее + подсветка) — фиолетово-синими.
        // Бурый серый камень, без подсветки: фиолетовым их красит только свет фронта, пока он идёт.
        private static readonly Color WnRockDark = new Color(.42f, .35f, .30f, 1f), WnRockLight = new Color(.58f, .50f, .43f, 1f);

        private static void WnRock(ParticleSystem system, Vector3 at, Vector3 velocity, float size)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startColor = Color.Lerp(WnRockDark, WnRockLight, Random.value), startSize = size,
                startLifetime = Random.Range(1.1f, 1.4f),
                rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)), applyShapeToPosition = false
            }, 1);
        }

        private void ReleaseWreckNinthRun(WreckNinthRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object))
            {
                if (run.Light != null) run.Light.intensity = 0f;
                Release(run.Fx);
            }
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
            run.Light = null;
        }

        private void ForgetWreckNinthLunge()
        {
            foreach (WreckNinthRun run in _wnRuns) ReleaseWreckNinthRun(run);
        }
    }
}
