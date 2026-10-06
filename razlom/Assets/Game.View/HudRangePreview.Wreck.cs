using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    // Крушение, ритм v4 (владелец 06.10, «пусть будет так чище»): на полу только тонкий кремовый штрих — тот же вид,
    // что пунктир дуги прыжка «Абордажа» (Look.Dash: кремовый, с тонкой тенью), но сплошной и без заливки:
    // * контур следа всей серии до первого нажатия — куски HudWreckReachRules.Outline одной линией;
    // * линия полосы выпада между третьим нажатием и контактом.
    // Геометрию задаёт вызывающий (HudWreckReach — из Sim); здесь только меш.
    internal sealed partial class HudRangePreview
    {
        /// <summary>
        /// Контур следа Крушения: куски <paramref name="pieces"/> в осях героя (x — по <paramref name="forward"/>,
        /// y — влево от него, метры от <paramref name="hero"/>) сплошным тонким штрихом.
        /// </summary>
        public void WreckOutline(Vector3 hero, Vector3 forward, List<HudWreckOutlinePiece> pieces, float strength = 1f)
        {
            if (pieces == null || pieces.Count == 0) return;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();
            var left = new Vector3(-forward.z, 0f, forward.x);
            float angle = Angle(forward);
            Style(strength, Open, Look.Dash);
            for (int i = 0; i < pieces.Count; i++)
            {
                HudWreckOutlinePiece piece = pieces[i];
                if (piece.Arc)
                {
                    if (piece.Radius <= .01f || Mathf.Abs(piece.To - piece.From) <= .001f) continue;
                    Vector3 centre = hero + forward * piece.Cx + left * piece.Cy;
                    Band(centre, Vector3.right, Vector3.forward, piece.Radius, DashHalfWidth, angle + piece.From, angle + piece.To);
                }
                else Stroke(hero + forward * piece.Ax + left * piece.Ay, hero + forward * piece.Bx + left * piece.By, DashHalfWidth);
            }
        }

        /// <summary>Тонкая линия полосы выпада от <paramref name="from"/> до <paramref name="to"/> — тем же штрихом, что контур.</summary>
        public void WreckLine(Vector3 from, Vector3 to, float strength = 1f)
        {
            Vector3 flat = to - from;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return;
            Style(strength, Open, Look.Dash);
            Stroke(from, to, DashHalfWidth);
        }
    }
}
