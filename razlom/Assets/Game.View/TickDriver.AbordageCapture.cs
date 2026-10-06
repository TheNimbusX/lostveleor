using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ТОЛЬКО СЪЁМКА: стенд Абордажа v2 (02.10) одним прогоном изолированного плеера. Без ключа
    /// -capture-enemy-case abordage этот код не вызывается (ветка case "abordage" в TickDriver.Stonehoof.cs),
    /// обычная игра и редактор его не видят.
    ///
    ///   capture.ps1 -Encounter forest-guardian -Enemies 6 -ExtraArgs '-capture-pack','-capture-enemy-case','abordage'
    ///       [,'-capture-skill-form','abordage:quake|geyser|breach'] [,'-capture-abordage-casts','N'] [,'-capture-abordage-aim']
    ///
    /// Каст — как у игрока мышью (выбор врага как у Шквала, владелец 02.10 ~23:00): режим прицела на слоте, наведение
    /// на цель и подтверждение тем же ResolveTargetAim, что и ЛКМ, — InputFrame.AbilityTarget = цель. С
    /// -capture-abordage-aim прицел с наведением держится AbordageTourAimTicks до нажатия: HUD (-Hud) рисует кольцо
    /// дальности, пунктир к посадке и подсветку цели, как у игрока.
    ///
    /// Раскладка на каждый каст своя (<see cref="AbordageTourCasts"/>), в осях каста: цель на дистанции из таблицы
    /// спеки, рядом три свидетеля (<see cref="AbordageTourWitness"/>): сбоку — кольцо Обвала и падение Гейзера; за
    /// спиной близко и далеко — конус Пробоины (и падение Гейзера); четвёртый — за героем. Роли целей: лёгкий
    /// Хранитель (5 м, потом 2 м), тяжёлый — Камнекопыт (3,5 м; стенд добавляет его сам), элита — Хранитель с
    /// меткой Sim.MarkElite (6,8 м: ровно 7 м — граница дальности, Fix64 из float мог бы её перейти). Гейзер
    /// поднимает только лёгкого — тяжёлый и элита стоят (флаг AbordageGeyserLift в журнале). Метка элиты — только
    /// Sim (полоски элиты на стенде нет): в журнале раскладки она названа. Незанятые роли ждут за героем вне
    /// досягаемости форм. Герой перед каждым кастом стоит в S лицом вправо по экрану — каст влево и по диагоналям
    /// проверяет доворот в замахе.
    ///
    /// Враги заморожены (не ходят, не бьют), здоровье доливается в тик нажатия. Журнал: [abordage-tour] —
    /// раскладка, нажатия, события Sim 56–63, урон, оглушения, смерти; [abordage-probe] — каждый кадр вокруг каста:
    /// фаза Sim, клип базового слоя, корень, стопы, таз, взгляд, высота модели цели (подъём Гейзера), точки кадра
    /// героя и цели; vf — номер кадра video_frames (кадры для листов — по нему, а не по -Times).
    /// </summary>
    public sealed partial class TickDriver
    {
        private const int AbordageTourSettleTicks = 45, AbordageTourResettleTicks = 30, AbordageTourTailTicks = 45;
        private const int AbordageTourStartWaitTicks = 12, AbordageTourAimTicks = 24, AbordageTourMaxCasts = 8;
        private const int AbordageTourProbeLeadTicks = 4, AbordageTourProbeTailTicks = 30, AbordageTourDone = 5;
        private const int AbordageTourHealth = 5000;

        private enum AbordageTourRole : byte { Light, Heavy, Elite }

        private struct AbordageTourCast
        {
            public readonly AbordageTourRole Role;
            public readonly float Distance;
            /// <summary>Направление каста в осях кадра: x — вправо по экрану, y — вверх.</summary>
            public readonly Vector2 Direction;

            public AbordageTourCast(AbordageTourRole role, float distance, float right, float up)
            {
                Role = role; Distance = distance; Direction = new Vector2(right, up).normalized;
            }
        }

        /// <summary>Касты по порядку (дальше — по кругу): цель, дистанция центр — центр, направление на экране.</summary>
        private static readonly AbordageTourCast[] AbordageTourCasts =
        {
            new AbordageTourCast(AbordageTourRole.Light, 5f, 1f, 0f),
            new AbordageTourCast(AbordageTourRole.Heavy, 3.5f, -1f, 0f),
            new AbordageTourCast(AbordageTourRole.Elite, 6.8f, .8f, .6f),
            new AbordageTourCast(AbordageTourRole.Light, 2f, .6f, -.8f),
        };

        /// <summary>Свидетели в осях каста от цели: (вдоль каста, поперёк).</summary>
        private static readonly Vector2[] AbordageTourWitness =
        {
            new Vector2(-.4f, 1.9f),   // сбоку: волна Обвала, падение Гейзера; вне конуса Пробоины
            new Vector2(2.6f, .5f),    // за спиной близко: конус Пробоины, падение Гейзера; волна Обвала не достаёт
            new Vector2(3.6f, -1f),    // за спиной далеко: только конус Пробоины
        };

        /// <summary>Четвёртый свидетель — от героя: волна Обвала задевает его только на коротком касте.</summary>
        private static readonly Vector2 AbordageTourBystander = new Vector2(-1.6f, -2.2f);

        /// <summary>Места незанятых ролей — от героя, за спиной, вне досягаемости форм.</summary>
        private static readonly Vector2[] AbordageTourParking = { new Vector2(-3.2f, 3.4f), new Vector2(-3.4f, -4.4f) };

        private int _abordageTourGeneration = -1, _abordageTourStep, _abordageTourNext, _abordageTourCast, _abordageTourCasts;
        private int _abordageTourSlot = -1, _abordageTourLatchTick = -1, _abordageTourTarget = -1, _abordageTourSerial;
        private int _abordageTourPressTick = -1, _abordageTourEndTick = -1, _abordageTourAimUntil = -1;
        private int _abordageTourLight = -1, _abordageTourElite = -1, _abordageTourHeavy = -1, _abordageTourBystanderId = -1;
        private bool _abordageTourReady, _abordageTourAim;
        /// <summary>Кадров video_frames на диске к концу прошлого кадра (ведёт проба) — для строк aim/press.</summary>
        private int _abordageTourVf;
        private PelagForm _abordageTourForm;
        private Vector2 _abordageTourOrigin, _abordageTourRight = Vector2.right, _abordageTourUp = Vector2.up;
        private readonly int[] _abordageTourWitnessIds = new int[3];
        private readonly System.Collections.Generic.List<int> _abordageTourSpare = new System.Collections.Generic.List<int>();

        private static FixVec2 AbordageTourFix(Vector2 v) => new FixVec2(Fix64.FromDouble(v.x), Fix64.FromDouble(v.y));
        private static Vector2 AbordageTourFlat(FixVec2 v) => new Vector2(v.X.ToFloat(), v.Y.ToFloat());
        private static string AbordageTourTime() => Time.time.ToString("F3", CultureInfo.InvariantCulture);

        private AbordageTourCast AbordageTourCastAt(int cast) => AbordageTourCasts[cast % AbordageTourCasts.Length];

        /// <summary>Направление каста в мире (оси кадра от камеры).</summary>
        private Vector2 AbordageTourAxis(AbordageTourCast cast)
            => (_abordageTourRight * cast.Direction.x + _abordageTourUp * cast.Direction.y).normalized;

        /// <summary>Точка в осях каста: from + вдоль × x + поперёк × y.</summary>
        private static Vector2 AbordageTourAt(Vector2 from, Vector2 along, Vector2 local)
            => from + along * local.x + new Vector2(-along.y, along.x) * local.y;

        private int AbordageTourRoleId(AbordageTourRole role)
            => role == AbordageTourRole.Heavy ? _abordageTourHeavy : role == AbordageTourRole.Elite ? _abordageTourElite : _abordageTourLight;

        private void CaptureAbordageTour(int tick)
        {
            if (_abordageTourGeneration != Generation)
            {
                _abordageTourGeneration = Generation;
                _abordageTourReady = SetupAbordageTour();
                _abordageTourStep = 0; _abordageTourCast = 0;
                _abordageTourLatchTick = _abordageTourPressTick = _abordageTourEndTick = _abordageTourAimUntil = -1;
                _abordageTourNext = tick + AbordageTourSettleTicks;
                if (_abordageTourReady) StartCoroutine(AbordageTourProbe(Generation));
            }
            if (!_abordageTourReady) return;
            var entities = Sim.Entities;
            // Нажатие держится на всех кадрах своего тика: CaptureStonehoofInput сбрасывает защёлку каждый кадр.
            if (tick == _abordageTourLatchTick)
            {
                _pending.Aim = entities.Position[_abordageTourTarget];
                _pending.AbilityTarget = _abordageTourTarget;
                _abilityLatch = (byte)(1 << _abordageTourSlot);
                return;
            }
            switch (_abordageTourStep)
            {
                case 0:
                    // Каст: места стоят, перезарядка прошла, герой свободен.
                    if (tick < _abordageTourNext || Sim.AbilityReadyTick(_abordageTourSlot) > tick || Sim.AbordageActive) return;
                    int target = AbordageTourRoleId(AbordageTourCastAt(_abordageTourCast).Role);
                    if (_abordageTourAim && _abordageTourAimUntil < 0)
                    {
                        _abordageTourAimUntil = tick + AbordageTourAimTicks;
                        Debug.Log($"[abordage-tour] aim cast={_abordageTourCast} vf={_abordageTourVf} tick={tick} t={AbordageTourTime()} target={target}");
                    }
                    if (_abordageTourAim && tick < _abordageTourAimUntil)
                    {
                        // Режим прицела, как после клавиши слота: курсор на цели (CaptureStonehoofInput снимает его каждый кадр).
                        _targetAimSlot = _abordageTourSlot;
                        HoveredEntity = target;
                        return;
                    }
                    _abordageTourStep = AbordageTourPress(tick, target) ? 1 : 4;
                    return;
                case 1:
                    if (Sim.Abordage.Serial != _abordageTourSerial) { _abordageTourStep = 2; return; }
                    if (tick - _abordageTourPressTick < AbordageTourStartWaitTicks) return;
                    // Sim не принял каст (цель, Лавидий, перезарядка) — следующий через паузу, не виснем.
                    Debug.Log($"[abordage-tour] refused cast={_abordageTourCast} tick={tick} ready={Sim.AbilityReadyTick(_abordageTourSlot)}"
                        + $" lav={entities.Lavidium[Simulation.PlayerId].ToFloat().ToString("F0", CultureInfo.InvariantCulture)}");
                    AbordageTourAfterCast(tick);
                    return;
                case 2:
                    if (Sim.AbordageActive) return;
                    _abordageTourEndTick = tick;
                    Debug.Log($"[abordage-tour] ended cast={_abordageTourCast} tick={tick} t={AbordageTourTime()}"
                        + $" ticks={tick - _abordageTourPressTick} hero={AbordageTourFlat(entities.Position[Simulation.PlayerId]).ToString("F2")}");
                    AbordageTourAfterCast(tick);
                    return;
                case 3:
                    if (tick < _abordageTourNext) return;
                    if (_abordageTourCast >= _abordageTourCasts)
                    {
                        _abordageTourStep = 4;
                        Debug.Log($"[abordage-tour] done tick={tick} t={AbordageTourTime()} casts={_abordageTourCasts}");
                        return;
                    }
                    PlaceAbordageTour(_abordageTourCast);
                    _abordageTourStep = 0;
                    _abordageTourNext = tick + AbordageTourResettleTicks;
                    return;
            }
        }

        private void AbordageTourAfterCast(int tick)
        {
            _abordageTourCast++;
            _abordageTourAimUntil = -1;
            // Хвост: волна, струя и падение воды Гейзера (24 тика от удара) доигрывают после конца каста.
            _abordageTourStep = 3;
            _abordageTourNext = tick + AbordageTourTailTicks;
        }

        /// <summary>
        /// Нажатие тем же путём, что ЛКМ в режиме прицела: слот в прицеле, курсор на цели, ResolveTargetAim(confirm).
        /// Лавидий полон, здоровье долито; защёлка держится на всех кадрах тика. False — цели нет, стенд кончается.
        /// </summary>
        private bool AbordageTourPress(int tick, int target)
        {
            var entities = Sim.Entities;
            _abordageTourTarget = target;
            if (target <= 0 || !entities.Alive[target]) { Debug.Log($"[abordage-tour] no target cast={_abordageTourCast}"); return false; }
            entities.Lavidium[Simulation.PlayerId] = Fix64.FromInt(entities.MaxLavidium[Simulation.PlayerId]);
            AbordageTourHeal();
            _abordageTourSerial = Sim.Abordage.Serial;
            _abordageTourLatchTick = _abordageTourPressTick = tick;
            AbilityBuild build = Sim.GetAbility(_abordageTourSlot);
            bool valid = Sim.ValidAbilityTarget(target, build);
            _pending.Aim = entities.Position[target];
            _targetAimSlot = _abordageTourSlot;
            HoveredEntity = target;
            ResolveTargetAim(true, false);
            // Курсор «уходит» с цели: подсветка наведения не висит на ней весь каст.
            HoveredEntity = -1;
            // Подтверждение не прошло (как ЛКМ мимо годной цели) — нажатия нет, через паузу шаг 1 запишет отказ.
            if ((_abilityLatch & (1 << _abordageTourSlot)) == 0) _abordageTourLatchTick = -1;
            AbordageTourCast cast = AbordageTourCastAt(_abordageTourCast);
            float distance = (AbordageTourFlat(entities.Position[target]) - AbordageTourFlat(entities.Position[Simulation.PlayerId])).magnitude;
            Debug.Log($"[abordage-tour] press cast={_abordageTourCast} tick={tick} t={AbordageTourTime()} vf={_abordageTourVf} slot={_abordageTourSlot}"
                + $" form={Sim.FormAt(_abordageTourSlot)} target={target} role={cast.Role} kind={entities.Kind[target]}"
                + $" elite={(Sim.IsElite(target) ? 1 : 0)} dist={distance.ToString("F2", CultureInfo.InvariantCulture)}"
                + $" valid={(valid ? 1 : 0)} latched={((_abilityLatch & (1 << _abordageTourSlot)) != 0 ? 1 : 0)}"
                + $" hero={AbordageTourFlat(entities.Position[Simulation.PlayerId]).ToString("F2")}");
            return true;
        }

        private void AbordageTourHeal()
        {
            var entities = Sim.Entities;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                if (entities.MaxHealth[i] < AbordageTourHealth)
                {
                    entities.Stats[i].SetBase(StatType.MaxHealth, Fix64.FromInt(AbordageTourHealth));
                    entities.RefreshStats(i);
                }
                entities.Health[i] = entities.MaxHealth[i];
            }
        }

        private void AbordageTourFreeze(int id)
        {
            var entities = Sim.Entities;
            entities.Stats[id].SetBase(StatType.MoveSpeed, Fix64.Zero);
            entities.Stats[id].SetBase(StatType.Damage, Fix64.Zero);
            entities.RefreshStats(id);
            entities.NextAttackTick[id] = int.MaxValue;
        }

        /// <summary>
        /// Стенд: Абордаж и форма в наборе (как F8), роли, тяжёлый Камнекопыт, метка элиты, оси от камеры,
        /// точка каста на свободной земле, раскладка первого каста.
        /// </summary>
        private bool SetupAbordageTour()
        {
            var sim = Sim; var entities = sim.Entities;
            RunLoadout loadout = Session != null ? Session.ActiveLoadout : null;
            int line = PelagForms.LineOf(PelagForm.AbordageQuake);
            if (loadout == null || line < 0) { Debug.LogWarning("[abordage-tour] нет набора забега"); return false; }
            // Набор съёмки Абордажа не держит (пул 6) — кладём его в последний слот, как стенд Шквала.
            if (!loadout.Owns(line) && !loadout.Put(RunLoadout.Slots - 1, line))
            { Debug.LogWarning("[abordage-tour] Абордаж не встал в набор"); return false; }
            PelagForm wanted = CaptureRig.SkillForm;
            _abordageTourForm = PelagForms.LineOf(wanted) == line ? wanted : PelagForm.None;
            bool set = loadout.DebugSetForm(line, _abordageTourForm);
            RefreshAbilityBuild();
            _abordageTourSlot = loadout.SlotOf(line);
            if (_abordageTourSlot < 0 || sim.GetAbility(_abordageTourSlot)?.DefinitionId != AbilityDefinition.AnchorLeapId)
            { Debug.LogWarning("[abordage-tour] слота Абордажа нет"); return false; }

            string[] args = System.Environment.GetCommandLineArgs();
            _abordageTourAim = System.Array.IndexOf(args, "-capture-abordage-aim") >= 0;
            _abordageTourCasts = AbordageTourCasts.Length;
            int castsAt = System.Array.IndexOf(args, "-capture-abordage-casts");
            if (castsAt >= 0 && castsAt + 1 < args.Length && int.TryParse(args[castsAt + 1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int casts))
                _abordageTourCasts = Mathf.Clamp(casts, 1, AbordageTourMaxCasts);

            // Оси кадра: «вправо» — правая ось камеры на земле, «вверх» — влево от неё (вверх по экрану).
            Camera view = Camera.main;
            Vector3 right = view != null ? view.transform.right : Vector3.right;
            _abordageTourRight = new Vector2(right.x, right.z);
            if (_abordageTourRight.sqrMagnitude < 1e-4f) _abordageTourRight = Vector2.right;
            _abordageTourRight.Normalize();
            _abordageTourUp = new Vector2(-_abordageTourRight.y, _abordageTourRight.x);

            // Роли по порядку живых врагов: лёгкая цель, элита, три свидетеля, четвёртый; лишние — далеко.
            _abordageTourLight = _abordageTourElite = _abordageTourHeavy = _abordageTourBystanderId = -1;
            for (int k = 0; k < _abordageTourWitnessIds.Length; k++) _abordageTourWitnessIds[k] = -1;
            _abordageTourSpare.Clear();
            int count = 0;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i] || entities.Side[i] == Faction.Wole) continue;
                if (count == 0) _abordageTourLight = i;
                else if (count == 1) _abordageTourElite = i;
                else if (count < 2 + _abordageTourWitnessIds.Length) _abordageTourWitnessIds[count - 2] = i;
                else if (count == 2 + _abordageTourWitnessIds.Length) _abordageTourBystanderId = i;
                else _abordageTourSpare.Add(i);
                count++;
            }
            if (_abordageTourLight < 0) { Debug.LogWarning("[abordage-tour] врагов нет"); return false; }
            if (_abordageTourElite > 0) sim.MarkElite(_abordageTourElite);
            // Тяжёлый — Камнекопыт: не лёгкий по правилу форм (AbordageLight) — Гейзер его не поднимает,
            // Пробоина и волна не двигают. Вид привяжется по событию Spawn, место даст раскладка.
            FixVec2 aside = entities.Position[Simulation.PlayerId] + AbordageTourFix(-_abordageTourRight * 40f);
            _abordageTourHeavy = sim.AddKindTestEnemy(EnemyKind.ForestStonehoof, aside, 100);
            AbordageTourHeal();
            _abordageTourOrigin = FindAbordageTourOrigin();
            PlaceAbordageTour(0);
            Debug.Log($"[abordage-tour] layout form={_abordageTourForm} set={set} slotForm={sim.FormAt(_abordageTourSlot)}"
                + $" slot={_abordageTourSlot} range={sim.GetAbility(_abordageTourSlot).Get(AbilityStatType.Radius).ToFloat().ToString("F2", CultureInfo.InvariantCulture)}"
                + $" light={_abordageTourLight} elite={_abordageTourElite}(sim) heavy={_abordageTourHeavy}({entities.Kind[_abordageTourHeavy]})"
                + $" witnesses={_abordageTourWitnessIds[0]},{_abordageTourWitnessIds[1]},{_abordageTourWitnessIds[2]}"
                + $" bystander={_abordageTourBystanderId} spare={_abordageTourSpare.Count} casts={_abordageTourCasts} aim={_abordageTourAim}"
                + $" origin={_abordageTourOrigin.ToString("F2")} right={_abordageTourRight.ToString("F2")} up={_abordageTourUp.ToString("F2")}"
                + $" heroR={entities.BodyRadius[Simulation.PlayerId].ToFloat().ToString("F2", CultureInfo.InvariantCulture)}");
            return true;
        }

        /// <summary>
        /// Точка каста: ближайшая к месту героя, где раскладки всех кастов стоят на земле карты вида, а путь
        /// S → посадка у цели проходим. В Sim стенда стен нет — проверка по карте вида, чтобы тела не стояли в
        /// камне и герой не летел сквозь уступ. Без карты — место героя.
        /// </summary>
        private Vector2 FindAbordageTourOrigin()
        {
            var map = Run != null ? Run.Map : null;
            Vector2 spawn = AbordageTourFlat(Sim.Entities.Position[Simulation.PlayerId]);
            if (map == null) return spawn;
            for (int ring = 0; ring <= 14; ring++)
                for (int x = -ring; x <= ring; x++)
                    for (int y = -ring; y <= ring; y++)
                    {
                        if (System.Math.Max(System.Math.Abs(x), System.Math.Abs(y)) != ring) continue;
                        if (AbordageTourFits(map, spawn + new Vector2(x, y))) return spawn + new Vector2(x, y);
                    }
            Debug.LogWarning("[abordage-tour] свободной земли под раскладку нет — точка героя");
            return spawn;
        }

        private bool AbordageTourFits(LayoutMap map, Vector2 origin)
        {
            var entities = Sim.Entities;
            Fix64 heroRadius = entities.BodyRadius[Simulation.PlayerId];
            Fix64 body = entities.BodyRadius[_abordageTourLight] + Fix64.Ratio(1, 10);
            Fix64 heavy = entities.BodyRadius[_abordageTourHeavy] + Fix64.Ratio(1, 10);
            if (!map.IsWalkable(AbordageTourFix(origin), heroRadius)) return false;
            for (int c = 0; c < AbordageTourCasts.Length; c++)
            {
                AbordageTourCast cast = AbordageTourCasts[c];
                Vector2 along = AbordageTourAxis(cast);
                Vector2 target = origin + along * cast.Distance;
                Fix64 targetBody = cast.Role == AbordageTourRole.Heavy ? heavy : body;
                if (!map.IsWalkable(AbordageTourFix(target), targetBody)) return false;
                if (!map.CanTravel(AbordageTourFix(origin), AbordageTourFix(target), heroRadius)) return false;
                for (int k = 0; k < AbordageTourWitness.Length; k++)
                    if (!map.IsWalkable(AbordageTourFix(AbordageTourAt(target, along, AbordageTourWitness[k])), body)) return false;
                if (!map.IsWalkable(AbordageTourFix(AbordageTourAt(origin, along, AbordageTourBystander)), body)) return false;
                for (int k = 0; k < AbordageTourParking.Length; k++)
                    if (!map.IsWalkable(AbordageTourFix(AbordageTourAt(origin, along, AbordageTourParking[k])), heavy)) return false;
            }
            return true;
        }

        /// <summary>Все на места каста: герой в S лицом вправо по экрану, цель, свидетели, незанятые роли, заморозка.</summary>
        private void PlaceAbordageTour(int castIndex)
        {
            var entities = Sim.Entities;
            AbordageTourCast cast = AbordageTourCastAt(castIndex);
            Vector2 along = AbordageTourAxis(cast);
            Vector2 target = _abordageTourOrigin + along * cast.Distance;
            entities.Position[Simulation.PlayerId] = AbordageTourFix(_abordageTourOrigin);
            entities.Facing[Simulation.PlayerId] = AbordageTourFix(_abordageTourRight);
            int targetId = AbordageTourRoleId(cast.Role), parked = 0;
            AbordageTourPut(targetId, target, castIndex, "target");
            foreach (int id in new[] { _abordageTourLight, _abordageTourHeavy, _abordageTourElite })
            {
                if (id <= 0 || id == targetId || parked >= AbordageTourParking.Length) continue;
                AbordageTourPut(id, AbordageTourAt(_abordageTourOrigin, along, AbordageTourParking[parked++]), castIndex, "parked");
            }
            for (int k = 0; k < _abordageTourWitnessIds.Length; k++)
                AbordageTourPut(_abordageTourWitnessIds[k], AbordageTourAt(target, along, AbordageTourWitness[k]), castIndex, "witness" + k);
            AbordageTourPut(_abordageTourBystanderId, AbordageTourAt(_abordageTourOrigin, along, AbordageTourBystander), castIndex, "bystander");
            for (int k = 0; k < _abordageTourSpare.Count; k++)
                AbordageTourPut(_abordageTourSpare[k], _abordageTourOrigin - _abordageTourRight * (40f + 3f * k), castIndex, "spare");
            Sim.Grid.Rebuild(entities);
        }

        private void AbordageTourPut(int id, Vector2 at, int castIndex, string role)
        {
            var entities = Sim.Entities;
            if (id <= 0 || id >= entities.Count || !entities.Alive[id]) return;
            entities.Position[id] = AbordageTourFix(at);
            entities.Velocity[id] = FixVec2.Zero;
            ForcedMotion.Clear(entities, id);
            Vector2 look = _abordageTourOrigin - at;
            if (look.sqrMagnitude > 1e-4f) entities.Facing[id] = AbordageTourFix(look.normalized);
            AbordageTourFreeze(id);
            Debug.Log($"[abordage-tour] place cast={castIndex} {role} id={id} kind={entities.Kind[id]} at={at.ToString("F2")}");
        }

        /// <summary>События Абордажа (56–63) и то, что идёт за ними: урон, оглушение, смерть врага.</summary>
        private static bool AbordageTourLogs(SimEvent e)
            => (e.Type >= SimEventType.AbordageThrow && e.Type <= SimEventType.AbordageEnded)
               || ((e.Type == SimEventType.Damage || e.Type == SimEventType.Stun || e.Type == SimEventType.Death)
                   && e.Target > Simulation.PlayerId);

        /// <summary>Каждый кадр после отрисовки: события и, вокруг каста, фаза, клип, корень, стопы, таз, подъём цели.</summary>
        private IEnumerator AbordageTourProbe(int generation)
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
            while (Generation == generation && _abordageTourStep < AbordageTourDone)
            {
                yield return wait;
                if (frames != null)
                    while (File.Exists(Path.Combine(frames, "frame_" + videoFrame.ToString("0000", CultureInfo.InvariantCulture) + ".jpg"))) videoFrame++;
                _abordageTourVf = videoFrame;
                var sim = Sim;
                if (sim == null) continue;
                var contexts = FrameEventContexts;
                for (int i = 0; i < contexts.Count; i++)
                {
                    SimEvent e = contexts[i].Event;
                    if (!AbordageTourLogs(e)) continue;
                    Debug.Log($"[abordage-tour] ev vf={videoFrame} t={AbordageTourTime()} tick={contexts[i].SimulationTick} {e.Type}"
                        + $" tgt={e.Target} amount={e.Amount} flag={(e.Flag ? 1 : 0)} variant={e.ActionVariant}"
                        + $" pos={AbordageTourFlat(e.Position).ToString("F2")}");
                }
                if (_abordageTourStep == 4) { _abordageTourStep = AbordageTourDone; break; }
                int tick = sim.Tick;
                AbordageState s = sim.Abordage;
                // Окно пробы: прицел -capture-abordage-aim, нажатие − 4 тика … конец + 30 (падение Гейзера — конец + 15).
                bool near = sim.AbordageActive || (_abordageTourStep == 0 && _abordageTourAimUntil >= 0)
                    || (_abordageTourPressTick >= 0 && tick >= _abordageTourPressTick - AbordageTourProbeLeadTicks
                    && (_abordageTourEndTick < _abordageTourPressTick || tick <= _abordageTourEndTick + AbordageTourProbeTailTicks));
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
                    Debug.Log($"[abordage-probe] bones hips={hips != null} chest={chest != null} feet={leftFoot != null}/{rightFoot != null}"
                        + $" toe={leftToe != null} animator={animator != null}");
                }
                Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
                forward.Normalize();
                line.Clear();
                line.Append("[abordage-probe] vf=").Append(videoFrame).Append(" t=").Append(AbordageTourTime())
                    .Append(" tick=").Append(tick).Append(" a=").Append(Alpha.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" cast=").Append(_abordageTourCast).Append(" ph=").Append(s.Phase).Append(" tgt=").Append(s.Target)
                    .Append(" rel=").Append(s.ReleaseTick).Append(" bite=").Append(s.BiteTick).Append(" arr=").Append(s.ArriveTick)
                    .Append(" end=").Append(s.PhaseEndTick);
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
                    // Выход в покой (ревью 03.10): жест сабли и слои стойки — вес шагов стойки 0/1 рвал позу.
                    var gear = animator.GetComponent<PelagEquipmentView>();
                    int stance = animator.GetLayerIndex("Saber Stance"), steps = animator.GetLayerIndex("Saber Footwork");
                    line.Append(" dp=").Append(gear != null ? gear.DrawPhase.ToString("F2", CultureInfo.InvariantCulture) : "-")
                        .Append(" st=").Append(stance >= 0 ? animator.GetLayerWeight(stance).ToString("F2", CultureInfo.InvariantCulture) : "-")
                        .Append(" fw=").Append(steps >= 0 ? animator.GetLayerWeight(steps).ToString("F2", CultureInfo.InvariantCulture) : "-");
                }
                AppendProbe(line, "root", body.position);
                line.Append(" yaw=").Append((Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg).ToString("F1", CultureInfo.InvariantCulture));
                if (leftFoot != null) AppendProbe(line, "lf", leftFoot.position);
                if (rightFoot != null) AppendProbe(line, "rf", rightFoot.position);
                if (leftToe != null) AppendProbe(line, "lt", leftToe.position);
                if (hips != null) AppendProbe(line, "hips", hips.position);
                if (chest != null) AppendProbe(line, "chest", chest.position);
                // Цель каста: высота модели (Гейзер поднимает вид, Sim держит тело на месте) и флаг подъёма Sim.
                int target = _abordageTourTarget;
                Transform targetView = null;
                if (target > 0 && arena.TryGetEntityView(target, out targetView))
                    line.Append(" ty=").Append(targetView.position.y.ToString("F3", CultureInfo.InvariantCulture))
                        .Append(" up=").Append(sim.AbordageLifted(target) ? 1 : 0);
                Camera shot = Camera.main;
                if (shot != null)
                {
                    // Точки кадра (доли, y сверху) на высоте таза — центры вырезов листов: герой и цель.
                    AppendViewport(line, "scr", shot.WorldToViewportPoint(body.position + Vector3.up * .8f));
                    if (targetView != null) AppendViewport(line, "tscr", shot.WorldToViewportPoint(targetView.position + Vector3.up * .8f));
                }
                Debug.Log(line.ToString());
            }
        }

        private static void AppendViewport(StringBuilder line, string name, Vector3 viewport)
        {
            line.Append(' ').Append(name).Append("=(").Append(viewport.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append((1f - viewport.y).ToString("F3", CultureInfo.InvariantCulture)).Append(')');
        }

        /// <summary>
        /// -Skill … -LiveSkill: цель каста съёмки. Прежде всегда враг 1. Абордаж (02.10) кастуется только по годной
        /// цели, как Шквал: для него — враг 1, если годен, иначе ближайший годный (Sim.ValidAbilityTarget);
        /// никого — −1, и Sim отказывает без цены и кулдауна.
        /// </summary>
        private int CaptureLiveSkillTarget(int definition)
        {
            int first = Sim.Entities.Count > 1 ? 1 : -1;
            if (definition != AbilityDefinition.AnchorLeapId) return first;
            AbilityBuild build = null;
            for (int slot = 0; slot < Simulation.AbilitySlots && build == null; slot++)
                if (Sim.GetAbility(slot)?.DefinitionId == definition) build = Sim.GetAbility(slot);
            if (build == null || Sim.ValidAbilityTarget(first, build)) return first;
            var entities = Sim.Entities;
            int best = -1;
            Fix64 bestSq = Fix64.Zero;
            for (int i = 1; i < entities.Count; i++)
            {
                if (!Sim.ValidAbilityTarget(i, build)) continue;
                Fix64 distanceSq = FixVec2.DistanceSq(entities.Position[Simulation.PlayerId], entities.Position[i]);
                if (best >= 0 && distanceSq >= bestSq) continue;
                best = i;
                bestSq = distanceSq;
            }
            return best;
        }
    }
}
