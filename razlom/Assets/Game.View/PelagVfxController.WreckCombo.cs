using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Махи 1–2 Крушения и знак на задетом на СТЕКЕ СЕРИИ САБЛИ (06.10 вечер; ассеты — Editor/PelagWreckComboVfxSetup,
    /// числа и палитра — PelagWreckComboLook). Раскладка — как у волны сабли (.SabreCombo), материал — «холодное железо».
    /// Всё — от событий Sim в очереди Крушения до тика показа:
    ///  • мах (этапы 0, 1 и «Четвёртый») — горизонтальный полумесяц на высоте головы якоря вокруг героя, наружный край —
    ///    доля сектора махов Sim; мах 1 справа налево: голова полумесяца слева, нормаль вверх; мах 2 (и «Четвёртый») —
    ///    зеркало: голова справа, нормаль вниз, крупнее и ярче. 07.10: махи 1–2 рождаются с захода — в тик входа головы Sim
    ///    в сектор (.WreckComboSchedule), голова идёт от края входа к середине к тику контакта, дальше бежит к краю
    ///    (_HeadFrom/_HeadSeconds материала); без захода (нет нажатия в очереди, «Четвёртый») — в контакт, заход промотан;
    ///  • задетый махом (Damage маха, из PlayWreckSwingHit после отброса) — вспышка-звезда у груди ближе к камере,
    ///    искры по ходу маха, сколы и комья; у маха 2 крупнее. 07.10: знак — в миг, когда голова волны проходит цель.
    /// Формы — та же сборка другой краской: волна — блоком свойств (у базы блок снимается: цвета материала), искры и
    /// вспышка — цветом частиц до старта. 07.10: Призрачный якорь — палитра его якоря и без чернил (фиолетовый край).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private MaterialPropertyBlock _wcBlock, _wcGhostBlock;
        private static readonly int WcDeepId = Shader.PropertyToID("_Deep"), WcMidId = Shader.PropertyToID("_Mid"),
            WcLightId = Shader.PropertyToID("_Light"), WcHotId = Shader.PropertyToID("_Hot"), WcHotShadeId = Shader.PropertyToID("_HotShade");
        private static readonly string[] WcWaveParts = { "Wave", "Echo" };
        private static readonly string[] WcSwingBitParts = { "Sparks", "Chips" };
        private static readonly string[] WcMarkBitParts = { "Burst", "Sparks", "Chips" };
        private readonly HashSet<Renderer> _wcGhostTinted = new HashSet<Renderer>();
        private static readonly List<ParticleSystem> WcSystems = new List<ParticleSystem>(8);

        /// <summary>Префабы махов на стеке сабли собраны и в библиотеке: махи и знак рисуются ими.</summary>
        private bool WreckComboReady => _pools != null && (int)PelagVfxId.WreckComboHit < _pools.Length
            && _pools[(int)PelagVfxId.WreckComboSwing] != null && _pools[(int)PelagVfxId.WreckComboSwingHeavy] != null
            && _pools[(int)PelagVfxId.WreckComboHit] != null;

        /// <summary>
        /// Контакт маха (тик показа): волна уже идёт с захода — только отметка контакта; иначе рождается здесь с промотанным
        /// заходом (раскадровка с контакта — прежняя). False — префабов нет (тогда прежний путь).
        /// </summary>
        private bool PlayWreckComboSwing(in WkPending p)
        {
            if (!WreckComboReady || !PelagWreckSwingRules.Draws(p.Stage)) return false;
            if (CaptureRig.NoVfx) return true;
            if (ConfirmWreckComboWave(p.Serial, p.Stage)) return true;
            Vector3 forward = p.Wave.Dir;
            forward.y = 0f;
            forward = forward.sqrMagnitude > .01f ? forward.normalized : PlayerFacing();
            SpawnWreckComboSwing(p.Stage, p.Serial, p.Tick, forward, p.At, PelagWreckComboLook.LeadSeconds, true);
            return true;
        }

        /// <summary>
        /// Полумесяц маха stage вокруг <paramref name="at"/> по направлению удара. <paramref name="skip"/> — сколько секунд
        /// от рождения частицы уже прошло (заход начат раньше кадра или промотан целиком).
        /// </summary>
        private bool SpawnWreckComboSwing(int stage, int serial, int contact, Vector3 forward, Vector3 at, float skip, bool confirmed)
        {
            int side = Simulation.WreckSwingSide(stage) < 0 || stage == PelagWreckSwingRules.StageFourth ? 1 : 0;
            PelagVfxId id = stage == PelagWreckSwingRules.StageSwing ? PelagVfxId.WreckComboSwing : PelagVfxId.WreckComboSwingHeavy;
            if (!TryAcquire(id, out GameObject go, out PelagVfxElement element)) return false;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // Голова — по ходу якоря: мах 1 уходит влево, мах 2 — вправо (тот же поворот на 180° вокруг оси удара).
            Vector3 headSide = side == 1 ? right : -right;
            Vector3 normal = side == 1 ? Vector3.down : Vector3.up;
            Quaternion roll = Quaternion.AngleAxis(PelagWreckComboLook.Roll[side], forward);
            Quaternion rotation = Quaternion.LookRotation(roll * normal, roll * headSide);
            Vector3 center = at + Vector3.up * PelagWreckComboLook.Height[side] - forward * PelagWreckComboLook.Back;
            float radius = PelagWreckComboLook.Radius(side, WreckSwingRadius());
            TintWreckCombo(go.transform, _wsLastForm);
            int index = ReserveActive();
            element.Begin(center, rotation);
            go.transform.localScale = Vector3.one * radius;
            if (skip > 1e-4f)
            {
                go.GetComponentsInChildren(true, WcSystems);
                foreach (ParticleSystem system in WcSystems) { system.Simulate(skip, false, false, false); system.Play(false); }
            }
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = center, End = center, Motion = Motion.Static, FollowIndex = -1
            };
            float shown = NoteWreckComboWave(index, go, serial, stage, contact, side, center, forward, headSide, skip, confirmed);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-combo-swing] stage={stage} tick={contact} side={side} center={center.ToString("F2")} r={radius:F2} forward={forward.ToString("F2")} form={_wsLastForm} shown={shown:F2} skip={skip:F3} lead={(confirmed ? "contact" : "early")}");
            return true;
        }

        /// <summary>
        /// Знак на задетом махом (из PlayWreckSwingHit после отброса): вспышка у груди ближе к камере, искры по ходу маха,
        /// сколы и комья. False — префабов нет (тогда прежний знак).
        /// </summary>
        private bool PlayWreckComboMark(Vector3 body, Vector3 dir, float radius, WreckSwingWeight weight)
        {
            if (!WreckComboReady) return false;
            if (CaptureRig.NoVfx) return true;
            int heavy = weight == WreckSwingWeight.Heavy ? 1 : 0;
            float groundY = WreckGroundAt(body);
            var foot = new Vector3(body.x, groundY + .03f, body.z);
            Vector3 position = foot + Vector3.up * Mathf.Clamp(.5f + .45f * radius, .75f, 1.3f);
            Camera camera = Camera.main;
            // Перед поверхностью тела, иначе вспышка тонет в модели.
            if (camera != null) position += (camera.transform.position - position).normalized * .45f;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : PlayerFacing();
            if (!TryAcquire(PelagVfxId.WreckComboHit, out GameObject go, out PelagVfxElement element)) return true;
            bool ghost = _wsLastForm == PelagForm.WreckGhostAnchor;
            TintWreckComboHit(go.transform, _wsLastForm, true);
            if (!ghost) TintWreckComboGhostBits(go.transform, false, WcMarkBitParts);   // своя краска снимается до чужих
            TintWreckNinthBits(go.transform, _wsLastForm, true);   // .WreckNinth: знак маха в индиго
            TintWreckBreakMark(go.transform, _wsLastForm);   // .WreckBreakwaterBits: белое звезды — мятой и только в сердцевине
            if (ghost) TintWreckComboGhostBits(go.transform, true, WcMarkBitParts);
            int index = ReserveActive();
            element.Begin(position, Quaternion.LookRotation(dir, Vector3.up));
            go.transform.localScale = Vector3.one * PelagWreckComboLook.HitScale[heavy];
            _active[index] = new ActiveFx
            {
                Active = true, Id = PelagVfxId.WreckComboHit, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = position, End = position, Motion = Motion.Static, FollowIndex = -1
            };
            return true;
        }

        private static Color WcColor(float r, float g, float b) => new Color(r, g, b, 1f);

        private static Color WcHex(int hex, float a = 1f)
        {
            PelagWreckSwingLook.Rgb(hex, out float r, out float g, out float b);
            return new Color(r, g, b, a);
        }

        private static Color WcLinear(float r, float g, float b, float gain)
            => new Color(Mathf.GammaToLinearSpace(r) * gain, Mathf.GammaToLinearSpace(g) * gain, Mathf.GammaToLinearSpace(b) * gain, 1f);

        /// <summary>Краска формы на волну и эхо (у базы блок снят — цвета материала), на искры и сколы.</summary>
        private void TintWreckCombo(Transform root, PelagForm form)
        {
            bool tinted = form != PelagForm.None;
            bool ghost = form == PelagForm.WreckGhostAnchor;
            PelagWreckComboLook.Palette c = PelagWreckComboLook.ForSwing(form);
            if (_wcBlock == null) _wcBlock = new MaterialPropertyBlock();
            foreach (string name in WcWaveParts)
            {
                Renderer renderer = root.Find(name)?.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (!tinted) { renderer.SetPropertyBlock(null); continue; }
                _wcBlock.Clear();
                _wcBlock.SetColor(WcDeepId, WcColor(c.DeepR, c.DeepG, c.DeepB));
                _wcBlock.SetColor(WcMidId, WcColor(c.MidR, c.MidG, c.MidB));
                _wcBlock.SetColor(WcLightId, WcColor(c.LightR, c.LightG, c.LightB));
                _wcBlock.SetColor(WcHotId, WcColor(c.HotR, c.HotG, c.HotB));
                if (form == PelagForm.WreckBreakwater) _wcBlock.SetColor(WgOutlineId, WbContour);   // .WreckBreakwaterBits: контур — тёмная зелень, не чёрный
                if (ghost)
                {
                    // 07.10: как у его якоря — без чернил: край светится фиолетовым, второй тон кромки сиреневый, не голубой.
                    _wcBlock.SetColor(WgOutlineId, WcHex(PelagWreckComboLook.GhostOutlineHex, PelagWreckComboLook.GhostOutlineAlpha));
                    _wcBlock.SetColor(WcHotShadeId, WcHex(PelagWreckComboLook.GhostHotShadeHex));
                }
                renderer.SetPropertyBlock(_wcBlock);
            }
            TintWreckComboSparks(root.Find("Sparks")?.GetComponent<ParticleSystem>(), c);
            TintWreckComboChips(root, ghost);
            TintWreckComboGhostBits(root, ghost, WcSwingBitParts);
        }

        /// <summary>Грань сколов: сталь (база и прочие формы), у Призрачного якоря — фиолетовая сталь.</summary>
        private static void TintWreckComboChips(Transform root, bool ghost)
        {
            ParticleSystem chips = root.Find("Chips")?.GetComponent<ParticleSystem>();
            if (chips == null) return;
            var main = chips.main;
            float[] dark = ghost ? PelagWreckComboLook.GhostChipDark : PelagWreckComboLook.ChipFaceDark;
            float[] light = ghost ? PelagWreckComboLook.GhostChipLight : PelagWreckComboLook.ChipFaceLight;
            main.startColor = new ParticleSystem.MinMaxGradient(WcColor(dark[0], dark[1], dark[2]), WcColor(light[0], light[1], light[2]));
        }

        /// <summary>
        /// Краска формы на вспышку и искры знака. <paramref name="swing"/> — знак задетого махом: палитра махов, светлые
        /// сколы и комья края (07.10); иначе (выпад) — принятый знак: палитра выпада, прежние сколы и комья, без краски
        /// призрака махов (объекты пула общие).
        /// </summary>
        private void TintWreckComboHit(Transform root, PelagForm form, bool swing)
        {
            if (!swing) TintWreckComboGhostBits(root, false, WcMarkBitParts);
            WreckComboHitDebris(root, swing, form == PelagForm.WreckGhostAnchor);
            PelagWreckComboLook.Palette c = swing ? PelagWreckComboLook.ForSwing(form) : PelagWreckComboLook.For(form);
            ParticleSystem burst = root.Find("Burst")?.GetComponent<ParticleSystem>();
            if (burst != null)
            {
                // Выпад ставит звезде своё ядро блоком (.WreckComboLungeBits) — у знака маха блок снят, ядро материала.
                burst.GetComponent<Renderer>()?.SetPropertyBlock(null);
                var main = burst.main;
                // Цвет частиц — вершинный, шейдер его не переводит: основной тон формы — в линейное пространство.
                main.startColor = WcLinear(Mathf.Lerp(c.MidR, c.LightR, .6f), Mathf.Lerp(c.MidG, c.LightG, .6f), Mathf.Lerp(c.MidB, c.LightB, .6f), 1.05f);
            }
            TintWreckComboSparks(root.Find("Sparks")?.GetComponent<ParticleSystem>(), c);
        }

        private static Material _wcMarkChip, _wcSwingChip;

        /// <summary>
        /// Сколы и комья знака: у маха — светлый скол края (материал, число, размер, грань; у призрака — фиолетовая сталь),
        /// у выпада — принятые (префаб). Ставится каждый раз: объект пула мог прийти от другого пути.
        /// </summary>
        private static void WreckComboHitDebris(Transform root, bool swing, bool ghost)
        {
            ParticleSystem chips = root.Find("Chips")?.GetComponent<ParticleSystem>();
            if (chips != null)
            {
                Renderer r = chips.GetComponent<Renderer>();
                if (_wcMarkChip == null) _wcMarkChip = Resources.Load<Material>("VFX/Pelag/Materials/M_WreckCombo_Chip");
                if (_wcSwingChip == null) _wcSwingChip = Resources.Load<Material>("VFX/Pelag/Materials/M_WreckCombo_SwingChip");
                Material m = swing ? _wcSwingChip : _wcMarkChip;
                if (r != null && m != null && r.sharedMaterial != m) r.sharedMaterial = m;
                if (swing) TintWreckComboChips(root, ghost);
                else WcDebris(chips, PelagWreckComboLook.MarkChipDark, PelagWreckComboLook.MarkChipLight);
                WcDebris(chips, swing ? PelagWreckComboLook.HitChipCount : PelagWreckComboLook.MarkChipCount,
                    swing ? PelagWreckComboLook.HitChipSizeMin : PelagWreckComboLook.MarkChipSizeMin,
                    swing ? PelagWreckComboLook.HitChipSizeMax : PelagWreckComboLook.MarkChipSizeMax);
            }
            ParticleSystem clods = root.Find("Clods")?.GetComponent<ParticleSystem>();
            if (clods != null)
            {
                WcDebris(clods, swing ? PelagWreckComboLook.HitClodDark : PelagWreckComboLook.MarkClodDark,
                    swing ? PelagWreckComboLook.HitClodLight : PelagWreckComboLook.MarkClodLight);
                WcDebris(clods, swing ? PelagWreckComboLook.HitClodCount : PelagWreckComboLook.MarkClodCount,
                    swing ? PelagWreckComboLook.HitClodSizeMin : PelagWreckComboLook.MarkClodSizeMin,
                    swing ? PelagWreckComboLook.HitClodSizeMax : PelagWreckComboLook.MarkClodSizeMax);
            }
        }

        private static void WcDebris(ParticleSystem p, float[] dark, float[] light)
        {
            var main = p.main;
            main.startColor = new ParticleSystem.MinMaxGradient(WcColor(dark[0], dark[1], dark[2]), WcColor(light[0], light[1], light[2]));
        }

        private static void WcDebris(ParticleSystem p, int count, float sizeMin, float sizeMax)
        {
            var main = p.main;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            var emission = p.emission;
            if (emission.burstCount < 1) return;
            ParticleSystem.Burst burst = emission.GetBurst(0);
            burst.count = count;
            emission.SetBurst(0, burst);
        }

        private static void TintWreckComboSparks(ParticleSystem sparks, in PelagWreckComboLook.Palette c)
        {
            if (sparks == null) return;
            var main = sparks.main;
            main.startColor = new ParticleSystem.MinMaxGradient(WcColor(c.HotR, c.HotG, c.HotB), WcLinear(c.LightR, c.LightG, c.LightB, 1.25f));
        }

        /// <summary>
        /// Призрачный якорь (07.10): у искр, сколов и вспышки маха обвод — фиолетовый край свечения вместо чернил, тень у края —
        /// сиреневая вместо голубой материала. Не призрак — снимается только своя краска (объекты пула общие).
        /// </summary>
        private void TintWreckComboGhostBits(Transform root, bool ghost, string[] parts)
        {
            if (root == null) return;
            if (_wcGhostBlock == null) _wcGhostBlock = new MaterialPropertyBlock();
            foreach (string name in parts)
            {
                Renderer r = root.Find(name)?.GetComponent<Renderer>();
                Material m = r != null ? r.sharedMaterial : null;
                if (m == null || !m.HasProperty(WgOutlineId) || !m.HasProperty(WgShadeId)) continue;
                if (!ghost)
                {
                    if (_wcGhostTinted.Remove(r)) r.SetPropertyBlock(null);
                    continue;
                }
                r.GetPropertyBlock(_wcGhostBlock);
                _wcGhostBlock.SetColor(WgOutlineId, WcHex(PelagWreckComboLook.GhostOutlineHex, PelagWreckComboLook.GhostOutlineAlpha));
                _wcGhostBlock.SetColor(WgShadeId, WcHex(PelagWreckComboLook.GhostBitShadeHex));
                r.SetPropertyBlock(_wcGhostBlock);
                _wcGhostTinted.Add(r);
            }
        }
    }
}
