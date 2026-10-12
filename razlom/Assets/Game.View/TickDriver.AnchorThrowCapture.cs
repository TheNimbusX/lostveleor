using System;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Съёмка Броска якоря (07.10, клипы по листу поз): -capture-skill anchor-throw — стенд живого навыка с Броском в слоте 0.
    ///
    /// * -capture-throw-series N (1–4) — N бросков, каждый через ThrowCaptureSpacing тиков (кулдаун 108 + бросок 25);
    /// * -capture-throw-yaws 0,-135,60 — поворот каждого броска от -CastYaw, градусы (0 — вправо экрана, −135 — вверх-влево,
    ///   60 — вниз-вправо, к камере); без ключа — все 0;
    /// * -capture-throw-form net|fan|harpoon — форма в слоте (PelagForms.AppendFormNodes, как набор забега); список через
    ///   запятую (07.10, формы VFX: net,fan,harpoon) — у каждого броска серии своя форма (слот пересобирается перед нажатием;
    ///   none — без формы);
    /// * -capture-throw-lane — враги (Хранители, -Encounter forest-guardian) стоят на линии броска на 2,6 / 4,1 / 5,5 / 6,6 м,
    ///   третий помечен элитой (тяжёлый: урон и оглушение без тяги, Simulation.AbordageLight); стоят смирно, здоровья 2000;
    ///   между бросками шагом 3 м/с идут на линию следующего броска. Только под -razlom-capture.
    /// * -capture-throw-lane-shifts 0,0,2.4 — места линии броска number сдвинуты вдоль, м (Гарпун: укус дальше, долгая борозда).
    /// </summary>
    public sealed partial class TickDriver
    {
        /// <summary>Тиков между бросками серии: кулдаун Броска 108 + бросок 25 + запас на возврат врагов.</summary>
        private const int ThrowCaptureSpacing = 150;

        private static int _throwCaptureSeries = -1;
        private static float[] _throwCaptureYaws;
        private static int _throwCaptureLane = -1;
        private static int _throwCaptureFormRead = -1;
        private static PelagForm _throwCaptureForm;
        private static PelagForm[] _throwCaptureForms = Array.Empty<PelagForm>();

        private static int ThrowCaptureSeries
        {
            get
            {
                if (_throwCaptureSeries >= 0) return _throwCaptureSeries;
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-capture-throw-series");
                int value = 1;
                if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int parsed)) value = Mathf.Clamp(parsed, 1, 4);
                return _throwCaptureSeries = value;
            }
        }

        /// <summary>Поворот броска number (0…) от направления каста стенда, градусы.</summary>
        private static float ThrowCapturePressYaw(int number)
        {
            if (_throwCaptureYaws == null)
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-capture-throw-yaws");
                string[] raw = at >= 0 && at + 1 < args.Length ? args[at + 1].Split(',') : new string[0];
                _throwCaptureYaws = new float[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    float.TryParse(raw[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                        out _throwCaptureYaws[i]);
            }
            return _throwCaptureYaws.Length == 0 ? 0f : _throwCaptureYaws[Mathf.Clamp(number, 0, _throwCaptureYaws.Length - 1)];
        }

        private static float[] _throwCaptureShifts;

        /// <summary>
        /// -capture-throw-lane-shifts 0,0,2.4 (08.10, формы VFX): сдвиг мест линии броска number вдоль, м — у Гарпуна первая цель
        /// дальше (укус на ~5 м, долгая борозда до полукольца ловли, как на кадре form-harpoon-v2); без ключа — 0.
        /// </summary>
        private static float ThrowCaptureShift(int number)
        {
            if (_throwCaptureShifts == null)
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-capture-throw-lane-shifts");
                string[] raw = at >= 0 && at + 1 < args.Length ? args[at + 1].Split(',') : new string[0];
                _throwCaptureShifts = new float[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    float.TryParse(raw[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                        out _throwCaptureShifts[i]);
            }
            return _throwCaptureShifts.Length == 0 ? 0f : _throwCaptureShifts[Mathf.Clamp(number, 0, _throwCaptureShifts.Length - 1)];
        }

        private static bool ThrowCaptureLane
        {
            get
            {
                if (_throwCaptureLane < 0) _throwCaptureLane = Array.IndexOf(Environment.GetCommandLineArgs(), "-capture-throw-lane") >= 0 ? 1 : 0;
                return _throwCaptureLane == 1;
            }
        }

        /// <summary>-capture-throw-form net|fan|harpoon (или AnchorThrowNet/Fan/Harpoon): форма Броска на стенде.</summary>
        internal static PelagForm ThrowCaptureForm
        {
            get
            {
                if (_throwCaptureFormRead >= 0) return _throwCaptureForm;
                _throwCaptureFormRead = 1;
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-capture-throw-form");
                string list = at >= 0 && at + 1 < args.Length ? args[at + 1].Trim().ToLowerInvariant() : "";
                string[] parts = list.Split(',');
                _throwCaptureForms = new PelagForm[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    string raw = parts[i].Trim();
                    if (raw.StartsWith("anchorthrow")) raw = raw.Substring(11);
                    _throwCaptureForms[i] = raw == "net" ? PelagForm.AnchorThrowNet : raw == "fan" ? PelagForm.AnchorThrowFan
                        : raw == "harpoon" ? PelagForm.AnchorThrowHarpoon : PelagForm.None;
                }
                _throwCaptureForm = _throwCaptureForms[0];
                if (at >= 0) Debug.Log($"[throw-capture] form {_throwCaptureForm} (key '{list}', forms {_throwCaptureForms.Length})");
                return _throwCaptureForm;
            }
        }

        /// <summary>Узлы слота Броска на стенде: форма -capture-throw-form или ничего.</summary>
        private static int ThrowCaptureNodes(out AbilityNode[] nodes)
        {
            PelagForm form = ThrowCaptureForm;
            nodes = form != PelagForm.None ? new AbilityNode[4] : Array.Empty<AbilityNode>();
            return form != PelagForm.None ? PelagForms.AppendFormNodes(form, nodes, 0) : 0;
        }

        /// <summary>
        /// Список форм (-capture-throw-form net,fan,harpoon): перед броском number слот 0 пересобирается под его форму
        /// (как набор забега: PelagKit.PoolDefinition(10) + узлы формы). Один ключ без запятой — ничего не делает.
        /// </summary>
        private void ApplyThrowCaptureForm(int number)
        {
            PelagForm first = ThrowCaptureForm;
            if (_throwCaptureForms.Length < 2 || Sim == null) return;
            PelagForm form = _throwCaptureForms[Mathf.Clamp(number, 0, _throwCaptureForms.Length - 1)];
            var nodes = form != PelagForm.None ? new AbilityNode[4] : Array.Empty<AbilityNode>();
            int count = form != PelagForm.None ? PelagForms.AppendFormNodes(form, nodes, 0) : 0;
            Sim.SetAbility(0, PelagKit.PoolDefinition(10), nodes, count);
            Debug.Log($"[throw-capture] throw {number} form {form} (first {first})");
        }

        /// <summary>Места на линии броска: вдоль (м от героя) и поперёк (влево +).</summary>
        private static readonly Vector2[] ThrowLaneSpots =
        {
            new Vector2(2.6f, .15f), new Vector2(4.1f, -.2f), new Vector2(5.5f, .1f), new Vector2(6.6f, -.15f),
            new Vector2(3.4f, 1.9f), new Vector2(5f, -2.1f)
        };

        private FixVec2 _throwLaneCenter;
        private bool _throwLanePlaced;

        private FixVec2 ThrowLaneDir(int number)
        {
            Vector3 d = Quaternion.Euler(0f, CaptureRig.CastYaw + ThrowCapturePressYaw(number), 0f) * Vector3.right;
            return new FixVec2(Fix64.FromDouble(d.x), Fix64.FromDouble(d.z)).Normalized();
        }

        private FixVec2 ThrowLaneHome(int enemy, int number)
        {
            Vector2 spot = ThrowLaneSpots[(enemy - 1) % ThrowLaneSpots.Length];
            FixVec2 dir = ThrowLaneDir(number);
            var side = new FixVec2(-dir.Y, dir.X);
            return _throwLaneCenter + dir * Fix64.FromDouble(spot.x + ThrowCaptureShift(number)) + side * Fix64.FromDouble(spot.y);
        }

        /// <summary>Расстановка в начале съёмки (TickDriver, elapsed 0, после штатной расстановки живого навыка).</summary>
        private void PlaceThrowCaptureLane()
        {
            if (Sim == null) return;
            EntityStore entities = Sim.Entities;
            _throwLaneCenter = entities.Position[Simulation.PlayerId];
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                entities.Stats[i].SetBase(StatType.MaxHealth, Fix64.FromInt(2000));
                entities.Stats[i].SetBase(StatType.MoveSpeed, Fix64.Zero);
                entities.Stats[i].SetBase(StatType.Damage, Fix64.Zero);
                entities.RefreshStats(i);
                entities.Health[i] = 2000;
                entities.NextAttackTick[i] = int.MaxValue;
                if (!ThrowCaptureLane) continue;
                entities.Position[i] = ThrowLaneHome(i, 0);
                entities.Facing[i] = (entities.Position[Simulation.PlayerId] - entities.Position[i]).Normalized();
                if (i == 3) Sim.MarkElite(i);
            }
            Sim.Grid.Rebuild(entities);
            _throwLanePlaced = true;
            Debug.Log($"[throw-capture] lane={ThrowCaptureLane} enemies={entities.Count - 1} series={ThrowCaptureSeries} form={ThrowCaptureForm}");
        }

        /// <summary>
        /// Между бросками: с 40 тиков после броска до 12 тиков перед следующим враги шагом 3 м/с идут на линию следующего
        /// броска (их сдвинула тяга); здоровье доливается.
        /// </summary>
        private void HoldThrowCaptureLane(int elapsed)
        {
            if (!ThrowCaptureLane || Sim == null || !_throwLanePlaced) return;
            int since = elapsed - 18;
            if (since < 0) return;
            int next = since / ThrowCaptureSpacing + 1;
            int phase = since % ThrowCaptureSpacing;
            if (next >= ThrowCaptureSeries || phase < 40 || phase > ThrowCaptureSpacing - 12) return;
            EntityStore entities = Sim.Entities;
            Fix64 step = Fix64.Ratio(3, 30);
            for (int i = 1; i < entities.Count; i++)
            {
                if (!entities.Alive[i]) continue;
                entities.Health[i] = entities.MaxHealth[i];
                FixVec2 to = ThrowLaneHome(i, next) - entities.Position[i];
                Fix64 length = to.Length;
                if (length > Fix64.Ratio(1, 100)) entities.Position[i] += length > step ? to * (step / length) : to;
                entities.Facing[i] = (entities.Position[Simulation.PlayerId] - entities.Position[i]).Normalized();
            }
            Sim.Grid.Rebuild(entities);
        }
    }
}
