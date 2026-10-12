using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Девятый вал: ленты веера (центр, эхо, края) и вал по земле — кадр за кадром, как гребень выпада базы
    /// (PelagVfxController.WreckComboLunge.BuildWreckCrest): голова на фронте Sim, точка ленты стареет с тех пор, как через
    /// неё прошёл фронт, высота — профиль гребня базы, вперёд ниже и уже (PelagWreckNinthLook). Статично и без контроллера —
    /// его же зовёт проба в редакторе (artifacts/wreck/v4/forms-vfx/ninth/tools).
    /// UV: u — метры вдоль полосы / 4 (+ сдвиг ленты), v — низ/верх (у вала по земле — поперёк, −1 … +1), z — возраст точки
    /// (с), w — возраст удара (с).
    /// </summary>
    public static class PelagWreckNinthCrest
    {
        public struct Lane
        {
            /// <summary>Корень меша (точка удара на земле), начало полосы (герой), ход полосы (единичный, XZ).</summary>
            public Vector3 Root, Origin, Dir;
            public float Start, End, Step, HalfWidth;
            public int Tick, Travel;
        }

        public struct Ribbon
        {
            /// <summary>Сдвиг поперёк полосы — доля полуширины Sim (0 — ось, + влево от хода, − вправо).</summary>
            public float Side;
            /// <summary>Наклон наружу от вертикали, град (только у краёв; центр стоит к экрану).</summary>
            public float Tilt;
            /// <summary>Высота у фронта, м (до сужения), задержка от фронта Sim, тики, сдвиг рисунка.</summary>
            public float Height, DelayTicks, USeed;
            /// <summary>Сдвиг ленты (эхо — от камеры).</summary>
            public Vector3 Offset;
        }

        private static readonly List<Vector3> Vertices = new List<Vector3>(512);
        private static readonly List<Vector4> Uv = new List<Vector4>(512);
        private static readonly List<int> Triangles = new List<int>(1536);

        /// <summary>
        /// Стоячая лента на тик показа <paramref name="shown"/>. <paramref name="lift"/> — «вверх» центральной ленты (к экрану:
        /// нормаль к взгляду и полосе), <paramref name="ground"/> — высота земли. False — ленты ещё (или уже) нет.
        /// </summary>
        public static bool Build(Mesh mesh, in Lane lane, in Ribbon ribbon, float shown, Vector3 lift, System.Func<float, float, float> ground)
        {
            mesh.Clear();
            float local = shown - ribbon.DelayTicks;
            if (!Span(lane, local, out float tail, out float front, out int segments, out float global)) return false;
            var side = new Vector3(-lane.Dir.z, 0f, lane.Dir.x);
            float sign = ribbon.Side >= 0f ? 1f : -1f;
            float tilt = ribbon.Tilt * Mathf.Deg2Rad;
            Vector3 up = ribbon.Side == 0f ? lift : (Vector3.up * Mathf.Cos(tilt) + side * (sign * Mathf.Sin(tilt))).normalized;
            Begin();
            for (int i = 0; i <= segments; i++)
            {
                float s = Mathf.Lerp(tail, front, i / (float)segments);
                float age = AgeAt(lane, local, s);
                float t = PelagWreckNinthLook.PathFraction(s, lane.Start, lane.End);
                float end = PelagWreckNinthLook.EndTaper(s, lane.End);
                float h = ribbon.Height * PelagWreckNinthLook.HeightTaper(t) * end * PelagWreckNinthLook.Profile(age);
                float lateral = ribbon.Side * lane.HalfWidth * PelagWreckNinthLook.LateralTaper(t) * (.4f + .6f * end);
                if (s < lane.Start)
                {
                    float k = PelagWreckComboLungeLook.Smooth01((s - tail) / PelagWreckComboLungeLook.TailLead);
                    h *= k;
                    lateral *= .35f + .65f * k;
                }
                Vector3 axis = lane.Origin + lane.Dir * s + side * lateral + ribbon.Offset;
                axis.y = ground(axis.x, axis.z) - PelagWreckComboLungeLook.Sink;
                Vector3 top = axis + up * (h + PelagWreckComboLungeLook.Sink) + lane.Dir * (PelagWreckComboLungeLook.Lean * h);
                float u = s / PelagWreckComboLungeLook.MetersPerU + ribbon.USeed;
                Vertices.Add(axis - lane.Root);
                Vertices.Add(top - lane.Root);
                Uv.Add(new Vector4(u, 0f, age, global));
                Uv.Add(new Vector4(u, 1f, age, global));
                if (i < segments) Quad(i * 2, 2);
            }
            return Finish(mesh);
        }

        /// <summary>
        /// Вал по земле (второй слой под веером): поперёк — ±полуширина Sim × сужение × разлёт шипов; ширина раскрывается за
        /// фронтом за время подъёма гребня. Три вершины поперёк (середина — ось).
        /// </summary>
        public static bool BuildGround(Mesh mesh, in Lane lane, float shown, float lift, System.Func<float, float, float> ground)
        {
            mesh.Clear();
            if (!Span(lane, shown, out float tail, out float front, out int segments, out float global)) return false;
            var side = new Vector3(-lane.Dir.z, 0f, lane.Dir.x);
            Begin();
            for (int i = 0; i <= segments; i++)
            {
                float s = Mathf.Lerp(tail, front, i / (float)segments);
                float age = AgeAt(lane, shown, s);
                float t = PelagWreckNinthLook.PathFraction(s, lane.Start, lane.End);
                float open = Mathf.Clamp01(age / PelagWreckNinthLook.RiseSeconds);
                float w = lane.HalfWidth * PelagWreckNinthLook.LateralTaper(t) * PelagWreckNinthLook.GroundSpread * (.35f + .65f * open)
                    * (.3f + .7f * PelagWreckNinthLook.EndTaper(s, lane.End));
                if (s < lane.Start) w *= .4f + .6f * PelagWreckComboLungeLook.Smooth01((s - tail) / PelagWreckComboLungeLook.TailLead);
                float u = s / PelagWreckComboLungeLook.MetersPerU;
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 p = lane.Origin + lane.Dir * s + side * (k * w);
                    p.y = ground(p.x, p.z) + lift;
                    Vertices.Add(p - lane.Root);
                    Uv.Add(new Vector4(u, k, age, global));
                }
                if (i < segments) { Quad(i * 3, 3); Quad(i * 3 + 1, 3); }
            }
            return Finish(mesh);
        }

        /// <summary>Лента «к экрану»: нормаль к взгляду и полосе, вверх (как у гребня базы).</summary>
        public static Vector3 Lift(Vector3 view, Vector3 dir)
        {
            Vector3 lift = Vector3.Cross(view, dir);
            if (lift.sqrMagnitude < 1e-4f) lift = Vector3.up;
            lift.Normalize();
            return lift.y < 0f ? -lift : lift;
        }

        private static bool Span(in Lane lane, float local, out float tail, out float front, out int segments, out float global)
        {
            front = PelagWreckVfxRules.WaveFront(local, lane.Tick, lane.Start, lane.Step, lane.Travel, lane.End);
            tail = lane.Start - PelagWreckComboLungeLook.TailLead;
            segments = Mathf.Clamp(Mathf.CeilToInt((front - tail) / PelagWreckComboLungeLook.Segment), 2, 140);
            global = Mathf.Max(0f, PelagWreckVfxRules.Seconds(local - lane.Tick + 1f));
            return front > tail + .05f && local >= lane.Tick - 1f;
        }

        private static float AgeAt(in Lane lane, float local, float s)
        {
            float pass = lane.Tick - 1f + Mathf.Max(0f, s - lane.Start) / Mathf.Max(.05f, lane.Step);
            return Mathf.Max(0f, PelagWreckVfxRules.Seconds(local - pass));
        }

        private static void Begin()
        {
            Vertices.Clear();
            Uv.Clear();
            Triangles.Clear();
        }

        /// <summary>Четырёхугольник между столбцами вершин: v — нижняя вершина этого столбца, stride — вершин в столбце.</summary>
        private static void Quad(int v, int stride)
        {
            Triangles.Add(v); Triangles.Add(v + 1); Triangles.Add(v + stride);
            Triangles.Add(v + 1); Triangles.Add(v + stride + 1); Triangles.Add(v + stride);
        }

        private static bool Finish(Mesh mesh)
        {
            mesh.SetVertices(Vertices);
            mesh.SetUVs(0, Uv);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return true;
        }
    }
}
