using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ТОЛЬКО СЪЁМКА: обход рывка Пелага (02.10) одним прогоном изолированного плеера.
    /// Без ключа -capture-enemy-case dash этот код не вызывается (ветка case "dash" в
    /// TickDriver.Stonehoof.cs), обычная игра и редактор его не видят.
    ///
    /// Стенд Хранителей (стен в Sim нет: SetupTestArena):
    ///   capture.ps1 -Encounter forest-guardian -Enemies 6 -ExtraArgs '-capture-pack','-capture-enemy-case','dash'
    ///   восемь рывков с места по восьмиугольнику со стороной 4 м (все восемь направлений), разбег и рывок
    ///   сквозь пачку из пяти Хранителей с бегом дальше, рывок к шестому и сразу зажатая атака (серия сабли).
    /// Стенд Камнекопыта (поляна со стенами):
    ///   capture.ps1 -Encounter forest-stonehoof -Enemies 1 -ExtraArgs '-capture-enemy-case','dash'
    ///   тот же восьмиугольник и рывок с места в стену с 1,5 м — ранняя остановка.
    /// Враги заморожены: не ходят и не бьют. Каждый кадр после WaitForEndOfFrame пишет строку
    /// [dash-probe]: клип базового слоя, кости стоп, головы и груди, голова пенного следа.
    /// </summary>
    public sealed partial class TickDriver
    {
        private const int DashTourSettleTicks = 45, DashTourOctagonGapTicks = 54, DashTourWallWaitTicks = 40;
        private const int DashTourComboAttackDelay = 2, DashTourComboAttackTicks = 80, DashTourTailTicks = 40;
        private const double DashTourSide = 4.0, DashTourWallGap = 1.5, DashTourOctagonStart = 17.5;
        private const double DashTourPackPress = 13.75, DashTourComboDistance = 6.2;
        private static readonly double[] DashTourPackAlong = { 12.0, 12.0, 12.0, 10.5, 10.5 };
        private static readonly double[] DashTourPackLateral = { 0.0, 2.6, -2.6, 1.3, -1.3 };

        private int _dashTourGeneration = -1, _dashTourStep, _dashTourNext, _dashTourPress = -1, _dashTourCombo = -1;
        private int _dashTourLatchTick = -1;
        private byte _dashTourLatchFlags;
        private bool _dashTourReady, _dashTourWalls, _dashTourPack;
        private Vector2 _dashTourWall, _dashTourStop, _dashTourTarget, _dashTourLatchAim;
        private readonly Vector2[] _dashTourOctagon = new Vector2[9];

        private static FixVec2 DashTourFix(Vector2 v) => new FixVec2(Fix64.FromDouble(v.x), Fix64.FromDouble(v.y));
        private static Vector2 DashTourFlat(FixVec2 v) => new Vector2(v.X.ToFloat(), v.Y.ToFloat());
        private static Vector2 DashTourRotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private void CaptureDashTour(int tick)
        {
            // Стык рывок → бег (06.10, ключ -capture-dash-run): TickDriver.DashRunCapture.cs.
            if (DashRunRequested) { CaptureDashRunTour(tick); return; }
            var entities = Sim.Entities;
            if (_dashTourGeneration != Generation)
            {
                _dashTourGeneration = Generation;
                _dashTourReady = SetupDashTour();
                _dashTourStep = 0; _dashTourNext = tick + DashTourSettleTicks; _dashTourPress = _dashTourLatchTick = -1;
                if (_dashTourReady) StartCoroutine(DashTourProbe(Generation));
            }
            if (!_dashTourReady) return;
            // Нажатие держится на всех кадрах своего тика: CaptureStonehoofInput сбрасывает защёлку
            // каждый кадр, а тик Sim шагает не на каждом кадре.
            if (tick == _dashTourLatchTick)
            {
                _pending.Aim = DashTourFix(_dashTourLatchAim); _pending.Flags = _dashTourLatchFlags;
                _abilityLatch = (byte)(1 << PelagKit.DashSlot);
                return;
            }
            Vector2 hero = DashTourFlat(entities.Position[Simulation.PlayerId]);
            bool ready = Sim.AbilityReadyTick(PelagKit.DashSlot) <= tick && !Sim.PelagDash.Moving;
            Vector2 wallPoint = _dashTourStop - _dashTourWall * (float)DashTourWallGap;
            switch (_dashTourStep)
            {
                case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7:
                    // 1) Восьмиугольник: рывок с места к следующей вершине.
                    if (tick < _dashTourNext || !ready) return;
                    DashTourPress(tick, _dashTourOctagon[_dashTourStep + 1], 0, "octagon-" + _dashTourStep);
                    _dashTourStep++;
                    _dashTourNext = tick + DashTourOctagonGapTicks;
                    return;
                case 8:
                    if (tick < _dashTourNext) return;
                    if (_dashTourPack)
                    {
                        // 2) Разбег к стене, рывок сквозь пачку, бег дальше до точки у стены.
                        MoveCaptured(DashTourFix(wallPoint));
                        if (Vector2.Dot(_dashTourStop - hero, _dashTourWall) <= DashTourPackPress && ready)
                        {
                            DashTourPress(tick, wallPoint, (byte)InputFlags.MoveOrder, "pack");
                            _dashTourStep = 9;
                        }
                        return;
                    }
                    if (_dashTourWalls)
                    {
                        DashTourTeleport(wallPoint, _dashTourWall, "wall-start");
                        _dashTourStep = 10; _dashTourNext = tick + DashTourSettleTicks;
                    }
                    else { _dashTourStep = 11; _dashTourNext = tick; }
                    return;
                case 9:
                    MoveCaptured(DashTourFix(wallPoint));
                    if (((hero - wallPoint).sqrMagnitude < .25f || tick - _dashTourPress > 150) && !Sim.PelagDash.Moving
                        && entities.Velocity[Simulation.PlayerId].LengthSq.Raw == 0)
                    {
                        Debug.Log($"[dash-tour] stopped tick={tick} hero={hero.ToString("F2")} walls={_dashTourWalls}");
                        _dashTourStep = _dashTourWalls ? 10 : 11;
                        _dashTourNext = tick + DashTourWallWaitTicks;
                    }
                    return;
                case 10:
                    // 3) Рывок с места в стену: 1,5 м до неё, тело встаёт раньше полной дальности.
                    if (tick < _dashTourNext || !ready) return;
                    DashTourPress(tick, _dashTourStop + _dashTourWall * 3f, 0, "wall");
                    _dashTourStep = 11; _dashTourNext = tick + 36;
                    return;
                case 11:
                    if (tick < _dashTourNext || !ready) return;
                    if (_dashTourCombo <= 0 || !entities.Alive[_dashTourCombo])
                    { _dashTourStep = 13; _dashTourNext = tick + DashTourTailTicks; return; }
                    // 4) Рывок к Хранителю и сразу атака: серия сабли после постановки.
                    DashTourPress(tick, DashTourFlat(entities.Position[_dashTourCombo]), 0, "combo");
                    _dashTourStep = 12; _dashTourNext = tick + DashTourComboAttackDelay;
                    return;
                case 12:
                    if (tick < _dashTourNext) return;
                    if (tick < _dashTourNext + DashTourComboAttackTicks && entities.Alive[_dashTourCombo])
                    {
                        _pending.Flags = (byte)InputFlags.Attack; _pending.AttackTarget = _dashTourCombo;
                        _pending.Aim = entities.Position[_dashTourCombo]; AttackHeld = true;
                        return;
                    }
                    _dashTourStep = 13; _dashTourNext = tick + DashTourTailTicks;
                    Debug.Log($"[dash-tour] combo-released tick={tick}");
                    return;
                case 13:
                    if (tick < _dashTourNext) return;
                    _dashTourStep = 14;
                    Debug.Log($"[dash-tour] done tick={tick}");
                    return;
            }
        }

        /// <summary>Нажатие рывка: прицел в точку, Лавидий полон; держится на всех кадрах тика.</summary>
        private void DashTourPress(int tick, Vector2 aim, byte flags, string label)
        {
            var entities = Sim.Entities;
            entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(entities.MaxLavidium[Simulation.PlayerId]);
            _dashTourLatchTick = tick; _dashTourLatchAim = aim; _dashTourLatchFlags = flags;
            _pending.Aim = DashTourFix(aim); _pending.Flags = flags;
            _abilityLatch = (byte)(1 << PelagKit.DashSlot);
            _dashTourPress = tick;
            Vector2 hero = DashTourFlat(entities.Position[Simulation.PlayerId]);
            Debug.Log($"[dash-tour] press {label} tick={tick} t={Time.time.ToString("F3", CultureInfo.InvariantCulture)}"
                + $" hero={hero.ToString("F2")} aim={aim.ToString("F2")} dir={(aim - hero).normalized.ToString("F2")}");
        }

        private void DashTourTeleport(Vector2 point, Vector2 facing, string label)
        {
            var entities = Sim.Entities;
            entities.Position[Simulation.PlayerId] = DashTourFix(point);
            entities.Facing[Simulation.PlayerId] = DashTourFix(facing.normalized);
            Sim.Grid.Rebuild(entities);
            Debug.Log($"[dash-tour] teleport {label} to={point.ToString("F2")} facing={facing.normalized.ToString("F2")}");
        }

        /// <summary>
        /// Раскладка обхода: стена и коридор от неё (направления кратны 45° — восемь направлений
        /// экрана), пачка в коридоре, шестой Хранитель сбоку у стены, восьмиугольник за коридором
        /// или, если там тесно, на свободном месте поляны. Враги заморожены.
        /// </summary>
        private bool SetupDashTour()
        {
            var sim = Sim; var entities = sim.Entities; var map = Run != null ? Run.Map : null;
            if (map == null) { Debug.LogWarning("[dash-tour] нет карты стенда"); return false; }
            if (sim.GetAbility(PelagKit.DashSlot)?.DefinitionId != AbilityDefinition.DashId)
                sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), System.Array.Empty<AbilityNode>(), 0);
            // Хранители стоят на SetupTestArena — стен в Sim нет; Камнекопыт и новые мобы — на карте поляны.
            _dashTourWalls = !CaptureRig.GuardianShowcase;
            Fix64 heroRadius = entities.BodyRadius[Simulation.PlayerId];
            Fix64 guardRadius = entities.Count > 1 ? entities.BodyRadius[1] : Fix64.Ratio(85, 100);
            int enemies = 0;
            for (int i = 1; i < entities.Count; i++) if (entities.Alive[i] && entities.Side[i] != Faction.Wole) enemies++;
            bool packCount = enemies >= DashTourPackAlong.Length;
            Vector2 spawn = DashTourFlat(entities.Position[Simulation.PlayerId]);
            bool Travel(Vector2 a, Vector2 b) => map.CanTravel(DashTourFix(a), DashTourFix(b), heroRadius);
            bool Free(Vector2 p, Fix64 r) => map.IsWalkable(DashTourFix(p), r + Fix64.Ratio(1, 10));

            int bestScore = -1; Vector2 bestWall = Vector2.zero, bestStop = Vector2.zero; float bestSide = 1f;
            for (int pass = 0; pass < 2; pass++)
                for (int k = pass; k < 16; k += 2)
                    for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                    {
                        float side = sideIndex == 0 ? 1f : -1f;
                        Vector2 d = new Vector2(Mathf.Cos(k * Mathf.PI / 8f), Mathf.Sin(k * Mathf.PI / 8f));
                        float lo = 0f, hi = 0f;
                        for (float s = .25f; s <= 40f; s += .25f)
                        {
                            if (!Travel(spawn, spawn + d * s)) { hi = s; break; }
                            lo = s;
                        }
                        if (hi <= 0f || lo < 2f) continue;
                        for (int i = 0; i < 8; i++)
                        {
                            float mid = (lo + hi) * .5f;
                            if (Travel(spawn, spawn + d * mid)) lo = mid; else hi = mid;
                        }
                        Vector2 stop = spawn + d * lo;
                        if (!Travel(stop - d * 6f, stop)) continue;
                        // Нечётные направления берутся, только если чётные заметно хуже.
                        int score = DashTourLayoutScore(stop, d, side, Travel, Free, guardRadius, packCount) * 2 - pass;
                        if (score > bestScore) { bestScore = score; bestWall = d; bestStop = stop; bestSide = side; }
                    }
            if (bestScore < 0) { Debug.LogWarning("[dash-tour] стена не найдена"); return false; }
            _dashTourWall = bestWall; _dashTourStop = bestStop;
            Vector2 lateral = new Vector2(-bestWall.y, bestWall.x);
            Vector2 X(double s) => bestStop - bestWall * (float)s;
            Vector2 e = lateral * bestSide;
            _dashTourPack = packCount && DashTourCorridorOk(bestStop, bestWall, Travel, Free, guardRadius);

            // Восьмиугольник: вершина 0 на оси коридора, центр — от стены; иначе свободное место поляны.
            Vector2 start = X(DashTourOctagonStart);
            if (!DashTourOctagonFits(start, e, bestSide, Travel))
            {
                _dashTourPack = false;
                float best = float.MaxValue;
                for (int x = -18; x <= 18; x++)
                    for (int y = -18; y <= 18; y++)
                    {
                        Vector2 candidate = spawn + new Vector2(x, y);
                        if (!DashTourOctagonFits(candidate, e, bestSide, Travel)) continue;
                        bool clear = true;
                        for (int i = 1; i < entities.Count && clear; i++)
                        {
                            if (!entities.Alive[i]) continue;
                            Vector2 enemy = DashTourFlat(entities.Position[i]);
                            Vector2 at = candidate;
                            for (int n = 0; n < 8 && clear; n++)
                            {
                                Vector2 next = at + DashTourRotate(e, bestSide * 45f * n) * (float)DashTourSide;
                                clear = (enemy - at).magnitude > 2.5f && (enemy - (at + next) * .5f).magnitude > 2.5f;
                                at = next;
                            }
                        }
                        float distance = (candidate - spawn).sqrMagnitude;
                        if (clear && distance < best) { best = distance; start = candidate; }
                    }
            }
            _dashTourOctagon[0] = start;
            for (int i = 0; i < 8; i++)
                _dashTourOctagon[i + 1] = _dashTourOctagon[i] + DashTourRotate(e, bestSide * 45f * i) * (float)DashTourSide;
            // Цель серии — в 6,2 м от точки, откуда рывок к ней начнётся: у стены (без стен в Sim
            // герой там и стоит после бега) — рывок кончается в 2,2 м от тела, в досягаемости сабли.
            Vector2 comboFrom = _dashTourWalls ? bestStop : X(DashTourWallGap);
            _dashTourTarget = comboFrom + DashTourRotate(-bestWall, -bestSide * 40f) * (float)DashTourComboDistance;

            entities.Position[Simulation.PlayerId] = DashTourFix(_dashTourOctagon[0]);
            entities.Facing[Simulation.PlayerId] = DashTourFix(e);
            // Хранители: пять в пачку поперёк коридора, шестой сбоку у стены — цель серии.
            // Без пачки (Камнекопыт) враг стоит, где поставил стенд.
            int placed = 0; _dashTourCombo = -1;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                if (_dashTourPack)
                {
                    Vector2 spot;
                    if (placed < DashTourPackAlong.Length)
                        spot = X(DashTourPackAlong[placed]) + lateral * (float)DashTourPackLateral[placed];
                    else if (_dashTourCombo < 0) { spot = _dashTourTarget; _dashTourCombo = i; }
                    else spot = X(DashTourOctagonStart + 40);
                    entities.Position[i] = DashTourFix(spot);
                    entities.Facing[i] = DashTourFix((_dashTourOctagon[0] - spot).normalized);
                }
                placed++;
                entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                entities.Stats[i].SetBase(StatType.Damage, Fix64.Zero);
                entities.RefreshStats(i);
                entities.NextAttackTick[i] = int.MaxValue;
            }
            sim.Grid.Rebuild(entities);
            Debug.Log($"[dash-tour] layout score={bestScore} walls={_dashTourWalls} pack={_dashTourPack} spawn={spawn.ToString("F2")}"
                + $" wallDir={bestWall.ToString("F2")} stop={bestStop.ToString("F2")} side={bestSide}"
                + $" heroR={heroRadius.ToFloat().ToString("F2", CultureInfo.InvariantCulture)} guardR={guardRadius.ToFloat().ToString("F2", CultureInfo.InvariantCulture)}"
                + $" octagon0={_dashTourOctagon[0].ToString("F2")} target={_dashTourTarget.ToString("F2")} combo={_dashTourCombo} enemies={placed}");
            return true;
        }

        private static bool DashTourOctagonFits(Vector2 start, Vector2 e, float side, System.Func<Vector2, Vector2, bool> travel)
        {
            Vector2 at = start;
            for (int i = 0; i < 8; i++)
            {
                Vector2 next = at + DashTourRotate(e, side * 45f * i) * (float)DashTourSide;
                if (!travel(at, next)) return false;
                at = next;
            }
            return true;
        }

        private static bool DashTourCorridorOk(Vector2 stop, Vector2 d, System.Func<Vector2, Vector2, bool> travel,
            System.Func<Vector2, Fix64, bool> free, Fix64 guardRadius)
        {
            Vector2 lateral = new Vector2(-d.y, d.x);
            Vector2 X(double s) => stop - d * (float)s;
            if (!travel(X(DashTourOctagonStart), X(0))) return false;
            for (int i = 0; i < DashTourPackAlong.Length; i++)
                if (!free(X(DashTourPackAlong[i]) + lateral * (float)DashTourPackLateral[i], guardRadius)) return false;
            return true;
        }

        private static int DashTourLayoutScore(Vector2 stop, Vector2 d, float side,
            System.Func<Vector2, Vector2, bool> travel, System.Func<Vector2, Fix64, bool> free, Fix64 guardRadius, bool pack)
        {
            Vector2 lateral = new Vector2(-d.y, d.x);
            Vector2 X(double s) => stop - d * (float)s;
            int score = 0;
            if (pack && DashTourCorridorOk(stop, d, travel, free, guardRadius)) score += 4;
            Vector2 from = X(DashTourWallGap);
            Vector2 target = from + DashTourRotate(-d, -side * 40f) * (float)DashTourComboDistance;
            if (free(target, guardRadius) && travel(from, from + (target - from).normalized * 4f)) score++;
            if (DashTourOctagonFits(X(DashTourOctagonStart), lateral * side, side, travel)) score += 2;
            return score;
        }

        /// <summary>Каждый кадр после отрисовки: клип, кости, голова следа (строки [dash-probe]).</summary>
        private IEnumerator DashTourProbe(int generation)
        {
            var wait = new WaitForEndOfFrame();
            string output = null;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-out") output = args[i + 1];
            string frames = output != null ? Path.Combine(output, "video_frames") : null;
            float shotWidth = 1920f, shotHeight = 1080f;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-capture-width") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out shotWidth);
                if (args[i] == "-capture-height") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out shotHeight);
            }
            int videoFrame = 0;
            var arena = GetComponent<ArenaView>();
            Transform body = null, hips = null, chest = null, neck = null, head = null, top = null,
                leftFoot = null, rightFoot = null, leftToe = null, rightToe = null;
            Animator animator = null; Renderer anchor = null; bool dashParameter = false;
            MeshFilter[] wakes = null; int wakeSerial = -1;
            var line = new StringBuilder(512);
            while (Generation == generation && _dashTourStep < 14)
            {
                yield return wait;
                if (frames != null)
                    while (File.Exists(Path.Combine(frames, "frame_" + videoFrame.ToString("0000", CultureInfo.InvariantCulture) + ".jpg"))) videoFrame++;
                var sim = Sim;
                if (sim == null || arena == null || !arena.TryGetEntityView(Simulation.PlayerId, out Transform view)) continue;
                if (view != body)
                {
                    body = view;
                    hips = chest = neck = head = top = leftFoot = rightFoot = leftToe = rightToe = null; anchor = null;
                    foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                        switch (bone.name)
                        {
                            case "mixamorig:Hips": hips = bone; break;
                            case "mixamorig:Spine2": chest = bone; break;
                            case "mixamorig:Neck": neck = bone; break;
                            case "mixamorig:Head": head = bone; break;
                            case "mixamorig:HeadTop_End": top = bone; break;
                            case "mixamorig:LeftFoot": leftFoot = bone; break;
                            case "mixamorig:RightFoot": rightFoot = bone; break;
                            case "mixamorig:LeftToeBase": leftToe = bone; break;
                            case "mixamorig:RightToeBase": rightToe = bone; break;
                            case "Pelag_AnchorGrip_Equipped": anchor = bone.GetComponent<Renderer>(); break;
                        }
                    animator = body.GetComponentInChildren<Animator>();
                    dashParameter = false;
                    if (animator != null)
                        foreach (var parameter in animator.parameters) if (parameter.name == "DashPhase") dashParameter = true;
                    Debug.Log($"[dash-probe] bones hips={hips != null} chest={chest != null} neck={neck != null} head={head != null}"
                        + $" top={top != null} feet={leftFoot != null}/{rightFoot != null} toes={leftToe != null}/{rightToe != null}"
                        + $" anchor={anchor != null} animator={animator != null} dashPhase={dashParameter}");
                }
                PelagDashState dash = sim.PelagDash;
                Vector3 root = body.position;
                Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                line.Clear();
                line.Append("[dash-probe] vf=").Append(videoFrame).Append(" t=").Append(Time.time.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" tick=").Append(sim.Tick).Append(" a=").Append(Alpha.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" step=").Append(_dashTourStep).Append(" serial=").Append(dash.Serial)
                    .Append(" moving=").Append(dash.Moving ? 1 : 0).Append(" stopTick=").Append(dash.StopTick)
                    .Append(" cut=").Append(dash.CutShort ? 1 : 0);
                if (animator != null)
                {
                    for (int layer = 0; layer < animator.layerCount; layer++)
                    {
                        float weight = layer == 0 ? 1f : animator.GetLayerWeight(layer);
                        if (weight < .05f) continue;
                        var clips = animator.GetCurrentAnimatorClipInfo(layer);
                        string name = clips.Length > 0 ? clips[0].clip.name : "-";
                        var state = animator.GetCurrentAnimatorStateInfo(layer);
                        line.Append(" L").Append(layer).Append('=').Append(name).Append('@')
                            .Append(state.normalizedTime.ToString("F2", CultureInfo.InvariantCulture))
                            .Append('w').Append(weight.ToString("F2", CultureInfo.InvariantCulture));
                        if (animator.IsInTransition(layer))
                        {
                            var next = animator.GetNextAnimatorClipInfo(layer);
                            line.Append("->").Append(next.Length > 0 ? next[0].clip.name : "-");
                        }
                    }
                    if (dashParameter) line.Append(" ph=").Append(animator.GetFloat("DashPhase").ToString("F3", CultureInfo.InvariantCulture));
                }
                AppendProbe(line, "root", root);
                Camera shot = Camera.main;
                if (shot != null)
                {
                    // Точка кадра (пиксели снимка, y сверху) на высоте таза — центр выреза листов.
                    Vector3 viewport = shot.WorldToViewportPoint(root + Vector3.up * .8f);
                    line.Append(" scr=(").Append((viewport.x * shotWidth).ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                        .Append(((1f - viewport.y) * shotHeight).ToString("F0", CultureInfo.InvariantCulture)).Append(')');
                }
                line.Append(" fwd=(").Append(forward.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(forward.z.ToString("F2", CultureInfo.InvariantCulture)).Append(')');
                if (leftFoot != null) AppendProbe(line, "lf", leftFoot.position);
                if (rightFoot != null) AppendProbe(line, "rf", rightFoot.position);
                if (leftToe != null) AppendProbe(line, "lt", leftToe.position);
                if (rightToe != null) AppendProbe(line, "rt", rightToe.position);
                if (hips != null) AppendProbe(line, "hips", hips.position);
                if (head != null) AppendProbe(line, "head", head.position);
                if (top != null) AppendProbe(line, "top", top.position);
                if (chest != null && neck != null && head != null)
                {
                    Vector3 chestUp = (neck.position - chest.position).normalized;
                    Vector3 headUp = ((top != null ? top.position : head.position + (head.position - neck.position)) - head.position).normalized;
                    line.Append(" pitchRel=").Append(Vector3.SignedAngle(chestUp, headUp, right).ToString("F1", CultureInfo.InvariantCulture))
                        .Append(" chestLean=").Append(Vector3.SignedAngle(Vector3.up, chestUp, right).ToString("F1", CultureInfo.InvariantCulture))
                        .Append(" headLean=").Append(Vector3.SignedAngle(Vector3.up, headUp, right).ToString("F1", CultureInfo.InvariantCulture));
                }
                if (anchor != null && anchor.enabled && anchor.gameObject.activeInHierarchy)
                {
                    Bounds b = anchor.bounds;
                    line.Append(" anchorTop=").Append(b.max.y.ToString("F2", CultureInfo.InvariantCulture));
                    AppendProbe(line, "anchorC", b.center);
                }
                if (dash.Serial != 0)
                {
                    Vector3 from = new Vector3(dash.From.X.ToFloat(), root.y, dash.From.Y.ToFloat());
                    Vector3 dir = new Vector3(dash.Direction.X.ToFloat(), 0f, dash.Direction.Y.ToFloat());
                    line.Append(" dfrom=(").Append(from.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                        .Append(from.z.ToString("F2", CultureInfo.InvariantCulture)).Append(") ddir=(")
                        .Append(dir.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(dir.z.ToString("F3", CultureInfo.InvariantCulture)).Append(')');
                    if (wakes == null || wakeSerial != dash.Serial)
                    {
                        wakeSerial = dash.Serial;
                        var all = FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                        int count = 0;
                        foreach (var filter in all) if (filter.sharedMesh != null && filter.sharedMesh.name == "Рывок: пенный след") count++;
                        wakes = new MeshFilter[count];
                        count = 0;
                        foreach (var filter in all) if (filter.sharedMesh != null && filter.sharedMesh.name == "Рывок: пенный след") wakes[count++] = filter;
                    }
                    MeshFilter best = null; float bestDistance = float.MaxValue;
                    foreach (var filter in wakes)
                    {
                        if (filter == null || !filter.gameObject.activeInHierarchy) continue;
                        float distance = (filter.transform.position - from).sqrMagnitude;
                        if (distance < bestDistance) { bestDistance = distance; best = filter; }
                    }
                    if (best != null && bestDistance < .25f)
                    {
                        Bounds local = best.sharedMesh.bounds;
                        AppendProbe(line, "wakeHead", best.transform.TransformPoint(new Vector3(0f, 0f, local.max.z)));
                    }
                }
                Debug.Log(line.ToString());
            }
        }

        private static void AppendProbe(StringBuilder line, string name, Vector3 value)
        {
            line.Append(' ').Append(name).Append("=(")
                .Append(value.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(value.z.ToString("F3", CultureInfo.InvariantCulture)).Append(')');
        }
    }
}
