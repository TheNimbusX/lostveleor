using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ТОЛЬКО СЪЁМКА: стенд Шквала (шаг 2 переделки, 02.10) одним прогоном изолированного плеера.
    /// Без ключа -capture-enemy-case squall этот код не вызывается (ветка case "squall" в
    /// TickDriver.Stonehoof.cs), обычная игра и редактор его не видят.
    ///
    ///   capture.ps1 -Encounter forest-guardian -Enemies 6 -ExtraArgs '-capture-pack','-capture-enemy-case','squall'
    ///       [,'-capture-skill-form','squall:hunt|foam|elusive'] [,'-capture-squall-live'] [,'-capture-squall-casts','N']
    ///   capture.ps1 -Encounter forest-guardian -Enemies 1 -ExtraArgs '-capture-enemy-case','squall'   (облёт одной цели)
    ///
    /// Раскладка «зигзаг»: от точки каста вправо по экрану (ось — правая ось камеры на земле), шесть мест
    /// <see cref="SquallTourSpots"/> подобраны по правилу выбора цели Sim (ближайшая в конусе 110° с бонусом
    /// стороны): прыжки 2 → 4 → 6 → 4 тика, удары строго чередуются. Каст k (с нуля) нечётный — зеркально
    /// поперёк оси: поворот идёт против проводки удара. Форма — из -capture-skill-form, ставится набору тем же
    /// путём, что F8 «Шквал: форма» (RunLoadout.DebugSetForm). Охота: места ранены по
    /// <see cref="SquallTourHuntHealth"/> — добыча, добивание с лишним прыжком, облёт добычи.
    ///
    /// Враги заморожены (не ходят и не бьют); с -capture-squall-live со второго каста их отпускает нажатие — идут
    /// по полосам Пенного следа и бьют сквозь Неуловимого (первый каст остаётся чистым). Перед каждым кастом все
    /// места ставятся заново (живые по порядку мест), враги снова заморожены.
    /// Журнал: [squall-tour] — раскладка, нажатия, события Sim (прыжок, удар, добивание, возврат, полоса, конец);
    /// [squall-probe] — каждый кадр вокруг каста: клип базового слоя, фаза Sim, корень, стопы, таз, взгляд, точка
    /// кадра; vf — номер кадра video_frames (кадры для листов берутся по нему, а не по -Times).
    /// </summary>
    public sealed partial class TickDriver
    {
        private const int SquallTourSettleTicks = 45, SquallTourResettleTicks = 30, SquallTourTailTicks = 105;
        private const int SquallTourStartWaitTicks = 12, SquallTourProbeLeadTicks = 4, SquallTourProbeTailTicks = 10;
        private const int SquallTourDefaultCasts = 2, SquallTourMaxCasts = 6, SquallTourKillHealth = 60;
        private const int SquallTourDone = 5;

        /// <summary>Места врагов: (вдоль, поперёк) от точки каста, метры; вдоль — вправо по экрану, поперёк — вверх.</summary>
        private static readonly Vector2[] SquallTourSpots =
        {
            new Vector2(2.6f, 0f), new Vector2(4.2f, 2.8f), new Vector2(8.0f, -.4f),
            new Vector2(10.0f, 2.9f), new Vector2(13.0f, -.8f), new Vector2(5.4f, -4.8f),
        };

        /// <summary>Охота: доля здоровья на месте; меньше нуля — SquallTourKillHealth (первый удар убивает).</summary>
        private static readonly float[] SquallTourHuntHealth = { 1f, .2f, -1f, .5f, 1f, .1f };

        private int _squallTourGeneration = -1, _squallTourStep, _squallTourNext, _squallTourCast, _squallTourCasts;
        private int _squallTourSlot = -1, _squallTourLatchTick = -1, _squallTourTarget = -1, _squallTourSerial;
        private int _squallTourPressTick = -1, _squallTourEndTick = -1;
        private bool _squallTourReady, _squallTourLive;
        private PelagForm _squallTourForm;
        private Vector2 _squallTourOrigin, _squallTourAlong = Vector2.right, _squallTourSide = Vector2.up;
        private readonly int[] _squallTourIds = new int[6], _squallTourSpotOf = new int[6];
        private readonly Fix64[] _squallTourSpeed = new Fix64[6], _squallTourDamage = new Fix64[6];

        private static FixVec2 SquallTourFix(Vector2 v) => new FixVec2(Fix64.FromDouble(v.x), Fix64.FromDouble(v.y));
        private static Vector2 SquallTourFlat(FixVec2 v) => new Vector2(v.X.ToFloat(), v.Y.ToFloat());

        private Vector2 SquallTourSpot(int k, bool mirror)
        {
            Vector2 s = SquallTourSpots[k];
            return _squallTourOrigin + _squallTourAlong * s.x + _squallTourSide * (mirror ? -s.y : s.y);
        }

        private void CaptureSquallTour(int tick)
        {
            if (_squallTourGeneration != Generation)
            {
                _squallTourGeneration = Generation;
                _squallTourReady = SetupSquallTour();
                _squallTourStep = 0; _squallTourCast = 0; _squallTourLatchTick = _squallTourPressTick = _squallTourEndTick = -1;
                _squallTourNext = tick + SquallTourSettleTicks;
                if (_squallTourReady) StartCoroutine(SquallTourProbe(Generation));
            }
            if (!_squallTourReady) return;
            var entities = Sim.Entities;
            // Нажатие держится на всех кадрах своего тика: CaptureStonehoofInput сбрасывает защёлку каждый кадр.
            if (tick == _squallTourLatchTick)
            {
                _pending.Aim = entities.Position[_squallTourTarget];
                _pending.AbilityTarget = _squallTourTarget;
                _abilityLatch = (byte)(1 << _squallTourSlot);
                return;
            }
            SquallState squall = Sim.Squall;
            switch (_squallTourStep)
            {
                case 0:
                    // Каст: места стоят, перезарядка прошла, герой свободен.
                    if (tick < _squallTourNext || Sim.AbilityReadyTick(_squallTourSlot) > tick || Sim.SquallActive) return;
                    SquallTourPress(tick);
                    _squallTourStep = 1;
                    return;
                case 1:
                    if (squall.Serial != _squallTourSerial) { _squallTourStep = 2; return; }
                    if (tick - _squallTourPressTick < SquallTourStartWaitTicks) return;
                    // Sim не принял каст (цель, Лавидий, перезарядка) — следующий через паузу, не виснем.
                    Debug.Log($"[squall-tour] refused cast={_squallTourCast} tick={tick} ready={Sim.AbilityReadyTick(_squallTourSlot)}"
                        + $" lav={entities.Lavidium[Simulation.PlayerId].ToFloat().ToString("F0", CultureInfo.InvariantCulture)}");
                    SquallTourAfterCast(tick);
                    return;
                case 2:
                    if (Sim.SquallActive) return;
                    _squallTourEndTick = tick;
                    Debug.Log($"[squall-tour] ended cast={_squallTourCast} tick={tick} t={SquallTourTime()}"
                        + $" ticks={tick - _squallTourPressTick} hero={SquallTourFlat(entities.Position[Simulation.PlayerId]).ToString("F2")}");
                    SquallTourAfterCast(tick);
                    return;
                case 3:
                    if (tick < _squallTourNext) return;
                    if (_squallTourCast >= _squallTourCasts)
                    {
                        _squallTourStep = 4;
                        Debug.Log($"[squall-tour] done tick={tick} t={SquallTourTime()} casts={_squallTourCasts}");
                        return;
                    }
                    PlaceSquallTour(_squallTourCast % 2 == 1);
                    _squallTourStep = 0;
                    _squallTourNext = tick + SquallTourResettleTicks;
                    return;
            }
        }

        private void SquallTourAfterCast(int tick)
        {
            _squallTourCast++;
            // Хвост: полосы пены живут 3 с, выход доигрывает; отпущенные враги идут по полосам.
            _squallTourStep = 3;
            _squallTourNext = tick + SquallTourTailTicks;
        }

        private static string SquallTourTime() => Time.time.ToString("F3", CultureInfo.InvariantCulture);

        /// <summary>Нажатие Шквала по первому месту: Лавидий полон, взгляд вдоль оси; держится на всех кадрах тика.</summary>
        private void SquallTourPress(int tick)
        {
            var entities = Sim.Entities;
            _squallTourTarget = -1;
            for (int k = 0; k < _squallTourIds.Length && _squallTourTarget < 0; k++)
            {
                int id = _squallTourIds[k];
                if (id > 0 && entities.Alive[id]) _squallTourTarget = id;
            }
            if (_squallTourTarget < 0) { _squallTourStep = 4; Debug.Log("[squall-tour] no target"); return; }
            entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(entities.MaxLavidium[Simulation.PlayerId]);
            SquallTourHeal();
            // -capture-squall-live: первый каст — чистый (замороженные), со второго враги отпущены.
            if (_squallTourLive && _squallTourCast >= 1) ReleaseSquallTour(tick);
            _squallTourSerial = Sim.Squall.Serial;
            _squallTourLatchTick = _squallTourPressTick = tick;
            _pending.Aim = entities.Position[_squallTourTarget];
            _pending.AbilityTarget = _squallTourTarget;
            _abilityLatch = (byte)(1 << _squallTourSlot);
            Debug.Log($"[squall-tour] press cast={_squallTourCast} mirror={(_squallTourCast % 2 == 1 ? 1 : 0)} tick={tick}"
                + $" t={SquallTourTime()} slot={_squallTourSlot} form={Sim.FormAt(_squallTourSlot)} target={_squallTourTarget}"
                + $" hero={SquallTourFlat(entities.Position[Simulation.PlayerId]).ToString("F2")}");
        }

        /// <summary>
        /// Стенд: Шквал и форма в наборе (как F8), оси от камеры, точка каста на свободной земле, места, заморозка.
        /// </summary>
        private bool SetupSquallTour()
        {
            var sim = Sim; var entities = sim.Entities;
            RunLoadout loadout = Session != null ? Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(PelagForm.SquallHunt);
            if (loadout == null || line < 0) { Debug.LogWarning("[squall-tour] нет набора забега"); return false; }
            // Набор съёмки держит Шквал в слоте 4; если его там нет — ставим туда же.
            if (!loadout.Owns(line) && !loadout.Put(RunLoadout.Slots - 1, line))
            { Debug.LogWarning("[squall-tour] Шквал не встал в набор"); return false; }
            PelagForm wanted = CaptureRig.SkillForm;
            _squallTourForm = PelagForms.LineOf(wanted) == line ? wanted : PelagForm.None;
            bool set = loadout.DebugSetForm(line, _squallTourForm);
            RefreshAbilityBuild();
            _squallTourSlot = loadout.SlotOf(line);
            if (_squallTourSlot < 0 || sim.GetAbility(_squallTourSlot)?.DefinitionId != AbilityDefinition.ChainStepId)
            { Debug.LogWarning("[squall-tour] слота Шквала нет"); return false; }

            string[] args = System.Environment.GetCommandLineArgs();
            _squallTourLive = System.Array.IndexOf(args, "-capture-squall-live") >= 0;
            _squallTourCasts = SquallTourDefaultCasts;
            int castsAt = System.Array.IndexOf(args, "-capture-squall-casts");
            if (castsAt >= 0 && castsAt + 1 < args.Length && int.TryParse(args[castsAt + 1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int casts))
                _squallTourCasts = Mathf.Clamp(casts, 1, SquallTourMaxCasts);

            // Оси кадра: «вдоль» — правая ось камеры на земле, «поперёк» — влево от неё (вверх по экрану).
            Camera view = Camera.main;
            Vector3 right = view != null ? view.transform.right : Vector3.right;
            _squallTourAlong = new Vector2(right.x, right.z);
            if (_squallTourAlong.sqrMagnitude < 1e-4f) _squallTourAlong = Vector2.right;
            _squallTourAlong.Normalize();
            _squallTourSide = new Vector2(-_squallTourAlong.y, _squallTourAlong.x);

            int count = 0;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                if (count < _squallTourIds.Length)
                {
                    _squallTourIds[count] = i;
                    _squallTourSpeed[count] = entities.Stats[i].GetBase(StatType.MoveSpeed);
                    _squallTourDamage[count] = entities.Stats[i].GetBase(StatType.Damage);
                    count++;
                }
                else
                {
                    // Лишние (больше шести) — далеко за краем кадра, заморожены.
                    entities.Position[i] = entities.Position[Simulation.PlayerId] + SquallTourFix(-_squallTourAlong * 40f);
                    SquallTourFreeze(i);
                }
            }
            for (int k = count; k < _squallTourIds.Length; k++) _squallTourIds[k] = -1;
            _squallTourOrigin = FindSquallTourOrigin(count);
            PlaceSquallTour(false);
            Debug.Log($"[squall-tour] layout form={_squallTourForm} set={set} slotForm={sim.FormAt(_squallTourSlot)} slot={_squallTourSlot}"
                + $" enemies={count} casts={_squallTourCasts} live={_squallTourLive} origin={_squallTourOrigin.ToString("F2")}"
                + $" along={_squallTourAlong.ToString("F2")} side={_squallTourSide.ToString("F2")}"
                + $" heroR={entities.BodyRadius[Simulation.PlayerId].ToFloat().ToString("F2", CultureInfo.InvariantCulture)}");
            return count > 0;
        }

        /// <summary>
        /// Точка каста: ближайшая к месту героя, где оба варианта раскладки (прямой и зеркальный) стоят на земле
        /// карты, а путь каст → места по порядку → обратно проходим. В Sim стенда Хранителей стен нет — проверка
        /// по карте вида, чтобы Хранитель не стоял в камне. Без карты — место героя.
        /// </summary>
        private Vector2 FindSquallTourOrigin(int count)
        {
            var entities = Sim.Entities; var map = Run != null ? Run.Map : null;
            Vector2 spawn = SquallTourFlat(entities.Position[Simulation.PlayerId]);
            if (map == null || count == 0) return spawn;
            Fix64 heroRadius = entities.BodyRadius[Simulation.PlayerId];
            Fix64 bodyRadius = entities.BodyRadius[_squallTourIds[0]] + Fix64.Ratio(1, 10);
            for (int ring = 0; ring <= 14; ring++)
                for (int x = -ring; x <= ring; x++)
                    for (int y = -ring; y <= ring; y++)
                    {
                        if (System.Math.Max(System.Math.Abs(x), System.Math.Abs(y)) != ring) continue;
                        _squallTourOrigin = spawn + new Vector2(x, y);
                        if (SquallTourFits(map, count, heroRadius, bodyRadius)) return _squallTourOrigin;
                    }
            Debug.LogWarning("[squall-tour] свободной земли под раскладку нет — точка героя");
            return spawn;
        }

        private bool SquallTourFits(LayoutMap map, int count, Fix64 heroRadius, Fix64 bodyRadius)
        {
            if (!map.IsWalkable(SquallTourFix(_squallTourOrigin), heroRadius)) return false;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector2 at = _squallTourOrigin;
                for (int k = 0; k < count && k < SquallTourSpots.Length; k++)
                {
                    Vector2 spot = SquallTourSpot(k, pass == 1);
                    if (!map.IsWalkable(SquallTourFix(spot), bodyRadius)) return false;
                    if (k < 4 && !map.CanTravel(SquallTourFix(at), SquallTourFix(spot), heroRadius)) return false;
                    if (k < 4) at = spot;
                }
                if (!map.CanTravel(SquallTourFix(at), SquallTourFix(_squallTourOrigin), heroRadius)) return false;
            }
            return true;
        }

        /// <summary>Все на места: герой в точку каста лицом вдоль оси, живые враги по порядку мест, здоровье, заморозка.</summary>
        private void PlaceSquallTour(bool mirror)
        {
            var entities = Sim.Entities;
            entities.Position[Simulation.PlayerId] = SquallTourFix(_squallTourOrigin);
            entities.Facing[Simulation.PlayerId] = SquallTourFix(_squallTourAlong);
            for (int k = 0; k < _squallTourSpotOf.Length; k++) _squallTourSpotOf[k] = -1;
            int spot = 0;
            for (int k = 0; k < _squallTourIds.Length; k++)
            {
                int id = _squallTourIds[k];
                if (id <= 0 || !entities.Alive[id]) continue;
                Vector2 at = SquallTourSpot(spot, mirror);
                entities.Position[id] = SquallTourFix(at);
                entities.Facing[id] = SquallTourFix((_squallTourOrigin - at).normalized);
                SquallTourFreeze(id);
                _squallTourSpotOf[k] = spot;
                Debug.Log($"[squall-tour] place cast={_squallTourCast} spot={spot} id={id} at={at.ToString("F2")}");
                spot++;
            }
            Sim.Grid.Rebuild(entities);
        }

        /// <summary>
        /// Здоровье по местам — в тик нажатия, а не при расстановке: запас стенда (PrepareEnemyCase, 5000)
        /// приходит кадром позже первой расстановки и перезаписал бы раны Охоты.
        /// </summary>
        private void SquallTourHeal()
        {
            var entities = Sim.Entities;
            for (int k = 0; k < _squallTourIds.Length; k++)
            {
                int id = _squallTourIds[k], spot = _squallTourSpotOf[k];
                if (id <= 0 || spot < 0 || !entities.Alive[id]) continue;
                int max = entities.MaxHealth[id];
                float share = _squallTourForm == PelagForm.SquallHunt ? SquallTourHuntHealth[spot] : 1f;
                entities.Health[id] = share < 0f ? System.Math.Min(max, SquallTourKillHealth) : System.Math.Max(1, Mathf.RoundToInt(max * share));
                Debug.Log($"[squall-tour] hp cast={_squallTourCast} spot={spot} id={id} hp={entities.Health[id]}/{max}");
            }
        }

        private void SquallTourFreeze(int id)
        {
            var entities = Sim.Entities;
            entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            entities.Stats[id].SetBase(StatType.Damage, Fix64.Zero);
            entities.RefreshStats(id);
            entities.NextAttackTick[id] = int.MaxValue;
        }

        /// <summary>-capture-squall-live: нажатие отпускает врагов — штатный шаг, урон и замах.</summary>
        private void ReleaseSquallTour(int tick)
        {
            var entities = Sim.Entities;
            for (int k = 0; k < _squallTourIds.Length; k++)
            {
                int id = _squallTourIds[k];
                if (id <= 0 || !entities.Alive[id]) continue;
                entities.Stats[id].SetBase(StatType.MoveSpeed, _squallTourSpeed[k]);
                entities.Stats[id].SetBase(StatType.Damage, _squallTourDamage[k]);
                entities.RefreshStats(id);
                entities.NextAttackTick[id] = tick;
            }
        }

        /// <summary>Каждый кадр после отрисовки: события Шквала и, вокруг каста, клип, фаза, корень, стопы, таз.</summary>
        private IEnumerator SquallTourProbe(int generation)
        {
            var wait = new WaitForEndOfFrame();
            string output = null;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-out") output = args[i + 1];
            string frames = output != null ? Path.Combine(output, "video_frames") : null;
            int videoFrame = 0;
            var arena = GetComponent<ArenaView>();
            Transform body = null, hips = null, chest = null, leftFoot = null, rightFoot = null, leftToe = null;
            Animator animator = null;
            var line = new StringBuilder(512);
            while (Generation == generation && _squallTourStep < SquallTourDone)
            {
                yield return wait;
                if (frames != null)
                    while (File.Exists(Path.Combine(frames, "frame_" + videoFrame.ToString("0000", CultureInfo.InvariantCulture) + ".jpg"))) videoFrame++;
                var sim = Sim;
                if (sim == null) continue;
                var contexts = FrameEventContexts;
                for (int i = 0; i < contexts.Count; i++)
                {
                    SimEvent e = contexts[i].Event;
                    bool squall = e.Type >= SimEventType.SquallJump && e.Type <= SimEventType.SquallFoamStrip;
                    bool death = e.Type == SimEventType.Death && e.Target > Simulation.PlayerId;
                    if (!squall && !death) continue;
                    Debug.Log($"[squall-tour] ev vf={videoFrame} t={SquallTourTime()} tick={contexts[i].SimulationTick} {e.Type}"
                        + $" tgt={e.Target} amount={e.Amount} flag={(e.Flag ? 1 : 0)} variant={e.ActionVariant}"
                        + $" pos={SquallTourFlat(e.Position).ToString("F2")}");
                }
                if (_squallTourStep == 4) { _squallTourStep = SquallTourDone; break; }
                int tick = sim.Tick;
                bool near = sim.SquallActive || (_squallTourPressTick >= 0 && tick >= _squallTourPressTick - SquallTourProbeLeadTicks
                    && (_squallTourEndTick < _squallTourPressTick || tick <= _squallTourEndTick + SquallTourProbeTailTicks));
                if (!near || arena == null || !arena.TryGetEntityView(Simulation.PlayerId, out Transform found)) continue;
                if (found != body)
                {
                    body = found;
                    hips = chest = leftFoot = rightFoot = leftToe = null;
                    foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                        switch (bone.name)
                        {
                            case "mixamorig:Hips": hips = bone; break;
                            case "mixamorig:Spine2": chest = bone; break;
                            case "mixamorig:LeftFoot": leftFoot = bone; break;
                            case "mixamorig:RightFoot": rightFoot = bone; break;
                            case "mixamorig:LeftToeBase": leftToe = bone; break;
                        }
                    animator = body.GetComponentInChildren<Animator>();
                    Debug.Log($"[squall-probe] bones hips={hips != null} chest={chest != null} feet={leftFoot != null}/{rightFoot != null}"
                        + $" toe={leftToe != null} animator={animator != null}");
                }
                SquallState s = sim.Squall;
                Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
                forward.Normalize();
                line.Clear();
                line.Append("[squall-probe] vf=").Append(videoFrame).Append(" t=").Append(SquallTourTime())
                    .Append(" tick=").Append(tick).Append(" a=").Append(Alpha.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" cast=").Append(_squallTourCast).Append(" ph=").Append(s.Phase).Append(" idx=").Append(s.Index)
                    .Append(" bh=").Append(s.Backhand ? 1 : 0).Append(" tgt=").Append(s.Target).Append(" next=").Append(s.NextTarget)
                    .Append(" fly=").Append(s.FlightStartTick).Append('-').Append(s.ArriveTick);
                if (animator != null)
                {
                    var clips = animator.GetCurrentAnimatorClipInfo(0);
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    line.Append(" L0=").Append(clips.Length > 0 ? clips[0].clip.name : "-").Append('@')
                        .Append(state.normalizedTime.ToString("F2", CultureInfo.InvariantCulture));
                    if (animator.IsInTransition(0))
                    {
                        var next = animator.GetNextAnimatorClipInfo(0);
                        line.Append("->").Append(next.Length > 0 ? next[0].clip.name : "-");
                    }
                }
                AppendProbe(line, "root", body.position);
                line.Append(" yaw=").Append((Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg).ToString("F1", CultureInfo.InvariantCulture));
                if (leftFoot != null) AppendProbe(line, "lf", leftFoot.position);
                if (rightFoot != null) AppendProbe(line, "rf", rightFoot.position);
                if (leftToe != null) AppendProbe(line, "lt", leftToe.position);
                if (hips != null) AppendProbe(line, "hips", hips.position);
                if (chest != null) AppendProbe(line, "chest", chest.position);
                Camera shot = Camera.main;
                if (shot != null)
                {
                    // Точка кадра (доли, y сверху) на высоте таза — центр выреза листов.
                    Vector3 viewport = shot.WorldToViewportPoint(body.position + Vector3.up * .8f);
                    line.Append(" scr=(").Append(viewport.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append((1f - viewport.y).ToString("F3", CultureInfo.InvariantCulture)).Append(')');
                }
                Debug.Log(line.ToString());
            }
        }
    }
}
