using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using AnchorBake;

namespace Wreck4Anchor
{
    /// <summary>Настройка прогона: времена фаз по плану, крепление на спине (v3), рукоять (спина / одна рука / две руки), воронка.</summary>
    public sealed partial class Sim
    {
        public readonly Timeline TL; public readonly HeadModel Head; readonly JsonElement _plan;
        public readonly float L, Eye, R;
        public float TGrab = -1, TDrawEnd = -1, TRel = -1, TCon = -1, THold = -1, TShort = -1, TStow0 = -1, TLay0 = -1, TLay1 = -1, TLand = -1, TEnd;
        public readonly List<(float T, Vector3 Target)> Hits = new List<(float, Vector3)>();
        public Vector3 ImpactPoint, ImpactCentre, V0; public bool HasV0;
        public float BiteKeep, DrawPeel, ApexOut, BiteBounce, BiteDamp, DrawLift, DrawBack, DrawSpin, DrawPitch, StowLand, StowUp, StowOut, StowBack, Reach;
        public readonly Mace Mace; public float SoftScale, SoftEnd, RollRate;

        // Крепление v3 (animation-v3/timing.json stow_mount): кость Spine2, центр головы (−0,085; −0,321; −0,151) м, поворот Spine2·Rz(−25°).
        static readonly Vector3 CentreLocal = new Vector3(-.085f, -.321f, -.151f);
        static readonly Quaternion MountLocal = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -25f * MathF.PI / 180f);
        Vector3 _exitLocal, _uBackLocal, _uGripLocal;

        public Sim(JsonElement plan, Timeline tl, HeadModel head)
        {
            _plan = plan; TL = tl; Head = head;
            L = Timeline.F(plan, "chain", .45f); Eye = head.EyeLocal.Length(); R = L + Eye;
            Mace = new Mace(tl) { Eye = Eye };
            var m = Opt("mace");
            Mace.Omega = Timeline.F(m, "omega", Mace.Omega); Mace.Zeta = Timeline.F(m, "zeta", Mace.Zeta); Mace.Droop = Timeline.F(m, "droop", Mace.Droop);
            Mace.MaxW = Timeline.F(m, "maxW", Mace.MaxW); Mace.ImpactMaxW = Timeline.F(m, "impactMaxW", Mace.ImpactMaxW); Mace.Recoil = Timeline.F(m, "recoil", Mace.Recoil);
            var draw = Opt("draw"); var lunge = Opt("lunge"); var stow = Opt("stow");
            if (draw.ValueKind == JsonValueKind.Object) { TGrab = At(draw, "grab"); TDrawEnd = At(draw, "end"); }
            DrawLift = Timeline.F(draw, "lift", .8f); DrawBack = Timeline.F(draw, "back", .25f); DrawSpin = Timeline.F(draw, "spin", 6f); DrawPitch = Timeline.F(draw, "pitch", 0f); ApexOut = Timeline.F(draw, "out", .15f); DrawPeel = Timeline.F(draw, "peel", 3f);
            TRel = At(lunge, "release"); TCon = At(lunge, "contact"); THold = At(lunge, "hold"); TShort = At(lunge, "short");
            Reach = Timeline.F(lunge, "reach", 2.2f); BiteBounce = Timeline.F(lunge, "bounce", .08f); BiteDamp = Timeline.F(lunge, "bite", 30f); BiteKeep = Timeline.F(lunge, "keep", .1f);
            if (lunge.ValueKind == JsonValueKind.Object && lunge.TryGetProperty("windup", out _))
            {
                Mace.OvT0 = At(lunge, "windup"); Mace.OvT1 = TRel;
                TL.Root(TRel, out _, out _, out var qr);
                Mace.OvApex = Vector3.Normalize(Vector3.Transform(new Vector3(0, 1, -Timeline.F(lunge, "apexBack", .25f)), qr));
                Mace.OvDir = Vector3.Normalize(Vector3.Transform(new Vector3(Timeline.F(lunge, "relSide", 0), Timeline.F(lunge, "relUp", .6f), Timeline.F(lunge, "relFwd", .77f)), qr));
            }
            SoftScale = Timeline.F(Opt("mace"), "soft", .55f); SoftEnd = Timeline.F(Opt("mace"), "softEnd", 3f);
            RollRate = Timeline.F(Opt("mace"), "rollRate", 4f); Mace.Feed = Timeline.F(Opt("mace"), "feed", .6f);
            if (stow.ValueKind == JsonValueKind.Object) { TStow0 = At(stow, "start"); TLay0 = At(stow, "lay0"); TLay1 = At(stow, "lay1"); TLand = At(stow, "land"); }
            StowLand = Timeline.F(stow, "landSpeed", 1.5f);
            StowUp = Timeline.F(stow, "up", .8f); StowOut = Timeline.F(stow, "out", .15f); StowBack = Timeline.F(stow, "back", .1f);
            TEnd = tl.Duration + Timeline.F(plan, "tail", 0f);
            foreach (var c in Opt("contacts").EnumerateArray())
            {
                float T = At(c, "frame");
                TL.Root(T, out var r, out _, out var qy);
                Hits.Add((T, r + Vector3.Transform(new Vector3(Timeline.F(c, "side", 0), Timeline.F(c, "h", .85f), Timeline.F(c, "fwd", 1.25f)), qy)));
                Mace.Aims.Add((T, Timeline.F(c, "aimIn", 6f) / 30f, Timeline.F(c, "aimOut", 4f) / 30f, Vector3.Zero));
            }
            CalibrateHandle();
            WhipConfig();
            TL.Root(TCon, out var rc, out _, out var qc);
            ImpactPoint = rc + Vector3.Transform(new Vector3(0, 0, Reach), qc);
        }

        JsonElement Opt(string name) => _plan.TryGetProperty(name, out var e) ? e : default;
        float At(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? TL.TimeOf(e.GetProperty("seg").GetInt32(), v.GetSingle()) : -1f;

        void CalibrateHandle()
        {
            // Выход цепи рукояти на спине — там, где её кладёт правая рука в Stow (кадр клипа в конце), в осях Spine2.
            float tB = TL.Duration;
            var inv = Quaternion.Conjugate(TL.Spine2Rot(tB));
            _exitLocal = Vector3.Transform(TL.Grip(tB) - TL.Spine2(tB), inv);
            Vector3 ringLocal = CentreLocal + Vector3.Transform(Head.EyeLocal, MountLocal);
            _uBackLocal = Vector3.Normalize(_exitLocal - ringLocal);
            // Ось рукояти в осях хвата правой кисти — по кадрам, где обе кисти на рукояти (одна рука держит так же).
            Vector3 acc = Vector3.Zero;
            for (float T = 0; T <= TL.Duration; T += 1f / 60f)
                if (TL.HandGap(T) < .2f) acc += Vector3.Transform(TL.Axis(T), Quaternion.Conjugate(TL.GripRot(T)));
            _uGripLocal = acc.LengthSquared() > 0 ? Vector3.Normalize(acc) : Vector3.UnitY;
        }

        public Vector3 MountCentre(float T) => TL.Spine2(T) + Vector3.Transform(CentreLocal, TL.Spine2Rot(T));
        public Quaternion MountRot(float T) => Quaternion.Normalize(TL.Spine2Rot(T) * MountLocal);
        public Vector3 BackExit(float T) => TL.Spine2(T) + Vector3.Transform(_exitLocal, TL.Spine2Rot(T));
        public float ExitOffsetAtGrab => TGrab < 0 ? 0 : Vector3.Distance(BackExit(TGrab), TL.Grip(TGrab));

        static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

        /// <summary>Доля «рукоять на спине»: до хвата 1, хват — за тик в руку, уборка — с lay0 до lay1 обратно на спину.</summary>
        float BackWeight(float T)
        {
            if (TGrab >= 0 && T < TGrab) return 1;
            if (TGrab >= 0 && T < TGrab + 1f / 30f) return 1 - Smooth((T - TGrab) * 30f);
            if (TLay0 >= 0 && T >= TLay0) return Smooth((T - TLay0) / Math.Max(1e-3f, TLay1 - TLay0));
            return 0;
        }

        /// <summary>Точка, из которой выходит цепь: на спине — крепление рукояти, в руке — хват клипа.</summary>
        public Vector3 Exit(float T)
        {
            float wb = BackWeight(T);
            if (TLay0 >= 0 && T >= TLay0) return TL.Grip(T);          // клип уборки сам кладёт хват в крепление (кадры 4–8)
            return Vector3.Lerp(TL.Grip(T), BackExit(T), wb);
        }

        /// <summary>Рукоять для показа: h1 — выход цепи, h0 — конец у кисточки.</summary>
        public void Handle(float T, out Vector3 h0, out Vector3 h1)
        {
            h1 = Exit(T);
            Vector3 ax = TL.Axis(T), two = TL.LFist(T) - ax * .09f;
            Vector3 one = h1 - Vector3.Transform(_uGripLocal, TL.GripRot(T)) * .30f;
            float w2 = 1 - Smooth((TL.HandGap(T) - .22f) / .15f);
            Vector3 hand = Vector3.Lerp(one, two, w2);
            Vector3 back = h1 + Vector3.Transform(_uBackLocal, TL.Spine2Rot(T)) * .28f;
            h0 = Vector3.Lerp(hand, back, BackWeight(T));
        }

        /// <summary>Земля: плоскость 0 и воронка выпада (до 4,5 см глубиной, радиус 0,35 м).</summary>
        public float Ground(Vector3 p)
        {
            float d = new Vector2(p.X - ImpactPoint.X, p.Z - ImpactPoint.Z).Length();
            return d < .35f ? -.045f * (1 - d * d / (.35f * .35f)) : 0f;
        }

        public void Chest(float T, out Vector3 right, out Vector3 up, out Vector3 back)
        {
            Vector3 o = TL.Spine2(T); right = Vector3.Normalize(TL.Bone(T, "RightArm") - TL.Bone(T, "LeftArm"));
            up = TL.Bone(T, "Neck") - o; up = Vector3.Normalize(up - Vector3.Dot(up, right) * right);
            back = -Vector3.Cross(right, up);
        }

        /// <summary>Глубина самой нижней точки оболочки под центром при повороте q (положительная).</summary>
        public float CrownDepth(Quaternion q)
        {
            float lo = 0;
            foreach (var h in Head.HullLocal) lo = Math.Min(lo, Vector3.Transform(h, q).Y);
            return -lo;
        }

        /// <summary>Поворот в полёте/на земле: веретено к руке (цепь прямая), лапы горизонтально поперёк линии.</summary>
        public static Quaternion ChainFrame(Vector3 towardHand, Vector3 zPrev)
        {
            Vector3 y = Vector3.Normalize(towardHand), x = Vector3.Cross(y, Vector3.UnitY);
            if (x.LengthSquared() < 1e-6f) x = Vector3.UnitX;
            x = Vector3.Normalize(x);
            return Q.Frame(y, Vector3.Cross(x, y), zPrev);
        }
    }
}
