namespace Game.Sim
{
    /// <summary>
    /// Крушение v4 в игре (06.10, клипы Pelag_AN_Wreck4_*): шаг выпада.
    ///
    /// Тело клипа Lunge стоит в своих осях, а корень едет вперёд на 0,6 м по времени выпада сабли
    /// (как Simulation.SabreCombo: StepSabreLunge — шаг Sim, а не сдвиг вида; ART/.../animation-v4/scripts/
    /// v4_lib.py lunge_root: корень 0,15 / 0,30 / 0,45 / 0,60 м в кадрах 5–8). Шаги — в последние
    /// WreckLungeTicks тиков замаха выпада, тик удара включительно (до удара в этом же тике). Упирается в тело
    /// врага и в стену; в корнях и под чужим принудительным движением шага нет. Точка удара считается от героя
    /// каждый тик замаха — после шага она на WreckSlamReach (2,2 м) перед ним, как в превью v4.
    ///
    /// Снятие якоря со спины (клип Draw, 8 кадров) в Sim сроков не добавляет: первый мах — 5 тиков, как у сабли;
    /// вид сжимает снятие в начало замаха (PelagWreckClipRules.DrawShare).
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Шаг выпада (как SabreLungeDistance): столько метров вперёд за WreckLungeTicks тиков.</summary>
        public static readonly Fix64 WreckLungeDistance = Fix64.Ratio(6, 10);

        /// <summary>Тиков шага выпада: последние тики замаха, тик удара включительно.</summary>
        public const int WreckLungeTicks = 4;

        /// <summary>Первый тик шага выпада (шаги — по тик удара включительно) при замахе с этого контакта.</summary>
        private int WreckLungeFrom => _wreck.ContactTick - System.Math.Max(1, System.Math.Min(WreckLungeTicks,
            _wreck.ContactTick - _wreck.StageStartTick)) + 1;

        /// <summary>Шаг выпада этого тика (замах выпада, до удара в этом же тике).</summary>
        private void StepWreckLunge()
        {
            if (_wreck.Stage != WreckStages - 1 || _wreck.Phase != WreckPhase.Windup) return;
            if (Tick < WreckLungeFrom || Tick > _wreck.ContactTick || HeroRooted) return;
            if (ForcedMotion.IsActive(Entities, PlayerId)) return;
            int ticks = _wreck.ContactTick - WreckLungeFrom + 1;
            FixVec2 direction = _wreck.Direction;
            Fix64 allowed = WreckLungeDistance / ticks;
            FixVec2 from = Entities.Position[PlayerId];
            Fix64 body = Entities.BodyRadius[PlayerId];
            for (int i = 1; i < Entities.Count; i++)
            {
                if (!Entities.Alive[i] || Entities.Side[i] == Entities.Side[PlayerId]) continue;
                FixVec2 offset = Entities.Position[i] - from;
                Fix64 along = FixVec2.Dot(offset, direction);
                if (along.Raw <= 0) continue;
                Fix64 across = Fix64.Abs(offset.X * direction.Y - offset.Y * direction.X);
                Fix64 gap = body + Entities.BodyRadius[i];
                if (across >= gap) continue;
                Fix64 touch = along - Fix64.Sqrt(gap * gap - across * across);
                if (touch < allowed) allowed = touch;
            }
            if (allowed.Raw <= 0) return;
            FixVec2 to = WreckTravel(from, direction * allowed, body);
            if (to.X == from.X && to.Y == from.Y) return;
            Entities.Position[PlayerId] = to;
            Entities.Facing[PlayerId] = direction;
            // Шаг идёт после движения и расталкивания этого тика: бой и цели должны видеть новое место.
            Grid.Rebuild(Entities);
        }

        /// <summary>Сдвиг на step кусками, как ходьба (CanTravel): у стены — до неё, вдоль не скользит.</summary>
        private FixVec2 WreckTravel(FixVec2 from, FixVec2 step, Fix64 body)
        {
            if (_layout == null && _campWalkMap == null) return from + step;
            Fix64 length = step.Length;
            int pieces = System.Math.Max(1, (length / (LayoutMap.CellSize / Fix64.FromInt(8))).ToInt() + 1);
            FixVec2 piece = step / Fix64.FromInt(pieces);
            for (int p = 0; p < pieces; p++)
            {
                if (!CanTravel(from, from + piece, body)) break;
                from += piece;
            }
            return from;
        }

        /// <summary>
        /// Сколько шага выпада ещё впереди (для следа HUD до удара): до нажатия выпада — весь, в его замахе — остаток
        /// (тиков шага с текущего, Tick — следующий тик Sim). Стена — по CanTravel, тела врагов не учитываются.
        /// </summary>
        private Fix64 WreckLungeAhead(FixVec2 origin, FixVec2 direction, bool locked)
        {
            if (!locked || _wreck.Phase == WreckPhase.Charge) return WreckLungeReach(origin, direction, WreckLungeDistance);
            int total = System.Math.Max(1, _wreck.ContactTick - WreckLungeFrom + 1);
            int steps = WreckClamp(_wreck.ContactTick - System.Math.Max(Tick, WreckLungeFrom) + 1, 0, total);
            return steps <= 0 ? Fix64.Zero : WreckLungeReach(origin, direction, WreckLungeDistance * Fix64.Ratio(steps, total));
        }

        private Fix64 WreckLungeReach(FixVec2 origin, FixVec2 direction, Fix64 distance)
        {
            FixVec2 to = WreckTravel(origin, direction * distance, Entities.BodyRadius[PlayerId]);
            return FixVec2.Dot(to - origin, direction);
        }
    }
}
