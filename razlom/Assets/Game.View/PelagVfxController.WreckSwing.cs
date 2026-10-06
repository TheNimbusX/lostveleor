using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Махи Крушения v4 «холодное железо», вид V2 (06.10; целевые кадры swing1.png, swing2.png — числа
    /// PelagWreckSwingRules, цвета PelagWreckSwingLook, знаки на телах — .WreckSwingHit, ассеты —
    /// Editor/PelagWreckSwingVfxSetup V2). СЕРП: каждый кадр показа (поздний кадр PelagWreckLateHook, после рига
    /// якоря) берутся рукоять (PelagAnchorRig.ChainGrip) и голова якоря (центр её рендеров — голову ставит риг по
    /// запечке клипа), путь копится в PelagWreckSwingSweep, серп строится из рядов между кадрами
    /// (PelagWreckSwingRibbon) — сплошной при любом FPS. Пишется, пока голова в секторе маха Sim
    /// (Simulation.TryGetWreckSweep: from − 1 … to); голова пошла обратно, новый мах вошёл в свой сектор или сектор
    /// кончился — серп замирает и рассыпается (хвост первым). По наружной кромке едут 2–3 звена-призрака (меш звена
    /// «железа» базы), с головы в хлёст срываются холодные искры. Цвет — строка формы одной таблицы. Голову ведёт
    /// прежний PelagAnchorSlamView (риг не взял серию) — серпа нет. Рождение — от этапа серии (событие Sim в очереди
    /// до тика показа), не опросом.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        private sealed class WreckSwingRun
        {
            public bool Active, Frozen;
            public int Fx = -1, Serial, Stage, StageStart, Contact, Side, Against;
            public float From, Until, FrozenAt, BornAt, Seed, GlintCarry;
            public WreckSwingWeight Weight;
            public PelagForm Form;
            public GameObject Object;
            public MeshFilter Filter;
            public Renderer Renderer;
            public readonly Transform[] Links = new Transform[3];
            public readonly Renderer[] LinkRenderers = new Renderer[3];
            public ParticleSystem Glints;
            public readonly PelagWreckSwingSweep Sweep = new PelagWreckSwingSweep();
            public readonly PelagWreckSwingRibbon Ribbon = new PelagWreckSwingRibbon();
            public Vector3 Head, Velocity;
            public bool HasHead;
        }

        private readonly WreckSwingRun[] _wsRuns = { new WreckSwingRun(), new WreckSwingRun(), new WreckSwingRun() };
        private WreckSwingRun _wsNow;
        private MaterialPropertyBlock _wsBlock;
        private PelagAnchorRig _wsRig;
        private Transform _wsRigBody;
        private static readonly int WsTintId = Shader.PropertyToID("_Tint"), WsLightId = Shader.PropertyToID("_TintLight"),
            WsFadeId = Shader.PropertyToID("_Fade");

        /// <summary>Префабы махов собраны (и «железо» базы): махи — серпом, знаки — росчерком и сколами.</summary>
        private bool WreckSwingReady => WreckIronReady && (int)PelagVfxId.WreckSwingHit < _pools.Length
            && _pools[(int)PelagVfxId.WreckSwingArc] != null && _pools[(int)PelagVfxId.WreckSwingHit] != null;

        private static Color WsColor(int hex, float a = 1f)
        {
            PelagWreckSwingLook.Rgb(hex, out float r, out float g, out float b);
            return new Color(r, g, b, a);
        }

        /// <summary>
        /// Нажатие этапа (из BeginWreckStageVfx, тик показа): махи всех форм — серп (без пенной ленты, серпа пака
        /// и капель); выпад — серпа нет, прошлый рассыпается своим сроком.
        /// </summary>
        private void BeginWreckSwingVfx(WreckArcRun arc, in WkPending p)
        {
            if (!WreckSwingReady || !PelagWreckSwingRules.Draws(p.Stage)) return;
            arc.Iron = true;
            arc.SweepDone = true;
            _wkIronSeries = true;
            _wsLastForm = p.Form;
            // 06.10: лента за головой отвергнута владельцем — махи рисует .WreckIronSwing (техника серии сабли) в контакт.
            if (WreckComboReady || WreckIronSwingReady || WreckPaintedSwingReady) return;
            WreckSwingRun run = TakeWreckSwing();
            run.Serial = p.Serial;
            run.Stage = p.Stage;
            run.StageStart = p.Tick;
            run.Contact = p.Contact;
            run.Side = Simulation.WreckSwingSide(p.Stage);
            run.From = PelagWreckSwingRules.RecordFrom(p.Tick, PelagWreckSwingRules.FallbackFrom(p.Contact));
            run.Until = PelagWreckSwingRules.RecordUntil(PelagWreckSwingRules.FallbackTo(p.Contact));
            run.Weight = PelagWreckSwingRules.WeightOf(p.Stage);
            run.Form = p.Form;
            run.Frozen = run.HasHead = false;
            run.Against = 0;
            run.GlintCarry = 0f;
            run.BornAt = Time.time;
            run.Seed = Random.Range(0f, 8f);
            run.Sweep.Clear();
            int fx = WiSpawn(PelagVfxId.WreckSwingArc, PlayerPosition(), Quaternion.identity, 0f, 6f, out GameObject go);
            if (fx < 0) return;
            go.transform.localScale = Vector3.one;
            run.Fx = fx;
            run.Object = go;
            Transform root = go.transform;
            run.Filter = root.Find("Crescent")?.GetComponent<MeshFilter>();
            run.Renderer = run.Filter != null ? run.Filter.GetComponent<Renderer>() : null;
            run.Glints = root.Find("Glints")?.GetComponent<ParticleSystem>();
            run.Ribbon.Begin();
            if (_wsBlock == null) _wsBlock = new MaterialPropertyBlock();
            PelagWreckSwingLook.Row look = PelagWreckSwingLook.For(run.Form);
            _wsBlock.Clear();
            _wsBlock.SetColor(WsTintId, WsColor(look.Tint));
            _wsBlock.SetColor(WsLightId, WsColor(look.Light));
            if (run.Renderer != null) run.Renderer.SetPropertyBlock(_wsBlock);
            for (int i = 0; i < run.Links.Length; i++)
            {
                run.Links[i] = root.Find("Link" + i);
                run.LinkRenderers[i] = run.Links[i] != null ? run.Links[i].GetComponent<Renderer>() : null;
                if (run.LinkRenderers[i] != null) run.LinkRenderers[i].enabled = false;
            }
            run.Active = true;
            _wsNow = run;
        }

        private WreckSwingRun TakeWreckSwing()
        {
            WreckSwingRun pick = null;
            foreach (WreckSwingRun run in _wsRuns)
                if (!run.Active) { pick = run; break; }
                else if (pick == null || run.BornAt < pick.BornAt) pick = run;
            ReleaseWreckSwing(pick);
            return pick;
        }

        private void ReleaseWreckSwing(WreckSwingRun run)
        {
            if (run.Active && WkStill(run.Fx, run.Object)) Release(run.Fx);
            run.Active = false;
            run.Fx = -1;
            run.Object = null;
            if (_wsNow == run) _wsNow = null;
        }

        private void ForgetWreckSwings()
        {
            foreach (WreckSwingRun run in _wsRuns) ReleaseWreckSwing(run);
            _wsNow = null;
            _wsLastStage = -1;
            // Арену сменили (серии Sim снова с 1) — секторы прошлых махов не в счёт.
            _wsSweepSerial = -1;
            _wsSweepFrom[0] = _wsSweepFrom[1] = int.MaxValue;
            _wsSweepTo[0] = _wsSweepTo[1] = int.MinValue;
            System.Array.Clear(_wsHitBySwing1, 0, _wsHitBySwing1.Length);
        }

        /// <summary>Выход цепи из кулака этого кадра: риг якоря (ведёт серию), иначе кисть с цепью ArenaView.</summary>
        private Vector3 WreckSwingGrip()
        {
            if (_arena.TryGetEntityView(Simulation.PlayerId, out Transform body) && (body != _wsRigBody || _wsRig == null))
            {
                _wsRigBody = body;
                _wsRig = body.GetComponentInChildren<PelagAnchorRig>(true);
            }
            return _wsRig != null && _wsRig.Owning ? _wsRig.ChainGrip : _arena.PlayerChainHandPosition;
        }

        /// <summary>Ось вращения серпа — корень тела героя этого кадра (не точка Sim: тело могут вести клипы).</summary>
        private Vector3 WreckSwingPivot()
            => _wsRigBody != null ? _wsRigBody.position : PlayerPosition();

        private static System.Numerics.Vector3 WsN(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
    }
}
