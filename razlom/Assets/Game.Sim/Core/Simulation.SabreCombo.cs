namespace Game.Sim
{
    /// <summary>
    /// ОБЫЧНАЯ АТАКА ПЕЛАГА — СЕРИЯ САБЛИ ИЗ ТРЁХ УДАРОВ (владелец 01.10).
    ///
    /// «Более рогаликовская»: удар бьёт всех в секторе перед героем, а не
    /// одну выбранную цель; два быстрых зеркальных среза и тяжёлый третий с
    /// выпадом. Таблица по тикам, правила ввода и отмен и откуда взяты числа —
    /// razlom/Docs/PelagBasicCombo.md. Здесь только то, что нужно, чтобы
    /// читать код.
    ///
    /// Удар живёт от старта до EndTick. Направление и сроки фиксируются на
    /// старте (SabreSwingState), сектор проверяется один раз, в ContactTick.
    /// Следующий удар начинается не раньше EndTick: при зажатой атаке — ровно
    /// в него, нажатие во время удара ждёт его конца. Серия продолжается, если
    /// новый удар начался не позже SabreChainResetTicks после конца прошлого
    /// удара или действия, которое его прервало; иначе — снова первый.
    ///
    /// Урон идёт обычным путём автоатаки (ApplyAttack): крит, «Верный удар»,
    /// «Раскол брони», масло, артефакты, дары лагеря и огонь «Ладно смазал»
    /// работают как работали. Убийство серией — basicAttackKill, как раньше.
    ///
    /// Режим кандидата 30.09 (Simulation.BasicAttack, EnablePelagBasicCombo)
    /// отвергнут владельцем; пока он включён в стенде, серия молчит — две
    /// обычные атаки одновременно не живут.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// База стата скорости атаки героя — удары серии в секунду: три удара
        /// за 30 тиков. «+20% скорости атаки» ускоряет серию на 20%.
        /// </summary>
        public const int SabreHitsPerSecond = 3;

        // Фазы ударов при базовых трёх ударах в секунду, тики: от старта до
        // контакта и до конца удара. Удар 1 и 2 — 4/8, добивающий — 7/14:
        // вся серия ровно 30 тиков. Числа из таблицы PelagBasicCombo.md.
        private static readonly int[] SabreWindupBase = { 4, 4, 7 };
        private static readonly int[] SabreCycleBase = { 8, 8, 14 };

        /// <summary>Тик контакта при базовой скорости, для представления и тестов.</summary>
        public static int SabreBaseContactTicks(int hit) => SabreWindupBase[hit];

        /// <summary>Длина удара при базовой скорости, тики.</summary>
        public static int SabreBaseCycleTicks(int hit) => SabreCycleBase[hit];

        /// <summary>Подготовка не короче двух тиков, восстановление — тоже: иначе удар не читается.</summary>
        private const int SabreMinPhaseTicks = 2;

        /// <summary>Выпад добивающего идёт последними тиками замаха (база — 4 тика: t3…t6).</summary>
        public const int SabreLungeTicksBase = 4;

        /// <summary>Длина выпада добивающего. Скоростью атаки не меняется — меняется только темп.</summary>
        public static readonly Fix64 SabreLungeDistance = Fix64.Ratio(6, 10);

        /// <summary>
        /// Радиус сектора от центра героя. Задето всё, чьё тело касается
        /// сектора: Хранитель (тело 0,85) — центром до 3,35 м. Встают мобы на
        /// 1,3–2,2 м, сектор накрывает их с запасом и задевает второй ряд.
        /// </summary>
        public static readonly Fix64 SabreReach = Fix64.Ratio(5, 2);

        /// <summary>Косинус половины сектора: 55° в каждую сторону, сектор 110°.</summary>
        public static readonly Fix64 SabreArcCos = Fix64.Ratio(5736, 10000);

        /// <summary>Нажатие вне удара ждёт столько тиков (0,2 с); во время удара — до его конца.</summary>
        public const int SabrePressBufferTicks = 6;

        /// <summary>Серия живёт столько тиков после конца удара или прервавшего действия (0,33 с).</summary>
        public const int SabreChainResetTicks = 10;

        // Урон от стата урона: на эталонном герое (54) 45 / 45 / 90 — Хранитель
        // первой арены (270) умирает от пятого удара, «5–6 обычных» владельца.
        private static readonly Fix64 SabreLightScale = Fix64.Ratio(5, 6);
        private static readonly Fix64 SabreFinisherScale = Fix64.Ratio(5, 3);

        /// <summary>Толчок целей добивающего: от героя, за SabreShoveTicks тиков.</summary>
        public static readonly Fix64 SabreShoveDistance = Fix64.Ratio(45, 100);
        public const int SabreShoveTicks = 5;

        /// <summary>Добивающий, который кого-то ранил, возвращает лавидий (DESIGN, 30.09).</summary>
        public const int SabreFinisherRefund = 10;

        private SabreSwingState _sabre;
        private int _sabreNextHit;
        private int _sabreChainUntil = -1;
        private int _sabrePressUntil = -1;
        private FixVec2 _sabrePressAim;
        private readonly int[] _sabreTargets;

        /// <summary>Текущий или последний удар серии. Для представления и тестов.</summary>
        public SabreSwingState SabreSwing => _sabre;

        /// <summary>Каким будет следующий удар, если начать его сейчас: 0, 1 или 2.</summary>
        public int SabreNextHit => Tick <= _sabreChainUntil ? _sabreNextHit : 0;

        private bool SabreOn => !_pelagBasicComboEnabled;

        /// <summary>Замах до контакта: скорость хода 75%.</summary>
        private bool SabreWindup => SabreOn && _sabre.ActiveAt(Tick) && !_sabre.ContactDone;

        /// <summary>Пока идёт удар, корпус смотрит туда, куда он бьёт.</summary>
        private bool SabreDirectionLocked => SabreOn && _sabre.ActiveAt(Tick);

        /// <summary>
        /// Тик выпада добивающего: тело ведёт выпад, а не ходьба. В корнях
        /// выпада нет (решение владельца 29.09: связанный бьёт на месте).
        /// </summary>
        private bool SabreLungeNow => SabreOn && _sabre.ActiveAt(Tick) && !_sabre.ContactDone
            && _sabre.LungeStartTick >= 0 && Tick >= _sabre.LungeStartTick && Tick < _sabre.ContactTick
            && !HeroRooted;

        // Удержание ЛКМ — не намерение оборвать Вихрь или Крушение: удар
        // начинается после них, а не вместо их хвоста.
        // Шквал держит саблю, пока держит героя (Simulation.Squall): до хвоста выхода.
        private bool SabreBlockedByAbility => WhirlwindChanneling || _wreckSlot >= 0 || SquallHoldsHero
            || _playerAction.ActiveAt(Tick) && _playerAction.DefinitionId == AbilityDefinition.WhirlwindId;

        /// <summary>Каждый тик до разбора ввода: оглушение и смерть сбрасывают серию, старое нажатие гаснет.</summary>
        private void UpdateSabreChain()
        {
            if (!SabreOn) return;
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick))
            {
                ResetSabre(preserveSerial: true);
                return;
            }
            if (Tick > _sabrePressUntil) ClearSabrePress();
        }

        /// <summary>
        /// Старт удара — до движения: замедление замаха и направление корпуса
        /// действуют уже в первый тик удара, а сектор проверяется в
        /// ResolveAttacks тика контакта по позициям после хода.
        /// </summary>
        private void PrimeSabreSwing(in InputFrame input)
        {
            if (!SabreOn || !Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick)) return;
            if (input.Has(InputFlags.AttackPressed))
            {
                _sabrePressAim = input.Aim;
                int until = Tick + SabrePressBufferTicks;
                if (_sabre.ActiveAt(Tick) && _sabre.EndTick > until) until = _sabre.EndTick;
                _sabrePressUntil = until;
            }
            bool pressed = _sabrePressUntil >= Tick;
            if (!pressed && !input.Has(InputFlags.Attack)) return;

            // Явно нажатая способность сильнее серии в этот же тик; Лик Пустоты — тоже.
            if (input.AbilityMask != 0) return;
            if (input.Has(InputFlags.UseArtifact) && Artifact == RunArtifact.VoidVisage
                && Tick >= _artifactReadyTick) return;
            if (_sabre.ActiveAt(Tick) || !_playerAction.CanChainAt(Tick)
                || Entities.ForcedTicksLeft[PlayerId] > 0 || SabreBlockedByAbility || VoidPhased) return;

            FixVec2 aim = pressed ? _sabrePressAim : input.Aim;
            ClearSabrePress();
            FixVec2 position = Entities.Position[PlayerId];
            FixVec2 direction = aim - position;
            // Курсор под самим героем — бьёт туда, куда смотрит.
            if (direction.LengthSq.Raw == 0) direction = Entities.Facing[PlayerId];
            if (direction.LengthSq.Raw == 0) direction = new FixVec2(Fix64.One, Fix64.Zero);
            direction = direction.Normalized();

            int hit = SabreNextHit;
            int windup = SabrePhaseTicks(SabreWindupBase[hit]);
            int recovery = SabrePhaseTicks(SabreCycleBase[hit] - SabreWindupBase[hit]);
            int lunge = -1;
            if (hit == 2)
            {
                int lungeTicks = System.Math.Min(SabreLungeTicksBase, windup - 1);
                lunge = Tick + windup - System.Math.Max(1, lungeTicks);
            }

            int serial = _sabre.Serial + 1;
            // Хвост прошлого действия (после его контакта) снимается новым ударом.
            CancelPlayerAction();
            _sabre = new SabreSwingState
            {
                Serial = serial, Hit = hit, StartTick = Tick, ContactTick = Tick + windup,
                EndTick = Tick + windup + recovery, LungeStartTick = lunge, Direction = direction,
            };
            // Номер в серии двигает только контакт: снятый до него удар повторится.
            _sabreNextHit = hit;
            _sabreChainUntil = _sabre.EndTick + SabreChainResetTicks;
            SetActionClock(-1, 0, _sabre.ContactTick, _sabre.EndTick);
            Entities.PendingAttackTarget[PlayerId] = -1;
            Entities.AttackImpactTick[PlayerId] = _sabre.ContactTick;
            Entities.PendingAttackVariant[PlayerId] = hit;
            Entities.NextAttackTick[PlayerId] = _sabre.EndTick;
            // Корпус встаёт по удару сразу, как в Hades: доворот по 20° за тик
            // не успевал бы за замахом в 4 тика.
            Entities.Facing[PlayerId] = direction;
            PreparedGiftOrdinaryAttackStarted();
            _events.Add(SimEvent.Attack(PlayerId, -1, position, hit));
        }

        /// <summary>Фаза удара при нынешней скорости атаки: база × 3 / скорость, не короче двух тиков.</summary>
        private int SabrePhaseTicks(int baseTicks)
        {
            Fix64 speed = Fix64.Max(Fix64.Ratio(1, 100), Entities.Stats[PlayerId].Get(StatType.AttackSpeed));
            int ticks = CombatStats.RoundToInt(Fix64.FromInt(baseTicks * SabreHitsPerSecond) / speed);
            return System.Math.Max(SabreMinPhaseTicks, System.Math.Min(CombatStats.MaxAttackCooldown, ticks));
        }

        /// <summary>
        /// Контакт: сектор проверяется по позициям после хода и расталкивания,
        /// каждая цель — один раз. Событие SabreContact идёт и при промахе:
        /// вид рисует дугу удара в любом случае, пену на целях — по Damage.
        /// </summary>
        private void ResolveSabreContact()
        {
            if (!SabreOn || !_sabre.ActiveAt(Tick) || _sabre.ContactDone || Tick < _sabre.ContactTick) return;
            if (!Entities.Alive[PlayerId] || Statuses.IsStunned(PlayerId, Tick)) return;
            if (VoidPhased) { InterruptSabreSwing(); return; }

            _sabre.ContactDone = true;
            _sabreNextHit = (_sabre.Hit + 1) % 3;
            _sabreChainUntil = _sabre.EndTick + SabreChainResetTicks;
            Entities.AttackImpactTick[PlayerId] = 0;

            FixVec2 origin = Entities.Position[PlayerId];
            var sector = EnemyTelegraph.Sector(origin, _sabre.Direction, SabreReachNow, SabreArcCosNow);
            // Сначала список, потом урон: убийство внутри ApplyAttack может
            // родить детёнышей Расщепеня и сдвинуть счёт сущностей.
            int count = 0;
            for (int target = 1; target < Entities.Count; target++)
            {
                if (!Entities.Alive[target] || Entities.Side[target] == Entities.Side[PlayerId]) continue;
                if (!ThicketHitTouches(in sector, target)) continue;
                _sabreTargets[count++] = target;
            }
            _sabre.Hits = count;
            _events.Add(new SimEvent(SimEventType.SabreContact, PlayerId, -1, count, _sabre.IsFinisher,
                origin, DamageType.Physical, DamageOrigin.BasicAttack, _sabre.Hit));

            Fix64 scale = _sabre.IsFinisher ? SabreFinisherScale : SabreLightScale;
            bool landed = false;
            for (int k = 0; k < count; k++)
            {
                int target = _sabreTargets[k];
                if (!Entities.Alive[target]) continue;
                if (ApplyAttack(PlayerId, target, _sabre.Hit, scale) > 0) landed = true;
                if (_sabre.IsFinisher) ShoveBySabre(target);
            }
            if (_sabre.IsFinisher && landed) RefundLavidium(SabreFinisherRefund);
        }

        /// <summary>
        /// Шаг выпада добивающего. Ровная доля длины за тик; упирается в тело
        /// врага (тела касаются, но не входят друг в друга) и в стену — не
        /// скользит вдоль неё, как и выпад «На вылет».
        /// </summary>
        private void StepSabreLunge()
        {
            FixVec2 direction = _sabre.Direction;
            int lungeTicks = System.Math.Max(1, _sabre.ContactTick - _sabre.LungeStartTick);
            Fix64 allowed = SabreLungeDistance / lungeTicks;
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

            FixVec2 step = direction * allowed;
            if (_layout == null && _campWalkMap == null)
            {
                Entities.Position[PlayerId] = from + step;
                return;
            }
            int pieces = System.Math.Max(1, (allowed / (LayoutMap.CellSize / Fix64.FromInt(8))).ToInt() + 1);
            FixVec2 piece = step / Fix64.FromInt(pieces);
            for (int p = 0; p < pieces; p++)
            {
                if (!CanTravel(from, from + piece, body)) break;
                from += piece;
            }
            Entities.Position[PlayerId] = from;
        }

        /// <summary>
        /// Толчок добивающего. Только лёгкие рядовые, которых ничто другое не
        /// держит: Хранитель, Корнеполз, детёныш Расщепеня, Плюй-плод. Вендиго
        /// и Шипомёт не толкаются (владелец 29.09), Камнекопыт тяжёл, элиты и
        /// босс стоят. Корнехват и Расщепень сбрасывают свои действия от любого
        /// принудительного движения — толчок превратил бы серию в оглушение.
        /// Сам толчок замах не сбивает (ForcedMotion.IsInterrupting).
        /// </summary>
        private void ShoveBySabre(int target)
        {
            if (!Entities.Alive[target] || Entities.PushWeight[target].Raw <= 0 || IsElite(target)) return;
            if (ForcedMotion.IsActive(Entities, target)) return;
            EnemyKind kind = Entities.Kind[target];
            if (kind != EnemyKind.None && kind != EnemyKind.ForestGuardian && kind != EnemyKind.ForestRootSwarm
                && kind != EnemyKind.ForestSplitling && kind != EnemyKind.ForestBud) return;
            FixVec2 away = Entities.Position[target] - Entities.Position[PlayerId];
            away = away.LengthSq.Raw == 0 ? _sabre.Direction : away.Normalized();
            ForcedMotion.Begin(Entities, target, Entities.Position[target] + away * SabreShoveDistance,
                SabreShoveTicks, ForcedMotionKind.Shoved);
        }

        /// <summary>
        /// Удар снят: уход, способность после контакта, оглушение. Номер в
        /// серии остаётся тем, что поставил контакт (или старт, если контакта
        /// не было), окно продолжения считается от этого тика — способность
        /// продлит его до своего конца (ExtendSabreChain).
        /// </summary>
        private void InterruptSabreSwing()
        {
            if (!SabreOn) return;
            ClearSabrePress();
            if (!_sabre.ActiveAt(Tick)) return;
            _sabre.Interrupted = true;
            if (_sabreChainUntil < Tick + SabreChainResetTicks) _sabreChainUntil = Tick + SabreChainResetTicks;
            Entities.AttackImpactTick[PlayerId] = 0;
            Entities.PendingAttackVariant[PlayerId] = 0;
            Entities.NextAttackTick[PlayerId] = Tick;
        }

        /// <summary>Действие, начатое посреди серии, держит её до своего конца плюс окно продолжения.</summary>
        private void ExtendSabreChain(int actionEnd)
        {
            if (!SabreOn || _sabreNextHit == 0) return;
            int until = actionEnd + SabreChainResetTicks;
            if (until > _sabreChainUntil) _sabreChainUntil = until;
        }

        private void ClearSabrePress()
        {
            _sabrePressUntil = -1;
            _sabrePressAim = FixVec2.Zero;
        }

        /// <summary>Сброс серии: смерть, оглушение, новая расстановка. Номер удара в сцене не переиспользуется.</summary>
        private void ResetSabre(bool preserveSerial = false)
        {
            int serial = preserveSerial ? _sabre.Serial : 0;
            _sabre = new SabreSwingState { Serial = serial, Interrupted = serial > 0, LungeStartTick = -1 };
            _sabreNextHit = 0;
            _sabreChainUntil = -1;
            ClearSabrePress();
        }

        private void HashSabreCombo(ref ulong hash)
        {
            HashSabreBuild(ref hash);   // ветка сабли (Simulation.Forms): только активный билд
            if (!SabreOn) return;
            Hashing.Mix(ref hash, 0x53414252);   // "SABR"
            _sabre.HashInto(ref hash);
            Hashing.Mix(ref hash, _sabreNextHit);
            Hashing.Mix(ref hash, _sabreChainUntil);
            Hashing.Mix(ref hash, _sabrePressUntil);
            Hashing.Mix(ref hash, _sabrePressAim.X);
            Hashing.Mix(ref hash, _sabrePressAim.Y);
        }
    }
}
