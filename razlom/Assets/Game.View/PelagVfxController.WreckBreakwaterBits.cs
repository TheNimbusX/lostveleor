using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Волнорез — куски стены (см. .WreckBreakwater): выход плиты (пыль и комья у подошвы, крошка, зелёные искры, полоса
    /// света в щели, трещины по земле), распад плиты (обломки — 3D-камни, пыль, искры), подхват (всплеск гребня под телом,
    /// крошка), несомые (крошка из-под тела до обрушения), обрушение Sim у конца стены (о преграду — каменный взрыв).
    /// Прежние пенные корона, кольцо воды и всплеск этой формы больше не рождаются (их префабы и код на месте).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        // Вершинные цвета частиц — в линейном пространстве (как у стека выпада).
        private static readonly Color WbGreen = new Color(.014f, .45f, .21f, 1f);
        private static readonly Color WbMint = new Color(.30f, 1.05f, .70f, 1f);
        /// <summary>Контур стека в форме Волнореза — тёмная морская зелень (владелец 07.10: чёрная обводка «херовая»).</summary>
        private static readonly Color WbContour = new Color(.012f, .10f, .065f, .9f);
        private static readonly Color WbSeam = new Color(.05f, .85f, .42f, 1f);
        private static readonly Color WbChunkLight = new Color(.95f, .94f, .92f, 1f), WbChunkDark = new Color(.70f, .68f, .66f, 1f);

        /// <summary>Плита вышла: пыль и комья в стороны от подошвы, крошка у подошвы, искры из щели, свет в щели, трещины.</summary>
        private void EmitWreckBreakPop(WreckBreakRun run, in PelagWreckBreakwaterLook.Slab slab, Vector3 foot, float stand)
        {
            float k = slab.Main ? 1f : .6f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 side = run.Side * s;
                Vector3 at = foot + side * (slab.Width * .5f) + Vector3.up * .18f;
                WclEmit(run.Dust, at, side * Random.Range(.5f, 1.2f) + Vector3.up * Random.Range(.25f, .6f) + run.Dir * Random.Range(-.2f, .6f),
                    Color.Lerp(WclDust, WclDustDark, Random.value), Random.Range(.36f, .55f) * k, Random.Range(.34f, .5f), Random.Range(0f, 360f));
                if (Random.value < .5f + .4f * k)
                    WclEmit(run.Clods, at, side * Random.Range(.8f, 2f) + Vector3.up * Random.Range(2.2f, 3.6f) + run.Dir * Random.Range(-.3f, .8f),
                        Color.Lerp(WclClod, WclClodLight, Random.value), Random.Range(.15f, .26f), Random.Range(.5f, .75f), Random.Range(0f, 360f));
            }
            // Раунд 2: над стеной ничего не летело — на целевом кадре над гребнем крупные камни; у части главных плит — один вверх.
            if (slab.Main && Random.value < .4f)
                WbChunk(run.Chunks, foot + Vector3.up * (slab.Height * .7f) + run.Dir * .2f,
                    run.Dir * Random.Range(.6f, 2f) + Vector3.up * Random.Range(4.6f, 6.4f) + run.Side * Random.Range(-1.2f, 1.2f),
                    Random.Range(.28f, .42f), Random.Range(1.2f, 1.5f));
            int rubble = slab.Main ? 2 : 1;
            for (int r = 0; r < rubble; r++)
                WbChunk(run.Chunks, foot + run.Side * Random.Range(-.5f, .5f) * slab.Width + Vector3.up * .25f,
                    run.Dir * Random.Range(-.6f, .9f) + Vector3.up * Random.Range(1.2f, 2.4f) + run.Side * Random.Range(-1f, 1f),
                    Random.Range(.14f, .24f), Random.Range(1.3f, 1.7f));
            for (int s = 0; s < (slab.Main ? 3 : 1); s++)
                WclEmit(run.Sparks, foot - run.Dir * (slab.Thick * .5f) + Vector3.up * Random.Range(.2f, .8f) * slab.Height,
                    run.Dir * Random.Range(-1f, 2.5f) + Vector3.up * Random.Range(2.4f, 5f) + run.Side * Random.Range(-1.6f, 1.6f),
                    s % 2 == 0 ? WbMint : WbGreen, Random.Range(.035f, .06f), Random.Range(.16f, .3f), 0f);
            if (!slab.Main) return;
            // Щель за плитой (между ней и прошлой): стоячий веер света от земли (лучи вверх) — его закрывают соседние плиты,
            // в кадре он виден только в щели.
            if (run.Seams != null)
                run.Seams.Emit(new ParticleSystem.EmitParams
                {
                    position = foot - run.Dir * (PelagWreckBreakwaterLook.MainSpacing * .5f) + run.Side * Random.Range(-.12f, .12f),
                    startColor = WbSeam, startLifetime = stand + .12f,
                    startSize3D = new Vector3(slab.Width * .95f, slab.Height * 1.3f, 1f), rotation = 0f, applyShapeToPosition = false
                }, 1);
            float angle = WclDecalAngle(run.Dir) + Random.Range(-12f, 12f) + (Random.value < .5f ? 180f : 0f);
            Vector3 mark = foot + Vector3.up * .03f;
            WbDecal(run.GroundGlow, mark, angle, slab.Width * 1.25f, .95f, WbGreen, stand + .35f);
            WbDecal(run.GroundCrack, mark, angle + 90f, 1.05f, slab.Width * 1.45f, WclScorch, stand + 1.6f);
        }

        /// <summary>Плита рвётся: обломки 3D-камня из её тела вверх и вперёд, пыль, зелёные искры из трещин, комья.</summary>
        private void EmitWreckBreakCrumble(WreckBreakRun run, in PelagWreckBreakwaterLook.Slab slab, Vector3 foot)
        {
            int count = slab.Main ? 4 : 2;
            float scale = slab.Height / 1.4f;
            for (int c = 0; c < count; c++)
            {
                Vector3 at = foot + Vector3.up * (slab.Height * Random.Range(.15f, .75f)) + run.Dir * (slab.Height * Random.Range(.1f, .45f))
                    + run.Side * Random.Range(-.4f, .4f) * slab.Width;
                Vector3 v = run.Dir * Random.Range(.4f, 2.2f) + Vector3.up * Random.Range(1.8f, 4.2f) + run.Side * Random.Range(-1.6f, 1.6f);
                WbChunk(run.Chunks, at, v, Random.Range(.22f, .40f) * scale, Random.Range(1.1f, 1.5f));
            }
            for (int s = -1; s <= 1; s += 2)
                WclEmit(run.Dust, foot + run.Side * (s * slab.Width * .35f) + Vector3.up * (.3f * slab.Height),
                    run.Side * (s * Random.Range(.4f, 1f)) + Vector3.up * Random.Range(.2f, .5f) + run.Dir * Random.Range(0f, .8f),
                    Color.Lerp(WclDust, WclDustDark, Random.value), Random.Range(.45f, .7f) * scale, Random.Range(.40f, .6f), Random.Range(0f, 360f));
            for (int s = 0; s < (slab.Main ? 4 : 2); s++)
                WclEmit(run.Sparks, foot + Vector3.up * (slab.Height * Random.Range(.2f, .7f)),
                    run.Dir * Random.Range(.5f, 3f) + Vector3.up * Random.Range(2f, 4.5f) + run.Side * Random.Range(-2f, 2f),
                    s % 2 == 0 ? WbMint : WbGreen, Random.Range(.035f, .06f), Random.Range(.14f, .26f), 0f);
            if (slab.Main)
                WclEmit(run.Clods, foot + Vector3.up * .3f, run.Dir * Random.Range(.4f, 1.6f) + Vector3.up * Random.Range(2.2f, 3.4f) + run.Side * Random.Range(-1f, 1f),
                    Color.Lerp(WclClod, WclClodLight, Random.value), Random.Range(.16f, .26f), Random.Range(.5f, .75f), Random.Range(0f, 360f));
        }

        /// <summary>
        /// Подхват стеной (из PlayWreckCatch после подъёма тела): под телом — всплеск гребня (стоячая полузвезда в зелени),
        /// крошка и комья вверх, искры, пыль; несомого — в список крошки до обрушения. True — пены этой формы больше нет.
        /// </summary>
        private bool PlayWreckBreakwaterCatch(in WkPending p, Vector3 body)
        {
            if (!WreckBreakwaterReady) return false;
            if (CaptureRig.NoVfx) return true;
            WreckBreakRun run = LatestWreckBreakRun();
            if (run == null) return true;
            var foot = new Vector3(body.x, WreckGroundAt(body) + .02f, body.z);
            float size = p.Flag ? 1.25f : 1.0f;
            if (run.HalfFx != null)
                run.HalfFx.Emit(new ParticleSystem.EmitParams
                {
                    position = foot + run.Dir * .15f, startColor = WbGreen, startLifetime = .30f, startSize3D = new Vector3(size, size * .5f, 1f),
                    rotation = 0f, applyShapeToPosition = false
                }, 1);
            for (int c = 0; c < (p.Flag ? 4 : 3); c++)
                WbChunk(run.Chunks, foot + Vector3.up * .3f + run.Side * Random.Range(-.3f, .3f),
                    -run.Dir * Random.Range(.2f, 1.2f) + Vector3.up * Random.Range(2.6f, 4.4f) + run.Side * Random.Range(-1.4f, 1.4f),
                    Random.Range(.14f, .26f), Random.Range(1f, 1.3f));
            for (int s = 0; s < 4; s++)
                WclEmit(run.Sparks, foot + Vector3.up * Random.Range(.3f, .8f), run.Dir * Random.Range(.5f, 3f) + Vector3.up * Random.Range(2.5f, 5f)
                    + run.Side * Random.Range(-2f, 2f), s % 2 == 0 ? WbMint : WbGreen, Random.Range(.035f, .06f), Random.Range(.16f, .28f), 0f);
            for (int s = -1; s <= 1; s += 2)
                WclEmit(run.Dust, foot + run.Side * (s * .35f) + Vector3.up * .2f, run.Side * (s * Random.Range(.5f, 1.1f)) + Vector3.up * .4f,
                    Color.Lerp(WclDust, WclDustDark, Random.value), Random.Range(.40f, .6f), Random.Range(.35f, .5f), Random.Range(0f, 360f));
            if (p.Flag && p.Target >= 0 && !run.Carried.Contains(p.Target))
            {
                run.Carried.Add(p.Target);
                run.CarriedUntil.Add(p.Tick + Mathf.Max(1, p.Amount));
            }
            return true;
        }

        /// <summary>Несомые на гребне: до обрушения из-под тела сыплется крошка и комья (тело ведёт Sim, вид его не двигает).</summary>
        private void UpdateWreckBreakCarry(WreckBreakRun run, float shown)
        {
            if (run.Carried.Count == 0 || CaptureRig.NoVfx) return;
            run.CarryClock += Time.deltaTime;
            if (run.CarryClock < PelagWreckBreakwaterLook.CarryChipSeconds) return;
            run.CarryClock = 0f;
            for (int i = 0; i < run.Carried.Count; i++)
            {
                if (shown > run.CarriedUntil[i]) continue;
                Vector3 body = EntityPosition(run.Carried[i], run.Root);
                var at = new Vector3(body.x, WreckGroundAt(body) + .35f, body.z);
                WbChunk(run.Chunks, at + run.Side * Random.Range(-.3f, .3f), run.Dir * Random.Range(-1.2f, .4f) + Vector3.up * Random.Range(1f, 2.2f)
                    + run.Side * Random.Range(-1f, 1f), Random.Range(.10f, .18f), Random.Range(.8f, 1.1f));
                if (Random.value < .5f)
                    WclEmit(run.Clods, at, Vector3.up * Random.Range(1.2f, 2.4f) + run.Side * Random.Range(-1.2f, 1.2f) - run.Dir * Random.Range(0f, 1f),
                        Color.Lerp(WclClod, WclClodLight, Random.value), Random.Range(.12f, .2f), Random.Range(.4f, .6f), Random.Range(0f, 360f));
            }
        }

        /// <summary>
        /// Знак задетого стеной и обрушением (из PlayWreckComboLungeMark): раунд 1 — звёзды обрушения выходили почти белыми
        /// (в тёплом грейде кремовыми); белое — бледной мятой и только в сердцевине, контур — тёмная зелень. Блок звезды стек
        /// базы снимает/ставит заново при каждом знаке — у других форм значения не протекают.
        /// </summary>
        private void TintWreckBreakMark(Transform root, PelagForm form)
        {
            if (form != PelagForm.WreckBreakwater || root == null) return;
            Renderer star = root.Find("Burst")?.GetComponent<Renderer>();
            if (star == null) return;
            if (_wbBlock == null) _wbBlock = new MaterialPropertyBlock();
            star.GetPropertyBlock(_wbBlock);
            _wbBlock.SetFloat(WclCoreWidthId, .68f);
            _wbBlock.SetColor(WgCoreId, new Color(.55f, 1.05f, .80f, 1f));
            _wbBlock.SetColor(WgOutlineId, WbContour);
            star.SetPropertyBlock(_wbBlock);
        }

        private static void WbChunk(ParticleSystem system, Vector3 at, Vector3 velocity, float size, float life)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startColor = Color.Lerp(WbChunkDark, WbChunkLight, Random.value), startSize = size,
                startLifetime = life, rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f)),
                applyShapeToPosition = false
            }, 1);
        }

        private static void WbDecal(ParticleSystem system, Vector3 at, float angle, float width, float length, Color color, float life)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, startColor = color, startSize3D = new Vector3(width, length, 1f), rotation = angle, startLifetime = life,
                applyShapeToPosition = false
            }, 1);
        }
    }
}
