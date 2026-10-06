using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace AnchorBake
{
    public sealed class CheckRow
    {
        public string Id, Name, Value, Target, Where;
        public bool? Pass;   // null = information only
    }

    /// <summary>DESIGN 5 offline checks (o) on one bake, per kind (swing, slam/release, loop, windup) + the critic's targets.
    /// Times are real seconds from StageStartTick; "frame" columns are 60 fps render frames unless marked tick.</summary>
    public sealed class Validation
    {
        public readonly List<CheckRow> Rows = new List<CheckRow>();
        public double MaxStretch, ContactError, ContactRadius, ContactHeight, ContactAngle, ContactSpeed, MaxSpeed,
            MaxJump, MaxStep, GripJump, Penetration, ChainClearance = double.MaxValue, MaxSlackFast, MaxTension,
            GuidePeak, GuideDeltaV, GroundTime, FirstGroundTick = -1, LoopDp, LoopDv, ReleaseAngle;
        public int SlackFastFrames, FastFrames, JumpFrame, PenetrationFrame, SlackFrame;
        public string PenetrationWhere = "", ClearanceWhere = "";
        public bool AllPass = true;
        static readonly CultureInfo I = CultureInfo.InvariantCulture;
        public static string N(double v, string f = "0.000") => v.ToString(f, I);

        public void Add(string id, string name, string value, string target, bool? pass, string where = "")
        {
            Rows.Add(new CheckRow { Id = id, Name = name, Value = value, Target = target, Pass = pass, Where = where });
            if (pass == false) AllPass = false;
        }

        public static Validation Run(BakeRun run, GripTrack track, HeadModel head, ExternalChecks ext)
        {
            var v = new Validation();
            var cfg = run.Cfg; float L = cfg.ChainLength;
            var body = new Body(track);
            var probe = head.NewBody(cfg.Mass, cfg.Inertia);
            int frames = (int)Math.Floor(run.Records[^1].T * 60 + 1e-4);
            var p60 = new List<Rec>();
            for (int k = 0; k <= frames; k++) p60.Add(BakeSim.At(run, k / 60f));
            for (int k = 1; k + 1 < p60.Count; k++)
            {
                double j = (p60[k + 1].P - 2 * p60[k].P + p60[k - 1].P).Length();
                if (j > v.MaxJump) { v.MaxJump = j; v.JumpFrame = k; }
                v.GripJump = Math.Max(v.GripJump, (p60[k + 1].Grip - 2 * p60[k].Grip + p60[k - 1].Grip).Length());
            }
            for (int k = 1; k < p60.Count; k++) v.MaxStep = Math.Max(v.MaxStep, (p60[k].P - p60[k - 1].P).Length());
            float dt = run.Records.Count > 1 ? run.Records[1].T - run.Records[0].T : 1 / 120f;
            float sinceGround = 1f;
            bool wasGrounded = true;
            for (int i = 0; i < run.Records.Count; i++)
            {
                var r = run.Records[i];
                sinceGround = r.Grounded ? 0 : sinceGround + dt;
                // Удар оземь = посадка: голова была в воздухе и коснулась (лежавшая на земле с начала — не удар).
                if (r.Grounded && !wasGrounded && v.FirstGroundTick < 0 && r.Tick >= cfg.ContactFrame - 3) v.FirstGroundTick = r.Tick;
                wasGrounded = r.Grounded;
                v.MaxStretch = Math.Max(v.MaxStretch, r.Span - L);
                v.MaxSpeed = Math.Max(v.MaxSpeed, r.V.Length());
                v.MaxTension = Math.Max(v.MaxTension, r.Tension);
                if (r.Grounded) v.GroundTime += dt;
                float rel = (r.RingV - r.GripV).Length();
                // §5 #10: not counted 2 render frames after a ground touch (bounce) and while the head leaves the back mount.
                if (rel > 8f && !r.Settling && sinceGround > 2f / 60f)
                {
                    v.FastFrames++;
                    float slack = L - r.Span;
                    if (slack > .02f) { v.SlackFastFrames++; if (slack > v.MaxSlackFast) { v.MaxSlackFast = slack; v.SlackFrame = (int)Math.Round(r.T * 60); } }
                }
                if (r.Settling) continue;   // "кроме кадров на спине" (§5 #2)
                var anatomy = body.Anatomy(r.Frame);
                double pen = HeadContact.Penetration(probe, r.P, r.Q, anatomy, out string where);
                if (pen > v.Penetration) { v.Penetration = pen; v.PenetrationWhere = where; v.PenetrationFrame = (int)Math.Round(r.T * 60); }
                if (r.Taut)
                {
                    Vector3 dir = Vector3.Normalize(r.Ring - r.Grip);
                    Vector3 from = r.Grip + dir * .15f;
                    foreach (var c in anatomy)
                    {
                        double cl = c.SegmentClearance(from, r.Ring);
                        if (cl < v.ChainClearance) { v.ChainClearance = cl; v.ClearanceWhere = c.Name + " @f" + Math.Round(r.T * 60); }
                    }
                }
            }
            v.SlackFastFrames = (int)Math.Round(v.SlackFastFrames * dt * 60); v.FastFrames = (int)Math.Round(v.FastFrames * dt * 60);
            var at = BakeSim.At(run, run.ContactTime);
            v.ContactRadius = Math.Sqrt(at.P.X * at.P.X + at.P.Z * at.P.Z);
            v.ContactHeight = at.P.Y - cfg.Ground;
            v.ContactAngle = Math.Atan2(at.P.X, at.P.Z) * 180 / Math.PI;
            v.ContactSpeed = at.V.Length();
            float T = cfg.GuideTicks / (track.Fps * cfg.ClipRate);
            v.GuidePeak = cfg.GuideAccel.Length() * 30.0 / 16.0;   // max of 30u^2(1-u)^2 = 30/16
            v.GuideDeltaV = cfg.GuideAccel.Length() * T;

            bool animOk = ext.AnimSocket >= 0 && ext.AnimSocket <= .005 && ext.AnimAngle <= 1.0;
            v.Add("1a", "Хват: экспорт против клипа Unity (.anim, FK)", ext.AnimSocket < 0 ? "нет" : N(ext.AnimSocket * 1000, "0.0") + " мм / " + N(ext.AnimAngle, "0.0") + "°",
                "≤ 5 мм, ≤ 1° (§3.5)", ext.AnimSocket < 0 ? (bool?)null : animOk, ext.AnimNote);
            v.Add("1b", "Хват: экспорт против кисти в игре (съёмка)", ext.CaptureMax < 0 ? "нет" : "rms " + N(ext.CaptureRms * 100, "0.0") + " / max " + N(ext.CaptureMax * 100, "0.0") + " см",
                "≤ 1 см", ext.CaptureMax < 0 ? (bool?)null : ext.CaptureMax <= .01, ext.CaptureNote);
            v.Add("1c", "Хват в запечке: начало цепи = гнездо кисти", "0,0 см (по построению)", "≤ 1 мм", true, "цепь берёт хват из того же трека");
            v.Add("2", "Проникновение головы (68 точек) в тело", N(v.Penetration * 100, "0.0") + " см", "≤ 1 см", v.Penetration <= .01,
                v.Penetration > 0 ? v.PenetrationWhere + " @f" + v.PenetrationFrame : "");
            v.Add("3", "Натянутая цепь сквозь тело (зазор)", v.ChainClearance == double.MaxValue ? "натяга нет" : N(v.ChainClearance * 100, "0.0") + " см",
                "≥ −1 см", v.ChainClearance == double.MaxValue ? (bool?)null : v.ChainClearance >= -.01, v.ClearanceWhere);
            v.Add("4", "Растяжение цепи (кольцо–хват − L)", N(v.MaxStretch * 1000, "0.0") + " мм", "≤ 5 мм", v.MaxStretch <= .005);
            KindRows(v, run, body, at);
            v.Add("7", "Наведение (последние 3 тика)", cfg.GuideAccel == Vector3.Zero ? "не включено" : "пик " + N(v.GuidePeak, "0") + " м/с², ΔV " + N(v.GuideDeltaV, "0.0") + " м/с",
                "≤ 40 м/с², ≤ 3 м/с", cfg.GuideAccel == Vector3.Zero ? (bool?)null : v.GuidePeak <= 40 && v.GuideDeltaV <= 3);
            v.Add("8", "Скачок за кадр 60 к/с (2-я разность пути головы)", N(v.MaxJump, "0.000") + " м", "< 0,15 м", v.MaxJump < .15,
                "@f" + v.JumpFrame + "; макс. шаг " + N(v.MaxStep, "0.00") + " м/кадр; кисть клипа " + N(v.GripJump, "0.000") + " м");
            v.Add("10", "Провис на скорости (> 8 м/с отн. хвата)", v.SlackFastFrames + " из " + v.FastFrames + " кадров, макс " + N(v.MaxSlackFast * 100, "0") + " см",
                "провис < 2 см", v.SlackFastFrames == 0, v.SlackFastFrames > 0 ? "@f" + v.SlackFrame : "");
            v.Add("i1", "Натяжение цепи, пик", N(v.MaxTension, "0") + " Н", "—", null);
            v.Add("i2", "Касания тела (выталкивание физикой)", run.BodyContacts + " кадров, макс " + N(run.MaxBodyPush * 100, "0.0") + " см", "—", null);
            v.Add("i3", "Голова на земле / отскоков", N(v.GroundTime, "0.00") + " с / " + run.Bounces, "—", null);
            v.Add("16", "Цена живой физики офлайн (AnchorRigCore.StepLive)", N(run.MillisecondsPerFrame60, "0.000") + " мс/кадр", "≤ 0,15 мс", run.MillisecondsPerFrame60 <= .15);
            return v;
        }

        static void KindRows(Validation v, BakeRun run, Body body, Rec at)
        {
            var cfg = run.Cfg;
            string ct = "тик " + N(cfg.ContactFrame, "0.#");
            switch (cfg.Kind)
            {
                case BakeKind.Swing:
                    v.ContactError = (BakeSim.NearestValid(at.P) - at.P).Length();
                    v.Add("5", "Точка контакта в " + ct + ": до дуги 2,2–2,7 м / h 0,5–0,9 / ±10°", N(v.ContactError, "0.00") + " м", "≤ 0,15 м", v.ContactError <= .15,
                        "r " + N(v.ContactRadius, "0.00") + " м, h " + N(v.ContactHeight, "0.00") + " м, угол " + N(v.ContactAngle, "0") + "°");
                    v.Add("6", "Скорость головы в контакте", N(v.ContactSpeed, "0.0") + " м/с", "20–30 м/с", v.ContactSpeed >= 20 && v.ContactSpeed <= 30,
                        "пик за мах " + N(v.MaxSpeed, "0.0") + " м/с");
                    break;
                case BakeKind.Slam:
                case BakeKind.Release:
                    float miss = new Vector2(at.P.X - cfg.Impact.X, at.P.Z - cfg.Impact.Z).Length();
                    double tickError = v.FirstGroundTick < 0 ? 99 : v.FirstGroundTick - cfg.ContactFrame;
                    v.ContactError = miss + (v.FirstGroundTick < 0 ? 1 : Math.Abs(tickError) * .1);
                    v.Add("5", "Удар оземь в " + ct + ": центр головы над точкой Sim", N(miss, "0.00") + " м", "≤ 0,15 м", miss <= .15,
                        "точка Sim (" + N(cfg.Impact.X, "0.00") + "; " + N(cfg.Impact.Z, "0.00") + "), голова (" + N(at.P.X, "0.00") + "; " + N(at.P.Z, "0.00") + "), h " + N(v.ContactHeight, "0.00") + " м");
                    v.Add("5t", "Посадка головы на землю (из воздуха)", v.FirstGroundTick < 0 ? "нет посадки" : "тик " + N(v.FirstGroundTick, "0.00"), ct + " ± 0,5", v.FirstGroundTick >= 0 && Math.Abs(tickError) <= .5);
                    var before = BakeSim.At(run, Math.Max(0, (float)(v.FirstGroundTick < 0 ? cfg.ContactFrame : v.FirstGroundTick) / 30f - 1f / 120f));
                    v.ContactSpeed = before.V.Length();
                    v.Add("6", "Скорость головы в касании", N(v.ContactSpeed, "0.0") + " м/с", "16–28 м/с", v.ContactSpeed >= 16 && v.ContactSpeed <= 28);
                    if (cfg.OverheadFrame > 0)
                    {
                        var o = BakeSim.At(run, cfg.OverheadFrame / 30f);
                        double above = o.P.Y - body.HeadTopAt(o.Frame).Y;
                        v.Add("5o", "Над головой в тик " + N(cfg.OverheadFrame, "0"), N(above, "0.00") + " м над макушкой", "> 0", above > 0);
                    }
                    break;
                case BakeKind.Loop:
                    var first = run.Records[0]; var last = run.Records[^1];
                    v.LoopDp = (last.P - first.P).Length(); v.LoopDv = (last.V - first.V).Length();
                    v.ContactError = v.LoopDp;
                    v.Add("13", "Цикл: первый = последний сэмпл", N(v.LoopDp * 1000, "0.0") + " мм / " + N(v.LoopDv, "0.00") + " м/с", "≤ 5 мм, ≤ 0,3 м/с", v.LoopDp <= .005 && v.LoopDv <= .3,
                        "прогон " + cfg.Cycles + " оборотов, записан последний");
                    double sumY = 0, sumV = 0;
                    foreach (var r in run.Records) { sumY += r.P.Y - body.HeadTopAt(r.Frame).Y; sumV += r.V.Length(); }
                    v.Add("i4", "Круг над головой: высота над макушкой / скорость", N(sumY / run.Records.Count, "0.00") + " м / " + N(sumV / run.Records.Count, "0.0") + " м/с", "—", null);
                    break;
                case BakeKind.Windup:
                    var rel = BakeSim.At(run, cfg.ReleaseFrame / 30f);
                    v.ReleaseAngle = rel.V.Length() < 1e-3f ? 180 : Math.Acos(Math.Clamp(rel.V.Z / rel.V.Length(), -1, 1)) * 180 / Math.PI;
                    v.ContactError = v.ReleaseAngle / 100.0;
                    v.Add("5w", "Выпуск в тик " + N(cfg.ReleaseFrame, "0.#") + ": скорость головы вдоль Direction", N(v.ReleaseAngle, "0") + "°", "≤ 15°", v.ReleaseAngle <= 15,
                        "|v| " + N(rel.V.Length(), "0.0") + " м/с");
                    break;
            }
        }
    }

    public sealed class ExternalChecks
    {
        public double AnimSocket = -1, AnimAngle = 0, CaptureRms = -1, CaptureMax = -1;
        public string AnimNote = "", CaptureNote = "";
    }
}
