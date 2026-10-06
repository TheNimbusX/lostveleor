using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Махи Крушения v4 на РИСОВАННЫХ текстурах (06.10; ассеты — Editor/PelagWreckPaintedVfxSetup, числа —
    /// PelagWreckPaintedLook). Процедурные полумесяцы «железа» (.WreckIronSwing) отвязаны: их записи библиотеки сняты.
    /// Всё — от событий Sim в очереди до тика показа:
    ///  • контакт маха (WreckStage этапа 0, 1 и «Четвёртого») — рисованный полумесяц со звеньями на полумесяце пака,
    ///    центр чуть впереди героя на высоте пояса, наружный радиус — досягаемость цепи; голова раскрывается по дуге за
    ///    якорем и уходит чуть дальше направления удара, хвост — за спину со стороны, откуда шёл якорь; мах 2 — зеркало;
    ///  • задетый махом (Damage маха) — рисованная звезда удара у тела и сколы-спрайты по ходу маха (вращение, тяжесть).
    /// Цвет формы — только синие места рисунка (_TintMul блоком свойств): база — свои цвета текстуры.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private MaterialPropertyBlock _wpBlock;
        private static readonly int WpTintId = Shader.PropertyToID("_TintMul"), WpWhiteId = Shader.PropertyToID("_TintWhite");

        /// <summary>Префабы рисованного Крушения собраны и в библиотеке.</summary>
        private bool WreckPaintedSwingReady => _pools != null && (int)PelagVfxId.WreckPaintedHit < _pools.Length
            && _pools[(int)PelagVfxId.WreckPaintedSwing] != null && _pools[(int)PelagVfxId.WreckPaintedSwingHeavy] != null
            && _pools[(int)PelagVfxId.WreckPaintedHit] != null;

        /// <summary>Цвет формы на рендерер рисунка: множитель синих мест и осветление (база — 1, 1, 1 и 0).</summary>
        private void TintWreckPainted(Renderer renderer, PelagForm form)
        {
            if (renderer == null) return;
            if (_wpBlock == null) _wpBlock = new MaterialPropertyBlock();
            PelagWreckPaintedLook.TintMul(form, out float r, out float g, out float b);
            renderer.GetPropertyBlock(_wpBlock);
            _wpBlock.SetColor(WpTintId, new Color(r, g, b, 1f));
            _wpBlock.SetFloat(WpWhiteId, PelagWreckPaintedLook.TintWhite(form));
            renderer.SetPropertyBlock(_wpBlock);
        }

        private void TintWreckPainted(Transform root, string child, PelagForm form)
            => TintWreckPainted(root.Find(child)?.GetComponent<Renderer>(), form);

        /// <summary>Контакт маха (тик показа): рисованный полумесяц. False — префабов нет (тогда прежний путь).</summary>
        private bool PlayWreckPaintedSwing(in WkPending p)
        {
            if (!WreckPaintedSwingReady || !PelagWreckSwingRules.Draws(p.Stage)) return false;
            if (CaptureRig.NoVfx) return true;
            int side = Simulation.WreckSwingSide(p.Stage) < 0 || p.Stage == PelagWreckSwingRules.StageFourth ? 1 : 0;
            PelagVfxId id = p.Stage == PelagWreckSwingRules.StageSwing ? PelagVfxId.WreckPaintedSwing : PelagVfxId.WreckPaintedSwingHeavy;
            if (!TryAcquire(id, out GameObject go, out PelagVfxElement element)) return true;
            Vector3 forward = p.Wave.Dir;
            forward.y = 0f;
            forward = forward.sqrMagnitude > .01f ? forward.normalized : PlayerFacing();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // Голова — по ходу якоря: мах 1 уходит влево (−right), мах 2 — вправо; мах 2 — зеркало (нормаль вниз).
            Vector3 headSide = side == 1 ? right : -right;
            Vector3 normal = side == 1 ? Vector3.down : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(normal, headSide);
            int k = side;
            Vector3 center = p.At + Vector3.up * PelagWreckPaintedLook.SwingHeight[k] + forward * PelagWreckPaintedLook.SwingAhead[k];
            float radius = PelagWreckPaintedLook.SwingRadius[k];
            TintWreckPainted(go.transform, "Wave", _wsLastForm);
            int index = ReserveActive();
            element.Begin(center, rotation);
            go.transform.localScale = Vector3.one * radius;
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = center, End = center, Motion = Motion.Static, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-painted-swing] stage={p.Stage} tick={p.Tick} side={side} center={center.ToString("F2")} r={radius:F2} forward={forward.ToString("F2")} form={_wsLastForm}");
            return true;
        }

        /// <summary>
        /// Знак на задетом махом (из PlayWreckSwingHit после отброса): рисованная звезда у груди ближе к камере, сколы
        /// по ходу маха и вверх. False — префабов нет (тогда прежний знак).
        /// </summary>
        private bool PlayWreckPaintedMark(Vector3 body, Vector3 dir, float radius, WreckSwingWeight weight)
        {
            if (!WreckPaintedSwingReady) return false;
            if (CaptureRig.NoVfx) return true;
            bool heavy = weight == WreckSwingWeight.Heavy;
            float groundY = WreckGroundAt(body);
            var foot = new Vector3(body.x, groundY + .03f, body.z);
            Vector3 chest = foot + Vector3.up * Mathf.Clamp(.5f + .45f * radius, .65f, 1.3f);
            Vector3 contact = chest - dir * Mathf.Clamp(.35f * radius, .15f, .5f);
            if (WiSpawn(PelagVfxId.WreckPaintedHit, contact, Quaternion.LookRotation(dir, Vector3.up), 0f, 0f, out GameObject go) < 0) return true;
            // Звезда: у маха 1 — ~0,9 м, у маха 2 — крупнее (~1,3 м), как на v4-swing-right.png (съёмка 1: была мелкой).
            go.transform.localScale = Vector3.one * (heavy ? 1.3f : .92f);
            Transform root = go.transform;
            TintWreckPainted(root, "Star", _wsLastForm);
            TintWreckPainted(root, "Chips", _wsLastForm);
            ParticleSystem chips = root.Find("Chips")?.GetComponent<ParticleSystem>();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            int big = heavy ? 4 : 3, small = heavy ? 4 : 3;
            for (int i = 0; i < big + small; i++)
            {
                bool large = i < big;
                Vector3 d = (dir * 1.1f + Vector3.up * Random.Range(.45f, 1.05f) + side * Random.Range(-.75f, .75f)).normalized;
                WpEmit(chips, chest + Random.insideUnitSphere * .15f, d * Random.Range(2.8f, 5.4f),
                    large ? Random.Range(.34f, .48f) * (heavy ? 1.12f : 1f) : Random.Range(.18f, .26f), Random.Range(.45f, .7f));
            }
            return true;
        }

        /// <summary>Скол или ком-спрайт: случайный поворот и вращение (2D-билборд), цвет — белый (свои цвета рисунка).</summary>
        private static void WpEmit(ParticleSystem system, Vector3 at, Vector3 velocity, float size, float life)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startColor = Color.white, startSize = size, startLifetime = life,
                rotation = Random.Range(0f, 360f), angularVelocity = Random.Range(380f, 820f) * (Random.value < .5f ? -1f : 1f),
                applyShapeToPosition = false
            }, 1);
        }
    }
}
