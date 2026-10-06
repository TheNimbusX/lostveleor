using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Махи 1–2 Крушения и знак на задетом на СТЕКЕ СЕРИИ САБЛИ (06.10 вечер; ассеты — Editor/PelagWreckComboVfxSetup,
    /// числа и палитра — PelagWreckComboLook). Раскладка — как у волны сабли (.SabreCombo), материал — «холодное железо».
    /// Всё — от событий Sim в очереди Крушения до тика показа:
    ///  • контакт маха (WreckStage этапа 0, 1 и «Четвёртого») — горизонтальный полумесяц на высоте головы якоря вокруг
    ///    героя, наружный край — доля сектора махов Sim; мах 1 справа налево: голова полумесяца слева, нормаль вверх;
    ///    мах 2 (и «Четвёртый») — зеркало: голова справа, нормаль вниз, крупнее и ярче. Голова раскрывается за якорем:
    ///    в тик контакта якорь посередине сектора, дальше бежит к краю (_HeadFrom/_HeadSeconds материала);
    ///  • задетый махом (Damage маха, из PlayWreckSwingHit после отброса) — вспышка-звезда у груди ближе к камере,
    ///    искры по ходу маха, сколы и комья; у маха 2 крупнее.
    /// Формы — та же сборка другой краской: волна — блоком свойств (у базы блок снимается: цвета материала), искры и
    /// вспышка — цветом частиц до старта.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private MaterialPropertyBlock _wcBlock;
        private static readonly int WcDeepId = Shader.PropertyToID("_Deep"), WcMidId = Shader.PropertyToID("_Mid"),
            WcLightId = Shader.PropertyToID("_Light"), WcHotId = Shader.PropertyToID("_Hot");

        /// <summary>Префабы махов на стеке сабли собраны и в библиотеке: махи и знак рисуются ими.</summary>
        private bool WreckComboReady => _pools != null && (int)PelagVfxId.WreckComboHit < _pools.Length
            && _pools[(int)PelagVfxId.WreckComboSwing] != null && _pools[(int)PelagVfxId.WreckComboSwingHeavy] != null
            && _pools[(int)PelagVfxId.WreckComboHit] != null;

        /// <summary>Контакт маха (тик показа): полумесяц по сектору этапа. False — префабов нет (тогда прежний путь).</summary>
        private bool PlayWreckComboSwing(in WkPending p)
        {
            if (!WreckComboReady || !PelagWreckSwingRules.Draws(p.Stage)) return false;
            if (CaptureRig.NoVfx) return true;
            int side = Simulation.WreckSwingSide(p.Stage) < 0 || p.Stage == PelagWreckSwingRules.StageFourth ? 1 : 0;
            PelagVfxId id = p.Stage == PelagWreckSwingRules.StageSwing ? PelagVfxId.WreckComboSwing : PelagVfxId.WreckComboSwingHeavy;
            if (!TryAcquire(id, out GameObject go, out PelagVfxElement element)) return true;
            Vector3 forward = p.Wave.Dir;
            forward.y = 0f;
            forward = forward.sqrMagnitude > .01f ? forward.normalized : PlayerFacing();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // Голова — по ходу якоря: мах 1 уходит влево, мах 2 — вправо (тот же поворот на 180° вокруг оси удара).
            Vector3 headSide = side == 1 ? right : -right;
            Vector3 normal = side == 1 ? Vector3.down : Vector3.up;
            Quaternion roll = Quaternion.AngleAxis(PelagWreckComboLook.Roll[side], forward);
            Quaternion rotation = Quaternion.LookRotation(roll * normal, roll * headSide);
            Vector3 center = p.At + Vector3.up * PelagWreckComboLook.Height[side] - forward * PelagWreckComboLook.Back;
            float radius = PelagWreckComboLook.Radius(side, WreckSwingRadius());
            TintWreckCombo(go.transform, _wsLastForm);
            int index = ReserveActive();
            element.Begin(center, rotation);
            go.transform.localScale = Vector3.one * radius;
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = center, End = center, Motion = Motion.Static, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-combo-swing] stage={p.Stage} tick={p.Tick} side={side} center={center.ToString("F2")} r={radius:F2} forward={forward.ToString("F2")} form={_wsLastForm}");
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
            TintWreckComboHit(go.transform, _wsLastForm);
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

        private static Color WcLinear(float r, float g, float b, float gain)
            => new Color(Mathf.GammaToLinearSpace(r) * gain, Mathf.GammaToLinearSpace(g) * gain, Mathf.GammaToLinearSpace(b) * gain, 1f);

        /// <summary>Краска формы на волну и эхо (у базы блок снят — цвета материала) и на искры.</summary>
        private void TintWreckCombo(Transform root, PelagForm form)
        {
            bool tinted = form != PelagForm.None;
            PelagWreckComboLook.Palette c = PelagWreckComboLook.For(form);
            if (_wcBlock == null) _wcBlock = new MaterialPropertyBlock();
            foreach (string name in new[] { "Wave", "Echo" })
            {
                Renderer renderer = root.Find(name)?.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (!tinted) { renderer.SetPropertyBlock(null); continue; }
                _wcBlock.Clear();
                _wcBlock.SetColor(WcDeepId, WcColor(c.DeepR, c.DeepG, c.DeepB));
                _wcBlock.SetColor(WcMidId, WcColor(c.MidR, c.MidG, c.MidB));
                _wcBlock.SetColor(WcLightId, WcColor(c.LightR, c.LightG, c.LightB));
                _wcBlock.SetColor(WcHotId, WcColor(c.HotR, c.HotG, c.HotB));
                renderer.SetPropertyBlock(_wcBlock);
            }
            TintWreckComboSparks(root.Find("Sparks")?.GetComponent<ParticleSystem>(), c);
        }

        /// <summary>Краска формы на вспышку и искры знака.</summary>
        private static void TintWreckComboHit(Transform root, PelagForm form)
        {
            PelagWreckComboLook.Palette c = PelagWreckComboLook.For(form);
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

        private static void TintWreckComboSparks(ParticleSystem sparks, in PelagWreckComboLook.Palette c)
        {
            if (sparks == null) return;
            var main = sparks.main;
            main.startColor = new ParticleSystem.MinMaxGradient(WcColor(c.HotR, c.HotG, c.HotB), WcLinear(c.LightR, c.LightG, c.LightB, 1.25f));
        }
    }
}
