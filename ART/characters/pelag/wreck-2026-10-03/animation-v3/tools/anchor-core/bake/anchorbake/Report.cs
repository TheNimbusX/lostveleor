using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace AnchorBake
{
    public sealed class Variant
    {
        public string Label; public BakeRun Run; public Validation V; public float Shift;
    }

    public static class Report
    {
        static string N(double v, string f = "0.00") => v.ToString(f, CultureInfo.InvariantCulture);

        public static string Table(Validation v)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| # | Проверка | Значение | Порог | Итог | Где / пояснение |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var r in v.Rows)
                sb.AppendLine($"| {r.Id} | {r.Name} | {r.Value} | {r.Target} | {(r.Pass == null ? "—" : r.Pass.Value ? "ok" : "**FAIL**")} | {r.Where} |");
            return sb.ToString();
        }

        public static void Console(string label, Validation v)
        {
            System.Console.WriteLine($"== {label}: {(v.AllPass ? "PASS" : "FAIL")}");
            foreach (var r in v.Rows)
                System.Console.WriteLine($"  {(r.Pass == null ? "info" : r.Pass.Value ? "ok  " : "FAIL"),-4} {r.Id,-3} {r.Name}: {r.Value} (порог {r.Target}) {r.Where}");
        }

        public static void Write(string path, string name, GripTrack t, HeadModel head, Variant baseRun, List<Variant> retime,
            Variant best, Variant guided, List<Variant> tempo, string startFrom, ExternalChecks ext, List<Variant> others)
        {
            var c = baseRun.Run.Cfg;
            var sb = new StringBuilder();
            sb.AppendLine($"# Запечка якоря: {name} (прототип, {DateTime.Now:dd.MM.yyyy HH:mm})");
            sb.AppendLine();
            sb.AppendLine($"Клип `{t.Clip}` ({t.Frames} кадров при 30 к/с, кадр = тик Sim), вид запечки `{c.Kind}`, хват — `{t.Hand}Hand` × `grip_socket.json`, контакт — тик {N(c.ContactFrame, "0.#")} от StageStartTick. " +
                          $"Цепь L = {N(c.ChainLength)} м постоянная, голова {N(c.Mass, "0")} кг, I = ({N(c.Inertia.X)}; {N(c.Inertia.Y)}; {N(c.Inertia.Z)}), кольцо {N(head.EyeLocal.Y)} м над центром, g = {N(c.Gravity)}, " +
                          $"шаг 1/240 с, сопротивление воздуха ({N(c.AirLinear, "0.0")}; {N(c.AirAngular, "0.0")}), земля y = {N(c.Ground)} (WeaponGroundHeight − корень). Физика — ТОТ ЖЕ `AnchorRigCore.StepLive`, что у рига в игре (файлы `Game.View` компилируются в инструмент): Advance → ConstrainCable → земля с отскоком → тело `AnchorRigBody` (корпус r 0,38, кольцо r 0,22, оболочка против головы, ног и рук) → воздух; без PullEye/TurnTowards/Catch/Launch.");
            sb.AppendLine();
            sb.AppendLine($"Старт: `{startFrom}`. Оси — корень тела Unity (x вправо, y вверх, z вперёд = Direction), метры при WoleScale 1,82.");
            sb.AppendLine();
            sb.AppendLine("## 1. Честная запечка нынешнего клипа (без ретайма и наведения)");
            sb.AppendLine();
            sb.Append(Table(baseRun.V));
            sb.AppendLine();
            if (others.Count > 0)
            {
                sb.AppendLine("## 1a. Те же проверки при других стартах головы");
                sb.AppendLine();
                sb.AppendLine("| старт | контакт: ошибка, м | r / h / угол | скорость в контакте / пик, м/с | провис на скорости | проникновение | скачок | натяг цепи, пик |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var o in others)
                {
                    var v = o.V;
                    sb.AppendLine($"| {o.Run.Cfg.Start} | {N(v.ContactError)} | {N(v.ContactRadius)} / {N(v.ContactHeight)} / {N(v.ContactAngle, "0")}° | {N(v.ContactSpeed, "0.0")} / {N(v.MaxSpeed, "0.0")} | {v.SlackFastFrames} из {v.FastFrames} кадров, макс {N(v.MaxSlackFast * 100, "0")} см | {N(v.Penetration * 100, "0.0")} см {v.PenetrationWhere} | {N(v.MaxJump, "0.000")} м | {N(v.MaxTension, "0")} Н |");
                }
                sb.AppendLine();
                sb.AppendLine("Старты: `rest` — голова лежит у ног на провисшей цепи; `taut:<угол>` — лежит на земле на почти прямой цепи (98 % L) под углом от +z (лучший случай для маха); `back` — с крепления спины, отпущена в кадре 0 (в нынешних клипах кадра снятия нет), корпус её не толкает, пока центр не вышел из капсулы корпуса (как в игре, `IgnoreTorso`).");
                sb.AppendLine();
            }
            if (retime.Count > 0)
            {
                sb.AppendLine("## 2. Ретайм руки ±1,5 кадра (DESIGN 3.3 п. 1): ошибка контакта без наведения");
                sb.AppendLine();
                sb.AppendLine("| сдвиг, кадры | ошибка, м | r, м | h, м | угол, ° | скорость, м/с | провис на скорости, кадры |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var r in retime)
                    sb.AppendLine($"| {N(r.Shift, "+0.00;-0.00;0")} | {N(r.V.ContactError)} | {N(r.V.ContactRadius)} | {N(r.V.ContactHeight)} | {N(r.V.ContactAngle, "0")} | {N(r.V.ContactSpeed, "0.0")} | {r.V.SlackFastFrames} |");
                sb.AppendLine();
                sb.AppendLine($"Лучший сдвиг: {N(best.Shift, "+0.00;-0.00;0")} кадра → ошибка {N(best.V.ContactError)} м.");
                sb.AppendLine();
            }
            if (guided != null)
            {
                sb.AppendLine("## 3. Наведение в последние 3 тика поверх лучшего ретайма (DESIGN 3.3 п. 2)");
                sb.AppendLine();
                sb.Append(Table(guided.V));
                sb.AppendLine();
            }
            if (tempo.Count > 0)
            {
                sb.AppendLine("## 4. Темп (AbilityExecutionTicks), без наведения");
                sb.AppendLine();
                sb.AppendLine("| темп | ошибка контакта, м | скорость, м/с | провис на скорости, кадры | растяжение, мм |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var r in tempo)
                    sb.AppendLine($"| {r.Label} | {N(r.V.ContactError)} | {N(r.V.ContactSpeed, "0.0")} | {r.V.SlackFastFrames} | {N(r.V.MaxStretch * 1000, "0.0")} |");
                sb.AppendLine();
            }
            sb.AppendLine("## 5. Путь головы по тикам (честная запечка)");
            sb.AppendLine();
            sb.AppendLine("| тик | голова x, y, z | |v|, м/с | r от корня, м | хват x, y, z | кольцо–хват, м | натяг | земля |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            var run = baseRun.Run;
            float lastTick = run.Records[^1].Tick;
            for (float tick = 0; tick <= lastTick + 1e-3f; tick += 1)
            {
                var r = BakeSim.At(run, tick / t.Fps / c.ClipRate);
                double rad = Math.Sqrt(r.P.X * r.P.X + r.P.Z * r.P.Z);
                sb.AppendLine($"| {N(tick, "0")}{(Math.Abs(tick - c.ContactFrame) < .01 ? " ◆" : "")} | {N(r.P.X)}, {N(r.P.Y)}, {N(r.P.Z)} | {N(r.V.Length(), "0.0")} | {N(rad)} | {N(r.Grip.X)}, {N(r.Grip.Y)}, {N(r.Grip.Z)} | {N(r.Span)} | {(r.Taut ? "да" : "")} | {(r.Grounded ? "да" : "")} |");
            }
            sb.AppendLine();
            sb.AppendLine("## 6. Проверка конвейера шага A");
            sb.AppendLine();
            sb.AppendLine($"- Против клипа, который собрал Unity (`.anim`, FK на покое v6 из FBX): гнездо хвата {(ext.AnimSocket < 0 ? "нет данных" : N(ext.AnimSocket * 1000, "0.0") + " мм, поворот кисти до " + N(ext.AnimAngle, "0.0") + "°")} (порог §3.5: 5 мм и 1°). {ext.AnimNote}");
            sb.AppendLine($"- Против кисти в игре (лог `[anchor-slam] hand=` съёмки 03.10): {(ext.CaptureMax < 0 ? "нет данных" : "rms " + N(ext.CaptureRms * 100, "0.0") + " см, max " + N(ext.CaptureMax * 100, "0.0") + " см")}. {ext.CaptureNote}");
            sb.AppendLine();
            sb.AppendLine("Файлы: `" + name + ".anchorbake.json` (формат DESIGN 2.1), `" + name + ".sheet.json` + `.sheet.jpg` (лист кадров), этот отчёт.");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
