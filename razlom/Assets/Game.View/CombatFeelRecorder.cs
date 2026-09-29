using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// ЗАПИСЬ БОЯ ДЛЯ МЕТРИК «ОЩУЩЕНИЯ» (план «Мобы леса v2», этап 0 и поток D).
    ///
    /// Владелец не может сказать словами, что не так с ИИ, — поэтому его бои
    /// пишутся потиково и меряются тем же, чем стенд бота (IArenaProbe): доля
    /// времени, когда надо уходить, свободная атака, погоня, замахи разом,
    /// мобы без дела, паузы без атак.
    ///
    /// ТОЛЬКО РЕДАКТОР И DEV-СБОРКА, ВЫКЛЮЧЕН ПО УМОЛЧАНИЮ. Включается флагом
    /// командной строки: -capture-feel &lt;путь.jsonl&gt; (или Begin(путь) из
    /// кода редактора). Хук — одна строка в TickDriver сразу после
    /// Session.Step: AfterStep(this, frame). В релизе вызов вырезает компилятор
    /// ([Conditional]), а без флага он стоит одну проверку поля.
    ///
    /// Формат — JSON Lines, строка на тик симуляции (только когда тик сдвинулся):
    /// первая строка {"meta":…}, дальше
    /// {"t":тик,"gen":поколение,"mode":"Rift","arena":глубина,"phase":"Clearing","enc":"forest.E05",
    ///  "hero":[x,y,fx,fy,hp,maxHp,alive],
    ///  "in":[flags,aimX,aimY,attackTarget,abilityMask,abilityHoldMask,abilityTarget,moveX,moveY,command],
    ///  "mobs":[[id,kind,x,y,fx,fy,hp,maxHp,elite],…],
    ///  "marks":[[serial,source,shape,startTick,impactTick,endTick,ox,oy,dx,dy,radius,inner,width,length],…],
    ///  "ev":[[type,source,target,amount,flag,x,y,variant],…]}.
    /// Мобы — только живые враги; метки — активные из общего списка Sim;
    /// события — все события этого тика (SimEventType и EnemyKind — числами).
    /// Координаты — метры Sim, три знака. Сбрасывается на диск раз в секунду
    /// боя и при выходе: падение игры теряет не больше секунды.
    ///
    /// Только читает Sim — детерминизм и хэш не трогает.
    /// </summary>
    public sealed class CombatFeelRecorder : MonoBehaviour
    {
        public const string CommandLineFlag = "-capture-feel";
        public const int FormatVersion = 1;

        private static bool _parsed;
        private static string _requestedPath;
        private static CombatFeelRecorder _instance;

        private TickDriver _driver;
        private StreamWriter _writer;
        private string _path;
        private readonly StringBuilder _line = new StringBuilder(4096);
        private int _lastTick = int.MinValue, _lastGeneration = int.MinValue, _unflushed;

        /// <summary>Путь записи из -capture-feel (или Begin), иначе null — запись выключена.</summary>
        public static string RequestedPath
        {
            get
            {
                if (!_parsed)
                {
                    _parsed = true;
                    string[] args = Environment.GetCommandLineArgs();
                    for (int i = 0; i + 1 < args.Length; i++)
                        if (string.Equals(args[i], CommandLineFlag, StringComparison.OrdinalIgnoreCase))
                            _requestedPath = args[i + 1];
                }
                return _requestedPath;
            }
        }

        /// <summary>Включить запись из кода редактора или стенда: файл откроется на ближайшем тике.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Begin(string path)
        {
            _parsed = true;
            _requestedPath = string.IsNullOrEmpty(path) ? null : path;
        }

        /// <summary>Остановить запись и закрыть файл.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void End()
        {
            _parsed = true;
            _requestedPath = null;
            if (_instance != null) _instance.Close();
        }

        /// <summary>Хук TickDriver: зовётся после каждого Session.Step.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void AfterStep(TickDriver driver, InputFrame frame)
        {
            string path = RequestedPath;
            if (path == null || driver == null) return;
            if (_instance == null || _instance._driver != driver)
            {
                _instance = driver.GetComponent<CombatFeelRecorder>();
                if (_instance == null) _instance = driver.gameObject.AddComponent<CombatFeelRecorder>();
                _instance._driver = driver;
            }
            _instance.Record(path, frame);
        }

        private void Record(string path, InputFrame frame)
        {
            Simulation sim = _driver.Sim;
            if (sim == null) return;
            if (sim.Tick == _lastTick && _driver.Generation == _lastGeneration) return;
            _lastTick = sim.Tick;
            _lastGeneration = _driver.Generation;
            if (_writer == null || _path != path)
            {
                Close();
                if (!Open(path)) return;
            }

            GameSession session = _driver.Session;
            RiftRun run = _driver.Run;
            EntityStore e = sim.Entities;
            var b = _line;
            b.Clear();
            b.Append("{\"t\":").Append(sim.Tick).Append(",\"gen\":").Append(_driver.Generation)
                .Append(",\"mode\":\"").Append(session != null ? session.Mode.ToString() : "-").Append('"');
            if (run != null)
            {
                b.Append(",\"arena\":").Append(run.Depth).Append(",\"phase\":\"").Append(run.Phase.ToString()).Append('"');
                if (run.CurrentEncounter != null) b.Append(",\"enc\":\"").Append(run.CurrentEncounter.Key).Append('"');
            }

            int hero = Simulation.PlayerId;
            b.Append(",\"hero\":[");
            Num(b, e.Position[hero].X); b.Append(','); Num(b, e.Position[hero].Y); b.Append(',');
            Num(b, e.Facing[hero].X); b.Append(','); Num(b, e.Facing[hero].Y); b.Append(',');
            b.Append(e.Health[hero]).Append(',').Append(e.MaxHealth[hero]).Append(',').Append(e.Alive[hero] ? 1 : 0).Append(']');

            b.Append(",\"in\":[").Append(frame.Flags).Append(',');
            Num(b, frame.Aim.X); b.Append(','); Num(b, frame.Aim.Y); b.Append(',');
            b.Append(frame.AttackTarget).Append(',').Append(frame.AbilityMask).Append(',').Append(frame.AbilityHoldMask)
                .Append(',').Append(frame.AbilityTarget).Append(',');
            Num(b, frame.MoveDirection.X); b.Append(','); Num(b, frame.MoveDirection.Y);
            b.Append(',').Append(frame.Command).Append(']');

            b.Append(",\"mobs\":[");
            bool first = true;
            for (int i = 1; i < e.Count; i++)
            {
                if (!e.Alive[i] || e.Side[i] == Faction.Wole) continue;
                if (!first) b.Append(',');
                first = false;
                b.Append('[').Append(i).Append(',').Append((int)e.Kind[i]).Append(',');
                Num(b, e.Position[i].X); b.Append(','); Num(b, e.Position[i].Y); b.Append(',');
                Num(b, e.Facing[i].X); b.Append(','); Num(b, e.Facing[i].Y); b.Append(',');
                b.Append(e.Health[i]).Append(',').Append(e.MaxHealth[i]).Append(',')
                    .Append(run != null && run.Encounters != null && run.Encounters.IsElite(i) ? 1 : 0).Append(']');
            }
            b.Append(']');

            b.Append(",\"marks\":[");
            first = true;
            for (int slot = 0; slot < sim.TelegraphHighWater; slot++)
            {
                if (!sim.TryGetTelegraph(slot, out EnemyTelegraph t) || !t.IsActive) continue;
                if (!first) b.Append(',');
                first = false;
                b.Append('[').Append(t.Serial).Append(',').Append(t.Source).Append(',').Append((int)t.Shape).Append(',')
                    .Append(t.StartTick).Append(',').Append(t.ImpactTick).Append(',').Append(t.EndTick).Append(',');
                Num(b, t.Origin.X); b.Append(','); Num(b, t.Origin.Y); b.Append(',');
                Num(b, t.Direction.X); b.Append(','); Num(b, t.Direction.Y); b.Append(',');
                Num(b, t.Radius); b.Append(','); Num(b, t.InnerRadius); b.Append(',');
                Num(b, t.Width); b.Append(','); Num(b, t.Length); b.Append(']');
            }
            b.Append(']');

            b.Append(",\"ev\":[");
            var events = sim.Events;
            for (int k = 0; k < events.Count; k++)
            {
                SimEvent ev = events[k];
                if (k > 0) b.Append(',');
                b.Append('[').Append((int)ev.Type).Append(',').Append(ev.Source).Append(',').Append(ev.Target).Append(',')
                    .Append(ev.Amount).Append(',').Append(ev.Flag ? 1 : 0).Append(',');
                Num(b, ev.Position.X); b.Append(','); Num(b, ev.Position.Y);
                b.Append(',').Append(ev.ActionVariant).Append(']');
            }
            b.Append("]}");

            _writer.WriteLine(b.ToString());
            if (++_unflushed >= Simulation.TicksPerSecond)
            {
                _unflushed = 0;
                _writer.Flush();
            }
        }

        private bool Open(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                _writer = new StreamWriter(path, false, new UTF8Encoding(false));
                _path = path;
                _unflushed = 0;
                _writer.WriteLine("{\"meta\":{\"format\":" + FormatVersion + ",\"ticksPerSecond\":" + Simulation.TicksPerSecond
                    + ",\"seed\":" + _driver.RunSeed.ToString(CultureInfo.InvariantCulture)
                    + ",\"startedUtc\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                    + "\",\"unity\":\"" + Application.unityVersion + "\"}}");
                UnityEngine.Debug.Log("[Разлом] Запись боя для метрик «ощущения»: " + Path.GetFullPath(path));
                return true;
            }
            catch (Exception error)
            {
                // Одна ошибка и выключение: иначе попытка открыть файл повторялась бы каждый тик.
                UnityEngine.Debug.LogError("[Разлом] Запись боя (" + CommandLineFlag + "): не открыть «" + path + "»: "
                    + error.Message);
                _requestedPath = null;
                _writer = null;
                return false;
            }
        }

        private void Close()
        {
            if (_writer == null) return;
            try { _writer.Flush(); _writer.Dispose(); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("[Разлом] Запись боя: " + error.Message); }
            _writer = null;
            _path = null;
        }

        private void OnApplicationQuit() => Close();

        private void OnDestroy()
        {
            Close();
            if (_instance == this) _instance = null;
        }

        private static void Num(StringBuilder b, Fix64 value)
            => b.Append(value.ToDouble().ToString("0.###", CultureInfo.InvariantCulture));
    }
}
