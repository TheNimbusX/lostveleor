using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Волнорез — обрушение стены (событие Sim WreckBreakwaterCrash, тик показа; см. .WreckBreakwater): стоящие плиты рушатся
    /// (сначала у конца стены), у конца — стоячая полузвезда в зелени, свет трещин по земле до круга обрушения Sim (2 м), пыль
    /// кольцом, обломки и зелёные искры; о преграду (урон 70, флаг события) — стоячий каменный взрыв: звезда, обломки вверх и
    /// назад, полосы света трещин у преграды, толчок камеры сильнее. Пенного кольца воды, короны и всплеска больше нет.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Обрушение (из PlayWreckCrash). True — пены этой формы больше нет (даже без живой стены).</summary>
        private bool PlayWreckBreakwaterCrash(in WkPending p)
        {
            if (!WreckBreakwaterReady) return false;
            if (CaptureRig.NoVfx) return true;
            WreckBreakRun run = LatestWreckBreakRun();
            if (run == null) return true;
            bool obstacle = p.Flag;
            float radius = p.Extra > 0 ? p.Extra / 100f : Simulation.WreckBreakwaterCrashRadius.ToFloat();
            // Стоящие плиты рушатся теперь (тик события может разойтись с ожидаемым на тик — берётся событие).
            run.Crashed = true;
            run.CrashTick = p.Tick;
            run.CrashShown = p.Tick;
            for (int i = 0; i < run.States.Length; i++)
                if (!run.States[i].Hidden)
                    run.States[i].Crumble = Mathf.Min(run.States[i].Crumble,
                        PelagWreckBreakwaterLook.CrumbleTick(run.States[i].Pass, p.Tick, run.Slabs[i].Along, run.Start, run.End));
            run.Carried.Clear();
            run.CarriedUntil.Clear();

            System.Func<float, float, float> ground = WreckIronGroundAt(run.Root.y);
            Vector3 end = new Vector3(p.At.x, 0f, p.At.z);
            end.y = ground(end.x, end.z);
            Vector3 dir = run.Dir, side = run.Side;
            float half = PelagWreckBreakwaterLook.CrashHalfWidth(radius, obstacle);
            if (run.HalfFx != null)
                run.HalfFx.Emit(new ParticleSystem.EmitParams
                {
                    position = end - dir * .15f, startColor = WbGreen, startLifetime = obstacle ? .40f : .32f,
                    startSize3D = new Vector3(half, half * .52f, 1f), rotation = 0f, applyShapeToPosition = false
                }, 1);
            float ground2 = PelagWreckBreakwaterLook.CrashGroundDiameter(radius);
            WbDecal(run.GroundGlow, end + Vector3.up * .035f, Random.Range(0f, 360f), ground2, ground2, WbGreen, obstacle ? .9f : .7f);
            WbDecal(run.GroundCrack, end + Vector3.up * .03f, Random.Range(0f, 360f), ground2 * .9f, ground2 * .9f, WclScorch, 2.2f);
            // Пыль кольцом до круга обрушения, наружу.
            int puffs = obstacle ? 14 : 10;
            for (int i = 0; i < puffs; i++)
            {
                float a = (i + Random.Range(-.3f, .3f)) * Mathf.PI * 2f / puffs;
                Vector3 radial = dir * Mathf.Cos(a) + side * Mathf.Sin(a);
                WclEmit(run.Dust, end + radial * (radius * .45f) + Vector3.up * .25f, radial * Random.Range(1f, 2.2f) + Vector3.up * Random.Range(.3f, .7f),
                    Color.Lerp(WclDust, WclDustDark, Random.value), Random.Range(.55f, .85f), Random.Range(.45f, .7f), Random.Range(0f, 360f));
            }
            // Обломки: о преграду — вверх и НАЗАД (стена разбилась о камень), иначе — вверх и вперёд по ходу.
            int rocks = PelagWreckBreakwaterLook.CrashRocks(obstacle);
            for (int i = 0; i < rocks; i++)
            {
                Vector3 back = obstacle ? -dir * Random.Range(.5f, 2.6f) : dir * Random.Range(.3f, 1.8f);
                Vector3 v = back + Vector3.up * Random.Range(obstacle ? 3.8f : 2.6f, obstacle ? 6.8f : 4.6f) + side * Random.Range(-2.4f, 2.4f);
                WbChunk(run.Chunks, end + side * Random.Range(-.6f, .6f) + Vector3.up * Random.Range(.3f, 1f), v,
                    Random.Range(obstacle ? .30f : .22f, obstacle ? .58f : .42f), Random.Range(1.2f, 1.6f));
            }
            int sparks = obstacle ? 24 : 14;
            for (int i = 0; i < sparks; i++)
                WclEmit(run.Sparks, end + Vector3.up * Random.Range(.3f, 1.1f), (obstacle ? -dir : dir) * Random.Range(.5f, 4f)
                    + Vector3.up * Random.Range(2.5f, 6f) + side * Random.Range(-3f, 3f), i % 2 == 0 ? WbMint : WbGreen,
                    Random.Range(.04f, .07f), Random.Range(.16f, .32f), 0f);
            for (int i = 0; i < (obstacle ? 6 : 4); i++)
                WclEmit(run.Clods, end + Vector3.up * .3f, Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Vector3.forward * Random.Range(1f, 2.4f)
                    + Vector3.up * Random.Range(2.4f, 3.8f), Color.Lerp(WclClod, WclClodLight, Random.value), Random.Range(.18f, .3f),
                    Random.Range(.5f, .8f), Random.Range(0f, 360f));
            if (obstacle)
            {
                // О преграду: стоячая звезда у камня и полосы света трещин вверх — «вспышка зелёных трещин».
                if (run.Star != null)
                    run.Star.Emit(new ParticleSystem.EmitParams
                    {
                        position = end - dir * .3f + Vector3.up * .95f, startColor = WbGreen, startLifetime = .28f,
                        startSize3D = new Vector3(2.2f, 2.2f, 1f), rotation = Random.Range(0f, 360f), applyShapeToPosition = false
                    }, 1);
                if (run.Seams != null)
                    for (int i = 0; i < 3; i++)
                        run.Seams.Emit(new ParticleSystem.EmitParams
                        {
                            position = end - dir * .35f + side * ((i - 1) * .55f + Random.Range(-.1f, .1f)), startColor = WbGreen,
                            startLifetime = .42f, startSize3D = new Vector3(.7f, Random.Range(1.6f, 2.3f), 1f), rotation = 0f, applyShapeToPosition = false
                        }, 1);
            }
            _juice?.PunchCamera(obstacle ? .26f : .18f, .05f);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-breakwater] crash tick={p.Tick} carried={p.Amount} obstacle={obstacle} r={radius:F2} end={end.ToString("F2")}");
            return true;
        }
    }
}
