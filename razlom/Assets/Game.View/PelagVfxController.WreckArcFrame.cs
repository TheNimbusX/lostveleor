using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v2 — кадр пенной дуги (поздний кадр, после рига якоря): путь головы, лента по нему,
    /// серп пака маха (.WreckSweep), вихрь брызг «над головой», капли с цепи в окне. Сроки этапа
    /// уточняются по снимку Sim (заряд Девятого вала сдвигает удар), пока номер серии и этап
    /// совпадают. Голову ведёт прежний PelagAnchorSlamView — путь не пишется, лента пустая.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Девятый вал, начало заряда: вихрь на голове и горб в точке удара.</summary>
        private void BeginWreckCharge(in WkPending p)
        {
            BeginWreckChargeArc(p);
            BeginWreckHump(p);
        }

        /// <summary>Отпустили (или 30): заряд замирает; полный — вспышка капель.</summary>
        private void ReleaseWreckCharge(in WkPending p)
        {
            ReleaseWreckChargeArc(p);
            ReleaseWreckHump(p);
        }

        private void UpdateWreckArcs(Simulation sim, float shown, float dt)
        {
            bool any = false;
            foreach (WreckArcRun run in _wkArcs) any |= run.Active;
            if (!any) return;
            bool hasHead = WreckHeadPoint(out Vector3 head);
            // Прежний путь головы (риг серию не взял): новую дугу поверх старых клипов не рисуем.
            bool legacy = WreckLegacyHead;
            if (legacy) hasHead = false;
            WreckState w = sim.Wreck;
            Camera camera = Camera.main;
            Vector3 eye = camera != null ? camera.transform.position : head + Vector3.up * 10f;
            foreach (WreckArcRun run in _wkArcs)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Fx = -1; run.Object = null; continue; }
                if (legacy || run.Iron) { run.Count = 0; if (legacy) run.HasHead = false; }
                if (w.Serial == run.Serial && w.Stage == run.Stage && w.Phase != WreckPhase.None)
                {
                    run.Contact = w.ContactTick;
                    if (w.OverheadTick >= 0) run.Overhead = w.OverheadTick;
                }
                bool newest = run == _wkArcNow;
                bool recording = newest && !run.Ended && hasHead && (run.Charging
                    ? shown <= run.Contact + PelagWreckVfxRules.ArcRecordAfterTicks
                    : PelagWreckVfxRules.ArcRecording(shown, run.StageStart, run.Contact));
                if (recording) RecordWreckHead(run, head, shown, dt);
                else if (newest && hasHead) TrackWreckHead(run, head, dt);

                float charge = run.Charging ? PelagWreckVfxRules.ChargeShown(shown, run.ChargeStart, run.ReleaseTick, run.ReleasedCharge) : 0f;
                float k = PelagWreckVfxRules.Charge01(charge);
                if (recording)
                {
                    // Хвост — только недавний путь головы (у вихря — больше круга с зарядом).
                    float tail = run.Charging ? PelagWreckVfxRules.VortexTailSeconds(k) : PelagWreckVfxRules.ArcTailSeconds;
                    TrimWreckArc(run, shown - tail * Simulation.TicksPerSecond);
                }
                float headHalf = run.Charging ? PelagWreckVfxRules.VortexHalfWidth(k)
                    : PelagWreckVfxRules.ArcHeadHalfWidth(run.Stage, PelagWreckVfxRules.ArcGrow(shown, run.StageStart, run.Contact));
                float age = PelagWreckVfxRules.ArcBreakAge(shown, run.Contact);
                if (run.Superseded && !recording) age = Mathf.Max(age, .05f);
                run.Flow += dt * (run.Charging ? 5f + 6f * k : 4f);
                if (run.Filter != null && !run.Iron)
                    run.Water.Build(FormWaterMesh.MeshFor(run.Filter, PelagWreckArcWater.MeshName), run.Object.transform.position,
                        run.Points, run.Count, eye, headHalf * .25f, headHalf, age + .06f, age, .15f + .25f * k, run.Flow, shown / Simulation.TicksPerSecond, 1f);

                if (!CaptureRig.NoVfx && newest && run.HasHead && !run.Iron)
                {
                    // Тело маха — серп пака: рождается за SweepLeadTicks до удара, его голова приходит к удару с головой якоря.
                    if (!run.Charging && !run.SweepDone && shown >= run.Contact - PelagWreckVfxRules.SweepLeadTicks)
                    {
                        run.SweepDone = true;
                        PlayWreckSweep(run, w);
                    }
                    // Удар оземь: вихрь брызг по вертикальной дуге в тик «над головой».
                    if (!run.Charging && run.Stage == 2 && run.Overhead >= 0 && !run.OverheadDone && shown >= run.Overhead)
                    {
                        run.OverheadDone = true;
                        Vector3 along = run.Velocity.sqrMagnitude > 1f ? run.Velocity.normalized : Vector3.up;
                        for (int i = 0; i < 14; i++)
                            WreckEmit(run.Drops, run.Head, (along + Random.insideUnitSphere * .6f).normalized * Random.Range(2f, 4.5f),
                                PelagWreckFormLook.DropColor(PelagForm.None, Random.Range(.2f, 1f)));
                    }
                    // Окно: капли срываются с цепи и головы, след тает.
                    if (w.Serial == run.Serial && w.Phase == WreckPhase.Window && run.Drops != null && dt > 0f)
                    {
                        run.DropCarry += dt * PelagWreckVfxRules.WindowDropRate;
                        int count = (int)run.DropCarry;
                        run.DropCarry -= count;
                        Vector3 hand = _arena.PlayerChainHandPosition;
                        for (int i = 0; i < count; i++)
                            WreckEmit(run.Drops, Vector3.Lerp(hand, run.Head, Random.Range(.3f, 1f)), run.Velocity * .15f + Random.insideUnitSphere * .3f,
                                PelagWreckFormLook.DropColor(PelagForm.None, Random.Range(.3f, 1f)));
                    }
                    // Вихрь Девятого вала: брызги срываются с головы по ходу вращения.
                    if (run.Charging && shown <= run.Contact && run.Drops != null && dt > 0f)
                    {
                        run.DropCarry += dt * (10f + 30f * k);
                        int count = (int)run.DropCarry;
                        run.DropCarry -= count;
                        for (int i = 0; i < count; i++)
                            WreckEmit(run.Drops, run.Head, run.Velocity * Random.Range(.15f, .3f) + Random.insideUnitSphere * .5f,
                                PelagWreckFormLook.DropColor(PelagForm.WreckNinthWave, Random.Range(.3f, 1f)));
                    }
                }

                bool finished = (run.Superseded || run.Ended || !newest) && PelagWreckVfxRules.ArcDone(shown, run.Contact)
                                && (run.EndShown < 0f || shown > run.EndShown + 18f);
                if (finished || Time.time - run.BornAt > 7.5f) ReleaseWreckArc(run);
            }
        }

        /// <summary>Новая точка пути головы (кадр показа) и скорость головы.</summary>
        private static void RecordWreckHead(WreckArcRun run, Vector3 head, float shown, float dt)
        {
            TrackWreckHead(run, head, dt);
            if (run.Count >= run.Points.Length)
            {
                for (int i = 1; i < run.Count; i++) { run.Points[i - 1] = run.Points[i]; run.Times[i - 1] = run.Times[i]; }
                run.Count--;
            }
            // Голова стоит (пауза, стоп-кадр) — точку не плодить.
            if (run.Count > 0 && (run.Points[run.Count - 1] - head).sqrMagnitude < 1e-6f) return;
            run.Points[run.Count] = head;
            run.Times[run.Count] = shown;
            run.Count++;
        }

        private static void TrackWreckHead(WreckArcRun run, Vector3 head, float dt)
        {
            if (run.HasHead && dt > 1e-4f) run.Velocity = Vector3.Lerp(run.Velocity, (head - run.Head) / dt, .6f);
            run.Head = head;
            run.HasHead = true;
        }

        /// <summary>Убрать точки старше <paramref name="oldest"/> (тик показа), оставив хотя бы две.</summary>
        private static void TrimWreckArc(WreckArcRun run, float oldest)
        {
            int drop = 0;
            while (drop < run.Count - 2 && run.Times[drop] < oldest) drop++;
            if (drop == 0) return;
            for (int i = drop; i < run.Count; i++) { run.Points[i - drop] = run.Points[i]; run.Times[i - drop] = run.Times[i]; }
            run.Count -= drop;
        }
    }
}
