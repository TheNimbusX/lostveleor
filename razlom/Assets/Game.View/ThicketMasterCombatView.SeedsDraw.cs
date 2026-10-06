using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Терновник» — рисование стручка-шипа и следа (ThicketMasterCombatView.Seeds.cs): устье в кусте, место на линии, кувырок,
    /// остановка, лента и частицы-функции (номер шипа, пройденный путь, тик Sim) — пауза, хит-стоп и Часы держат кадр, повтор
    /// даёт тот же рисунок. Без аллокаций.
    /// </summary>
    public sealed partial class ThicketMasterCombatView
    {
        private void DrawSeed(SeedView v, float clock, bool frozen)
        {
            var s = v.Seed;
            if (!v.Released)
            {
                DrawSeedWindup(v, clock);
                return;
            }
            Vector2 origin = new Vector2(s.Origin.X.ToFloat(), s.Origin.Y.ToFloat());
            Vector2 dir = SeedDirection(in s);
            float tip;
            if (v.Flying)
            {
                tip = ThicketMasterSeedRules.TipDistance(s.ReleaseTick, s.Travelled.ToFloat(), s.Length.ToFloat(), clock);
                // Песочные Часы: шип стоит в воздухе вместе с боссом — не отскакивает назад.
                if (frozen) tip = Mathf.Max(tip, v.ShownTip);
            }
            else
            {
                tip = v.Gone ? v.StopDistance : ThicketMasterSeedRules.StoppedTip(s.ReleaseTick, v.StopDistance, clock);
                tip = Mathf.Max(tip, Mathf.Min(v.ShownTip, v.StopDistance));
            }
            v.ShownTip = tip;
            float after = v.Flying ? -1f : clock - (v.Gone ? v.GoneTick : v.StopTick);
            if (after > SeedLingerTicks) { HideSeed(v); return; }
            PlaceSeedPod(v, origin, dir, tip, after);
            DrawSeedRibbon(v, origin, dir, tip, after);
            DrawSeedTrail(v, origin, dir, tip, clock, after);
        }

        /// <summary>
        /// Точка стручка на distance метров пути: линия на земле + высота полёта с подскоками; у выпуска — сдвиг к устью в
        /// кусте (сходит на нет за LaunchEaseMetres) и дуга схода.
        /// </summary>
        private Vector3 SeedPoint(SeedView v, Vector2 origin, Vector2 dir, float distance)
        {
            Vector2 p = origin + dir * distance;
            var point = new Vector3(p.x, GroundY(p.x, p.y) + ThicketMasterSeedRules.Lift(distance), p.y);
            if (v.HasMouth)
                point += v.LaunchOffset * ThicketMasterSeedRules.LaunchShare(distance)
                         + Vector3.up * ThicketMasterSeedRules.PopArc(distance);
            return point;
        }

        /// <summary>Свой поворот стручка по номеру шипа: стручки куста не одинаковы, повтор — тот же.</summary>
        private static Quaternion SeedTurn(int serial)
            => Quaternion.Euler(SeedRand(serial, 0, 1) * 360f, SeedRand(serial, 0, 2) * 360f, SeedRand(serial, 0, 3) * 360f);

        /// <summary>
        /// Рост куста: стручок набухает в устье своей линии (центр куста — начало пути минус ThicketBushThornStart; устье
        /// раздвигается, пока куст выходит из земли и пухнет, дрожит вместе с ним), к выпуску — блик.
        /// </summary>
        private void DrawSeedWindup(SeedView v, float clock)
        {
            var s = v.Seed;
            int launch = s.ReleaseTick;
            float scale = ThicketMasterSeedRules.PodScale;
            float glint = 0f;
            if (v.Gone)
            {
                float after = clock - v.GoneTick;
                if (after > ThicketMasterSeedRules.GoneTicks) { HideSeed(v); return; }
                scale *= ThicketMasterSeedRules.GoneScale(after) * v.MouthScale;
            }
            else
            {
                float grow = ThicketMasterSeedRules.GrowAge(clock, launch);
                Vector2 dir = SeedDirection(in s);
                Vector2 centre = new Vector2(s.Origin.X.ToFloat(), s.Origin.Y.ToFloat()) - dir * Simulation.ThicketBushThornStart.ToFloat();
                float open = Mathf.Min(1f, ThicketMasterSeedRules.Emerge(grow)) * ThicketMasterSeedRules.Swell(grow);
                Vector2 p = centre + dir * (ThicketMasterSeedRules.MouthRadius * open);
                float quiver = ThicketMasterSeedRules.Quiver(grow);
                float phase = v.Serial * 1.7f;
                // Стручок шевелится в листве ±2 см, в дрожи перед выпуском — до ±5 см (от тика — пауза держит).
                var stir = new Vector3(.02f * Mathf.Sin(clock * .45f + phase), .02f * Mathf.Sin(clock * .6f + phase * 2f), 0f)
                           + new Vector3(Mathf.Sin(clock * 2.9f + phase), Mathf.Sin(clock * 3.7f + phase * 3f), Mathf.Sin(clock * 3.3f + phase))
                           * (.03f * quiver);
                v.Mouth = new Vector3(p.x, GroundY(p.x, p.y) + ThicketMasterSeedRules.MouthLift * open, p.y) + stir;
                v.HasMouth = true;
                glint = ThicketMasterSeedRules.Glint(clock, launch);
                v.MouthScale = ThicketMasterSeedRules.PodWindupScale(grow);
                scale *= v.MouthScale * (1f + SeedGlintSwell * glint);
            }
            if (v.Pod != null)
            {
                Quaternion turn = Quaternion.Euler(clock * 2.5f + v.Serial * 37f, clock * 1.5f, 0f) * SeedTurn(v.Serial);
                v.Pod.SetPositionAndRotation(v.Mouth, turn);
                v.Pod.localScale = Vector3.one * Mathf.Max(.001f, scale);
                SetPodShown(v, scale > .002f);
                SetSeedGlow(v, 1f + SeedGlintBoost * glint);
            }
            if (v.Ribbon != null && v.Ribbon.enabled) v.Ribbon.enabled = false;
            // В устье — только слабый ореол стручка, в блике ярче (на тёмной поляне видно, куда смотрит шип).
            int motes = 0;
            if (v.Motes != null && scale > .002f && !v.Gone)
            {
                float alpha = .08f + .3f * glint;
                SetSeedParticle(ref v.MoteBuffer[motes++], v.Mouth, .5f * scale / ThicketMasterSeedRules.PodScale + .15f * glint,
                    Color.Lerp(SeedEmber, SeedEmberDeep, .3f), alpha, 0f, SeedSeed(v.Serial, 0, 90));
            }
            if (v.Motes != null) v.Motes.SetParticles(v.MoteBuffer, motes);
            if (v.Dust != null) v.Dust.SetParticles(v.DustBuffer, 0);
            if (v.Leaves != null) v.Leaves.SetParticles(v.LeafBuffer, 0);
            if (v.Splinters != null) v.Splinters.SetParticles(v.SplinterBuffer, 0);
        }

        /// <summary>
        /// Стручок в полёте и после: место по пути, нос — по касательной (дуга схода, подскок), кувырок по пройденному.
        /// Попал — лопается (ShatterScale); конец линии — клюёт и уходит в землю (Drop); снят — сжимается (GoneScale).
        /// </summary>
        private void PlaceSeedPod(SeedView v, Vector2 origin, Vector2 dir, float tip, float after)
        {
            if (v.Pod == null) return;
            float scale = ThicketMasterSeedRules.PodScale;
            Vector3 position = SeedPoint(v, origin, dir, tip);
            Vector3 ahead = SeedPoint(v, origin, dir, tip + .06f) - position;
            var flat = new Vector3(dir.x, 0f, dir.y);
            Vector3 forward = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : flat;
            float spin = ThicketMasterSeedRules.SpinDegrees(tip);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(spin, 0f, 0f) * SeedTurn(v.Serial);
            if (after >= 0f)
            {
                if (v.Gone) scale *= ThicketMasterSeedRules.GoneScale(after);
                else if (v.Hit) scale *= ThicketMasterSeedRules.ShatterScale(after);
                else
                {
                    ThicketMasterSeedRules.Drop(after, out float pitch, out float sink, out float k);
                    position.y -= sink;
                    rotation = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f)
                               * Quaternion.Euler(spin, 0f, 0f) * SeedTurn(v.Serial);
                    scale *= k;
                }
            }
            bool shown = scale > .002f;
            SetPodShown(v, shown);
            if (!shown) return;
            v.Pod.SetPositionAndRotation(position, rotation);
            v.Pod.localScale = Vector3.one * scale;
            SetSeedGlow(v, 1f);
        }

        private static void SetPodShown(SeedView v, bool shown)
        {
            if (v.PodRenderer != null && v.PodRenderer.enabled != shown) v.PodRenderer.enabled = shown;
        }

        /// <summary>Свечение кончиков шипов (подсетка 1): SeedTipGlow × k; пишется, только когда меняется.</summary>
        private static void SetSeedGlow(SeedView v, float k)
        {
            if (v.PodRenderer == null || v.TipSlot < 0 || Mathf.Abs(k - v.Glow) < .01f) return;
            v.Glow = k;
            v.Block.SetColor(EmissionColorId, SeedTipGlow * k);
            v.PodRenderer.SetPropertyBlock(v.Block, v.TipSlot);
        }

        /// <summary>Лента следа за стручком на высоте полёта (по той же точке пути — дуга схода и подскоки): гаснет за 3 тика.</summary>
        private void DrawSeedRibbon(SeedView v, Vector2 origin, Vector2 dir, float tip, float after)
        {
            if (v.Ribbon == null) return;
            float fade = after < 0f ? 1f : Mathf.Clamp01(1f - after / 3f);
            float head = Mathf.Max(0f, tip - ThicketMasterSeedRules.PodRadius * .8f);
            float tail = Mathf.Max(0f, head - SeedRibbonLength * fade);
            bool on = head - tail > .02f && fade > 0f;
            if (v.Ribbon.enabled != on) v.Ribbon.enabled = on;
            if (!on) return;
            var color = new Color(1f, 1f, 1f, .6f * fade);
            v.Ribbon.startColor = color; v.Ribbon.endColor = color;
            int points = v.Ribbon.positionCount;
            for (int i = 0; i < points; i++)
                v.Ribbon.SetPosition(i, SeedPoint(v, origin, dir, Mathf.Lerp(tail, head, i / (float)Mathf.Max(1, points - 1))));
        }
    }
}
