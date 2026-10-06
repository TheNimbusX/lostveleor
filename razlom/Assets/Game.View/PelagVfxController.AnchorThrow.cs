using Game.Sim;
using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    /// <summary>Переключатели вида Броска якоря (F8 → Пелаг → Формы: «Бросок якоря: якорь на риге»).</summary>
    public static class AnchorThrowVfxSwitches
    {
        /// <summary>
        /// true — голову и цепь ведёт система якоря на цепи (PelagAnchorRig, artifacts/anchor-core: режимы
        /// Thrown/Yank по линии Sim, Caught → маятник), если она стоит в проекте и взяла бросок. Иначе —
        /// ВРЕМЕННЫЙ путь Абордажа v2 (спека §5.1): своя голова из пула, прямая цепь из двух точек.
        /// </summary>
        public static bool UseRig = true;
    }

    /// <summary>
    /// БРОСОК ЯКОРЯ «морская пена» (03.10). Целевые кадры — ART/characters/pelag/anchor-throw-2026-10-03/
    /// chatgpt-results: A-base (натянутая прямая цепь по линии, бирюзовая вода вдоль неё, всплески на
    /// каждом задетом), B-net (широкий лист кобальтовой сети пены с белыми узлами), C-fan (два
    /// полупрозрачных водяных якоря индиго под ±30° с водяными цепями), D-harpoon (маджентовый веер
    /// укуса, тонкая струна цепи, борозда волока). Якорь на конце — наш якорь в настоящем размере.
    ///
    /// ВСЁ рождается от событий Sim 72–76 (AnchorThrowRelease/Hit/Yank/Catch/Ended), Stun приземления
    /// за Catch и снимка AnchorThrowState; разовые знаки ждут в очереди тика показа sim.Tick − 2 + Alpha
    /// (тело героя рисуется на нём). Голова каждой полосы — формула Sim (PelagAnchorThrowVfxRules.Head).
    /// Части: .AnchorThrowAnchor — голова, цепь, вода на цепи (риг или временный путь); .AnchorThrowCues —
    /// разовые знаки; .AnchorThrowForms — сеть Невода и призраки Веера; .AnchorThrowTow — струйки к
    /// цепи, борозды волока и вспаханная пена за головой. Цвета — одна таблица PelagAnchorThrowFormLook.
    /// Префабы — Editor/PelagAnchorThrowFoamVfxSetup (свои пулы, принятые префабы семьи под своими id).
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Префабы Броска собраны и в библиотеке.</summary>
        private bool AnchorThrowVfxReady => _pools != null && (int)PelagVfxId.AnchorThrowRibbon < _pools.Length
            && _pools[(int)PelagVfxId.AnchorThrowRibbon] != null;

        private enum AtKind : byte { Hit, Yank, Catch, Land, End }

        /// <summary>Шаг, ждущий тика показа.</summary>
        private struct AtPending
        {
            public AtKind Kind;
            public int Tick, Target, Serial, Lane, Amount;
            public bool Flag;
            public Vector3 At;
        }

        private const int AtQueueSize = 64;
        private readonly AtPending[] _atQueue = new AtPending[AtQueueSize];
        private int _atQueued;
        private Simulation _atSim;
        /// <summary>Тик последней ловли: Stun следом за AnchorThrowCatch того же тика — всплеск приземления.</summary>
        private int _atCatchTick = -1, _atCatchSerial = -1;

        private void PrepareAnchorThrowVfx()
        {
            if (_pools == null) return;
            PelagVfxId[] ids =
            {
                PelagVfxId.AnchorThrowRibbon, PelagVfxId.AnchorThrowSplash, PelagVfxId.AnchorThrowCrown, PelagVfxId.AnchorThrowKnock,
                PelagVfxId.AnchorThrowBurst, PelagVfxId.AnchorThrowWake, PelagVfxId.AnchorThrowDrag, PelagVfxId.AnchorThrowNet,
                PelagVfxId.AnchorThrowGhost, PelagVfxId.AnchorThrowAnchor, PelagVfxId.AnchorThrowChain
            };
            foreach (PelagVfxId id in ids)
                if ((int)id < _pools.Length && _pools[(int)id] != null) _pools[(int)id].Pool.PrewarmStep(8);
        }

        /// <summary>Событие Броска якоря. True — разобрано здесь; AbilityCast и Stun идут дальше по общему пути.</summary>
        private bool ConsumeAnchorThrowEvent(in SimEvent e, int index)
        {
            if (e.Type == SimEventType.Stun)
            {
                // Оглушение приземления — сразу за AnchorThrowCatch того же тика (Sim: событие ловли, потом Stun).
                if (_atCatchTick >= 0 && AnchorThrowVfxReady && EventTick(index) == _atCatchTick)
                    EnqueueAt(new AtPending { Kind = AtKind.Land, Tick = _atCatchTick, Target = e.Target, Serial = _atCatchSerial,
                        At = AtWorld(e.Position) });
                return false;
            }
            if (e.Type != SimEventType.Damage && e.Type != SimEventType.Death) _atCatchTick = -1;
            switch (e.Type)
            {
                case SimEventType.AbilityCast:
                    BeginAnchorThrowVfxCast(e.Amount);
                    return false;
                case SimEventType.AnchorThrowRelease:
                    if (AnchorThrowVfxReady) AnchorThrowReleased(e, EventTick(index));
                    return true;
                case SimEventType.AnchorThrowHit:
                    if (AnchorThrowVfxReady)
                        EnqueueAt(new AtPending { Kind = AtKind.Hit, Tick = EventTick(index), Target = e.Target, Lane = e.Amount, Flag = e.Flag,
                            Serial = e.ActionVariant, At = AtWorld(e.Position) });
                    return true;
                case SimEventType.AnchorThrowYank:
                    if (AnchorThrowVfxReady)
                        EnqueueAt(new AtPending { Kind = AtKind.Yank, Tick = EventTick(index), Target = e.Target, Amount = e.Amount, Flag = e.Flag,
                            Serial = e.ActionVariant, At = AtWorld(e.Position) });
                    return true;
                case SimEventType.AnchorThrowCatch:
                    if (!AnchorThrowVfxReady) return true;
                    _atCatchTick = EventTick(index);
                    _atCatchSerial = e.ActionVariant;
                    EnqueueAt(new AtPending { Kind = AtKind.Catch, Tick = _atCatchTick, Amount = e.Amount, Serial = e.ActionVariant, At = AtWorld(e.Position) });
                    return true;
                case SimEventType.AnchorThrowEnded:
                    if (AnchorThrowVfxReady)
                        EnqueueAt(new AtPending { Kind = AtKind.End, Tick = EventTick(index), Amount = e.Amount, Serial = e.ActionVariant, At = AtWorld(e.Position) });
                    return true;
                default:
                    return false;
            }
        }

        private Vector3 AtWorld(FixVec2 p) => new Vector3(p.X.ToFloat(), PlayerPosition().y, p.Y.ToFloat());

        private void EnqueueAt(in AtPending pending)
        {
            // Очередь полна (очень длинный кадр): старейший шаг показывается сразу.
            if (_atQueued >= _atQueue.Length) RunAnchorThrowCue(0, true);
            _atQueue[_atQueued++] = pending;
        }

        /// <summary>Кадр Броска якоря (LateUpdate после Шквала, до UpdateActive).</summary>
        private void UpdateAnchorThrowVfx()
        {
            Simulation sim = _driver.Sim;
            if (sim != _atSim)
            {
                // Арену сменили: тел прежнего боя нет — очередь и всё живое Броска отпускаются.
                ResetAnchorThrowVfx();
                _atSim = sim;
            }
            if (sim == null) return;
            float shown = PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha);
            float dt = Time.deltaTime;
            _atGroundBase = PlayerPosition().y;
            while (_atQueued > 0 && _atQueue[0].Tick <= shown) RunAnchorThrowCue(0, false);
            UpdateAnchorThrowRun(sim, shown, dt);
            UpdateAnchorThrowGhosts(shown, dt);
            UpdateAnchorThrowNet(shown, dt);
            UpdateAnchorThrowTows(sim, shown, dt);
            UpdateAnchorThrowWakes(dt);
        }

        private void ResetAnchorThrowVfx()
        {
            _atQueued = 0;
            _atCatchTick = -1;
            if (_atRun.Active) FinishAnchorThrowRun(false);
            ForgetAnchorThrowForms();
            ForgetAnchorThrowTows();
        }

        /// <summary>Показать шаг очереди <paramref name="index"/> и убрать его.</summary>
        private void RunAnchorThrowCue(int index, bool force)
        {
            AtPending p = _atQueue[index];
            for (int i = index + 1; i < _atQueued; i++) _atQueue[i - 1] = _atQueue[i];
            _atQueued--;
            if (CaptureRig.HasEnemyOverride)
            {
                // shown — тик показа в миг рождения знака (приёмка: shown ≥ tick и shown − tick < 1).
                Simulation sim = _driver.Sim;
                float shown = sim != null ? PelagAnchorThrowVfxRules.ShownTick(sim.Tick, _driver.Alpha) : -1f;
                Debug.Log($"[anchor-throw-vfx] cue {p.Kind} tick={p.Tick} shown={shown:F2} target={p.Target} lane={p.Lane} serial={p.Serial} force={force}");
            }
            switch (p.Kind)
            {
                case AtKind.Hit: PlayAnchorThrowHit(p); break;
                case AtKind.Yank: PlayAnchorThrowYank(p); break;
                case AtKind.Catch: PlayAnchorThrowCatch(p); break;
                case AtKind.Land: PlayAnchorThrowLand(p); break;
                case AtKind.End: EndAnchorThrowCue(p); break;
            }
        }

        // ---- рождение объектов Броска

        /// <summary>
        /// Объект пула в точке, цвет формы; <paramref name="drops"/> — перекрасить капли принятого префаба семьи
        /// (до Begin: вспышка рождается уже в цвете). −1 — эффектов нет (NoVfx) или пула нет.
        /// </summary>
        private int AtSpawn(PelagVfxId id, Vector3 at, Quaternion rotation, float scale, float duration, PelagForm form, bool drops, out GameObject go)
        {
            go = null;
            if (CaptureRig.NoVfx || !TryAcquire(id, out go, out PelagVfxElement element)) return -1;
            int index = ReserveActive();
            if (drops) PelagAnchorThrowFormLook.ApplyDrops(go, form);
            element.Begin(at, rotation);
            if (scale > 0f && !Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            PelagAnchorThrowFormLook.Apply(go, form);
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = duration > 0f ? duration : Mathf.Max(.05f, element.DefaultLifetime),
                Start = at, End = at, Motion = Motion.Static, FollowIndex = -1
            };
            return index;
        }

        private bool AtStill(int fx, GameObject go)
            => go != null && fx >= 0 && fx < _active.Length && _active[fx].Active && _active[fx].Object == go;

        private float _atGroundBase;
        private System.Func<float, float, float> _atGround;

        /// <summary>Земля в точке: лагерь — навигация, разлом — пол показанной арены с уступами; иначе высота ног.</summary>
        private float AnchorThrowGroundAt(float x, float z)
        {
            GameSession session = _driver.Session;
            if (session != null && session.Mode == GameMode.Camp)
            {
                if (NavMesh.SamplePosition(new Vector3(x, _atGroundBase + 1f, z), out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                    return Mathf.Clamp(hit.position.y, _atGroundBase - .6f, _atGroundBase + .8f);
                return _atGroundBase;
            }
            LayoutView layout = LayoutView.Shown;
            return layout != null ? layout.WeaponGroundHeight(x, z) : _atGroundBase;
        }

        /// <summary>Та же земля как функция для сеток под водой (корень высоты — ноги героя).</summary>
        private System.Func<float, float, float> AnchorThrowGround()
        {
            _atGroundBase = PlayerPosition().y;
            return _atGround ??= AnchorThrowGroundAt;
        }
    }
}
