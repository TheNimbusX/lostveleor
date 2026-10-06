using System;
using System.Collections.Generic;
using System.Numerics;
using Game.View;

namespace Wreck4Anchor
{
    /// <summary>Кадр выхода (60/с), оси Unity.</summary>
    public struct Frame
    {
        public float T, Cable, Span, Speed, Pen; public string Mode, PenWhere; public Vector3 P, V, Ring, Grip, H0, H1; public Quaternion Rot; public bool Grounded;
        public Vector3[] Chain; public Quaternion Qc; public float Bow, Strain, CPen, Dev;     // слой «цепь-хлыст»
    }

    public sealed partial class Sim
    {
        public readonly List<Frame> Frames = new List<Frame>();
        public readonly Dictionary<string, string> Events = new Dictionary<string, string>();
        public Vector3[] HitGot, HitVel; public Vector3 ReleaseMace, ReleaseP, ImpactGot; public float StowExcess, ReelEndRel;
        const float Dt = 1f / 120f;

        static float MinJerk(float u) { u = Math.Clamp(u, 0, 1); return u * u * u * (10 - 15 * u + 6 * u * u); }

        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u, out Vector3 du)
        {
            float v = 1 - u;
            du = 3 * v * v * (b - a) + 6 * v * u * (c - b) + 3 * u * u * (d - c);
            return v * v * v * a + 3 * v * v * u * b + 3 * v * u * u * c + u * u * u * d;
        }

        static Vector3 Herm(Vector3 a, Vector3 va, Vector3 b, Vector3 vb, float d, float u, out Vector3 v)
        {
            float u2 = u * u, u3 = u2 * u;
            v = ((6 * u2 - 6 * u) * a + (3 * u2 - 4 * u + 1) * va * d + (-6 * u2 + 6 * u) * b + (3 * u2 - 2 * u) * vb * d) / d;
            return (2 * u3 - 3 * u2 + 1) * a + (u3 - 2 * u2 + u) * va * d + (-2 * u3 + 3 * u2) * b + (u3 - u2) * vb * d;
        }

        /// <summary>Дуга A → вершина B → C за D с: две эрмитовы части, время делится по длине, скорость в вершине — средняя
        /// вдоль хорды A→C (без излома). va, vc — скорости на концах (м/с), v — скорость (м/с).</summary>
        static Vector3 Arc3(Vector3 a, Vector3 va, Vector3 b, Vector3 c, Vector3 vc, float D, float u, out Vector3 v)
        {
            float l1 = Vector3.Distance(a, b), l2 = Vector3.Distance(b, c), tb = D * l1 / Math.Max(1e-4f, l1 + l2);
            Vector3 vb = Vector3.Normalize(c - a) * ((l1 + l2) / D);
            float t = u * D;
            return t < tb ? Herm(a, va, b, vb, tb, t / tb, out v) : Herm(b, vb, c, vc, D - tb, (t - tb) / (D - tb), out v);
        }

        static Vector3 Bez4(Vector3[] c, float u, out Vector3 du)
        {
            float v = 1 - u;
            du = 4 * (v * v * v * (c[1] - c[0]) + 3 * v * v * u * (c[2] - c[1]) + 3 * v * u * u * (c[3] - c[2]) + u * u * u * (c[4] - c[3]));
            return v * v * v * v * c[0] + 4 * v * v * v * u * c[1] + 6 * v * v * u * u * c[2] + 4 * v * u * u * u * c[3] + u * u * u * u * c[4];
        }

        public void Run()
        {
            Frames.Clear(); Events.Clear();
            if (WhipOn) { WhipClock.Reset(); _chain.Substeps = 0; _chain.CapsuleTests = 0; WhipMaxStrain = WhipMaxPen = WhipMaxDev = 0; WhipPenAt = WhipStrainAt = ""; }
            HitGot = new Vector3[Hits.Count]; HitVel = new Vector3[Hits.Count];
            var fol = new Follower(); Vector3 N = Vector3.UnitY, p = Vector3.Zero, v = Vector3.Zero, prevP = Vector3.Zero, Eprev = Exit(0);
            string mode = "OnBack"; float cable = L; bool[] hitDone = new bool[Hits.Count];
            AnchorRigCore core = null; Func<Vector3, float> ground = Ground;
            // снятие
            Vector3 dr0 = Vector3.Zero, d1 = Vector3.Zero, om1 = Vector3.Zero; Quaternion q0 = Quaternion.Identity, q1 = Quaternion.Identity;
            // уборка
            Vector3[] sp = new Vector3[5]; Vector3 sw0 = Vector3.Zero, landV = Vector3.Zero; Quaternion sq0 = Quaternion.Identity;
            bool ovBegun = false; Vector3 yPrev = Vector3.UnitY, yBite = Vector3.UnitY; BiteFirst = -1f; BiteTop = -1f; float rc0 = R, yMin0 = 0; Vector3 ys = Vector3.UnitY, ysw = Vector3.Zero;
            void Enter(string next, float T, Vector3 E)
            {
                Vector3 cr, cu, cb;
                switch (next)
                {
                    case "Draw":
                        dr0 = p; q0 = fol.Rot;
                        Chest(TDrawEnd, out cr, out cu, out cb);
                        d1 = Vector3.Normalize(Vector3.Transform(Mace.Rest(TDrawEnd), Quaternion.CreateFromAxisAngle(cr, DrawPitch * MathF.PI / 180f)));
                        om1 = Mace.RestRate(TDrawEnd + 1f / 60f) + DrawSpin * cr;   // скорость покоя со стороны маха (на шве кисть левой садится на рукоять) om1 -= Vector3.Dot(om1, d1) * d1;
                        N = om1.Length() > 1e-3f ? Vector3.Normalize(om1) : cr;
                        q1 = Q.Frame(-d1, N, N);
                        {   // лапы симметричны: из двух равных поз (поворот на 180° вокруг веретена) — ближняя к позе на спине
                            var alt = Quaternion.Normalize(q1 * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
                            if (Q.Angle(alt, q0) < Q.Angle(q1, q0)) q1 = alt;
                        }
                        Events["grab"] = N_(T); break;
                    case "Mace":
                        if (TGrab >= 0) { Mace.Start(d1, om1); Events["drawn"] = N_(T); }
                        break;
                    case "Flight":
                    {
                        ReleaseMace = v; ys = Q.Ax(fol.Rot, Vector3.UnitY); ysw = fol.W - Vector3.Dot(fol.W, ys) * ys; yPrev = ys; float Tf = TCon - T;
                        Vector3 v0 = HasV0 ? V0 : (ImpactCentre - p) / Tf + new Vector3(0, 4.905f * Tf, 0);
                        core = NewCore(6f); core.Teleport(AnchorRigMode.InHandLive, new AnchorPose(p, fol.Rot, v0, Vector3.Zero));
                        Events["release"] = $"{{\"t\": {N_(T)}, \"p\": {V_(p)}, \"vMace\": {V_(v)}, \"v0\": {V_(v0)}, \"jump\": {N_((v0 - v).Length())}, \"turnDeg\": {N_(Ang(v0, v))}}}";
                        break;
                    }
                    case "Bite":
                    {
                        ImpactGot = p + v * Dt; ImpactRot = fol.Rot; yBite = ys; Vector3 ring = p + Vector3.Transform(Head.EyeLocal, fol.Rot);
                        // венец врезается в грунт: вылет вперёд гасит укус (а не цепь — она остаётся чуть провисшей и голову не дёргает)
                        var c2 = NewCore(Vector3.Distance(ring + v * Dt, E) + .03f, BiteBounce);
                        c2.Teleport(AnchorRigMode.InHandLive, new AnchorPose(p, fol.Rot, core.Body.Velocity, Vector3.Zero)); core = c2;
                        Events["impact"] = $"{{\"t\": {N_(T)}, \"p\": {V_(ImpactGot)}, \"target\": {V_(ImpactCentre)}, \"miss\": {N_(Vector3.Distance(ImpactGot, ImpactCentre))}, \"speed\": {N_(v.Length())}, \"cable\": {N_(c2.CableLength)}}}";
                        break;
                    }
                    case "Reel":
                    {
                        rc0 = Vector3.Distance(p, E); Vector3 d = (p - E) / rc0;
                        Mace.Start(d, Vector3.Cross(d, v - TL.GripVelocity(T)) / rc0); yMin0 = p.Y;
                        Events["hold"] = $"{{\"t\": {N_(T)}, \"settleSpeed\": {N_(v.Length())}, \"bounceTop\": {N_(BiteTop - BiteFirst)}}}"; break;
                    }
                    case "Mace2":
                        ReelEndRel = (v - TL.GripVelocity(T)).Length(); Events["short"] = $"{{\"t\": {N_(T)}, \"relSpeed\": {N_(ReelEndRel)}}}"; break;
                    case "Stow":
                    {
                        // одна дуга: скорость булавы в начале, вершина над правым плечом, сверху вниз вдоль спины на крепление
                        float D = TLand - TStow0; Vector3 m = MountCentre(TLand);
                        Chest(TLand, out cr, out cu, out cb);
                        sp[0] = p; sp[1] = v; sp[2] = TL.Bone(TStow0 + D * .55f, "RightArm") + cu * StowUp + cr * StowOut + cb * StowBack;
                        sp[3] = m; sp[4] = -cu * StowLand; sq0 = fol.Rot; sw0 = fol.W;
                        // лапы симметричны: на крепление ложится та из двух равных поз, до которой ближе (меньше поворота)
                        MountFlip = Q.Angle(sq0, Quaternion.Normalize(MountRot(TLand) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI))) < Q.Angle(sq0, MountRot(TLand));
                        Events["stow"] = N_(T); break;
                    }
                    case "Settle":
                        Events["land"] = $"{{\"t\": {N_(T)}, \"speed\": {N_(landV.Length())}, \"rotErrDeg\": {N_(Q.Angle(fol.Rot, MountS(T)) * 57.2958f)}}}"; break;
                }
            }
            if (TGrab < 0) { mode = "Mace"; Mace.Start(Mace.Rest(0), Mace.RestRate(0)); N = Vector3.Normalize(Mace.Om + new Vector3(1e-4f, 0, 0)); fol.Reset(Q.Frame(-Mace.D, N, N), Mace.Om); }
            else fol.Reset(MountRot(0), Vector3.Zero);
            int steps = (int)MathF.Round(TEnd / Dt);
            for (int step = 0; step <= steps; step++)
            {
                float T = step * Dt; Vector3 E = Exit(T);
                bool first = step == 0;
                string next = Phase(T);
                if (next != mode || first) Enter(next, T, E);
                mode = next;
                switch (mode)
                {
                    case "OnBack":
                        p = MountCentre(T); fol.Reset(MountRot(T), Vector3.Zero); cable = L; break;
                    case "Draw":
                    {
                        float D = TDrawEnd - TGrab, u = (T - TGrab) / D;
                        // одна дуга в мире: со спины вверх через правое плечо, вперёд-вниз в начало маха 1 (скорость маха на конце)
                        Vector3 p3 = TL.Grip(TDrawEnd) + R * d1, v3 = TL.GripVelocity(TDrawEnd) + R * Vector3.Cross(om1, d1);
                        Chest(TGrab, out var cr, out var cu, out var cb);
                        // одна дуга через вершину над правым плечом (две эрмитовы части, скорость непрерывна), на конце — скорость маха 1
                        Vector3 apex = TL.Bone(TGrab + D * .45f, "RightArm") + cu * DrawLift + cr * ApexOut + cb * DrawBack;
                        p = Arc3(dr0, cb * DrawPeel + cu * DrawPeel * 1.5f, apex, p3, v3, D, u, out _);   // отрыв от спины: назад и вверх
                        // цепь лежит на плече (огибает тело) — не тянем голову сквозь корпус; запас цепи — в отчёт
                        DrawSpan = Math.Max(DrawSpan, Vector3.Distance(p + Vector3.Transform(Head.EyeLocal, fol.Rot), E));
                        var qq = Q.Hermite(q0, Vector3.Zero, q1, om1, D, u, out var ww);
                        fol.Reset(qq, ww); cable = L; break;
                    }
                    case "Mace":
                    case "Mace2":
                    {
                        bool win = false;
                        for (int i = 0; i < Hits.Count; i++)
                        {
                            if (Math.Abs(T - Hits[i].T) <= 1f / 60f + 1e-4f) win = true;
                            if (!hitDone[i] && T >= Hits[i].T - 1e-5f)
                            {
                                hitDone[i] = true; HitGot[i] = E + Mace.D * R; HitVel[i] = v; Mace.Impact();
                                Events["lag" + i] = $"{{\"vsHandleDeg\": {N_(Ang(Mace.D, TL.Axis(T)))}, \"vsRestDeg\": {N_(Ang(Mace.D, Vector3.Normalize(TL.Axis(T) - Mace.Droop * Vector3.UnitY)))}}}";
                            }
                        }
                        if (Mace.OvT1 > 0 && T >= Mace.OvT0 && !ovBegun) { Mace.BeginOverride(); ovBegun = true; }
                        Mace.HangW = HangW(T); Mace.HangDrag = HangAir + HangKill * MathF.Exp(-Math.Max(0, T - THang) / .25f);
                        Mace.Step(T, Dt, R, win, Soft(T));
                        if (Mace.HangW > 0) { Mace.KeepAbove(E, R, Ground(E + Mace.D * R) + CrownDepth(fol.Rot) + .01f); HeadBodyPush(E, R, fol.Rot, T); }
                        p = E + Mace.D * R; cable = L;
                        N = PlaneNormal(N, Mace.Om, fol.Rot);
                        fol.Transport(-Mace.D, Mace.HangW > .5f ? Vector3.Zero : N, RollRate, Dt);   // висит: крен не ведём (не крутится на цепи)
                        break;
                    }
                    case "Flight":
                    case "Bite":
                    {
                        // ФИЗИКА: тело головы — ядро игры (тяжесть, сопротивление, земля, цепь); поворот — управляемый
                        core.Body.Rotation = fol.Rot; core.Body.AngularVelocity = mode == "Flight" ? fol.W : Vector3.Zero;
                        core.StepLive(Dt, Eprev, E, mode == "Flight" && T < TCon - Dt / 2 ? NoGround : ground, null, 0);
                        p = core.Body.Position; Vector3 vb = core.Body.Velocity;
                        // веретено: в полёте венцом вперёд (кольцо за головой, вдоль скорости), в воронке — кольцом к руке
                        // в воронке поворот держится (венец сидит в грунте — голову не проворачиваем, иначе она «выкапывается»)
                        Vector3 yRaw = mode == "Flight" ? (vb.Length() > 2f ? -vb : ys) : yBite;
                        if (mode == "Bite")
                        {   // оседание: голова чуть качается на венце и успокаивается (затухающий наклон 6°, без замирания)
                            float tb = T - TCon, ang = 6f * MathF.PI / 180f * MathF.Exp(-tb / .07f) * MathF.Sin(2 * MathF.PI * 9f * tb);
                            Vector3 ax = Vector3.Cross(yBite, Vector3.UnitY);
                            if (ax.LengthSquared() > 1e-6f) yRaw = Vector3.Transform(yBite, Quaternion.CreateFromAxisAngle(Vector3.Normalize(ax), ang));
                        }
                        ShankSpring(ref ys, ref ysw, ref yPrev, yRaw, 22f, Dt);
                        fol.Transport(ys, Vector3.Zero, RollRate, Dt);          // крен не трогаем: только перенос веретена
                        Vector3 ring = p + Vector3.Transform(Head.EyeLocal, fol.Rot);
                        cable = mode == "Flight" ? Math.Max(L, Vector3.Distance(ring, E)) : core.CableLength;   // выдача: цепь не короче L (сразу после выпуска — слабина)
                        if (mode == "Bite")
                        {   // УПРАВЛЯЕМО: укус венца — грунт держит голову в воронке (горизонталь гасится, к центру воронки);
                            // вертикаль (удар, отскок, оседание) остаётся физикой ядра
                            Vector3 hv = new Vector3(vb.X, 0, vb.Z), to = new Vector3(ImpactCentre.X - p.X, 0, ImpactCentre.Z - p.Z);
                            core.Body.Velocity -= hv * (1 - MathF.Exp(-BiteDamp * Dt)) - to * (BiteDamp * BiteDamp * .25f * Dt);
                            if (BiteFirst < -.5f)
                            {   // первый шаг в грунте: венец врезался — вылет вперёд гасится укусом (цепь не дёргает голову)
                                BiteFirst = p.Y; Vector3 vk = core.Body.Velocity;
                                core.Body.Velocity = new Vector3(vk.X * BiteKeep, vk.Y, vk.Z * BiteKeep);
                            }
                            BiteTop = Math.Max(BiteTop, p.Y);
                        }
                        break;
                    }
                    case "Reel":
                    {
                        float u = (T - THold) / (TShort - THold), Rc = rc0 + (R - rc0) * MinJerk(u), ell = Rc - Eye;
                        Mace.Step(T, Dt, Rc, false, .7f);
                        Mace.KeepAbove(E, Rc, Math.Min(.32f, yMin0 + (T - THold) * 2f));
                        ShankSpring(ref ys, ref ysw, ref yPrev, -Mace.D, 30f, Dt);
                        p = E + Mace.D * Rc; cable = ell;
                        Vector3 zf = Q.Ax(ChainFrame(-Mace.D, Q.Ax(fol.Rot, Vector3.UnitZ)), Vector3.UnitZ);
                        N = zf;
                        fol.Transport(ys, Vector3.Zero, RollRate, Dt);
                        break;
                    }
                    case "Stow":
                    {
                        float D = TLand - TStow0, u = (T - TStow0) / D;
                        p = Arc3(sp[0], sp[1], sp[2], sp[3], sp[4], D, u, out var dp);
                        var qq = Q.Hermite(sq0, sw0, MountS(TLand), Vector3.Zero, D, u, out var ww);
                        fol.Reset(qq, ww);
                        Vector3 ring = p + Vector3.Transform(Head.EyeLocal, qq);
                        float ex = Vector3.Distance(ring, E) - L;
                        if (ex > 0) StowExcess = Math.Max(StowExcess, ex);      // цепь огибает плечо: голову сквозь тело не тянем, запас — в отчёт
                        landV = dp; cable = L; break;
                    }
                    case "Settle":
                    {
                        float t = T - TLand, w = 38f, z = .35f, wd = w * MathF.Sqrt(1 - z * z);
                        Chest(T, out var cr, out var cu, out var cb);
                        Vector3 vin = landV - (MountCentre(TLand + 1f / 240f) - MountCentre(TLand - 1f / 240f)) * 120f;
                        Vector3 off = vin * (MathF.Exp(-z * w * t) * MathF.Sin(wd * t) / wd);
                        if (off.Length() > .04f) off *= .04f / off.Length();
                        p = MountCentre(T) + off;
                        float tilt = Vector3.Dot(off, cu) / .35f;
                        fol.Reset(Quaternion.Normalize(Quaternion.CreateFromAxisAngle(cr, tilt) * MountS(T)), Vector3.Zero);
                        cable = L; break;
                    }
                }
                if (WhipOn) WhipStep(T, mode, E, p, fol.Rot, first);
                v = first ? Vector3.Zero : (p - prevP) / Dt; prevP = p;
                if (T >= TRel - 1e-5f && T < TRel + Dt - 1e-5f) ReleaseP = p;
                Eprev = E;
                if (step % 2 == 0) Record(T, mode, p, v, fol.Rot, E, cable, core);
            }
        }

        /// <summary>После удара и до замаха следующего — мягкая пружина: голова не уходит за рукоятью далеко назад.</summary>
        float Soft(float T)
        {
            for (int i = 0; i < Hits.Count; i++)
            {
                float a = Hits[i].T + 2f / 30f, b = i + 1 < Hits.Count ? Hits[i + 1].T - SoftEnd / 30f : (Mace.OvT0 > 0 ? Mace.OvT0 : TRel);
                if (T > a && T < b) return SoftScale + (1 - SoftScale) * (1 - Mace.Bump(T, (a + b) / 2, (b - a) / 2, (b - a) / 2));
            }
            return 1f;
        }

        static float NoGround(Vector3 p) => -100f;

        public bool MountFlip; public float DrawSpan;
        Quaternion MountS(float T) => MountFlip ? Quaternion.Normalize(MountRot(T) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI)) : MountRot(T);

        /// <summary>Направление веретена — критически демпфированно к цели по кратчайшей дуге, ≤ 20 рад/с.</summary>
        static void ShankSpring(ref Vector3 y, ref Vector3 w, ref Vector3 prev, Vector3 target, float omega, float dt)
        {
            target = Vector3.Normalize(target);
            Vector3 wt = Q.Log(Q.Arc(prev, target)) / dt; prev = target;       // упреждение: скорость цели (без отставания)
            if (wt.Length() > 30f) wt *= 30f / wt.Length();
            Vector3 e = Q.Log(Q.Arc(y, target));
            w += (omega * omega * e + 2 * omega * (wt - w)) * (dt / (1 + 2 * omega * dt + omega * omega * dt * dt));
            w -= Vector3.Dot(w, y) * y;
            if (w.Length() > 20f) w *= 20f / w.Length();
            y = Vector3.Normalize(Vector3.Transform(y, Q.Exp(w * dt)));
        }

        string Phase(float T)
        {
            if (TGrab >= 0 && T < TGrab) return "OnBack";
            if (TGrab >= 0 && T < TDrawEnd) return "Draw";
            if (TRel >= 0 && T >= TRel && T < TCon) return "Flight";
            if (TRel >= 0 && T >= TCon && T < THold) return "Bite";
            if (TRel >= 0 && T >= THold && T < TShort) return "Reel";
            if (TStow0 >= 0 && T >= TLand) return "Settle";
            if (TStow0 >= 0 && T >= TStow0) return "Stow";
            return TRel >= 0 && T >= TShort ? "Mace2" : "Mace";
        }

        /// <summary>Нормаль плоскости маха: к направлению угловой скорости цепи (знак любой — лапы симметричны), плавно,
        /// и только когда голова реально идёт по дуге; иначе держится прошлая.</summary>
        static Vector3 PlaneNormal(Vector3 N, Vector3 om, Quaternion rot)
        {
            float w = om.Length(), k = Math.Clamp((w - 4f) / 6f, 0, 1);
            if (k <= 0) return N;
            Vector3 n = om / w; if (Vector3.Dot(n, N) < 0) n = -n;
            return Vector3.Normalize(Vector3.Lerp(N, n, Math.Min(1f, 12f * k * Dt)));
        }
    }
}
