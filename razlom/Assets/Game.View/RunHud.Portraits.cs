using Game.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// Портреты видов для «Убито» в итогах (кадр 4: круги мобов; владелец — «портреты ставить наши»). Картинок
    /// мобов в проекте нет, поэтому портрет снимается с настоящего тела в бою: маленькая камера смотрит на тело
    /// только на слой тел врагов (EnemyOutline) — без земли, травы и интерфейса — и рисует его в свою текстуру
    /// 160×160 с прозрачным фоном. Один маленький проход на вид за забег, без теней и постобработки; текстуры
    /// живут всю сессию и переснимаются в новом забеге. Нет снимка — в круге белый знак встречи.
    ///
    /// КОГДА СНИМАТЬ (проверка 30.09): на убийстве тело уже белое от вспышки (_HitFlash 0,92) и сразу трескается
    /// (EnemyPresentationProfile.KillBreakSeconds) — в круге выходил белый силуэт. Поэтому вид снимается при первой
    /// встрече: тело живо не меньше <see cref="PortraitSettle"/> с (проявилось из дыма) и не получало урона
    /// <see cref="PortraitCalm"/> с (вспышка удара сошла). Не больше одного снимка за кадр.
    /// </summary>
    public sealed partial class RunHud
    {
        const int PortraitSize = 160;
        const int PortraitKinds = 32;
        /// <summary>Взгляд камеры портрета: сверху-спереди, ниже игровой, — лицо, а не макушка.</summary>
        const float PortraitPitch = 30f, PortraitFov = 26f;
        /// <summary>Сколько секунд тело должно прожить и сколько не получать урона, чтобы его снять.</summary>
        const float PortraitSettle = 1f, PortraitCalm = .4f;

        readonly RenderTexture[] _portraits = new RenderTexture[PortraitKinds];
        readonly bool[] _portraitTaken = new bool[PortraitKinds];
        RiftRun _portraitRun;
        Simulation _portraitSim;
        // По сущностям: когда впервые увидена живой (−1 — нет) и когда последний раз получила урон.
        float[] _portraitSeenAt = new float[0], _portraitHurtAt = new float[0];
        Camera _portraitCamera;

        /// <summary>Портрет вида, снятый в этом забеге; null — не снят.</summary>
        private Texture Portrait(EnemyKind kind)
        {
            int index = (int)kind;
            return index < PortraitKinds && _portraitTaken[index] ? _portraits[index] : null;
        }

        /// <summary>
        /// Кадр забега: урон по телам из событий кадра и снимок первого спокойного тела ещё не снятого вида.
        /// Без выделения памяти: массивы растут только со сменой симуляции.
        /// </summary>
        private void WatchPortraits(RiftRun run, System.Collections.Generic.IReadOnlyList<SimEvent> events)
        {
            if (run != _portraitRun)
            {
                _portraitRun = run;
                for (int i = 0; i < _portraitTaken.Length; i++) _portraitTaken[i] = false;
            }
            Simulation sim = run.Sim;
            if (sim == null) return;
            EntityStore entities = sim.Entities;
            if (sim != _portraitSim)
            {
                // Другая арена — номера сущностей принадлежат другим телам.
                _portraitSim = sim;
                int capacity = Mathf.Max(entities.Capacity, entities.Count);
                if (_portraitSeenAt.Length < capacity)
                {
                    _portraitSeenAt = new float[capacity];
                    _portraitHurtAt = new float[capacity];
                }
                for (int i = 0; i < _portraitSeenAt.Length; i++) { _portraitSeenAt[i] = -1f; _portraitHurtAt[i] = -100f; }
            }
            float now = UiMotion.Now;
            int count = Mathf.Min(entities.Count, _portraitSeenAt.Length);
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type == SimEventType.Damage && (uint)e.Target < (uint)count) _portraitHurtAt[e.Target] = now;
            }
            for (int id = Simulation.PlayerId + 1; id < count; id++)
            {
                if (!entities.Alive[id] || entities.Side[id] == Faction.Wole) { _portraitSeenAt[id] = -1f; continue; }
                int index = (int)entities.Kind[id];
                if (index <= 0 || index >= PortraitKinds || _portraitTaken[index]) continue;
                if (_portraitSeenAt[id] < 0f) { _portraitSeenAt[id] = now; continue; }
                if (now - _portraitSeenAt[id] < PortraitSettle || now - _portraitHurtAt[id] < PortraitCalm) continue;
                _portraitTaken[index] = true;
                FixVec2 at = entities.Position[id];
                float height = RunHudSummary.PortraitHeight(entities.Kind[id]) * (run.BossId == id ? 1.25f : 1f);
                if (!CapturePortrait(index, new Vector3(at.X.ToFloat(), 0f, at.Y.ToFloat()), height)) _portraitTaken[index] = false;
                return;
            }
        }

        private bool CapturePortrait(int index, Vector3 feet, float height)
        {
            Camera main = Camera.main;
            int layer = LayerMask.NameToLayer("EnemyOutline");
            if (main == null || layer < 0) return false;
            if (_portraitCamera == null)
            {
                var host = new GameObject("Портрет врага для итогов");
                host.transform.SetParent(transform, false);
                _portraitCamera = host.AddComponent<Camera>();
                _portraitCamera.enabled = false;
                _portraitCamera.clearFlags = CameraClearFlags.SolidColor;
                _portraitCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _portraitCamera.fieldOfView = PortraitFov;
                _portraitCamera.nearClipPlane = .1f;
                _portraitCamera.farClipPlane = 80f;
                _portraitCamera.allowHDR = false;
                _portraitCamera.allowMSAA = false;
                var data = host.AddComponent<UniversalAdditionalCameraData>();
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
            }
            _portraitCamera.cullingMask = 1 << layer;
            RenderTexture target = _portraits[index];
            if (target == null)
            {
                target = new RenderTexture(PortraitSize, PortraitSize, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "Портрет вида " + index };
                target.Create();
                _portraits[index] = target;
            }
            // Со стороны игровой камеры, но ниже и ближе: кадр по росту тела.
            Vector3 forward = main.transform.forward;
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            float pitch = PortraitPitch * Mathf.Deg2Rad;
            Vector3 look = flat * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch);
            Vector3 focus = feet + Vector3.up * height * .5f;
            float distance = height * .62f / Mathf.Tan(PortraitFov * .5f * Mathf.Deg2Rad);
            Transform pose = _portraitCamera.transform;
            pose.position = focus - look * distance;
            pose.rotation = Quaternion.LookRotation(look, Vector3.up);
            try
            {
                _portraitCamera.targetTexture = target;
                _portraitCamera.Render();
            }
            finally
            {
                _portraitCamera.targetTexture = null;
            }
            return true;
        }

        private void OnDestroy()
        {
            if (_choiceOwner == this) _choiceOwner = null;
            for (int i = 0; i < _portraits.Length; i++)
            {
                if (_portraits[i] == null) continue;
                _portraits[i].Release();
                Destroy(_portraits[i]);
                _portraits[i] = null;
            }
        }
    }
}
