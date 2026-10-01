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
    /// ЗАПИСЬ ПОВОРОТА ТЕЛ МОБОВ ПО КАДРАМ (ревью 01.10, «лунная походка»; план —
    /// ART/characters/act-1-enemies/review/sidestep-fix-plan.md, п. 6). Только вид: читает то,
    /// что ArenaView уже поставил на экран, Sim не трогает.
    ///
    /// ТОЛЬКО РЕДАКТОР И DEV-СБОРКА, ВЫКЛЮЧЕНА ПО УМОЛЧАНИЮ. Флаг плеера:
    /// -capture-body-yaw &lt;путь.jsonl&gt; (в capture.ps1 — через -ExtraArgs). Хуки — три вызова в
    /// ArenaView.SyncTransforms; в релизе их вырезает компилятор ([Conditional]).
    ///
    /// Формат — JSON Lines, строка на кадр: первая {"meta":…}, дальше
    /// {"f":кадр,"t":тик Sim,"a":Alpha,"dt":секунды кадра,
    ///  "mobs":[[id,kind,bodyX,bodyZ,simX,simZ,velX,velY,moveStep,mode,posX,posZ],…]}:
    /// body — показанное тело, sim — интерполированный взгляд Sim (GetRenderFacing), vel —
    /// собственный шаг за тик (EntityStore.Velocity), moveStep — полный шаг за тик, mode —
    /// EnemyBodyMode (0 — по Sim, 1 — действие, 2 — по ходу, 3 — пятится), pos — отрисованное
    /// место корня (с отдачей и смещениями). Мера «лунной походки» та же, что в
    /// EnemyBodyFacingTests: шаг ≥ 45% полного и тело дальше 45° от хода
    /// (tools/body_yaw_report.py считает её по видам).
    /// </summary>
    public static class EnemyBodyYawRecorder
    {
        public const string CommandLineFlag = "-capture-body-yaw";
        public const int FormatVersion = 1;

        private static bool _parsed;
        private static string _path;
        private static StreamWriter _writer;
        private static readonly StringBuilder Line = new StringBuilder(8192);
        private static bool _inFrame, _any, _failed;
        private static int _unflushed;

        private static string RequestedPath
        {
            get
            {
                if (!_parsed)
                {
                    _parsed = true;
                    string[] args = Environment.GetCommandLineArgs();
                    for (int i = 0; i + 1 < args.Length; i++)
                        if (string.Equals(args[i], CommandLineFlag, StringComparison.OrdinalIgnoreCase))
                            _path = args[i + 1];
                }
                return _path;
            }
        }

        /// <summary>Начало кадра ArenaView: строка открывается, если запись включена.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void BeginFrame(TickDriver driver)
        {
            _inFrame = false;
            if (_failed || RequestedPath == null || driver == null || driver.Sim == null) return;
            if (_writer == null && !Open(driver)) return;
            var b = Line;
            b.Clear();
            b.Append("{\"f\":").Append(Time.frameCount).Append(",\"t\":").Append(driver.Sim.Tick)
                .Append(",\"a\":").Append(Num(driver.Alpha)).Append(",\"dt\":").Append(Num(Time.deltaTime))
                .Append(",\"mobs\":[");
            _inFrame = true;
            _any = false;
        }

        /// <summary>Одно живое тело врага в этом кадре — после поворота.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Mob(TickDriver driver, int id, EnemyKind kind, Vector3 body, Vector3 simFacing,
            FixVec2 velocity, Fix64 moveStep, EnemyBodyMode mode, Vector3 position)
        {
            if (!_inFrame) return;
            var b = Line;
            if (_any) b.Append(',');
            _any = true;
            b.Append('[').Append(id).Append(',').Append((int)kind).Append(',')
                .Append(Num(body.x)).Append(',').Append(Num(body.z)).Append(',')
                .Append(Num(simFacing.x)).Append(',').Append(Num(simFacing.z)).Append(',')
                .Append(Num(velocity.X.ToFloat())).Append(',').Append(Num(velocity.Y.ToFloat())).Append(',')
                .Append(Num(moveStep.ToFloat())).Append(',').Append((int)mode).Append(',')
                .Append(Num(position.x)).Append(',').Append(Num(position.z)).Append(']');
        }

        /// <summary>Конец кадра: строка уходит в файл (кадры без мобов не пишутся).</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void EndFrame()
        {
            if (!_inFrame) return;
            _inFrame = false;
            if (!_any || _writer == null) return;
            Line.Append("]}");
            _writer.WriteLine(Line.ToString());
            if (++_unflushed >= 60)
            {
                _unflushed = 0;
                _writer.Flush();
            }
        }

        private static bool Open(TickDriver driver)
        {
            try
            {
                string full = Path.GetFullPath(_path);
                string folder = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                _writer = new StreamWriter(full, false, new UTF8Encoding(false));
                _writer.WriteLine("{\"meta\":{\"format\":" + FormatVersion + ",\"ticksPerSecond\":" + Simulation.TicksPerSecond
                    + ",\"seed\":" + driver.RunSeed.ToString(CultureInfo.InvariantCulture)
                    + ",\"walkingShare\":0.45,\"moonwalkDegrees\":45"
                    + ",\"startedUtc\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}}");
                Application.quitting += Close;
                UnityEngine.Debug.Log("[Разлом] Запись поворота тел мобов: " + full);
                return true;
            }
            catch (Exception error)
            {
                // Одна ошибка и выключение: иначе попытка открыть файл повторялась бы каждый кадр.
                UnityEngine.Debug.LogError("[Разлом] Запись поворота тел (" + CommandLineFlag + "): не открыть «"
                    + _path + "»: " + error.Message);
                _failed = true;
                _writer = null;
                return false;
            }
        }

        private static void Close()
        {
            if (_writer == null) return;
            try { _writer.Flush(); _writer.Dispose(); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("[Разлом] Запись поворота тел: " + error.Message); }
            _writer = null;
        }

        private static string Num(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
