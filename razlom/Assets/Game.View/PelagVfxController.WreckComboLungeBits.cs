using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Выпад Крушения на стеке сабли — куски по полосе (как брызги и клочья у добивающего сабли, но якорная семья):
    /// на каждом шаге фронта Sim — искры с верха гребня вперёд и вверх, скол железа, комья по краям полосы, крупный
    /// камень (3D) не на каждом шаге, пыль у основания, след трещин по земле (тёмный и короткое кобальтовое свечение);
    /// в конце полосы — малая стоячая полузвезда, искры, камни, комья, пыль. Краска форм. Знак на задетых выпадом.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Фронт дошёл до новых шагов Sim: куски с каждого шага; последний шаг — всплеск конца полосы.</summary>
        private void EmitWreckComboLungeFront(WreckComboLungeRun run, float shown, Vector3 lift, System.Func<float, float, float> ground)
        {
            if (CaptureRig.NoVfx || run.Travel <= 0) return;
            int steps = Mathf.Clamp(Mathf.FloorToInt(shown - run.Tick + 1f + 1e-3f), 0, run.Travel);
            var side = new Vector3(-run.Dir.z, 0f, run.Dir.x);
            float decalAngle = WclDecalAngle(run.Dir);
            while (run.StepsDone < steps)
            {
                run.StepsDone++;
                float at = Mathf.Min(run.End, run.Start + run.Step * run.StepsDone);
                Vector3 axis = run.Origin + run.Dir * at;
                axis.y = ground(axis.x, axis.z);
                float h = run.Height;
                for (int i = 0; i < PelagWreckComboLungeLook.StepSparks; i++)
                {
                    Vector3 from = axis - run.Dir * Random.Range(.05f, .45f) + lift * (h * Random.Range(.55f, 1.05f));
                    Vector3 v = run.Dir * Random.Range(3f, 8.5f) + Vector3.up * Random.Range(1.2f, 4f) + side * Random.Range(-1.6f, 1.6f);
                    WclEmit(run.Sparks, from, v, i % 2 == 0 ? run.Hot : WclSparkBlue(run), Random.Range(.035f, .065f), Random.Range(.14f, .28f), 0f);
                }
                // L1: по два кома, скол и клуб на каждом шаге засыпали полосу бурым и синей галькой — теперь через шаг.
                bool odd = (run.StepsDone & 1) == 1;
                Vector3 chipAt = axis + side * Random.Range(-run.Half, run.Half) * .6f + Vector3.up * .15f;
                if (odd)
                    WclEmit(run.Chips, chipAt, run.Dir * Random.Range(.8f, 2.4f) + Vector3.up * Random.Range(2.6f, 4.2f) + side * Random.Range(-1f, 1f),
                        Color.Lerp(WclIronFace, WclIronSteel, Random.value), Random.Range(.14f, .22f), Random.Range(.35f, .55f), Random.Range(0f, 360f));
                {
                    float sign = odd ? 1f : -1f;
                    Vector3 lip = axis + side * sign * run.Half * Random.Range(.45f, .95f) - run.Dir * Random.Range(0f, run.Step);
                    lip.y = ground(lip.x, lip.z) + .1f;
                    WclEmit(run.Clods, lip, Vector3.up * Random.Range(2.2f, 3.8f) + side * sign * Random.Range(.7f, 1.8f) + run.Dir * Random.Range(.2f, 1.2f),
                        Color.Lerp(WclClod, WclClodLight, Random.value), Random.Range(.16f, .30f), Random.Range(.5f, .8f), Random.Range(0f, 360f));
                    Vector3 puff = axis + side * sign * run.Half * Random.Range(.6f, 1.0f);
                    puff.y = ground(puff.x, puff.z) + .2f;
                    if (!odd)
                        WclEmit(run.Dust, puff, side * sign * Random.Range(.4f, 1.0f) + Vector3.up * Random.Range(.2f, .5f) + run.Dir * Random.Range(0f, .8f),
                            Color.Lerp(WclDust, WclDustDark, Random.value), Random.Range(.30f, .48f), Random.Range(.30f, .45f), Random.Range(0f, 360f));
                }
                if (PelagWreckComboLungeLook.BigRockAt(run.StepsDone, run.Travel, Random.value))
                    WclRock(run.Rocks, axis + side * Random.Range(-.3f, .3f) + Vector3.up * .2f,
                        run.Dir * Random.Range(.5f, 1.6f) + Vector3.up * Random.Range(3.6f, 5.6f) + side * Random.Range(-.8f, .8f), Random.Range(.50f, .70f));
                if (Random.value < .5f)
                    WclRock(run.Rocks, axis + side * Random.Range(-run.Half, run.Half) * .8f + Vector3.up * .15f,
                        run.Dir * Random.Range(.2f, 1.2f) + Vector3.up * Random.Range(2.6f, 4.2f) + side * Random.Range(-1.4f, 1.4f), Random.Range(.24f, .36f));
                // След трещин: тёмный (живёт ~1 с) и короткое кобальтовое свечение того же места.
                Vector3 mark = run.Origin + run.Dir * (at - run.Step * .5f);
                mark.y = ground(mark.x, mark.z) + .03f;
                float angle = decalAngle + Random.Range(-10f, 10f) + (Random.value < .5f ? 180f : 0f);
                WclDecal(run.Scorch, mark, angle, run.Half * .9f, run.Step * 1.9f, WclScorch);
                WclDecal(run.ScorchGlow, mark, angle, run.Half * .9f, run.Step * 1.9f, run.Cobalt);
            }
            if (!run.Ended && run.StepsDone >= run.Travel) EndWreckComboLunge(run, lift, ground);
        }

        /// <summary>Конец полосы (последний шаг или преграда): малая стоячая полузвезда, искры веером, камни, комья, пыль.</summary>
        private void EndWreckComboLunge(WreckComboLungeRun run, Vector3 lift, System.Func<float, float, float> ground)
        {
            run.Ended = true;
            var side = new Vector3(-run.Dir.z, 0f, run.Dir.x);
            Vector3 end = run.Origin + run.Dir * run.End;
            end.y = ground(end.x, end.z);
            if (run.EndHalf != null)
                run.EndHalf.Emit(new ParticleSystem.EmitParams
                {
                    position = end, startColor = run.Cobalt, startLifetime = .30f, startSize3D = new Vector3(1.7f, .85f, 1f),
                    rotation = 0f, applyShapeToPosition = false
                }, 1);
            for (int i = 0; i < PelagWreckComboLungeLook.EndSparks; i++)
            {
                Vector3 v = run.Dir * Random.Range(2.5f, 7f) + Vector3.up * Random.Range(2f, 5f) + side * Random.Range(-3f, 3f);
                WclEmit(run.Sparks, end + lift * Random.Range(.2f, .6f), v, i % 2 == 0 ? run.Hot : WclSparkBlue(run), Random.Range(.04f, .07f), Random.Range(.16f, .30f), 0f);
            }
            for (int i = 0; i < 2; i++)
                WclRock(run.Rocks, end + side * Random.Range(-.3f, .3f) + Vector3.up * .2f,
                    run.Dir * Random.Range(.6f, 1.6f) + Vector3.up * Random.Range(3.4f, 5f) + side * Random.Range(-1.2f, 1.2f), Random.Range(.36f, .52f));
            for (int i = 0; i < 3; i++)
            {
                float a = Random.Range(-1.2f, 1.2f);
                Vector3 out2 = (run.Dir * Mathf.Cos(a) + side * Mathf.Sin(a)).normalized;
                WclEmit(run.Clods, end + Vector3.up * .15f, out2 * Random.Range(1f, 2.4f) + Vector3.up * Random.Range(2.4f, 3.8f),
                    Color.Lerp(WclClod, WclClodLight, Random.value), Random.Range(.18f, .30f), Random.Range(.5f, .8f), Random.Range(0f, 360f));
                WclEmit(run.Dust, end + Vector3.up * .2f + out2 * .3f, out2 * Random.Range(.6f, 1.4f) + Vector3.up * .3f,
                    Color.Lerp(WclDust, WclDustDark, Random.value), Random.Range(.55f, .9f), Random.Range(.45f, .75f), Random.Range(0f, 360f));
            }
        }

        // Вершинные цвета частиц — в линейном пространстве (как у махов, PelagWreckComboVfxSetup).
        private static readonly Color WclIronFace = new Color(.06f, .075f, .11f, 1f);
        private static readonly Color WclIronSteel = new Color(.10f, .13f, .20f, 1f);
        private static readonly Color WclClod = new Color(.12f, .07f, .035f, 1f), WclClodLight = new Color(.17f, .10f, .05f, 1f);
        private static readonly Color WclDust = new Color(.50f, .40f, .30f, .8f), WclDustDark = new Color(.42f, .32f, .23f, .8f);
        private static readonly Color WclScorch = new Color(.05f, .035f, .03f, .9f);
        private static readonly Color WclRockEarth = new Color(.72f, .63f, .56f, 1f), WclRockStone = new Color(.86f, .86f, .90f, 1f);

        private static readonly int WclCoreWidthId = Shader.PropertyToID("_CoreWidth");

        private static Color WclSparkBlue(WreckComboLungeRun run) => Color.Lerp(run.Cobalt, Color.white, .35f) * 1.15f;

        private static void WclEmit(ParticleSystem system, Vector3 at, Vector3 velocity, Color color, float size, float life, float rotation)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startColor = color, startSize = size, startLifetime = life,
                rotation = rotation, angularVelocity = rotation != 0f ? Random.Range(-600f, 600f) : 0f, applyShapeToPosition = false
            }, 1);
        }

        private static void WclRock(ParticleSystem system, Vector3 at, Vector3 velocity, float size)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startColor = Color.Lerp(WclRockEarth, WclRockStone, Random.value), startSize = size,
                startLifetime = Random.Range(1.05f, 1.35f),
                rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)), applyShapeToPosition = false
            }, 1);
        }

        private static void WclDecal(ParticleSystem system, Vector3 at, float angle, float width, float length, Color color)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, startColor = color, startSize3D = new Vector3(width, length, 1f), rotation = angle, applyShapeToPosition = false
            }, 1);
        }

        /// <summary>Поворот горизонтального билборда (градусы), при котором его длинная ось Y лежит вдоль полосы.</summary>
        private static float WclDecalAngle(Vector3 dir) => -Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        /// <summary>Цвета частиц формы: база — кобальт пака; формы — основной тон формы в линейном пространстве.</summary>
        private static void WreckComboLungeColors(WreckComboLungeRun run, PelagForm form)
        {
            PelagWreckComboLook.Palette c = PelagWreckComboLook.For(form);
            if (form == PelagForm.None || form == PelagForm.WreckShell)
            {
                run.Cobalt = new Color(.12f, .36f, 1.0f, 1f);
                run.Deep = new Color(.035f, .12f, .55f, 1f);
            }
            else
            {
                run.Cobalt = WcLinear(Mathf.Lerp(c.MidR, c.LightR, .25f), Mathf.Lerp(c.MidG, c.LightG, .25f), Mathf.Lerp(c.MidB, c.LightB, .25f), 1.05f);
                run.Deep = WcLinear(Mathf.Lerp(c.DeepR, c.MidR, .5f), Mathf.Lerp(c.DeepG, c.MidG, .5f), Mathf.Lerp(c.DeepB, c.MidB, .5f), 1f);
            }
            run.Hot = WcColor(c.HotR * 1.1f, c.HotG * 1.1f, c.HotB * 1.1f);
        }

        /// <summary>Краска формы на всплеск удара (до старта систем) и на ленты гребня (у базы блок снят — цвета материала).</summary>
        private void TintWreckComboLunge(Transform root, WreckComboLungeRun run)
        {
            WclStart(root, "Half", run.Cobalt);
            WclStart(root, "Star", run.Cobalt);
            WclStart(root, "StarEcho", Color.Lerp(run.Cobalt, Color.white, .55f) * 1.2f);
            WclStart(root, "GroundStar", run.Deep);
            WclStart(root, "Glow", run.Cobalt);
            // Тёмные трещины кратера и светящиеся в них — один лист Hovl одним поворотом (свет — внутри тёмных).
            float crackTurn = Random.Range(0f, Mathf.PI * 2f);
            foreach (string crack in new[] { "Glow", "Crater" })
            {
                ParticleSystem p = root.Find(crack)?.GetComponent<ParticleSystem>();
                if (p == null) continue;
                var main = p.main;
                main.startRotation = crackTurn;
            }
            ParticleSystem sparks = root.Find("Sparks")?.GetComponent<ParticleSystem>();
            if (sparks != null) { var main = sparks.main; main.startColor = new ParticleSystem.MinMaxGradient(run.Hot, WclSparkBlue(run)); }
            bool tinted = run.Form != PelagForm.None && run.Form != PelagForm.WreckShell;
            PelagWreckComboLook.Palette c = PelagWreckComboLook.For(run.Form);
            if (_wclBlock == null) _wclBlock = new MaterialPropertyBlock();
            foreach (string name in new[] { "Crest", "CrestEcho" })
            {
                Renderer renderer = root.Find(name)?.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (!tinted) { renderer.SetPropertyBlock(null); continue; }
                _wclBlock.Clear();
                _wclBlock.SetColor(WcDeepId, WcColor(c.DeepR, c.DeepG, c.DeepB));
                _wclBlock.SetColor(WcMidId, WcColor(c.MidR, c.MidG, c.MidB));
                _wclBlock.SetColor(WcLightId, WcColor(c.LightR, c.LightG, c.LightB));
                _wclBlock.SetColor(WcHotId, WcColor(c.HotR, c.HotG, c.HotB));
                renderer.SetPropertyBlock(_wclBlock);
            }
        }

        private static void WclStart(Transform root, string name, Color color)
        {
            ParticleSystem p = root.Find(name)?.GetComponent<ParticleSystem>();
            if (p == null) return;
            var main = p.main;
            main.startColor = color;
        }

        /// <summary>
        /// Задетый выпадом (круг удара, вал, стена Волнореза): знак махов на стеке сабли крупнее — как всплеск добивающего
        /// сабли ×1,4; ход — по полосе (вал, стена) или от точки удара (круг). False — префабов нет (прежний знак).
        /// </summary>
        private bool PlayWreckComboLungeMark(in WkPending p, Vector3 body, Vector3 dir)
        {
            if (!WreckComboReady || !WreckComboLungeReady) return false;
            if (p.Hit != WreckVfxHit.Circle && p.Hit != WreckVfxHit.Wave && p.Hit != WreckVfxHit.Wall) return false;
            if (CaptureRig.NoVfx) return true;
            Simulation sim = _driver.Sim;
            float radius = sim != null && (uint)p.Target < (uint)sim.Entities.Count
                ? sim.Entities.BodyRadius[p.Target].ToFloat() : EntityStore.DefaultBodyRadius.ToFloat();
            var foot = new Vector3(body.x, WreckGroundAt(body) + .03f, body.z);
            Vector3 position = foot + Vector3.up * Mathf.Clamp(.5f + .45f * radius, .75f, 1.3f);
            Camera camera = Camera.main;
            if (camera != null) position += (camera.transform.position - position).normalized * .45f;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : PlayerFacing();
            if (!TryAcquire(PelagVfxId.WreckComboHit, out GameObject go, out PelagVfxElement element)) return true;
            PelagForm form = p.Hit == WreckVfxHit.Wall ? PelagForm.WreckBreakwater : p.Wave.Look != PelagForm.None ? p.Wave.Look : p.Form;
            TintWreckComboHit(go.transform, form);
            // Звезда знака у задетых выпадом — белое только в сердцевине (L1: кремовые звёзды ×1,35 перекрывали гребень).
            Renderer star = go.transform.Find("Burst")?.GetComponent<Renderer>();
            if (star != null)
            {
                if (_wclBlock == null) _wclBlock = new MaterialPropertyBlock();
                _wclBlock.Clear();
                _wclBlock.SetFloat(WclCoreWidthId, .56f);
                star.SetPropertyBlock(_wclBlock);
            }
            int index = ReserveActive();
            element.Begin(position, Quaternion.LookRotation(dir, Vector3.up));
            go.transform.localScale = Vector3.one * (p.Hit == WreckVfxHit.Circle ? PelagWreckComboLungeLook.CircleHitScale : PelagWreckComboLungeLook.WaveHitScale);
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WreckComboHit, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = position, End = position, Motion = Motion.Static, FollowIndex = -1
            };
            return true;
        }
    }
}
