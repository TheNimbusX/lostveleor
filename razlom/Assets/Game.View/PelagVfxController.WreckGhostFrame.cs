using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Призрачный якорь — кадр: падение с разгоном (якорь лицом к камере, откинут назад, мельче вверху), следы падения,
    /// вход в землю в тик события 77 (сплющивание, вспышка, удар .WreckGhostLand, свет, толчок камеры, отдача задетых),
    /// стояние и распад снизу вверх осколками и искорками. Всё — по тику показа (PelagWreckGhostLook).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private void UpdateWreckGhostRun(WreckGhostRun run, float shown)
        {
            if (run.Tilt == null) return;
            float since = PelagWreckVfxRules.Seconds(shown - run.SlamTick);
            float fall = (shown - run.SlamTick) / Mathf.Max(1f, run.LandTick - run.SlamTick);
            float t = PelagWreckVfxRules.Seconds(shown - run.LandTick);
            Quaternion rotation = WreckGhostRotation();
            Vector3 axis = rotation * Vector3.up;
            float height, scale = 1f, squashY = 1f, squashXZ = 1f, appear = 1f, dissolve = 0f, flash = 0f;
            if (t < 0f)
            {
                height = PelagWreckGhostLook.FallHeight(fall);
                scale = PelagWreckGhostLook.FallScale(fall);
                appear = Mathf.Clamp01(since / PelagWreckGhostLook.AppearSeconds);
            }
            else
            {
                if (!run.Landed && !run.Fizzled)
                {
                    if (run.Confirmed) LandWreckGhost(run);
                    else run.Fizzled = true;
                }
                if (run.Fizzled)
                {
                    height = 0f;
                    dissolve = Mathf.Clamp01(t / PelagWreckGhostLook.FizzleSeconds);
                }
                else
                {
                    height = -PelagWreckGhostLook.SinkAt(t);
                    squashY = PelagWreckGhostLook.SquashY(t);
                    squashXZ = PelagWreckGhostLook.SquashXZ(t);
                    flash = PelagWreckGhostLook.FlashAt(t);
                    dissolve = PelagWreckGhostLook.DissolveAt(t);
                    if (run.Light != null && WkStill(run.LandFx, run.LandObject))
                        run.Light.intensity = CaptureRig.NoVfx ? 0f : WgLightPeak * PelagWreckGhostLook.LightAt(t);
                }
            }
            run.Tilt.SetPositionAndRotation(run.Point + axis * height, rotation);
            run.Tilt.localScale = new Vector3(scale * squashXZ, scale * squashY, scale * squashXZ);
            bool visible = dissolve < .999f;
            if (_wgBlock == null) _wgBlock = new MaterialPropertyBlock();
            foreach (Renderer r in run.Parts)
            {
                if (r == null) continue;
                if (r.enabled != visible) r.enabled = visible;
                if (!visible) continue;
                r.GetPropertyBlock(_wgBlock);
                _wgBlock.SetFloat(WgAgeId, since);
                _wgBlock.SetFloat(WgAppearId, appear);
                _wgBlock.SetFloat(WgDissolveId, dissolve);
                _wgBlock.SetFloat(WgFlashId, flash);
                r.SetPropertyBlock(_wgBlock);
            }
            if (CaptureRig.NoVfx || Time.deltaTime <= 0f) return;
            if (t < 0f && appear > .5f) EmitWreckGhostStreaks(run, axis);
            if (visible && dissolve > 0f) EmitWreckGhostShards(run, dissolve);
        }

        /// <summary>Якорь лицом к камере (плоскость лап поперёк взгляда), повёрнут на YawDegrees и откинут назад от камеры.</summary>
        private static Quaternion WreckGhostRotation()
        {
            Camera camera = Camera.main;
            Vector3 view = camera != null ? camera.transform.forward : Vector3.forward;
            view.y = 0f;
            view = view.sqrMagnitude > 1e-4f ? view.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, view);
            return Quaternion.AngleAxis(PelagWreckGhostLook.TiltDegrees, right)
                * Quaternion.AngleAxis(PelagWreckGhostLook.YawDegrees, Vector3.up) * Quaternion.LookRotation(view, Vector3.up);
        }

        /// <summary>Тик события 77 на показе: удар — префаб удара, свет, толчок камеры, отдача задетых наружу.</summary>
        private void LandWreckGhost(WreckGhostRun run)
        {
            run.Landed = true;
            if (TryAcquire(PelagVfxId.WreckGhostLand, out GameObject go, out PelagVfxElement element))
            {
                int index = ReserveActive();
                element.Begin(run.Point, Quaternion.LookRotation(run.Dir, Vector3.up));
                float radius = PelagWreckGhostLook.RingRadius(run.Amount);
                go.transform.localScale = Vector3.one * (radius / PelagWreckGhostLook.AuthoredRadius);
                _active[index] = new ActiveFx
                {
                    Active = true, Id = PelagVfxId.WreckGhostLand, Object = go, Element = element, Duration = element.DefaultLifetime,
                    Start = run.Point, End = run.Point, Motion = Motion.Static, FollowIndex = -1
                };
                run.LandFx = index;
                run.LandObject = go;
                run.Light = go.GetComponentInChildren<Light>(true);
                if (run.Light != null) run.Light.intensity = WgLightPeak * PelagWreckGhostLook.LightAt(0f);
            }
            _juice?.PunchCamera(PelagWreckGhostLook.CameraPunch, PelagWreckGhostLook.CameraZoom);
            Simulation sim = _driver.Sim;
            foreach (int target in run.Targets)
                if (sim != null && (uint)target < (uint)sim.Entities.Count)
                    WreckRecoil(target, FlatDirection(run.Point, EntityPosition(target, run.Point)));
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-ghost] land tick={run.LandTick} point={run.Point.ToString("F2")} r={PelagWreckGhostLook.RingRadius(run.Amount):F2} hits={run.Targets.Count}");
        }

        // Точки силуэта якоря в метрах меша (высота 1, ширина ±.40): кончики лап, крона, ствол, звенья.
        private static readonly Vector2[] WgOutline =
        {
            new Vector2(-.40f, .30f), new Vector2(.40f, .30f), new Vector2(-.30f, .16f), new Vector2(.30f, .16f), new Vector2(0f, .02f),
            new Vector2(-.06f, .55f), new Vector2(.06f, .70f), new Vector2(-.07f, .92f), new Vector2(.07f, .98f)
        };

        /// <summary>Следы падения: короткие тонкие черты по оси якоря от кончиков силуэта (белые и фиолетовые).</summary>
        private void EmitWreckGhostStreaks(WreckGhostRun run, Vector3 axis)
        {
            if (run.Streaks == null || run.BodyT == null) return;
            int count = Mathf.Max(1, Mathf.RoundToInt(Time.deltaTime * 140f));
            for (int i = 0; i < count; i++)
            {
                Vector2 p = WgOutline[Random.Range(0, WgOutline.Length)];
                Vector3 at = run.BodyT.TransformPoint(new Vector3(p.x, p.y, 0f)) + axis * Random.Range(.05f, .4f);
                Color color = Random.value < .5f ? new Color(.92f, .92f, 1.3f, .75f) : new Color(.38f, .34f, 1.05f, .7f);
                run.Streaks.Emit(new ParticleSystem.EmitParams
                {
                    position = at, velocity = -axis * Random.Range(.6f, 1.2f), startColor = color,
                    startSize = Random.Range(.05f, .08f), startLifetime = Random.Range(.08f, .14f), applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>Распад снизу вверх: на высоте фронта распада — осколки-кристаллы и искорки, всплывают и вертятся.</summary>
        private void EmitWreckGhostShards(WreckGhostRun run, float dissolve)
        {
            if (run.BodyT == null) return;
            float h = Mathf.Clamp01(dissolve * 1.1f);
            float half = h < .36f ? .40f : h > .88f ? .10f : .07f;
            int shards = Mathf.Max(1, Mathf.RoundToInt(Time.deltaTime * 170f));
            for (int i = 0; i < shards; i++)
            {
                Vector3 at = run.BodyT.TransformPoint(new Vector3(Random.Range(-half, half), Mathf.Clamp01(h + Random.Range(-.04f, .06f)), 0f));
                Vector3 v = Vector3.up * Random.Range(1.4f, 3.0f) + new Vector3(Random.Range(-.8f, .8f), 0f, Random.Range(-.8f, .8f));
                if (run.Shards != null && (i & 1) == 0)
                    run.Shards.Emit(new ParticleSystem.EmitParams
                    {
                        position = at, velocity = v, startColor = Color.Lerp(new Color(.30f, .24f, .80f, 1f), new Color(.62f, .56f, 1f, 1f), Random.value),
                        startSize = Random.Range(.12f, .22f), startLifetime = Random.Range(.40f, .62f), rotation = Random.Range(0f, 360f),
                        angularVelocity = Random.Range(-500f, 500f), applyShapeToPosition = false
                    }, 1);
                if (run.Motes != null)
                    run.Motes.Emit(new ParticleSystem.EmitParams
                    {
                        position = at, velocity = v * 1.3f, startColor = Random.value < .5f ? new Color(.92f, .92f, 1.3f, 1f) : new Color(.38f, .34f, 1.05f, 1f),
                        startSize = Random.Range(.035f, .06f), startLifetime = Random.Range(.25f, .42f), applyShapeToPosition = false
                    }, 1);
            }
        }
    }
}
