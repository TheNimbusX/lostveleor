using System;
using System.Numerics;

namespace Game.View
{
    // Файл запечки (DESIGN §2.1): Resources/Weapons/Pelag/AnchorBakes/<Clip>[_w<N>|_p<K>].anchorbake.json.
    // Разбор — JsonUtility в Unity, System.Text.Json (IncludeFields) в тестах и в tools/anchorbake.
    // Неизвестные ключи (source, seams, guidance, validation) разбор пропускает — они для отчёта.
    // Время: "t" — кадр клипа (= тик Sim при 30 к/с), строго i / sub (проверяется при разборе: секунды вместо кадров —
    // отказ, а не тихий сдвиг); "sec" — секунды, "src" — кадр исходного клипа до ретайма — только для отчёта.
    // Оси — корень тела после RazlomPelagAuthoredClips.Build: x вправо, y вверх, z вперёд, метры при росте тела 1,82.

    [Serializable]
    public sealed class AnchorBakeSample
    {
        public float t;
        public float[] p, q, v, w, grip, gripQ;
        public bool taut;
        public float tension;
    }

    [Serializable]
    public sealed class AnchorBakeContact
    {
        /// <summary>"swing" (сектор, голова на дуге) или "ground" (удар оземь, точка).</summary>
        public string kind;
        public float frame;
        public float[] point;
        public float radius;
    }

    [Serializable]
    public sealed class AnchorBakeRange
    {
        public float from, to;
    }

    [Serializable]
    public sealed class AnchorBakeRig
    {
        public float chainLength, mass, headSize, gravity;
        public float[] inertia, eyeLocal, airDrag;
        public string hull;
    }

    [Serializable]
    public sealed class AnchorBakeData
    {
        public int version;
        public string clip;
        public int windupTicks;
        public AnchorBakeRig rig;
        public int fps, sub, frames;
        public AnchorBakeSample[] samples;
        public AnchorBakeContact[] contacts;
        public AnchorBakeRange[] ground;
        /// <summary>Цикл (ChargeLoop): to &gt; from. Нет цикла — 0/0 (JsonUtility не умеет null).</summary>
        public AnchorBakeRange loop;
        /// <summary>С какого кадра голова уходит в живую физику; 0 — по умолчанию (оземь — кадр касания, иначе конец).</summary>
        public float liveFrom;
        /// <summary>Кадр «над головой» удара оземь (тик OverheadTick Sim); 0 — не задан.</summary>
        public float overheadFrame;
        /// <summary>Кадр, в который правая кисть проходит у ножен (timing.json клипа, DESIGN §4.3); 0 — не задан.</summary>
        public float sheathFrame;
        /// <summary>"swing", "slam", "loop", "release", "windup" — чем проверена запечка (для лога).</summary>
        public string kind;
    }

    /// <summary>
    /// grip_socket.json (DESIGN §3.1), версия 2 — один файл для игры и для tools/anchorbake: кольцо рукояти в осях
    /// <c>bone</c> (mixamorig:LeftHand), МЕТРЫ (в игре делится на lossyScale кисти), поворот рукояти — кватернион.
    /// support/abordage — то же для правой кисти (опорная на цепи; замах Абордажа).
    /// </summary>
    [Serializable]
    public sealed class AnchorGripSocketData
    {
        public int version;
        public string units;
        public string bone;
        public float[] position;
        public float[] rotation;
        public AnchorGripSocketPoint support, abordage;

        public bool Valid => version >= 2 && units == "metres" && position != null && position.Length >= 3;
    }

    [Serializable]
    public sealed class AnchorGripSocketPoint
    {
        public string bone;
        public float[] position;
    }

    /// <summary>Выборка запечённого пути головы (DESIGN §2.2): Эрмит по p и v, Slerp q, v и ω линейно.</summary>
    public sealed class AnchorBake
    {
        private readonly Vector3[] _p, _v, _w, _grip;
        private readonly Quaternion[] _q;
        private readonly bool[] _taut;
        private readonly float[] _tension;

        public string Clip { get; }
        public int WindupTicks { get; }
        public int Fps { get; }
        public int Sub { get; }
        public float StepSeconds { get; }
        public float EndFrame { get; }
        public float ContactFrame { get; } = -1f;
        public bool GroundContact { get; }
        public Vector3 ContactPoint { get; }
        public float LiveFrom { get; }
        public float OverheadFrame { get; } = -1f;
        public float SheathFrame { get; } = -1f;
        public string Kind { get; }
        public float LoopFrom { get; }
        public float LoopTo { get; }
        public bool Looped => LoopTo > LoopFrom;
        public float ChainLength { get; }
        public Vector3 EyeLocal { get; }
        public string Error { get; }
        public bool Valid => Error == null;

        public AnchorBake(AnchorBakeData data)
        {
            Error = Check(data);
            if (Error != null) { _p = _v = _w = _grip = Array.Empty<Vector3>(); _q = Array.Empty<Quaternion>(); _taut = Array.Empty<bool>(); _tension = Array.Empty<float>(); return; }
            Clip = data.clip; WindupTicks = data.windupTicks; Fps = data.fps; Sub = data.sub;
            StepSeconds = 1f / (Fps * Sub);
            int n = data.samples.Length;
            _p = new Vector3[n]; _v = new Vector3[n]; _w = new Vector3[n]; _grip = new Vector3[n];
            _q = new Quaternion[n]; _taut = new bool[n]; _tension = new float[n];
            for (int i = 0; i < n; i++)
            {
                var s = data.samples[i];
                _p[i] = V(s.p); _v[i] = V(s.v); _w[i] = V(s.w); _grip[i] = V(s.grip);
                _q[i] = Quaternion.Normalize(Q(s.q));
                if (i > 0 && Quaternion.Dot(_q[i - 1], _q[i]) < 0) _q[i] = Quaternion.Negate(_q[i]);
                _taut[i] = s.taut; _tension[i] = s.tension;
            }
            EndFrame = (n - 1) / (float)Sub;
            if (data.contacts != null && data.contacts.Length > 0)
            {
                var c = data.contacts[0];
                ContactFrame = c.frame;
                GroundContact = c.kind == "ground";
                ContactPoint = V(c.point);
            }
            LiveFrom = data.liveFrom > 0 ? Math.Min(data.liveFrom, EndFrame)
                : GroundContact && ContactFrame >= 0 ? ContactFrame : EndFrame;
            OverheadFrame = data.overheadFrame > 0 ? data.overheadFrame : -1f;
            SheathFrame = data.sheathFrame > 0 ? data.sheathFrame : -1f;
            Kind = data.kind;
            if (data.loop != null && data.loop.to > data.loop.from) { LoopFrom = data.loop.from; LoopTo = Math.Min(data.loop.to, EndFrame); }
            ChainLength = data.rig != null && data.rig.chainLength > 0 ? data.rig.chainLength : 0;
            EyeLocal = data.rig != null ? V(data.rig.eyeLocal) : Vector3.Zero;
        }

        private static string Check(AnchorBakeData d)
        {
            if (d == null) return "нет данных";
            if (d.fps <= 0 || d.sub <= 0) return "fps/sub";
            if (d.samples == null || d.samples.Length < 2) return "мало сэмплов";
            for (int i = 0; i < d.samples.Length; i++)
            {
                var s = d.samples[i];
                if (s == null || !Has(s.p, 3) || !Has(s.q, 4) || !Has(s.v, 3) || !Has(s.w, 3)) return "сэмпл " + i;
                if (i > 0 && s.t <= d.samples[i - 1].t) return "t не растёт у сэмпла " + i;
                if (Math.Abs(s.t - i / (float)d.sub) > 1e-3f) return "t сэмпла " + i + " не кадр (ожидается i/sub)";
            }
            return null;
        }

        private static bool Has(float[] a, int n) => a != null && a.Length >= n;
        private static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
        private static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.Identity;

        /// <summary>Кадр в пределах запечки: цикл заворачивается, остальное прижимается к [0, конец].</summary>
        public float Wrap(float frame)
        {
            if (Looped)
            {
                float span = LoopTo - LoopFrom;
                float u = (frame - LoopFrom) % span;
                if (u < 0) u += span;
                return LoopFrom + u;
            }
            return Math.Clamp(frame, 0f, EndFrame);
        }

        /// <summary>Точка хвата клипа в осях корня в кадре <paramref name="frame"/> (сверка с кистью в игре, §5 #1).</summary>
        public Vector3 GripAt(float frame)
        {
            float f = Wrap(frame) * Sub;
            int i = Math.Clamp((int)Math.Floor(f), 0, _grip.Length - 2);
            return Vector3.Lerp(_grip[i], _grip[i + 1], Math.Clamp(f - i, 0f, 1f));
        }

        /// <summary>Положение головы в осях корня (скорости — за секунду запечки).</summary>
        public AnchorPose Sample(float frame, out bool taut, out Vector3 grip)
        {
            float f = Wrap(frame) * Sub;
            int i = Math.Clamp((int)Math.Floor(f), 0, _p.Length - 2);
            float u = Math.Clamp(f - i, 0f, 1f), h = StepSeconds;
            float u2 = u * u, u3 = u2 * u;
            float h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
            Vector3 p = h00 * _p[i] + h10 * h * _v[i] + h01 * _p[i + 1] + h11 * h * _v[i + 1];
            taut = u < .5f ? _taut[i] : _taut[i + 1];
            grip = Vector3.Lerp(_grip[i], _grip[i + 1], u);
            return new AnchorPose(p, Quaternion.Normalize(Quaternion.Slerp(_q[i], _q[i + 1], u)),
                Vector3.Lerp(_v[i], _v[i + 1], u), Vector3.Lerp(_w[i], _w[i + 1], u));
        }

        public float TensionAt(float frame)
        {
            float f = Wrap(frame) * Sub;
            int i = Math.Clamp((int)Math.Round(f), 0, _tension.Length - 1);
            return _tension[i];
        }

        /// <summary>
        /// Из осей корня в мир: корень (положение, поворот, скорость, угловая скорость), масштаб тела
        /// (рост / 1,82) и темп <paramref name="rate"/> (кадров запечки на тик Sim — растяжка ускорения каста).
        /// </summary>
        public static AnchorPose ToWorld(in AnchorPose local, Vector3 rootPosition, Quaternion rootRotation,
            Vector3 rootVelocity, Vector3 rootAngularVelocity, float scale = 1f, float rate = 1f)
        {
            Vector3 offset = Vector3.Transform(local.Position * scale, rootRotation);
            return new AnchorPose(
                rootPosition + offset,
                Quaternion.Normalize(rootRotation * local.Rotation),
                Vector3.Transform(local.Velocity * (scale * rate), rootRotation) + rootVelocity + Vector3.Cross(rootAngularVelocity, offset),
                Vector3.Transform(local.AngularVelocity * rate, rootRotation) + rootAngularVelocity);
        }
    }

    /// <summary>Часы запечки по тикам Sim (DESIGN §2.2), без Unity и без Sim.</summary>
    public static class AnchorBakeClock
    {
        public const float TicksPerSecond = 30f;

        /// <summary>
        /// Кадр запечки удара из показанного тика: [StageStart … Contact] → [0 … contactFrame] кусочно-линейно
        /// (у удара оземь ещё Overhead → overheadFrame), после контакта 1:1. <paramref name="rate"/> — кадров на тик.
        /// </summary>
        public static float Frame(float shown, int startTick, int contactTick, float contactFrame,
            int overheadTick, float overheadFrame, out float rate)
        {
            if (contactTick <= startTick || contactFrame <= 0)
            {
                rate = 1f;
                return Math.Max(0f, contactFrame) + (shown - contactTick);
            }
            if (shown >= contactTick) { rate = 1f; return contactFrame + (shown - contactTick); }
            bool overhead = overheadFrame > 0 && overheadFrame < contactFrame && overheadTick > startTick && overheadTick < contactTick;
            if (overhead && shown < overheadTick)
            {
                rate = overheadFrame / (overheadTick - startTick);
                return Math.Max(0f, (shown - startTick) * rate);
            }
            if (overhead)
            {
                rate = (contactFrame - overheadFrame) / (contactTick - overheadTick);
                return overheadFrame + (shown - overheadTick) * rate;
            }
            rate = contactFrame / (contactTick - startTick);
            return Math.Max(0f, (shown - startTick) * rate);
        }

        /// <summary>
        /// Девятый вал: кадр цикла «вертолёта». Оборот <paramref name="periodStart"/> → <paramref name="periodEnd"/> секунд
        /// линейно по заряду (тик удержания) до <paramref name="maxCharge"/>; фаза — точный интеграл, без скачков.
        /// </summary>
        public static float LoopFrame(float shown, int chargeStartTick, float loopFrames, float periodStart, float periodEnd,
            int maxCharge, out float rate)
        {
            float c = Math.Max(0f, shown - chargeStartTick);
            float k = (periodEnd - periodStart) / Math.Max(1, maxCharge);
            float scale = loopFrames / TicksPerSecond;
            float cc = Math.Min(c, maxCharge);
            float frames = Math.Abs(k) < 1e-7f ? scale * cc / periodStart
                : scale / k * (float)Math.Log((periodStart + k * cc) / periodStart);
            if (c > maxCharge) frames += scale * (c - maxCharge) / periodEnd;
            rate = scale / (periodStart + k * cc);
            return frames;
        }

        /// <summary>Вариант выхода из цикла по фазе (8 запечек через 45°): индекс и дробный сдвиг в кадрах цикла.</summary>
        public static int ReleaseVariant(float loopFrame, float loopFrames, int variants, out float frameOffset)
        {
            float phase = loopFrame / loopFrames * variants;
            phase -= (float)Math.Floor(phase / variants) * variants;
            int index = (int)Math.Round(phase);
            frameOffset = (phase - index) / variants * loopFrames;
            return index % variants;
        }

        /// <summary>Вес поправки точки удара: 0 до contact − span, 1 с кадра контакта; наклон — на кадр.</summary>
        public static float CorrectionWeight(float frame, float contactFrame, float spanFrames, out float slopePerFrame)
        {
            float u = (frame - (contactFrame - spanFrames)) / spanFrames;
            slopePerFrame = AnchorRigMath.QuinticSlope(u) / spanFrames;
            return AnchorRigMath.Quintic(u);
        }
    }
}
