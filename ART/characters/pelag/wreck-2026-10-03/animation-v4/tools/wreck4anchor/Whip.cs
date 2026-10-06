using System;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using AnchorBake;

namespace Wreck4Anchor
{
    /// <summary>
    /// Слой «цепь-хлыст» (06.10, по Клинкам Хаоса): путь центра головы — прежний (управляемый, контакты те же),
    /// сверху — вторичное движение поворота головы (Secondary) и всегда живая цепь (WhipChain) с длиной по фазам.
    /// Включается ключом "whip" в плане; без него выход совпадает с прошлой версией (проверка регрессии).
    /// </summary>
    public sealed partial class Sim
    {
        public bool WhipOn; public int WhipLinks = 16, WhipSub = 2;
        public float WSwing = .53f, WFlight = .3f, WBite = .45f, WTake = .06f, WVmax = 40f, WSnap = .85f, WRelease = .025f;
        public float THang = -1, HangBlend = .2f, HangAir = .8f, HangKill = 7f, HangSlack = .015f;
        Secondary.Tune _tMace, _tFlight, _tBite;
        WhipChain _chain; readonly Secondary _sec = new Secondary();
        Quaternion _qd; Vector3 _ringD, _Eprev, _ringPrev; float _L, _s0, _sReel0, _Limp; string _wMode;
        readonly Capsule[] _caps = new Capsule[16];
        public readonly Stopwatch WhipClock = new Stopwatch(), SolverClock = new Stopwatch();
        public float WhipMaxStrain, WhipMaxPen, WhipMaxDev; public string WhipPenAt = "", WhipStrainAt = "";

        void WhipConfig()
        {
            var w = Opt("whip");
            WhipOn = w.ValueKind == JsonValueKind.Object;
            if (!WhipOn) return;
            WhipLinks = (int)Timeline.F(w, "links", 16); WhipSub = (int)Timeline.F(w, "sub", 2);
            WSwing = Timeline.F(w, "swing", WSwing); WFlight = Timeline.F(w, "flightSlack", WFlight); WBite = Timeline.F(w, "biteSlack", WBite);
            WTake = Timeline.F(w, "take", WTake); WSnap = Timeline.F(w, "snap", WSnap); WRelease = Timeline.F(w, "release", WRelease);
            WVmax = Timeline.F(w, "vmax", WVmax);
            _chain = new WhipChain(WhipLinks)
            {
                Drag = Timeline.F(w, "drag", 1.2f), Iterations = (int)Timeline.F(w, "iters", 12), Fold = Timeline.F(w, "fold", .55f),
                GripAim = Timeline.F(w, "gripAim", .3f), Friction = Timeline.F(w, "friction", 18f)
            };
            float deg = MathF.PI / 180f;
            _tMace = new Secondary.Tune { W = Timeline.F(w, "secW", 16), Z = Timeline.F(w, "secZ", .42f), F = Timeline.F(w, "secF", .75f),
                                          RollW = Timeline.F(w, "rollW", 12), RollZ = Timeline.F(w, "rollZ", .5f), RollF = Timeline.F(w, "rollF", .8f),
                                          MaxDev = Timeline.F(w, "maxDev", 28) * deg, MaxW = Timeline.F(w, "secMaxW", 35) };
            _tFlight = new Secondary.Tune { W = 20, Z = .45f, F = .8f, RollW = 14, RollZ = _tMace.RollZ, RollF = .85f, MaxDev = 20 * deg, MaxW = _tMace.MaxW };
            _tBite = new Secondary.Tune { W = 45, Z = .3f, F = .9f, RollW = 30, RollZ = .4f, RollF = .9f, MaxDev = 8 * deg };
            var hg = Opt("hang");
            if (hg.ValueKind == JsonValueKind.Object)
            {
                THang = At(hg, "start"); HangBlend = Timeline.F(hg, "blend", HangBlend); HangAir = Timeline.F(hg, "air", HangAir);
                HangKill = Timeline.F(hg, "kill", HangKill); HangSlack = Timeline.F(hg, "slack", HangSlack);
            }
        }

        public float HangW(float T) => THang < 0 ? 0 : Smooth((T - THang) / HangBlend);

        /// <summary>Доля вторичного движения: на спине 0, снятие — нарастает, уборка — спадает к посадке.</summary>
        float SecWeight(float T, string mode)
        {
            switch (mode)
            {
                case "OnBack": case "Settle": return 0;
                case "Draw": return Smooth((T - TGrab) / Math.Max(1e-3f, (TDrawEnd - TGrab) * .6f));
                case "Stow": return 1 - Smooth((T - TStow0) / Math.Max(1e-3f, TLand - TStow0 - .05f));
                default: return 1;
            }
        }

        Secondary.Tune SecTune(float T, string mode)
        {
            if (mode == "Bite") return _tBite;
            if (mode != "Flight") return _tMace;
            float k = Smooth((T - (TCon - .06f)) / .06f);       // к удару — жёстче: венец входит в грунт как у управляемого
            var t = _tFlight; t.W += (_tBite.W - t.W) * k; t.MaxDev += (_tBite.MaxDev - t.MaxDev) * k;
            return t;
        }

        /// <summary>Длина цепи по фазам (м). chord — расстояние кулак–кольцо сейчас.</summary>
        float WhipTarget(float T, string mode, float chord)
        {
            switch (mode)
            {
                case "OnBack": case "Settle": return Math.Max(WSwing, chord * 1.1f + .02f);
                case "Draw": case "Stow": return Math.Max(WSwing, chord * 1.12f + .03f);
                case "Flight":
                {   // выдача: цепь отстаёт дугой (слабина растёт до flightSlack), к удару выбирается в ноль — щелчок натяга
                    float u = (T - TRel) / (TCon - TRel);
                    return chord + (_s0 + (WFlight - _s0) * Smooth(u / .35f)) * (1 - Smooth((u - .4f) / (WSnap - .4f)));
                }
                case "Bite": return _Limp + WBite * (1 - MathF.Exp(-(T - TCon) / WRelease));   // рука сразу отдаёт ещё — цепь по инерции падает на землю
                case "Reel":
                {   // рывок: слабина выбирается за take с, дальше цепь идёт за головой и к концу — длина маха
                    float u = (T - THold) / (TShort - THold);
                    return chord + _sReel0 * (1 - Smooth((T - THold) / WTake)) + (WSwing - L) * Smooth(u) * Smooth(u);
                }
                default:
                {
                    float sw = Math.Max(WSwing, chord + .03f), hw = HangW(T);
                    return hw > 0 ? sw + (chord + HangSlack - sw) * hw : sw;
                }
            }
        }

        void WhipStep(float T, string mode, Vector3 E, Vector3 p, Quaternion qc, bool first)
        {
            if (first) { SolverClock.Reset(); }
            if (first || mode == "OnBack" || mode == "Settle") _sec.Reset(qc);
            else _sec.Step(qc, Dt, SecTune(T, mode));
            WhipMaxDev = Math.Max(WhipMaxDev, _sec.LastDev);
            _qd = _sec.Show(qc, SecWeight(T, mode));
            _ringD = p + Vector3.Transform(Head.EyeLocal, _qd);
            float chord = Vector3.Distance(_ringD, E);
            if (first)
            {
                _L = WhipTarget(T, mode, chord); _wMode = mode;
                _chain.Seed(E, _ringD, _L);
                _Eprev = E; _ringPrev = _ringD;
                PrepCaps(T, p);
                for (int k = 0; k < 120; k++) _chain.Step(1f / 240f, E, _ringD, HandleAxis(T), _caps, 13, Mask(E), Mask(_ringD), Ground);
            }
            if (mode != _wMode)
            {
                if (mode == "Flight") _s0 = _L - chord;
                if (mode == "Bite") _Limp = Math.Max(_L, chord);
                if (mode == "Reel") _sReel0 = Math.Max(0, _L - chord);
                _wMode = mode;
            }
            float target = WhipTarget(T, mode, chord), dl = Math.Clamp(target - _L, -WVmax * Dt, WVmax * Dt);
            _L = Math.Max(_L + dl, chord + .002f);
            if (first) return;
            WhipClock.Start();
            PrepCaps(T, p);
            Vector3 ax = HandleAxis(T); uint mA = Mask(E), mB = Mask(_ringD);
            for (int s = 1; s <= WhipSub; s++)
            {
                float u = s / (float)WhipSub;
                _chain.Length = _L;
                SolverClock.Start();
                _chain.Step(Dt / WhipSub, Vector3.Lerp(_Eprev, E, u), Vector3.Lerp(_ringPrev, _ringD, u), ax, _caps, 13, mA, mB, Ground);
                SolverClock.Stop();
            }
            WhipClock.Stop();
            _Eprev = E; _ringPrev = _ringD;
            if (_chain.MaxStrain > WhipMaxStrain) { WhipMaxStrain = _chain.MaxStrain; WhipStrainAt = $"{mode} t={T:0.000}"; }
            if (_chain.MaxPen > WhipMaxPen) { WhipMaxPen = _chain.MaxPen; WhipPenAt = $"{mode} t={T:0.000} {_caps[_chain.MaxPenCap].Name} node {_chain.MaxPenNode}"; }
        }

        Vector3 HandleAxis(float T)
        {
            Handle(T, out var h0, out var h1);
            Vector3 d = h1 - h0; return d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : Vector3.Zero;
        }

        /// <summary>Капсулы: 12 анатомических тела + веретено головы (от венца до кольца, без самого кольца).</summary>
        void PrepCaps(float T, Vector3 p)
        {
            var an = TL.Anatomy(T);
            for (int i = 0; i < 12; i++) _caps[i] = an[i];
            Vector3 y = Vector3.Transform(Vector3.UnitY, _qd);
            _caps[12] = new Capsule("shank", p - y * .33f, p + y * .2f, .065f);
        }

        uint Mask(Vector3 x) => _chain.Inside(x, _caps, 13);

        /// <summary>Висящая голова и тело: оболочка (точки Tripo) не входит в анатомические капсулы — центр выталкивается
        /// по нормали самой глубокой точки (3 прохода), скорость внутрь гасится с отскоком e. Возвращает глубину до правки.</summary>
        public float HeadBodyPush(Vector3 E, float R, Quaternion rot, float T, float e = .2f)
        {
            var body = TL.Anatomy(T); float first = -1;
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 p = E + Mace.D * R, nBest = Vector3.Zero; float dBest = 0;
                foreach (var h in Head.HullLocal)
                {
                    Vector3 x = p + Vector3.Transform(h, rot);
                    foreach (var c in body)
                    {
                        if (c.Name.Contains("forearm")) continue;              // кисти держат рукоять — их капсулы у самой цепи
                        float d = c.Depth(x, out var n);
                        if (d > dBest) { dBest = d; nBest = n; }
                    }
                }
                if (first < 0) first = dBest;
                if (dBest <= 1e-4f) break;
                Mace.D = Vector3.Normalize(p + nBest * dBest - E);
                Vector3 v = Vector3.Cross(Mace.Om, Mace.D) * R; float vn = Vector3.Dot(v, nBest);
                if (vn < 0) { v -= (1 + e) * vn * nBest; Mace.Om = Vector3.Cross(Mace.D, v) / R; }
                Mace.Om -= Vector3.Dot(Mace.Om, Mace.D) * Mace.D;
            }
            return first;
        }

        public int _chainSubsteps() => _chain.Substeps;
        public long _chainTests() => _chain.CapsuleTests;

        public Vector3[] ChainSnapshot()
        {
            var a = new Vector3[_chain.N + 1]; Array.Copy(_chain.X, a, a.Length); return a;
        }
    }
}
