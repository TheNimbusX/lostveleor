using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Бросок якоря · БАЗА «холодное железо» (07–08.10; кадры ART/characters/pelag/anchor-throw-2026-10-07/chatgpt-results/
    /// base-hit-v1.png, base-yank-v1.png) — и ФОРМЫ на ней (08.10: Невод form-net-v1, Веер form-fan-ghost-v1, Гарпун
    /// form-harpoon-v2). Каждый каст рисует этот стек (пена 03.10 больше не рождается); у формы — та же база в своей краске
    /// (префабы-перекраски PelagWreckComboVfxSetup.ThrowForm, цвета частиц PelagThrowFormLook.TintFor) плюс элементы формы.
    /// Стек слоёв как у принятого Крушения, якорная семья — материалом; у КАЖДОЙ полосы (Веер — три) свой набор:
    ///  • струна вдоль цепи (хват → кольцо, «Razlom/Anchor Throw Streak»): в полёте «комета» — широкая горячая у головы, к руке
    ///    тоньше, проявляется головой; в «держит» стягивается в ровную светящуюся струну; рывок — импульс света бежит от головы
    ///    к руке, шевроны в шейдере и пары искр-шевронов к Пелагу; ореол под ней;
    ///  • голова в полёте — искры назад и вспышка колючей звезды каждый тик; щелчок в конце полёта и на рывке — звезда крупнее;
    ///  • боковые полосы Веера — призрачный якорь (.AnchorThrowIronGhosts) и звенья призрачной цепи поверх струны;
    ///  • Невод — сеть из цепей по полосе Sim (.AnchorThrowIronNet);
    ///  • знак на каждом задетом, укус Гарпуна, оглушение, борозды тянутых, ловля — .AnchorThrowIronHits / .AnchorThrowIronFurrows.
    /// Числа — PelagAnchorThrowIronLook и PelagThrowFormLook; ленты — PelagAnchorThrowIronStrip; ассеты —
    /// Editor/PelagWreckComboVfxSetup.ThrowIron (база) и .ThrowForm (краски форм, сеть, призрак). Время — тик показа (как вся
    /// очередь Броска), голова и хват полосы 0 — те же, что у рига (AnchorThrowRun.Grip/Ring), боковые — формула Sim.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Одна полоса: струна, ореол, звенья призрачной цепи (Веер), искры, шевроны, вспышки головы.</summary>
        private sealed class AiRun
        {
            public bool Active, Arrived;
            public int Lane, Fx = -1, LastFlare = -1;
            public GameObject Object;
            public MeshFilter StreakFilter, HaloFilter, LinksFilter;
            public ParticleSystem Sparks, Chevrons, Flares;
            public float SparkCarry, ChevronCarry;
            public Vector3 Ring;
            public readonly AnchorThrowIronStreak Streak = new AnchorThrowIronStreak(), Halo = new AnchorThrowIronStreak();
            public readonly AnchorThrowLinkStrip Links = new AnchorThrowLinkStrip();
        }

        /// <summary>Общее для полос каста: номер, форма и её цвета, снимок Sim, хват и кольцо рига, высота выпуска, распад.</summary>
        private sealed class AiCast
        {
            public int Serial = -1;
            public PelagForm Form;
            public PelagThrowFormLook.Tint Tint = PelagThrowFormLook.BaseTint;
            public AnchorThrowState Snap;
            public Vector3 Grip, Ring;
            public float ReleaseHeight = 1.2f, BreakFrom = -1f;
        }

        private readonly AiCast _aic = new AiCast();
        private readonly AiRun[] _aiLanes = { new AiRun { Lane = 0 }, new AiRun { Lane = 1 }, new AiRun { Lane = 2 } };

        private static Color AiColor(PelagThrowFormLook.Rgb c) => new Color(c.R, c.G, c.B, 1f);

        private bool AnchorThrowIronReady => _pools != null && (int)PelagVfxId.AnchorThrowIronChain < _pools.Length
            && _pools[(int)PelagVfxId.AnchorThrowIronChain] != null;

        private bool AiOurs(int serial) => _aic.Serial >= 0 && _aic.Serial == serial;

        private bool AiAnyLane => _aiLanes[0].Active || _aiLanes[1].Active || _aiLanes[2].Active;

        /// <summary>Префаб формы, если её краска собрана; иначе — база (форма видна базой, а не пустотой).</summary>
        private PelagVfxId AiId(PelagVfxId form, PelagVfxId fallback)
            => _pools != null && (int)form < _pools.Length && _pools[(int)form] != null ? form : fallback;

        private PelagVfxId AiFormId(PelagVfxId net, PelagVfxId fan, PelagVfxId harpoon, PelagVfxId fallback)
            => AiId(_aic.Form == PelagForm.AnchorThrowNet ? net : _aic.Form == PelagForm.AnchorThrowFan ? fan
                : _aic.Form == PelagForm.AnchorThrowHarpoon ? harpoon : fallback, fallback);

        /// <summary>Объект пула стека: ставится, масштаб, учёт в активных; −1 — без эффектов или пула нет.</summary>
        private int AiSpawn(PelagVfxId id, Vector3 at, Quaternion rotation, float scale, float duration, out GameObject go)
        {
            go = null;
            if (CaptureRig.NoVfx || !TryAcquire(id, out go, out PelagVfxElement element)) return -1;
            int index = ReserveActive();
            element.Begin(at, rotation);
            if (scale > 0f && !Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = duration > 0f ? duration : Mathf.Max(.05f, element.DefaultLifetime),
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            return index;
        }

        /// <summary>Выпуск: полосы (Веер — три) от руки в краске формы; Веер — призраки; сеть Невода ляжет в «держит» сама.</summary>
        private void BeginAnchorThrowIronVfx(AnchorThrowRun run, float shown)
        {
            EndAiLanes();
            EndAiGhosts();
            EndAiNet();
            _aic.Serial = run.Serial;
            _aic.Form = PelagThrowFormLook.IsThrowForm(run.Form) ? run.Form : PelagForm.None;
            _aic.Tint = PelagThrowFormLook.TintFor(_aic.Form);
            _aic.Snap = run.Snap;
            _aic.Grip = run.Grip;
            _aic.Ring = run.Ring;
            _aic.ReleaseHeight = run.ReleaseHeight;
            _aic.BreakFrom = -1f;
            if (CaptureRig.NoVfx) return;
            int lanes = PelagAnchorThrowVfxRules.LaneCount(run.Snap);
            for (int lane = 0; lane < lanes; lane++) BeginAiLane(_aiLanes[lane], run.Ring);
            if (lanes == 3) { BeginAiGhost(1); BeginAiGhost(2); }
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[throw-iron] begin serial={run.Serial} form={_aic.Form} lanes={lanes} release={run.Snap.ReleaseTick} hold={run.Snap.HoldTick} taut={run.Snap.TautTick} catch={run.Snap.CatchTick} shown={shown:F2}");
        }

        private void BeginAiLane(AiRun ai, Vector3 fist)
        {
            EndAiLane(ai);
            PelagVfxId id = AiFormId(PelagVfxId.AnchorThrowFormChainNet, PelagVfxId.AnchorThrowFormChainFan,
                PelagVfxId.AnchorThrowFormChainHarpoon, PelagVfxId.AnchorThrowIronChain);
            ai.Fx = AiSpawn(id, Vector3.zero, Quaternion.identity, 0f, 8f, out ai.Object);
            if (ai.Fx < 0) return;
            Transform t = ai.Object.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            t.localScale = Vector3.one;
            ai.StreakFilter = t.Find("Streak")?.GetComponent<MeshFilter>();
            ai.HaloFilter = t.Find("Halo")?.GetComponent<MeshFilter>();
            ai.LinksFilter = ai.Lane > 0 ? t.Find("Links")?.GetComponent<MeshFilter>() : null;
            ai.Sparks = t.Find("Sparks")?.GetComponent<ParticleSystem>();
            ai.Chevrons = t.Find("Chevrons")?.GetComponent<ParticleSystem>();
            ai.Flares = t.Find("Flares")?.GetComponent<ParticleSystem>();
            if (ai.StreakFilter != null) FormWaterMesh.MeshFor(ai.StreakFilter, AnchorThrowIronStreak.MeshName).Clear();
            if (ai.HaloFilter != null) FormWaterMesh.MeshFor(ai.HaloFilter, AnchorThrowIronStreak.MeshName).Clear();
            Transform links = t.Find("Links");
            if (links != null) FormWaterMesh.MeshFor(links.GetComponent<MeshFilter>(), AnchorThrowLinkStrip.MeshName).Clear();
            ai.Active = true;
            ai.Arrived = false;
            ai.Ring = fist;
            ai.SparkCarry = ai.ChevronCarry = 0f;
            ai.LastFlare = -1;
        }

        /// <summary>Кадр стека (после головы рига — хват и кольцо уже этого кадра).</summary>
        private void UpdateAnchorThrowIronVfx(Simulation sim, float shown, float dt)
        {
            UpdateAiStuns(sim, shown);
            UpdateAiBite(sim);
            UpdateAiFurrows(sim, shown, dt);
            bool ghosts = AiAnyGhost;
            if (!AiAnyLane && !ghosts && !_aiNet.Active)
            {
                UpdateAiNet(sim, shown, dt);
                return;
            }
            AnchorThrowRun run = _atRun;
            if (run.Active && run.Serial == _aic.Serial)
            {
                _aic.Snap = run.Snap;
                _aic.Grip = run.Grip;
                _aic.Ring = run.Ring;
            }
            else if (_aic.BreakFrom < 0f) _aic.BreakFrom = shown;
            // Призраки первыми: кольцо призрака — конец его цепи в этом же кадре.
            if (ghosts) UpdateAiGhosts(shown, dt);
            UpdateAiNet(sim, shown, dt);
            AiView(out Vector3 forward);
            foreach (AiRun ai in _aiLanes) UpdateAiLane(ai, shown, dt, forward);
        }

        private void UpdateAiLane(AiRun ai, float shown, float dt, Vector3 forward)
        {
            if (!ai.Active) return;
            if (!AtStill(ai.Fx, ai.Object)) { ai.Active = false; return; }
            AnchorThrowState s = _aic.Snap;
            if (ai.Lane == 0) ai.Ring = _aic.Ring;
            int arrive = PelagAnchorThrowVfxRules.LaneArriveTick(s, ai.Lane);
            float breakS = _aic.BreakFrom >= 0f ? PelagAnchorThrowIronLook.BreakSeconds(shown, _aic.BreakFrom) : 0f;
            var frame = new AnchorThrowIronStreak.Frame
            {
                Grip = _aic.Grip, Ring = ai.Ring, ViewForward = forward,
                Tight = PelagAnchorThrowIronLook.Tighten(shown, Mathf.Min(s.HoldTick, arrive + 1), s.TautTick),
                SinceYank = (shown - s.TautTick) / PelagAnchorThrowIronLook.TicksPerSecond,
                Chevrons = PelagAnchorThrowIronLook.Chevrons(shown, s.TautTick, s.CatchTick),
                Opacity = PelagAnchorThrowIronLook.Appear(shown, s.ReleaseTick),
                BreakSeconds = breakS,
                Clock = (shown - s.ReleaseTick) / PelagAnchorThrowIronLook.TicksPerSecond,
                Scale = PelagThrowFormLook.StreakScale(_aic.Form)
            };
            if (ai.StreakFilter != null) ai.Streak.Build(FormWaterMesh.MeshFor(ai.StreakFilter, AnchorThrowIronStreak.MeshName), frame);
            frame.Scale = PelagAnchorThrowIronLook.HaloScale * PelagThrowFormLook.StreakScale(_aic.Form);
            if (ai.HaloFilter != null) ai.Halo.Build(FormWaterMesh.MeshFor(ai.HaloFilter, AnchorThrowIronStreak.MeshName), frame);
            // Призрачная цепь: звенья-стекло поверх струны (свет вокруг — струна в краске Веера).
            if (ai.LinksFilter != null)
                ai.Links.Build(FormWaterMesh.MeshFor(ai.LinksFilter, AnchorThrowLinkStrip.MeshName), _aic.Grip, ai.Ring, forward,
                    PelagThrowFormLook.GhostAppear(shown, s.ReleaseTick), frame.Opacity, breakS, frame.Clock);

            Vector3 axis = _aic.Grip - ai.Ring;
            float length = axis.magnitude;
            Vector3 toHand = length > 1e-3f ? axis / length : -AtDir(s, ai.Lane);
            Vector3 head = ai.Ring - toHand * PelagAnchorThrowIronLook.HeadFromRing;
            // С полутика после выпуска: в выпуск голова ещё у земли за правой (замах) — вспышка там читалась пятном у ног.
            bool live = breakS <= 0f && shown >= s.ReleaseTick + .5f;
            // Полёт: искры с головы назад и в стороны, вспышка колючей звезды каждый тик.
            if (live && shown < arrive && dt > 0f && ai.Sparks != null)
            {
                int count = PelagAnchorThrowIronLook.Spend(ref ai.SparkCarry, dt, 1f / PelagAnchorThrowIronLook.HeadSparkRate);
                for (int i = 0; i < count; i++)
                    AiSpark(ai.Sparks, head + Random.insideUnitSphere * .12f,
                        toHand * Random.Range(1.5f, 4.5f) + Random.insideUnitSphere * 1.8f + Vector3.up * Random.Range(.3f, 1.4f));
            }
            int tick = Mathf.FloorToInt(shown);
            float flareScale = PelagThrowFormLook.FlareScale(_aic.Form, ai.Lane);
            if (live && shown < arrive && tick != ai.LastFlare && PelagThrowFormLook.FlightFlares(_aic.Form, ai.Lane))
            {
                ai.LastFlare = tick;
                AiFlare(ai, head, PelagAnchorThrowIronLook.FlareWidth * flareScale * Random.Range(.85f, 1.1f), PelagAnchorThrowIronLook.FlareLife, false);
            }
            // Конец полёта (у Гарпуна — укус): голова встала, цепь щёлкнула — звезда крупнее и россыпь искр.
            if (live && !ai.Arrived && shown >= arrive)
            {
                ai.Arrived = true;
                AiFlare(ai, head, PelagAnchorThrowIronLook.SnapStar * flareScale, .16f, true);
                if (ai.Sparks != null)
                    for (int i = 0; i < 18; i++)
                        AiSpark(ai.Sparks, head, Random.onUnitSphere * Random.Range(2f, 6f) + Vector3.up * 1.2f);
            }
            // Рывок и возврат: пары искр-шевронов бегут по цепи к руке.
            if (live && shown >= s.TautTick && shown < s.CatchTick && dt > 0f)
            {
                int pairs = PelagAnchorThrowIronLook.Spend(ref ai.ChevronCarry, dt, 1f / PelagAnchorThrowIronLook.ChevronSparkRate);
                for (int i = 0; i < pairs; i++) AiChevron(ai, toHand, forward);
            }
            if (breakS > PelagAnchorThrowIronLook.StreakGoneSeconds + .35f) EndAiLane(ai);
        }

        private void AiSpark(ParticleSystem system, Vector3 at, Vector3 velocity)
        {
            if (system == null) return;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = velocity,
                startSize = Random.Range(.03f, .06f),
                startLifetime = Random.Range(.12f, .26f),
                startColor = Color.Lerp(AiColor(_aic.Tint.SparkHot), AiColor(_aic.Tint.SparkTint), Random.value),
                applyShapeToPosition = false
            }, 1);
        }

        /// <summary>Вспышка головы: колючая звезда цвета краски с белым ядром (крупная — щелчок натяга и рывок).</summary>
        private void AiFlare(AiRun ai, Vector3 at, float size, float life, bool big)
        {
            if (ai.Flares == null) return;
            Color main = AiColor(_aic.Tint.Flare);
            ai.Flares.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                startSize = size,
                startLifetime = life,
                startColor = big ? main : Color.Lerp(main, AiColor(_aic.Tint.FlareLight), Random.value),
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false
            }, 1);
        }

        /// <summary>Шеврон: две вытянутые искры из одной точки цепи, к руке и в стороны экрана — «>» остриём к Пелагу.</summary>
        private void AiChevron(AiRun ai, Vector3 toHand, Vector3 viewForward)
        {
            if (ai.Chevrons == null) return;
            Vector3 at = Vector3.Lerp(ai.Ring, _aic.Grip, PelagAnchorThrowIronLook.ChevronAt(Random.value));
            Vector3 side = Vector3.Cross(toHand, viewForward);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.up;
            float speed = Random.Range(PelagAnchorThrowIronLook.ChevronSpeedMin, PelagAnchorThrowIronLook.ChevronSpeedMax);
            Color color = Color.Lerp(AiColor(_aic.Tint.SparkHot), AiColor(_aic.Tint.SparkTint), Random.Range(.2f, .7f));
            float size = Random.Range(.07f, .10f);
            float life = Random.Range(.12f, .18f);
            for (int k = -1; k <= 1; k += 2)
                ai.Chevrons.Emit(new ParticleSystem.EmitParams
                {
                    position = at + side * (k * .04f),
                    velocity = (toHand + side * (k * PelagAnchorThrowIronLook.ChevronSpread)) * speed,
                    startSize = size,
                    startLifetime = life,
                    startColor = color,
                    applyShapeToPosition = false
                }, 1);
        }

        /// <summary>Рывок (событие в TautTick): на каждой полосе звезда у головы и залп шевронов; толчок камеры; борозды тянутых.</summary>
        private void PlayAnchorThrowIronYank(in AtPending p)
        {
            if (!AiOurs(p.Serial)) return;
            StartAiFurrows(p.Serial);
            if (CaptureRig.NoVfx) return;
            AiView(out Vector3 forward);
            foreach (AiRun ai in _aiLanes)
            {
                if (!ai.Active) continue;
                Vector3 axis = _aic.Grip - ai.Ring;
                Vector3 toHand = axis.sqrMagnitude > 1e-6f ? axis.normalized : -AtDir(_aic.Snap, ai.Lane);
                // Гарпун: звезда рывка осталась бы пятном на месте укуса (цель уезжает сразу) — свет держит знак укуса на теле.
                if (_aic.Form != PelagForm.AnchorThrowHarpoon)
                    AiFlare(ai, ai.Ring - toHand * PelagAnchorThrowIronLook.HeadFromRing,
                        PelagAnchorThrowIronLook.YankStar * PelagThrowFormLook.FlareScale(_aic.Form, ai.Lane), .14f, true);
                for (int i = 0; i < PelagAnchorThrowIronLook.YankChevronPairs; i++) AiChevron(ai, toHand, forward);
            }
            _juice?.PunchCamera(.15f, .02f);
        }

        /// <summary>Куда смотрит камера (ленты к камере, стороны шевронов на экране).</summary>
        private static void AiView(out Vector3 forward)
        {
            Camera camera = Camera.main;
            forward = camera != null ? camera.transform.forward : new Vector3(0f, -.74f, .67f);
        }

        private void EndAiLane(AiRun ai)
        {
            if (AtStill(ai.Fx, ai.Object))
            {
                if (ai.StreakFilter != null) FormWaterMesh.MeshFor(ai.StreakFilter, AnchorThrowIronStreak.MeshName).Clear();
                if (ai.HaloFilter != null) FormWaterMesh.MeshFor(ai.HaloFilter, AnchorThrowIronStreak.MeshName).Clear();
                if (ai.LinksFilter != null) FormWaterMesh.MeshFor(ai.LinksFilter, AnchorThrowLinkStrip.MeshName).Clear();
                Release(ai.Fx);
            }
            ai.Active = false;
            ai.Fx = -1;
            ai.Object = null;
            ai.LinksFilter = null;
        }

        private void EndAiLanes() { foreach (AiRun ai in _aiLanes) EndAiLane(ai); }

        private void ForgetAnchorThrowIronVfx()
        {
            EndAiLanes();
            EndAiGhosts();
            EndAiNet();
            _aic.Serial = -1;
            EndAiStuns();
            EndAiFurrows();
        }
    }
}
