namespace Game.Sim
{
    /// <summary>
    /// МАХИ ПО ХОДУ ГОЛОВЫ (06.10, ревью «Крушение v3»). Мах бьёт не весь сектор ±72° в тик
    /// контакта: голова кистеня проходит его за 7–8 тиков, и каждого врага в секторе бьёт
    /// в тот тик, когда голова проходит его угол. Тик контакта этапа — голова проходит
    /// направление этапа (взгляд Sim). Урон, сектор и оглушение махов — прежние.
    ///
    /// СТОРОНЫ v4 (06.10, сабельный ритм): мах 1 — справа налево, мах 2 — слева направо
    /// (WreckSwingSide): у маха 2 таблица зеркальна — первым бьёт левый край.
    ///
    /// Путь головы — пока запечки клипов v3 (ART/characters/pelag/wreck-2026-10-03/animation-v3/
    /// bake/Pelag_AN_Wreck2_Swing1_w7 и _Swing2_w14.anchorbake.json, азимут головы вокруг
    /// корня по четвертям тика; заглушка до запечки v4): голова 20–23° за тик.
    /// Враг получает тик, ближайший к проходу головы: граница между сдвигами k и k+1 —
    /// азимут головы в кадре контакт + k + ½. Таблица — целые tg × 10000 (без тригонометрии).
    ///
    /// Темп (AbilitySpeed) сжимает замах — сдвиги до контакта сжимаются с ним; проводка — нет.
    /// Нажатие следующего этапа мах не обрывает: голова летит дальше; при самом быстром темпе
    /// (мах 2 с удар 1 + 1) мах 2 входит в сектор в удар 1 + 3 — остаток маха 1 добивает разом.
    /// Срыв до контакта (рывок, оглушение, другой навык) — мах кончается там, где была голова.
    /// Девятый вал (06.10 вечером): мах, задевший хоть одного врага, — заряд выпада (Simulation.Wreck.Forms).
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Сдвиг первого столбца таблицы: голова входит в сектор за 3 тика до контакта.</summary>
        public const int WreckSweepFirstOffset = -3;

        /// <summary>Последний сдвиг: голова уходит из сектора через 4 тика после контакта.</summary>
        public const int WreckSweepLastOffset = 4;

        /// <summary>
        /// Первый мах (v3, контакт — кадр 7). Границы сдвигов −3|−2|−1|0|+1|+2|+3|+4, градусы от
        /// взгляда (+ — справа): 70,13 · 44,34 · 20,22 · −1,27 · −21,36 · −40,58 · −59,33.
        /// </summary>
        private static readonly int[] WreckSweepTan1 = { 27663, 9773, 3683, -222, -3911, -8566, -16859 };

        /// <summary>
        /// Второй мах (v3, контакт — кадр 14), градусы по ходу головы: 67,60 · 42,99 · 20,31 · −0,68 ·
        /// −20,96 · −41,06 · −61,32. С v4 мах 2 идёт слева направо — читается зеркально (+ — слева).
        /// </summary>
        private static readonly int[] WreckSweepTan2 = { 24261, 9322, 3702, -119, -3831, -8711, -18279 };

        /// <summary>Мах на пути: этап, серия, контакт, замах с темпом и без, первый и последний тик, направление. Stage −1 — нет.</summary>
        private struct WreckSweep
        {
            public int Stage, Serial, Contact, Windup, Base, From, To;
            public FixVec2 Dir;

            public void HashInto(ref ulong hash)
            {
                Hashing.Mix(ref hash, Stage); Hashing.Mix(ref hash, Serial); Hashing.Mix(ref hash, Contact);
                Hashing.Mix(ref hash, Windup); Hashing.Mix(ref hash, Base); Hashing.Mix(ref hash, From); Hashing.Mix(ref hash, To);
                Hashing.Mix(ref hash, Dir.X); Hashing.Mix(ref hash, Dir.Y);
            }
        }

        private static readonly WreckSweep WreckSweepIdle = new WreckSweep { Stage = -1 };

        /// <summary>Идущий мах и следующий (нажат, голова ещё не в секторе) — второй мах нажимают, пока первый не догулял.</summary>
        private WreckSweep _wreckSweep = WreckSweepIdle, _wreckSweepNext = WreckSweepIdle;

        /// <summary>Последний начатый мах (идущий или доигранный) — только виду; в ход Sim и хеш не входит.</summary>
        private WreckSweep _wreckSweepLast = WreckSweepIdle;
        private byte[] _wreckSweepHits;   // 1 — идущий мах его уже ударил

        /// <summary>
        /// Сдвиг тика удара от контакта по таблице (тики клипа, без темпа): мах stage (0, 1),
        /// направление этапа direction, враг в delta от героя. Мах 1: справа −3 … слева +4;
        /// мах 2 (слева направо): слева −3 … справа +4; в центре — 0; за плечом (не впереди) —
        /// край своей стороны.
        /// </summary>
        public static int WreckSweepOffset(int stage, FixVec2 direction, FixVec2 delta)
        {
            Fix64 forward = FixVec2.Dot(delta, direction);
            // Справа от взгляда: взгляд, повёрнутый на 90° по часовой (вид: x — X Sim, z — Y Sim).
            Fix64 right = delta.X * direction.Y - delta.Y * direction.X;
            // «Откуда голова входит»: мах слева направо (Side −1) — зеркало, сторона входа — левая.
            if (WreckSwingSide(stage) < 0) right = -right;
            if (forward.Raw <= 0)
            {
                if (forward.Raw == 0 && right.Raw == 0) return 0;
                return right.Raw >= 0 ? WreckSweepFirstOffset : WreckSweepLastOffset;
            }
            int[] tan = stage == 1 ? WreckSweepTan2 : WreckSweepTan1;
            int offset = WreckSweepFirstOffset;
            // Голова ещё не дошла до границы, а враг левее неё: справа налево — следующий сдвиг.
            for (int j = 0; j < tan.Length; j++)
                if (right * 10000 < forward * tan[j]) offset++;
            return offset;
        }

        /// <summary>Тик удара сдвига offset от контакта: до контакта — с темпом замаха windup из base.</summary>
        private static int WreckSweepTick(int contact, int offset, int windup, int baseWindup)
        {
            if (offset >= 0 || baseWindup <= 0) return contact + offset;
            return contact - (-offset * windup + baseWindup / 2) / baseWindup;
        }

        /// <summary>Нажатие маха (этапы 0, 1): мах заряжен — голова пойдёт по сектору к контакту этапа.</summary>
        private void ArmWreckSweep(int stage, int baseWindup)
        {
            int windup = AbilityExecutionTicks(baseWindup);
            var sweep = new WreckSweep
            {
                Stage = stage, Serial = _wreck.Serial, Contact = _wreck.ContactTick, Windup = windup, Base = baseWindup,
                From = WreckSweepTick(_wreck.ContactTick, WreckSweepFirstOffset, windup, baseWindup),
                To = _wreck.ContactTick + WreckSweepLastOffset, Dir = _wreck.Direction,
            };
            if (_wreckSweep.Stage >= 0) { _wreckSweepNext = sweep; return; }
            BeginWreckSweep(sweep);
        }

        private void BeginWreckSweep(in WreckSweep sweep)
        {
            if (_wreckSweepHits == null || _wreckSweepHits.Length < Entities.Capacity) _wreckSweepHits = new byte[Entities.Capacity];
            else System.Array.Clear(_wreckSweepHits, 0, _wreckSweepHits.Length);
            _wreckSweep = sweep;
            _wreckSweepLast = sweep;
        }

        /// <summary>
        /// Последний мах (идущий или доигранный): этап (0, 1), серия, тик контакта, первый и последний тик хода
        /// головы. Виду: Damage слота Крушения в тик from…to — удар этого маха (WreckStage — только в контакт).
        /// False — за расстановку махов не было.
        /// </summary>
        public bool TryGetWreckSweep(out int stage, out int serial, out int contact, out int from, out int to)
        {
            WreckSweep s = _wreckSweepLast;
            stage = s.Stage; serial = s.Serial; contact = s.Contact; from = s.From; to = s.To;
            return s.Stage >= 0;
        }

        /// <summary>Мах ещё в деле: его этап ударил (голова летит дальше) или этот этап серии ещё в замахе.</summary>
        private bool WreckSweepAlive(in WreckSweep sweep)
            => _wreck.Serial == sweep.Serial
               && (_wreck.Strikes > sweep.Stage || (_wreck.Phase == WreckPhase.Windup && _wreck.Stage == sweep.Stage));

        /// <summary>
        /// Каждый тик после шага серии (в тик контакта — после WreckStage): мах до удара этапа
        /// живёт, пока этап в замахе; после удара — пока жив герой и в слоте Крушение.
        /// </summary>
        private void UpdateWreckSweep()
        {
            if (_wreckSweepNext.Stage >= 0)
            {
                if (!WreckSweepAlive(_wreckSweepNext)) _wreckSweepNext = WreckSweepIdle;
                else if (Tick >= _wreckSweepNext.From)
                {
                    // Прошлый мах не догулял (v4: самый быстрый темп, мах 2 с удар 1 + 1) — добивает разом.
                    if (_wreckSweep.Stage >= 0) StepWreckSweep(true);
                    BeginWreckSweep(_wreckSweepNext);
                    _wreckSweepNext = WreckSweepIdle;
                }
            }
            if (_wreckSweep.Stage < 0) return;
            if (!Entities.Alive[PlayerId] || WreckBuild == null || !WreckSweepAlive(_wreckSweep)) { _wreckSweep = WreckSweepIdle; return; }
            if (Tick >= _wreckSweep.From) StepWreckSweep(false);
        }

        /// <summary>
        /// Шаг маха: враги сектора, через чей угол голова прошла в этот или прошлый тик (идущий
        /// навстречу голове за тик не перескакивает больше одного столбца). flush — все оставшиеся.
        /// </summary>
        private void StepWreckSweep(bool flush)
        {
            AbilityBuild build = WreckBuild;
            WreckSweep sweep = _wreckSweep;
            if (build == null || sweep.Stage < 0) { _wreckSweep = WreckSweepIdle; return; }
            int damage = WreckMomentum(build, sweep.Stage, build.Get(AbilityStatType.Damage).ToInt());
            // «Сотрясение»: второй мах оглушает.
            int stun = sweep.Stage == 1 && build.Has(AbilityFlag.WreckConcuss) ? WreckConcussTicks : 0;
            FixVec2 hero = Entities.Position[PlayerId];
            int count = CollectArc(hero, sweep.Dir, build.Get(AbilityStatType.Radius), build.Get(AbilityStatType.ArcCosine), _arcScratch);
            for (int c = 0; c < count; c++)
            {
                int id = _arcScratch[c];
                if ((uint)id >= (uint)_wreckSweepHits.Length || _wreckSweepHits[id] != 0) continue;
                int at = WreckSweepTick(sweep.Contact, WreckSweepOffset(sweep.Stage, sweep.Dir, Entities.Position[id] - hero),
                    sweep.Windup, sweep.Base);
                if (!flush && (at > Tick || at < Tick - 1)) continue;
                _wreckSweepHits[id] = 1;
                // Девятый вал (06.10 вечером): мах, задевший врага, — заряд выпада (один за мах).
                WreckNinthSwingLanded(sweep.Stage, sweep.Serial);
                ApplyAbilityDamage(PlayerId, id, WreckBigGame(build, id, damage), _wreck.Slot, DamageType.Physical);
                // Герой умер от отражения — расстановка сбросила и мах.
                if (_wreckSweep.Stage < 0 || !Entities.Alive[PlayerId]) { _wreckSweep = WreckSweepIdle; return; }
                if (stun > 0 && Entities.Alive[id]) StunByTalent(id, stun);
            }
            if (flush || Tick >= sweep.To) _wreckSweep = WreckSweepIdle;
        }

        /// <summary>Мах в хеше, пока он заряжен или идёт (вне махов хеш прежний).</summary>
        private void HashWreckSweep(ref ulong hash)
        {
            if (_wreckSweep.Stage < 0 && _wreckSweepNext.Stage < 0) return;
            Hashing.Mix(ref hash, 0x57525357);   // "WRSW"
            _wreckSweep.HashInto(ref hash);
            _wreckSweepNext.HashInto(ref hash);
            if (_wreckSweep.Stage < 0 || _wreckSweepHits == null) return;
            for (int i = 0; i < Entities.Count && i < _wreckSweepHits.Length; i++) Hashing.Mix(ref hash, (int)_wreckSweepHits[i]);
        }

        /// <summary>Расстановка и смерть героя: махи сняты, отметки чисты.</summary>
        private void ResetWreckSweep()
        {
            _wreckSweep = _wreckSweepNext = _wreckSweepLast = WreckSweepIdle;
            if (_wreckSweepHits != null) System.Array.Clear(_wreckSweepHits, 0, _wreckSweepHits.Length);
        }
    }
}
