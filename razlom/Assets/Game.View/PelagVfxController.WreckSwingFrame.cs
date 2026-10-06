using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Кадр серпа маха Крушения V2 (см. .WreckSwing): запись пути рукояти и головы каждый кадр показа, замирание,
    /// распад, звенья-призраки у наружной кромки, искры с головы.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Поздний кадр (после рига якоря и UpdateWreckArcs): путь, серп, звенья, искры.</summary>
        private void UpdateWreckSwings(Simulation sim, float shown, float dt)
        {
            bool any = false;
            foreach (WreckSwingRun run in _wsRuns) any |= run.Active;
            if (!any) return;
            bool hasHead = WreckHeadPoint(out Vector3 head) && !WreckLegacyHead;
            Vector3 grip = WreckSwingGrip();
            Vector3 pivot = WreckSwingPivot();
            bool hasSweep = sim.TryGetWreckSweep(out int sweepStage, out int sweepSerial, out _, out int sweepFrom, out int sweepTo);
            NoteWreckSweep(sim);
            WreckState w = sim.Wreck;
            foreach (WreckSwingRun run in _wsRuns)
            {
                if (!run.Active) continue;
                if (!WkStill(run.Fx, run.Object)) { run.Active = false; run.Fx = -1; run.Object = null; continue; }
                // Сектор маха — из Sim, пока это тот же мах той же серии (перетайминг и темп подхватываются сами).
                if (!run.Frozen && hasSweep && sweepSerial == run.Serial && sweepStage == run.Stage)
                {
                    run.From = PelagWreckSwingRules.RecordFrom(run.StageStart, sweepFrom);
                    run.Until = PelagWreckSwingRules.RecordUntil(sweepTo);
                }
                // Серию сорвали до удара (рывок, оглушение, смерть) — серпа нет.
                if (!run.Frozen && shown < run.Contact && (w.Serial != run.Serial || w.Phase == WreckPhase.None))
                {
                    ReleaseWreckSwing(run);
                    continue;
                }
                bool newest = run == _wsNow;
                bool recording = false;
                if (!run.Frozen)
                {
                    // Новый мах вошёл в свой сектор — голова уже на его пути: этот серп замирает.
                    bool superseded = !newest && _wsNow != null && _wsNow.Active && shown >= _wsNow.From;
                    if (hasHead && !superseded && PelagWreckSwingRules.Recording(shown, run.From, run.Until))
                    {
                        RecordWreckSwing(run, pivot, grip, head, shown, dt);
                        recording = !run.Frozen;
                    }
                    else if (superseded || shown > run.Until || (!hasHead && shown >= run.From)) FreezeWreckSwing(run, shown);
                }
                float erode = run.Frozen ? PelagWreckSwingRules.Erode(shown, run.FrozenAt, run.Weight) : 0f;
                if (CaptureRig.NoVfx) erode = 1f;
                if (run.Filter != null)
                    run.Ribbon.Build(FormWaterMesh.MeshFor(run.Filter, PelagWreckSwingRibbon.MeshName), run.Object.transform.position,
                        run.Sweep, run.Frozen ? run.Sweep.Newest : shown, run.Weight, erode, run.Seed);
                PlaceWreckSwingLinks(run, erode);
                if (recording && !CaptureRig.NoVfx) EmitWreckSwingGlints(run, dt);
                if ((run.Frozen && PelagWreckSwingRules.Done(shown, run.FrozenAt, run.Weight)) || Time.time - run.BornAt > 4f)
                    ReleaseWreckSwing(run);
            }
        }

        /// <summary>Кадр пути: рукоять и голова этого кадра; голова пошла против стороны маха — серп замирает.</summary>
        private static void RecordWreckSwing(WreckSwingRun run, Vector3 pivot, Vector3 grip, Vector3 head, float shown, float dt)
        {
            if (run.HasHead && dt > 1e-4f) run.Velocity = Vector3.Lerp(run.Velocity, (head - run.Head) / dt, .6f);
            run.Head = head;
            run.HasHead = true;
            run.Sweep.Add(shown, WsN(pivot), WsN(grip), WsN(head));
            float omega = run.Sweep.HeadOmega(Simulation.TicksPerSecond);
            run.Against = PelagWreckSwingRules.Against(run.Side, omega) ? run.Against + 1 : 0;
            if (run.Against >= PelagWreckSwingRules.ReverseFrames)
            {
                FreezeWreckSwing(run, shown);
                return;
            }
            run.Sweep.Trim(shown - PelagWreckSwingRules.WindowTicks(run.Weight) - 1f);
        }

        private static void FreezeWreckSwing(WreckSwingRun run, float shown)
        {
            if (run.Frozen) return;
            run.Frozen = true;
            run.FrozenAt = shown;
        }

        /// <summary>Звенья-призраки едут у наружной кромки серпа (u из правил), плашмя в его плоскости, гаснут с ним.</summary>
        private void PlaceWreckSwingLinks(WreckSwingRun run, float erode)
        {
            int count = PelagWreckSwingRules.LinkCount(run.Weight);
            float length = PelagWreckSwingRules.LinkLength(run.Weight);
            for (int i = 0; i < run.Links.Length; i++)
            {
                Transform link = run.Links[i];
                Renderer renderer = run.LinkRenderers[i];
                if (link == null || renderer == null) continue;
                float fade = 0f;
                if (i < count && run.Ribbon.At(PelagWreckSwingRules.LinkU(run.Weight, i), PelagWreckSwingRules.LinkInset,
                        out Vector3 point, out Vector3 along, out Vector3 across, out float band, out float alpha))
                {
                    fade = alpha * PelagWreckSwingRules.LinkFit(band, run.Weight) * (1f - PelagWreckSwingRules.Smooth01(erode * 1.6f));
                    Vector3 normal = Vector3.Cross(along, across);
                    if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
                    Quaternion rotation = Quaternion.LookRotation(along, normal) * Quaternion.Euler(0f, 0f, PelagWreckSwingRules.LinkRoll(i));
                    link.SetPositionAndRotation(point, rotation);
                    link.localScale = Vector3.one * length;
                }
                bool show = fade > .02f && !CaptureRig.NoVfx;
                if (renderer.enabled != show) renderer.enabled = show;
                if (!show) continue;
                PelagWreckSwingLook.Row look = PelagWreckSwingLook.For(run.Form);
                _wsBlock.Clear();
                _wsBlock.SetColor(WsTintId, WsColor(look.Tint));
                _wsBlock.SetColor(WsLightId, WsColor(look.Light));
                _wsBlock.SetFloat(WsFadeId, fade);
                renderer.SetPropertyBlock(_wsBlock);
            }
        }

        /// <summary>Голова хлещет — с неё срываются холодные искры по ходу (цвет — строка формы).</summary>
        private void EmitWreckSwingGlints(WreckSwingRun run, float dt)
        {
            if (run.Glints == null || dt <= 0f) return;
            float k = PelagWreckSwingRules.SpeedAlpha(run.Velocity.magnitude);
            run.GlintCarry += dt * PelagWreckSwingImpact.For(run.Weight).GlintRate * k * k;
            int count = (int)run.GlintCarry;
            run.GlintCarry -= count;
            Color light = WsColor(PelagWreckSwingLook.Light(run.Form));
            for (int i = 0; i < count; i++)
                WiEmit(run.Glints, run.Head + Random.insideUnitSphere * .12f, run.Velocity * Random.Range(.12f, .25f) + Random.insideUnitSphere * 1.2f,
                    Color.Lerp(light, Color.white, Random.Range(.3f, .8f)), Random.Range(.03f, .06f), Random.Range(.1f, .2f));
        }
    }
}
