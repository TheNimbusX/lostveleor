using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Крушение v2 — слои из паков (спека 5, «Мах 1–2» и «Удар оземь»):
    ///  • СЕРП МАХА — полумесяц пака CFXR «sword_trail 180 thick» (принятая волна серии сабли
    ///    VFX_Pelag_Sabre_Wave под своим id WreckSwing: шум пузырьков пака, пена по внешнему краю,
    ///    тонкий тёмный обвод, раскадровка по возрасту частицы). Горизонтальный, на высоте головы
    ///    якоря, внешний край — на радиусе головы, голова серпа уходит туда, куда летит настоящая
    ///    голова; рождается за PelagWreckVfxRules.SweepLeadTicks до удара — приходит к удару с ней.
    ///    Тонкая лента за головой (.WreckArc) остаётся следом когтя в замахе;
    ///  • КОРОТКАЯ ТРЕЩИНА удара оземь (кадр A) — тёмная копия линии раскола Рассекающего
    ///    (VFX_Pelag_Wreck_Crack, PelagWreckFoamVfxSetup): главная линия по полосе с ответвлениями и
    ///    щепками, без кольца и белых осколков.
    /// Оба рождаются от событий Sim (этап и удар в очереди до тика показа), не опросом.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        /// <summary>Серп маха в кадре показа: по стороне и скорости головы, на её радиусе и высоте.</summary>
        private void PlayWreckSweep(WreckArcRun run, in WreckState w)
        {
            Vector3 hero = PlayerPosition();
            Vector3 forward = w.Serial == run.Serial && w.Stage == run.Stage && w.Direction.LengthSq.Raw != 0
                ? new Vector3(w.Direction.X.ToFloat(), 0f, w.Direction.Y.ToFloat()).normalized
                : PlayerFacing();
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            int side = PelagWreckVfxRules.SweepHeadSide(Vector3.Dot(run.Velocity, right), run.SimSide);
            float ground = WreckGroundAt(hero);
            Vector3 flat = run.Head - hero;
            flat.y = 0f;
            float radius = PelagWreckVfxRules.SweepRadius(flat.magnitude);
            float height = PelagWreckVfxRules.SweepHeight(run.Head.y - ground);
            // Оси корня серпа (как у волны серии сабли): −X — удар, +Y — куда уходит голова, +Z — нормаль;
            // голова вправо — нормаль вниз (тот же поворот на 180° вокруг оси удара, без отрицательного масштаба).
            Vector3 headSide = side > 0 ? right : -right;
            Vector3 normal = side > 0 ? Vector3.down : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(normal, headSide);
            Vector3 centre = new Vector3(hero.x, ground + height, hero.z) - forward * PelagWreckVfxRules.SweepBack;
            int fx = WkSpawn(PelagVfxId.WreckSwing, centre, rotation, radius + PelagWreckVfxRules.SweepBack, 0f, run.Form, out _);
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-vfx] sweep stage={run.Stage} side={side} simSide={run.SimSide} radius={radius:F2} height={height:F2} contact={run.Contact} fx={fx}");
        }

        /// <summary>Короткая трещина удара оземь: от точки удара назад на треть радиуса и вперёд по полосе.</summary>
        private void PlayWreckCrack(Vector3 impact, Vector3 along, float impactRadius)
        {
            if (CaptureRig.NoVfx) return;
            along.y = 0f;
            if (along.sqrMagnitude < 1e-4f) along = PlayerFacing();
            along.Normalize();
            float radius = Mathf.Max(.3f, impactRadius);
            Vector3 origin = impact - along * (radius * PelagWreckVfxRules.CrackBackOfRadius);
            origin.y = WreckGroundAt(origin) + .04f;
            // Меш линии раскола лежит в XY вдоль +X: местная +Z смотрит вверх, +X — по полосе.
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, Vector3.Cross(Vector3.up, along));
            if (WkSpawn(PelagVfxId.WreckCrack, origin, rotation, 0f, 0f, PelagForm.None, out GameObject go) < 0) return;
            // Частицы линии читают масштаб корня при рождении — длина ставится после выдачи из пула.
            go.GetComponent<PelagCleaveSplitView>()?.Begin(radius * PelagWreckVfxRules.CrackLengthOfRadius);
        }
    }
}
