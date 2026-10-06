namespace Game.Sim
{
    /// <summary>Крушение · Волнорез: стена несёт лёгких и обрушивается (Simulation.Wreck.Forms); сброс и хеш серии.</summary>
    public sealed partial class Simulation
    {
        private readonly int[] _wreckCatchScratch = new int[64];

        /// <summary>Волнорез несёт этого врага на груди стены. Вид слегка приподнимает и наклоняет модель.</summary>
        public bool WreckCarried(int id)
            => _wreckCarried != null && (uint)id < (uint)_wreckCarried.Length && _wreckCarried[id];

        /// <summary>
        /// Шаг стены: все, кого коснулся фронт (и задетые кругом удара — без урона стены),
        /// ближние вдоль полосы первыми, ничьи — младший индекс. Лёгкий в пределе —
        /// несётся к концу стены (та же поперечная), оглушён до обрушения + 15 тиков;
        /// прочие — урон стены и сбивание, лёгкий сверх предела ещё и отброшен, как валом.
        /// </summary>
        private void SweepWreckWall(int step)
        {
            WreckBand(step, out Fix64 inner, out Fix64 outer);
            int found = 0;
            for (int i = PlayerId + 1; i < Entities.Count && found < _wreckCatchScratch.Length; i++)
                if (_wreckWaveHits[i] != 2 && WreckEnemy(i) && WreckInBand(i, inner, outer)) _wreckCatchScratch[found++] = i;
            // Вставками по «вдоль», при равенстве — по индексу: обход детерминирован.
            for (int a = 1; a < found; a++)
            {
                int id = _wreckCatchScratch[a];
                Fix64 along = WreckAlong(id);
                int b = a - 1;
                while (b >= 0 && WreckAlong(_wreckCatchScratch[b]) > along) { _wreckCatchScratch[b + 1] = _wreckCatchScratch[b]; b--; }
                _wreckCatchScratch[b + 1] = id;
            }

            AbilityBuild build = WreckBuildForWave;
            // До обрушения (тик после последнего шага): несомый едет на тик меньше и успевает к нему.
            int serial = _wreck.Serial, left = _wreck.WaveTravelTicks - step + 1;
            FixVec2 perp = new FixVec2(-_wreck.LaneDir.Y, _wreck.LaneDir.X);
            FixVec2 end = _wreck.LaneOrigin + _wreck.LaneDir * _wreck.WallEnd;
            for (int k = 0; k < found; k++)
            {
                int id = _wreckCatchScratch[k];
                if (!WreckEnemy(id)) continue;
                bool circle = _wreckWaveHits[id] == 1;
                _wreckWaveHits[id] = 2;
                bool carry = AbordageLight(id) && _wreck.CarriedCount < WreckBreakwaterCarryLimit;
                _events.Add(new SimEvent(SimEventType.WreckBreakwaterCatch, PlayerId, id, left, carry, Entities.Position[id]));
                if (!circle)
                {
                    int damage = build != null ? WreckBigGame(build, id, _wreck.WaveDamage) : _wreck.WaveDamage;
                    ApplyAbilityDamage(PlayerId, id, damage, _wreck.Slot, DamageType.Physical);
                    if (_wreck.Serial != serial || _wreck.WaveTick < 0) return;
                }
                if (!Entities.Alive[id]) continue;
                if (!carry)
                {
                    StunByTalent(id, WreckWaveStunTicks);
                    WreckShove(id, _wreck.LaneDir, WreckWaveKnockback, WreckWaveKnockbackTicks, ForcedMotionKind.Knockback);
                    continue;
                }
                _wreckCarried[id] = true;
                _wreck.CarriedCount++;
                StunByTalent(id, left + WreckBreakwaterCarryStunTicks);
                Fix64 across = FixVec2.Dot(Entities.Position[id] - _wreck.LaneOrigin, perp);
                if (left > 1) ForcedMotion.Begin(Entities, id, end + perp * across, left - 1, ForcedMotionKind.Carried);
            }
        }

        private Fix64 WreckAlong(int id) => FixVec2.Dot(Entities.Position[id] - _wreck.LaneOrigin, _wreck.LaneDir);

        /// <summary>
        /// Стена дошла и обрушилась: доля урона стены всем несомым и всем в 2 м (+ тело)
        /// у конца, о преграду — полный; лёгких рядом толкает наружу. Босса, которого стена
        /// уже ударила, обрушение не бьёт (70 стеной один раз); корпус, до которого стена
        /// не дошла, — бьёт, как всех у конца.
        /// </summary>
        private void WreckBreakwaterCrash()
        {
            FixVec2 end = _wreck.LaneOrigin + _wreck.LaneDir * _wreck.WallEnd;
            AbilityBuild build = WreckBuildForWave;
            int serial = _wreck.Serial;
            int damage = _wreck.WallStopped ? _wreck.WaveDamage : _wreck.WaveDamage * WreckBreakwaterCrashPercent / 100;
            _events.Add(new SimEvent(SimEventType.WreckBreakwaterCrash, PlayerId, -1, _wreck.CarriedCount, _wreck.WallStopped,
                end, DamageType.Physical, DamageOrigin.Ability, (WreckBreakwaterCrashRadius * 100).ToInt()));
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                bool carried = _wreckCarried[i];
                _wreckCarried[i] = false;
                if (carried && Entities.ForcedKind[i] == (byte)ForcedMotionKind.Carried) ForcedMotion.Clear(Entities, i);
                if (!WreckEnemy(i) || (Entities.Kind[i] == EnemyKind.ForestThicketMaster && _wreckWaveHits[i] == 2)) continue;
                FixVec2 delta = Entities.Position[i] - end;
                Fix64 r = WreckBreakwaterCrashRadius + ThicketBodyFrom(i, end);
                if (!carried && delta.LengthSq > r * r) continue;
                int hit = build != null ? WreckBigGame(build, i, damage) : damage;
                ApplyAbilityDamage(PlayerId, i, hit, _wreck.Slot, DamageType.Physical);
                if (_wreck.Serial != serial) return;
                if (carried || !Entities.Alive[i]) continue;
                Fix64 length = delta.Length;
                FixVec2 away = length.Raw == 0 ? _wreck.LaneDir : delta / length;
                WreckShove(i, away, WreckBreakwaterCrashShove, WreckBreakwaterCrashShoveTicks, ForcedMotionKind.Shoved);
            }
            _wreck.CarriedCount = 0;
        }

        // ---------- вид и HUD ----------

        /// <summary>
        /// Фронт вала (стены) виду: откуда полоса (герой в тик удара), направление, начало
        /// вдоль, докуда фронт дошёл к этому тику и полуширина. False — фронта нет.
        /// </summary>
        public bool TryGetWreckWave(out FixVec2 origin, out FixVec2 direction, out Fix64 from, out Fix64 reachNow, out Fix64 halfWidth)
        {
            origin = _wreck.LaneOrigin;
            direction = _wreck.LaneDir;
            from = _wreck.WaveStart;
            halfWidth = _wreck.LaneHalfWidth;
            reachNow = from;
            if (_wreck.WaveTick < 0) return false;
            int steps = WreckClamp(Tick - _wreck.WaveTick, 0, _wreck.WaveTravelTicks);
            reachNow = from + _wreck.WaveStep * steps;
            if (reachNow > _wreck.WallEnd) reachNow = _wreck.WallEnd;
            return true;
        }

        /// <summary>
        /// Превью удара оземь для HUD (чистая функция, та же геометрия, что у Sim): полоса
        /// от героя по курсору с учётом формы слота и зарядов Девятого вала этой серии; после
        /// нажатия выпада — по направлению Sim. Точка и круг удара; полоса — от точки удара
        /// до первой преграды оси (без неё — до конца). False — в слоте не Крушение.
        /// </summary>
        public bool WreckLanePreview(int slot, FixVec2 aim, out FixVec2 origin, out FixVec2 direction, out Fix64 from,
            out Fix64 to, out Fix64 halfWidth, out FixVec2 impact, out Fix64 impactRadius)
        {
            AbilityBuild build = (uint)slot < (uint)AbilitySlots ? _abilityBuilds[slot] : null;
            origin = Entities.Position[PlayerId];
            direction = FixVec2.Zero; from = to = halfWidth = impactRadius = Fix64.Zero; impact = origin;
            if (build == null || build.DefinitionId != AbilityDefinition.WreckId) return false;
            bool locked = _wreck.Slot == slot && _wreck.Stage == WreckStages - 1
                && (_wreck.Phase == WreckPhase.Windup || _wreck.Phase == WreckPhase.Charge);
            if (locked) direction = _wreck.Direction;   // без повторной нормировки — бит в бит как у Sim
            else
            {
                direction = aim - origin;
                if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
                direction = direction.LengthSq.Raw == 0 ? new FixVec2(Fix64.One, Fix64.Zero) : direction.Normalized();
            }
            // Выпад v4 сам везёт героя на 0,6 м (Simulation.Wreck.Lunge): след считается от места удара.
            Fix64 lunge = WreckLungeAhead(origin, direction, locked);
            int charges = _wreck.Slot == slot && _wreck.Phase != WreckPhase.None ? _wreck.NinthCharges : 0;
            WreckSlamGeometry(build, FormAt(slot), charges, origin + direction * lunge, direction, out _, out from,
                out impactRadius, out _, out halfWidth, out to, out _);
            from += lunge;
            to += lunge;
            impact = origin + direction * from;
            return true;
        }

        // ---------- буферы, сброс, хеш ----------

        /// <summary>Буферы на каждую сущность — при первом нажатии, как пена Шквала (EnsureSquallFoamBuffers).</summary>
        private void EnsureWreckBuffers()
        {
            if (_wreckWaveHits != null && _wreckWaveHits.Length >= Entities.Capacity) return;
            _wreckWaveHits = new byte[Entities.Capacity];
            _wreckCarried = new bool[Entities.Capacity];
        }

        /// <summary>Расстановка и смерть героя: серия снята (событие конца — если шла), вал, стена и Призрачный якорь гаснут.</summary>
        private void ResetWreck()
        {
            if (_wreck.Phase != WreckPhase.None && Entities.Count > PlayerId) EndWreck(WreckEnd.Interrupted);
            _wreck = new WreckState { Slot = -1, OverheadTick = -1, ChargeStartTick = -1, WaveTick = -1, ShellBurstTick = -1, GhostTick = -1 };
            ResetWreckSweep();
            if (_wreckWaveHits == null) return;
            System.Array.Clear(_wreckWaveHits, 0, _wreckWaveHits.Length);
            System.Array.Clear(_wreckCarried, 0, _wreckCarried.Length);
        }

        /// <summary>
        /// На прежнем месте StateHash. Прежние поля (_wreckSlot, _wreckStage, _wreckImpactTick,
        /// _wreckWindowEndTick, направление) — их постоянными значениями вне серии (−1, 0, −1,
        /// −1, 0, 0): закреплённые хеши без Крушения прежние бит в бит. Новое состояние — только
        /// после первой серии расстановки, буферы — пока живы вал и несомые.
        /// </summary>
        private void HashWreck(ref ulong hash)
        {
            Hashing.Mix(ref hash, -1);
            Hashing.Mix(ref hash, 0);
            Hashing.Mix(ref hash, -1);
            Hashing.Mix(ref hash, -1);
            Hashing.Mix(ref hash, 0L);
            Hashing.Mix(ref hash, 0L);
            if (_wreck.Serial == 0) return;
            Hashing.Mix(ref hash, 0x5752434B);   // "WRCK"
            _wreck.HashInto(ref hash);
            if (_wreck.WaveTick >= 0 && _wreckWaveHits != null)
            {
                Hashing.Mix(ref hash, 0x57525756);   // "WRWV"
                for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, (int)_wreckWaveHits[i]);
            }
            if (_wreck.CarriedCount > 0 && _wreckCarried != null)
            {
                Hashing.Mix(ref hash, 0x57524352);   // "WRCR"
                for (int i = 0; i < Entities.Count; i++) Hashing.Mix(ref hash, _wreckCarried[i] ? 1 : 0);
            }
            HashWreckSweep(ref hash);
        }
    }
}
