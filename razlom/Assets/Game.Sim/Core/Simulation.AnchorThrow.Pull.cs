namespace Game.Sim
{
    /// <summary>
    /// Бросок якоря: полукольцо мест, тяга Reeled по кривой Водоворота, кто доехал
    /// к ловле; буферы, сброс и хеш (Simulation.AnchorThrow).
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Натяг: все задетые «на тягу» живые — на полукольцо перед героем
        /// (AnchorThrowRing), тяга Reeled на ReturnTicks тиков. Путь каждого — для кривой.
        /// </summary>
        private void AnchorThrowStartTow()
        {
            int count = 0;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if ((_throwHit[i] & ThrowHitPull) == 0 || !AbordageEnemy(i)) continue;
                if (Entities.Kind[i] == EnemyKind.ForestThicketMaster) continue;
                _ringIds[count] = i;
                _ringPositions[count] = Entities.Position[i];
                _ringRadii[count] = Entities.BodyRadius[i];
                count++;
            }
            FixVec2 hero = Entities.Position[PlayerId];
            Fix64 rho = AnchorThrowRing(hero, _anchorThrow.Dir, Entities.BodyRadius[PlayerId],
                _ringPositions, _ringRadii, _ringIds, count, _ringSpots);
            int reeled = 0;
            for (int k = 0; k < count; k++)
            {
                int i = _ringIds[k];
                FixVec2 spot = _ringSpots[k];
                if (!ForcedMotion.Begin(Entities, i, spot, _anchorThrow.ReturnTicks, ForcedMotionKind.Reeled)) continue;
                _throwReeled[i] = true;
                _throwPullLength[i] = FixVec2.Distance(spot, Entities.Position[i]);
                _throwSlotAt[i] = spot;
                reeled++;
            }
            _anchorThrow.ReeledCount = reeled;
            _anchorThrow.RingRadius = reeled > 0 ? rho : Fix64.Zero;
        }

        /// <summary>
        /// Тики возврата до ловли: тело выпало из своей тяги — умерло или чужое движение
        /// (вал Крушения, Водоворот, толчок) перезаписало ForcedKind. Оглушения приземления не будет.
        /// </summary>
        private void AnchorThrowKeepTow()
        {
            for (int i = PlayerId + 1; i < Entities.Count; i++)
                if (_throwReeled[i] && (!Entities.Alive[i] || Entities.ForcedKind[i] != (byte)ForcedMotionKind.Reeled))
                    _throwReeled[i] = false;
        }

        /// <summary>
        /// В тик ловли последний шаг ResolveForcedMotion уже снял тягу (ForcedKind = None):
        /// доехал тот, кто ещё в своей тяге по флагу и не подхвачен чужим движением.
        /// </summary>
        private bool AnchorThrowArrived(int i)
            => _throwReeled[i] && Entities.Alive[i]
               && (Entities.ForcedKind[i] == (byte)ForcedMotionKind.None || Entities.ForcedKind[i] == (byte)ForcedMotionKind.Reeled);

        /// <summary>
        /// ResolveForcedMotion: шаг тела, которое тянет Бросок, — копия правила
        /// MaelstromPullStep (кривая Водоворота, догон ×1,35, упёртое стоит и догоняет)
        /// со своими массивами; файл Вихря не трогается. False — тело тянет не этот
        /// Бросок (сменил другой волок, толчок или срыв).
        /// </summary>
        private bool AnchorThrowPullStep(int id, int left, FixVec2 delta, out FixVec2 step)
        {
            step = delta;
            if (_throwReeled == null || !_throwReeled[id]) return false;
            if (_anchorThrow.Phase != AnchorThrowPhase.Taut && _anchorThrow.Phase != AnchorThrowPhase.Return) return false;
            int elapsed = Tick - _anchorThrow.TautTick - 1;
            int ticks = _anchorThrow.ReturnTicks;
            if (elapsed < 0 || left != ticks - elapsed
                || Entities.ForcedKind[id] != (byte)ForcedMotionKind.Reeled) return false;
            Fix64 length = _throwPullLength[id];
            Fix64 planned = length * (Fix64.One - MaelstromPullProgress(elapsed, ticks));
            Fix64 plannedStep = length * (MaelstromPullProgress(elapsed + 1, ticks) - MaelstromPullProgress(elapsed, ticks));
            Fix64 room = planned * MaelstromPullCatchUp;
            Fix64 remaining = delta.Length;
            if (remaining > room + MaelstromPullLagSlack)
            {
                delta = delta * (room / remaining);
                Entities.ForcedTarget[id] = Entities.Position[id] + delta;
                remaining = delta.Length;
            }
            if (remaining.Raw == 0) { step = delta; return true; }
            int whole = (MaelstromPullBend + 1) * ticks * ticks;
            Fix64 share = Fix64.Ratio(ticks + MaelstromPullBend * (2 * elapsed + 1),
                whole - elapsed * ticks - MaelstromPullBend * elapsed * elapsed);
            step = delta * share;
            // Отстало от плана — догоняет: план тика плюс отставание, не больше CatchUp × план тика.
            Fix64 back = remaining - planned + plannedStep;
            if (back > remaining * share + MaelstromPullLagSlack)
            {
                Fix64 most = plannedStep * MaelstromPullCatchUp;
                step = delta * ((back < most ? back : most) / remaining);
            }
            return true;
        }

        /// <summary>
        /// ПОЛУКОЛЬЦО МЕСТ (чистая функция; её же зовут тесты и, позже, HUD). Тела
        /// [0, count) сортируются на месте по углу от героя относительно dir — ключ
        /// Atan2(cross(dir, v), dot(dir, v)), ничьи — ids. S = Σ соседей (r_a + r_b + 0,15);
        /// ρ = max(clamp(S/π, r_героя + 0,3 + r_max, 2,6), S/(1,6π)) — дуга не шире 288°;
        /// θ₀ = −S/(2ρ), θ_k = θ_{k−1} + (r_{k−1} + r_k + 0,15)/ρ; место = герой + Rot(dir, θ_k)·ρ.
        /// Возвращает ρ (0 — тел нет). spots[k] — место k-го после сортировки.
        /// </summary>
        public static Fix64 AnchorThrowRing(FixVec2 hero, FixVec2 dir, Fix64 heroRadius,
            FixVec2[] positions, Fix64[] radii, int[] ids, int count, FixVec2[] spots)
        {
            if (count <= 0) return Fix64.Zero;
            // Вставками по ключу — устойчиво и без аллокаций; ничья — меньший id первым.
            for (int a = 1; a < count; a++)
            {
                FixVec2 p = positions[a];
                Fix64 r = radii[a];
                int id = ids[a];
                Fix64 key = AnchorThrowRingKey(hero, dir, p);
                int b = a - 1;
                while (b >= 0)
                {
                    Fix64 other = AnchorThrowRingKey(hero, dir, positions[b]);
                    if (other < key || other == key && ids[b] < id) break;
                    positions[b + 1] = positions[b]; radii[b + 1] = radii[b]; ids[b + 1] = ids[b];
                    b--;
                }
                positions[b + 1] = p; radii[b + 1] = r; ids[b + 1] = id;
            }

            Fix64 sum = Fix64.Zero, widest = radii[0];
            for (int k = 1; k < count; k++)
            {
                sum += radii[k - 1] + radii[k] + AnchorThrowRingSpacing;
                if (radii[k] > widest) widest = radii[k];
            }
            Fix64 inner = heroRadius + AnchorThrowRingGap + widest;
            Fix64 rho = Fix64.Clamp(sum / Fix64.Pi, inner, inner > AnchorThrowRingMax ? inner : AnchorThrowRingMax);
            Fix64 arc = sum / AnchorThrowRingArcLimit;
            if (arc > rho) rho = arc;

            Fix64 theta = -sum / (rho * 2);
            for (int k = 0; k < count; k++)
            {
                if (k > 0) theta += (radii[k - 1] + radii[k] + AnchorThrowRingSpacing) / rho;
                Fix64 c = Fix64.Cos(theta), s = Fix64.Sin(theta);
                var around = new FixVec2(dir.X * c - dir.Y * s, dir.X * s + dir.Y * c);
                spots[k] = hero + around * rho;
            }
            return rho;
        }

        /// <summary>Угол тела от героя относительно dir, радианы (−π…π): порядок на полукольце.</summary>
        private static Fix64 AnchorThrowRingKey(FixVec2 hero, FixVec2 dir, FixVec2 at)
        {
            FixVec2 v = at - hero;
            return Fix64.Atan2(dir.X * v.Y - dir.Y * v.X, FixVec2.Dot(dir, v));
        }

        private void EnsureAnchorThrowBuffers()
        {
            if (_throwHit != null) return;
            int capacity = Entities.Capacity;
            _throwHit = new byte[capacity];
            _throwReeled = new bool[capacity];
            _throwPullLength = new Fix64[capacity];
            _throwSlotAt = new FixVec2[capacity];
            _ringIds = new int[capacity];
            _ringPositions = new FixVec2[capacity];
            _ringSpots = new FixVec2[capacity];
            _ringRadii = new Fix64[capacity];
        }

        private void ResetAnchorThrow()
        {
            _anchorThrow = new AnchorThrowState { HarpoonTarget = -1 };
            if (_throwHit == null) return;
            System.Array.Clear(_throwHit, 0, _throwHit.Length);
            System.Array.Clear(_throwReeled, 0, _throwReeled.Length);
            System.Array.Clear(_throwPullLength, 0, _throwPullLength.Length);
            System.Array.Clear(_throwSlotAt, 0, _throwSlotAt.Length);
        }

        /// <summary>
        /// Только после первого Броска расстановки («ANCT» + снимок); кто задет — пока
        /// полёт…возврат; тяга (флаг, путь, место) — пока она жива (натяг…ловля).
        /// Без Броска хеш прежний бит в бит (CrowdStepHashPinTests, FormPinTests).
        /// </summary>
        private void HashAnchorThrow(ref ulong hash)
        {
            if (_anchorThrow.Serial == 0) return;
            Hashing.Mix(ref hash, 0x414E4354);   // "ANCT"
            _anchorThrow.HashInto(ref hash);
            AnchorThrowPhase phase = _anchorThrow.Phase;
            if (_throwHit == null || phase < AnchorThrowPhase.Flight || phase > AnchorThrowPhase.Return) return;
            Hashing.Mix(ref hash, 0x414E5448);   // "ANTH"
            for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, _throwHit[i]);
            if (phase < AnchorThrowPhase.Taut) return;
            Hashing.Mix(ref hash, 0x414E5452);   // "ANTR"
            for (int i = 0; i < Entities.Count; i++)
            {
                if (!_throwReeled[i]) continue;
                Hashing.Mix(ref hash, i);
                Hashing.Mix(ref hash, _throwPullLength[i]);
                Hashing.Mix(ref hash, _throwSlotAt[i].X); Hashing.Mix(ref hash, _throwSlotAt[i].Y);
            }
        }
    }
}
