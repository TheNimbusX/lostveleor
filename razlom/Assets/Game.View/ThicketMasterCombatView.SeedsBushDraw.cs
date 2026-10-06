using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// «Терновник» — рисование куста (ThicketMasterCombatView.SeedsBush.cs): поза частей по срокам Sim и часам босса
    /// (ThicketMasterSeedRules), сухой тон блоком свойств, частицы-функции (номер куста, тик) — пауза, хит-стоп и Часы
    /// держат кадр, повтор даёт тот же рисунок. Без аллокаций.
    /// </summary>
    public sealed partial class ThicketMasterCombatView
    {
        /// <summary>Верх листвы куста над землёй при полном росте, м (падуб ×1,2 — ~1,05 м).</summary>
        private const float BushCanopy = 1.05f;

        private static readonly Color BushCrackTone = new Color(.21f, .14f, .08f, .95f);
        private static readonly Color BushGlintTone = new Color(1f, .82f, .32f);

        private void DrawBush(BushView v, float clock)
        {
            // Поза — от срока, который Sim ещё сдвигает (Часы); пропавший куст держит позу кадра пропажи и вянет сам.
            float grow, launchAge, wither, growNow;
            if (!v.Lost)
            {
                grow = ThicketMasterSeedRules.GrowAge(clock, v.LaunchTick);
                launchAge = ThicketMasterSeedRules.LaunchAge(clock, v.WitherTick);
                wither = ThicketMasterSeedRules.WitherAge(clock, v.GoneTick);
                growNow = grow;
            }
            else
            {
                grow = ThicketMasterSeedRules.GrowAge(v.LostClock, v.LaunchTick);
                launchAge = ThicketMasterSeedRules.LaunchAge(v.LostClock, v.WitherTick);
                wither = Mathf.Max(0f, ThicketMasterSeedRules.WitherAge(v.LostClock, v.GoneTick))
                         + ThicketMasterSeedRules.LostWitherAge(clock - v.LostClock);
                growNow = grow + (clock - v.LostClock);
            }
            if (wither >= ThicketMasterSeedRules.WitherTicks + BushLingerTicks) { HideBush(v); return; }
            bool sprouted = grow >= 0f && (v.Sprouted || v.Lost);
            bool launched = grow >= ThicketMasterSeedRules.WindupTicks;
            float emerge = sprouted ? ThicketMasterSeedRules.Emerge(grow) : 0f;
            float swell = ThicketMasterSeedRules.Swell(grow);
            float quiver = v.Lost ? 0f : ThicketMasterSeedRules.Quiver(grow);
            float glint = v.Lost || launched ? 0f : ThicketMasterSeedRules.Glint(clock, v.LaunchTick);
            float recoil = v.Lost ? 0f : ThicketMasterSeedRules.Recoil(launchAge);
            float extension = launched ? ThicketMasterSeedRules.LaneSnap(launchAge) : ThicketMasterSeedRules.Bristle(grow);
            ThicketMasterSeedRules.Wither(wither, out float dry, out float droop, out float sink, out float shrink);
            bool body = sprouted && !ThicketMasterSeedRules.Withered(wither) && emerge > .002f;
            SetBushBody(v, body);
            // Выходит из земли снизу вверх (и уходит в неё, увядая): корень ниже пола на подъём и осадку.
            float pop = (1f - Mathf.Min(1f, emerge)) * .25f;
            v.Root.SetPositionAndRotation(v.Centre + Vector3.down * (sink * ThicketMasterSeedRules.SinkDepth + pop), Quaternion.Euler(0f, v.Yaw, 0f));
            if (body) PoseBushBody(v, grow, emerge, swell, quiver, recoil, extension, droop, shrink, clock);
            TintBush(v, dry);
            DrawBushGround(v, clock, wither, sprouted, emerge);
            DrawBushLeaves(v, growNow, wither, sprouted, sink, shrink, dry);
            DrawBushSparks(v, growNow, glint, body && !v.Lost);
            DrawBushDust(v, clock, wither, sprouted);
        }

        /// <summary>
        /// Части куста: листва пухнет и отдаёт на выпуске, шипы линий встают дыбом и опадают, стебли качаются и выходят
        /// вразнобой; всё дрожит перед выпуском, никнет и съёживается, увядая.
        /// </summary>
        private static void PoseBushBody(BushView v, float grow, float emerge, float swell, float quiver, float recoil, float extension,
            float droop, float shrink, float clock)
        {
            float phase = v.Serial * 1.3f;
            float open = Mathf.Min(1f, emerge);
            if (v.Core != null)
            {
                float size = emerge * swell * shrink * (1f + .015f * Mathf.Sin(clock * .35f + phase));
                // Отдача: листва приседает и раздаётся вширь; увядая — оседает.
                float up = (1f - .16f * recoil) * (1f - .25f * droop), wide = 1f + .1f * recoil + .06f * droop;
                // Вертикаль листвы — та ось сетки, что после поворота смотрит вверх (падуб — Z-вверх, повёрнут на −90° по X).
                var scale = v.CoreScale * size;
                scale.x *= v.CoreUp == 0 ? up : wide;
                scale.y *= v.CoreUp == 1 ? up : wide;
                scale.z *= v.CoreUp == 2 ? up : wide;
                v.Core.localScale = scale;
                v.Core.localPosition = v.CorePos * open;
                float jx = quiver * 4f * Mathf.Sin(clock * 3.1f + phase), jz = quiver * 4f * Mathf.Sin(clock * 2.7f + phase * 2f);
                v.Core.localRotation = Quaternion.Euler(jx + .8f * Mathf.Sin(clock * .3f + phase), 0f, jz) * v.CoreRot;
            }
            for (int k = 0; k < v.Lanes.Length; k++)
            {
                var t = v.Lanes[k];
                if (t == null) continue;
                float e = Mathf.Min(1.08f, emerge) * shrink;
                var s = v.LaneScale[k];
                t.localScale = new Vector3(s.x * e, s.y * e * extension, s.z * e);
                t.localPosition = v.LanePos[k] * (swell * shrink * open);
                float jitter = quiver * 7f * Mathf.Sin(clock * 3.3f + k * 1.9f + phase);
                t.localRotation = Quaternion.AngleAxis(droop * ThicketMasterSeedRules.DroopDegrees + jitter, v.LaneDroop[k]) * v.LaneRot[k];
            }
            int canes = v.Canes.Length;
            for (int i = 0; i < canes; i++)
            {
                var t = v.Canes[i];
                if (t == null) continue;
                float e = ThicketMasterSeedRules.CaneEmerge(grow, i, canes) * shrink;
                t.localScale = v.CaneScale[i] * Mathf.Max(.001f, e);
                t.localPosition = v.CanePos[i] * (swell * shrink);
                float sway = 2.5f * Mathf.Sin(clock * .5f + i * 1.3f + phase) + quiver * 6f * Mathf.Sin(clock * 2.9f + i * 2.1f);
                t.localRotation = Quaternion.AngleAxis(droop * ThicketMasterSeedRules.DroopDegrees * 1.15f + sway, v.CaneDroop[i]) * v.CaneRot[i];
            }
        }

        /// <summary>Сухой тон (листва бурая, кора серая) блоком свойств — пишется, только когда меняется.</summary>
        private static void TintBush(BushView v, float dry)
        {
            if (Mathf.Abs(dry - v.Dry) < .01f) return;
            v.Dry = dry;
            if (v.CoreRenderer != null)
            {
                v.LeafBlock.SetColor(BaseColorId, v.CoreBase * Color.Lerp(Color.white, BushDryLeaf, dry));
                v.CoreRenderer.SetPropertyBlock(v.LeafBlock);
            }
            v.WoodBlock.SetColor(BaseColorId, v.WoodBase * Color.Lerp(Color.white, BushDryWood, dry));
            for (int k = 0; k < v.Wood.Length; k++)
                if (v.Wood[k] != null) v.Wood[k].SetPropertyBlock(v.WoodBlock);
        }

        /// <summary>Пятно разрытой земли и трещины у основания: раскрываются с прорастанием (до него — чуть, земля шевелится), тают к уходу.</summary>
        private void DrawBushGround(BushView v, float clock, float wither, bool sprouted, float emerge)
        {
            float open = sprouted ? Mathf.Clamp01(emerge * 1.1f)
                : v.Lost || v.Order == 0 ? 0f : .35f * ThicketMasterSeedRules.Stir(clock, v.SproutTick);
            if (v.Lost && !sprouted) open = 0f;
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((wither - 10f) / 20f));
            int serial = v.Serial + 70000;
            var at = v.Centre + Vector3.up * .02f;
            int n = 0;
            if (v.Ground != null)
            {
                if (open > .002f && fade > .002f)
                    SetSeedParticle(ref v.GroundBuffer[n++], at, BushPatchSize * (.45f + .55f * open), Color.white, .85f * Mathf.Min(1f, open * 2f) * fade,
                        SeedRand(serial, 0, 201) * 360f, SeedSeed(serial, 0, 202));
                v.Ground.SetParticles(v.GroundBuffer, n);
            }
            n = 0;
            if (v.Cracks != null)
            {
                if (open > .002f && fade > .002f)
                    SetSeedParticle(ref v.CrackBuffer[n++], at + Vector3.up * .005f, BushCrackSize * (.3f + .7f * open), BushCrackTone,
                        BushCrackTone.a * Mathf.Min(1f, open * 1.5f) * fade, SeedRand(serial, 0, 203) * 360f, SeedSeed(serial, 0, 204));
                v.Cracks.SetParticles(v.CrackBuffer, n);
            }
        }

        /// <summary>
        /// Листья: в дрожи перед выпуском куст стряхивает 5 листьев, увядая — роняет 12 сухих (буреют к концу); падают,
        /// кружась, ложатся и тают. Время — от роста и увядания (пропавший куст вянет быстрее — и листья с ним).
        /// </summary>
        private void DrawBushLeaves(BushView v, float growNow, float wither, bool sprouted, float sink, float shrink, float dry)
        {
            if (v.Leaves == null) return;
            int serial = v.Serial + 70000, n = 0;
            float canopy = BushCanopy * shrink;
            if (sprouted)
                for (int j = 0; j < 5 && n < v.LeafBuffer.Length; j++)
                {
                    float age = growNow - (12f + 3.3f * j);
                    if (age >= 0f && age < 28f) BushLeaf(v, ref n, serial, j, age, 28f, canopy, 0f, 0f);
                }
            for (int j = 0; j < 12 && n < v.LeafBuffer.Length; j++)
            {
                float age = wither - 1.6f * j;
                if (!sprouted || age < 0f || age >= 30f) continue;
                // Сухой лист падает с осевшей листвы; тон — по сухости в миг, когда он сорвался.
                float sinkAt = sink * ThicketMasterSeedRules.SinkDepth;
                BushLeaf(v, ref n, serial, 100 + j, age, 30f, canopy - sinkAt * .5f, Mathf.Clamp01(dry + .3f), 1f);
            }
            v.Leaves.SetParticles(v.LeafBuffer, n);
        }

        /// <summary>Лист index: срывается с листвы (место по номеру), летит наружу, кружит, ложится; последние 30 % жизни тает.</summary>
        private void BushLeaf(BushView v, ref int n, int serial, int index, float age, float life, float canopy, float dry, float drop)
        {
            float a = SeedRand(serial, index, 211) * Mathf.PI * 2f, r = .15f + .35f * SeedRand(serial, index, 212);
            var outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            var at = v.Centre + outward * r + Vector3.up * Mathf.Max(.1f, canopy * (.55f + .4f * SeedRand(serial, index, 213)));
            var velocity = outward * (.6f + 1.1f * SeedRand(serial, index, 214)) + Vector3.up * ((1f - drop) * (.8f + .8f * SeedRand(serial, index, 215)));
            float t = age / Simulation.TicksPerSecond;
            float drag = (1f - Mathf.Exp(-2f * t)) * .5f;
            var side = new Vector3(-outward.z, 0f, outward.x);
            var p = at + new Vector3(velocity.x, 0f, velocity.z) * drag + side * (.12f * Mathf.Sin(t * 5f + index))
                    + Vector3.up * (velocity.y * t - .5f * SeedLeafGravity * t * t);
            float floor = GroundY(p.x, p.z) + .03f;
            bool landed = p.y <= floor;
            if (landed) p.y = floor;
            float tumble = landed ? Mathf.Min(t, .6f) : t;
            var color = Color.Lerp(Color.Lerp(SeedLeafLight, SeedLeafDark, SeedRand(serial, index, 216)), BushDryLeaf, dry);
            SetSeedParticle(ref v.LeafBuffer[n], p, .15f + .08f * SeedRand(serial, index, 217), color, SeedFade(age / life), 0f,
                SeedSeed(serial, index, 218));
            v.LeafBuffer[n++].rotation3D = new Vector3(SeedRand(serial, index, 219) * 360f + 300f * tumble,
                SeedRand(serial, index, 220) * 360f + 120f * tumble, SeedRand(serial, index, 221) * 360f + 260f * tumble);
        }

        /// <summary>
        /// Кончики шипов линий: последние 10 тиков роста с них всплывают угольки (по 3 на шип), за GlintTicks до выпуска —
        /// звёздочка блика на каждом кончике (шип вот-вот сорвётся).
        /// </summary>
        private void DrawBushSparks(BushView v, float growNow, float glint, bool live)
        {
            int serial = v.Serial + 70000, motes = 0, stars = 0;
            float windup = ThicketMasterSeedRules.WindupTicks;
            for (int k = 0; k < v.Lanes.Length && live; k++)
            {
                var lane = v.Lanes[k];
                if (lane == null) continue;
                Vector3 tip = lane.TransformPoint(Vector3.up);
                if (v.Motes != null)
                    for (int j = 0; j < 3 && motes < v.MoteBuffer.Length; j++)
                    {
                        float age = growNow - (windup - 10f + 3.3f * j + .7f * k);
                        const float life = 10f;
                        if (age < 0f || age >= life) continue;
                        float u = age / life;
                        int index = k * 3 + j;
                        var at = tip + Vector3.up * (.05f + .4f * u) + new Vector3(SeedRand(serial, index, 231) - .5f, 0f, SeedRand(serial, index, 232) - .5f) * .16f;
                        SetSeedParticle(ref v.MoteBuffer[motes++], at, (.06f + .04f * SeedRand(serial, index, 233)) * (1f - .4f * u),
                            Color.Lerp(SeedEmber, SeedEmberDeep, SeedRand(serial, index, 234)), .8f * (1f - u), 0f, SeedSeed(serial, index, 235));
                    }
                if (v.Glint != null && glint > .01f && stars < v.GlintBuffer.Length)
                    SetSeedParticle(ref v.GlintBuffer[stars++], tip, .55f * glint * (.9f + .2f * SeedRand(serial, k, 241)), BushGlintTone, glint,
                        SeedRand(serial, k, 242) * 360f + 40f * glint, SeedSeed(serial, k, 243));
            }
            if (v.Motes != null) v.Motes.SetParticles(v.MoteBuffer, motes);
            if (v.Glint != null) v.Glint.SetParticles(v.GlintBuffer, stars);
        }

        /// <summary>Пыль: до прорастания земля на месте куста шевелится (кусты 1–2), уходя в землю — куст поднимает низкую пыль.</summary>
        private void DrawBushDust(BushView v, float clock, float wither, bool sprouted)
        {
            if (v.Dust == null) return;
            int serial = v.Serial + 70000, n = 0;
            if (!v.Lost && v.Order > 0)
            {
                float stirAge = clock - (v.SproutTick - ThicketMasterSeedRules.StirTicks);
                for (int j = 0; j < 4 && n < v.DustBuffer.Length; j++)
                {
                    float age = stirAge - 2.2f * j;
                    const float life = 12f;
                    if (age < 0f || age >= life) continue;
                    BushPuff(v, ref n, serial, j, age / life, .25f, .6f, .35f, .6f, .22f);
                }
            }
            if (sprouted)
                for (int j = 0; j < 5 && n < v.DustBuffer.Length; j++)
                {
                    float age = wither - (7f + 2.5f * j);
                    const float life = 14f;
                    if (age < 0f || age >= life) continue;
                    BushPuff(v, ref n, serial, 20 + j, age / life, .45f, .7f, .4f, .7f, .2f);
                }
            v.Dust.SetParticles(v.DustBuffer, n);
        }

        /// <summary>Клуб пыли у основания: место по номеру на кольце inner…outer, встаёт, расходится наружу и тает (u — доля жизни).</summary>
        private void BushPuff(BushView v, ref int n, int serial, int index, float u, float inner, float outer, float sizeMin, float sizeMax, float alpha)
        {
            float a = SeedRand(serial, index, 251) * Mathf.PI * 2f, r = Mathf.Lerp(inner, outer, SeedRand(serial, index, 252));
            var outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            var ground = v.Centre + outward * r;
            ground.y = GroundY(ground.x, ground.z);
            var at = ground + outward * (.3f * u) + Vector3.up * (.12f + .22f * u);
            float size = Mathf.Lerp(sizeMin, sizeMax, Mathf.Sqrt(u)) * (.85f + .3f * SeedRand(serial, index, 253));
            SetSeedParticle(ref v.DustBuffer[n++], at, size, Color.Lerp(SeedDustLight, SeedDustDark, SeedRand(serial, index, 254)),
                alpha * (1f - u) * (1f - u) * Mathf.Clamp01(u * 6f), SeedRand(serial, index, 255) * 360f, SeedSeed(serial, index, 256));
        }
    }
}
