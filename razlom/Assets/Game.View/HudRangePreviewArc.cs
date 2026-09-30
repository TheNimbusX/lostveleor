using System;

namespace Game.View
{
    /// <summary>
    /// Пунктир дуги прыжка и броска в превью способностей (владелец 30.09, выбор 2a: «Абордаж» —
    /// пунктир прыжка от героя к месту приземления и диск с якорем) без UnityEngine: высота дуги и
    /// раскладка штрихов. Рисует HudRangePreview.JumpArc; проверяют тесты вне Unity
    /// (tools/Combat.Presentation.Tests/WorldEdgeMarksLayoutTests.cs).
    /// </summary>
    public static class HudRangePreviewArc
    {
        /// <summary>Вершина дуги над землёй, м: доля длины прыжка, но не выше <paramref name="max"/>.</summary>
        public static float Apex(float length, float ratio, float max)
        {
            if (!(length > 0f) || !(ratio > 0f)) return 0f;
            return Math.Min(length * ratio, Math.Max(0f, max));
        }

        /// <summary>Высота дуги на доле пути <paramref name="t"/>: парабола 4h·t(1−t), на концах — ноль.</summary>
        public static float Height(float t, float apex)
        {
            if (t <= 0f || t >= 1f) return 0f;
            return 4f * apex * t * (1f - t);
        }

        /// <summary>Сколько штрихов уместится на длине: штрих и промежуток, не меньше одного.</summary>
        public static int Dashes(float length, float dash, float gap)
        {
            if (!(length > 0f) || !(dash > 0f)) return 0;
            gap = Math.Max(0f, gap);
            return Math.Max(1, (int)Math.Floor((length + gap) / (dash + gap)));
        }

        /// <summary>
        /// Штрих <paramref name="index"/> из <paramref name="count"/> в долях пути [<paramref name="from"/>;
        /// <paramref name="to"/>]: штрихи и промежутки растянуты ровно на весь путь — первый начинается
        /// в начале, последний кончается в конце, без обрубка.
        /// </summary>
        public static void Dash(int index, int count, float dash, float gap, float from, float to, out float t0, out float t1)
        {
            t0 = t1 = from;
            if (count <= 0 || index < 0 || index >= count) return;
            float span = to - from;
            float total = count * dash + (count - 1) * Math.Max(0f, gap);
            if (!(total > 0f)) return;
            float k = span / total;
            t0 = from + index * (dash + Math.Max(0f, gap)) * k;
            t1 = t0 + dash * k;
        }
    }
}
