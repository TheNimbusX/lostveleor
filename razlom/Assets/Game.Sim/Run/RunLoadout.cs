namespace Game.Sim
{
    /// <summary>
    /// Способности и таланты на время одного забега.
    ///
    /// Решение владельца от 15 сентября: всё, что герой нашёл в Разломе,
    /// живёт до конца забега. Забег начинается с автоатаки и Вихря, три слота
    /// пусты; способности приходят с карточек и с элит.
    ///
    /// Таланты — УСИЛЕНИЯ СПОСОБНОСТИ (владелец, 24 сентября): уровней нет,
    /// выпадают в любом порядке. Поэтому хранится набор взятых — битовая маска
    /// на способность, а не «сколько взято».
    ///
    /// ХРАНИТСЯ ИНДЕКС В ПУЛЕ, А НЕ ОПРЕДЕЛЕНИЕ: так набор хешируется и
    /// сравнивается числами, а ролл карточек видит, чего у игрока ещё нет.
    ///
    /// ФОРМЫ (план 02.10) — свойство ЛИНИИ, а не слота: на каждую линию пула и на
    /// псевдолинию сабли (PelagKit.SabreLine) — выбранная форма и маска её талантов.
    /// «Линия» в параметрах ниже — индекс пула или PelagKit.SabreLine.
    /// </summary>
    public sealed class RunLoadout
    {
        public const int Slots = PelagKit.MainSlots;
        public const int EmptySlot = -1;

        /// <summary>
        /// Сколько усилений у способности по дизайну (владелец, 24 сентября) — столько граней
        /// у ромбика в HUD. Сейчас у линий по SabreTalents.TalentsPerLine (5); по три новых на
        /// способность — на утверждении.
        /// </summary>
        public const int MaxUpgrades = 8;

        /// <summary>
        /// Узлов на одну сборку: 8 талантов линии, узлы формы, таланты формы и отладочные
        /// таланты F8. Буфер был на 8 и молча отбрасывал лишнее (план форм 02.10).
        /// </summary>
        public const int MaxNodesPerSlot = 32;

        private readonly int[] _slots = new int[Slots];
        private readonly int[] _taken = new int[PelagKit.PoolSize];
        private readonly AbilityNode[] _nodes = new AbilityNode[MaxNodesPerSlot];

        // ---- формы: на линию пула и на саблю ----
        private readonly PelagForm[] _form = new PelagForm[PelagKit.PoolSize];
        private readonly int[] _formTaken = new int[PelagKit.PoolSize];
        private PelagForm _sabreForm;
        private int _sabreTaken, _sabreFormTaken;

        /// <summary>Линии пула (биты), чья форма ушла с навыком, — при FormRewardRules.FormLockedAfterRemoval.</summary>
        private int _formLocked;

        /// <summary>Растёт при каждой смене набора или ранга.</summary>
        public int Version { get; private set; }

        /// <summary>Растёт при каждом ApplyTo — представление по нему накладывает отладочные узлы заново.</summary>
        public int Applications { get; private set; }

        public RunLoadout() => ResetToStarter();

        /// <summary>Стартовый набор: Вихрь в первом слоте, остальное пусто, талантов и форм нет.</summary>
        public void ResetToStarter(int starterPoolIndex = PelagKit.StarterPoolIndex)
        {
            for (int i = 0; i < Slots; i++) _slots[i] = EmptySlot;
            System.Array.Clear(_taken, 0, _taken.Length);
            System.Array.Clear(_form, 0, _form.Length);
            System.Array.Clear(_formTaken, 0, _formTaken.Length);
            _sabreForm = PelagForm.None;
            _sabreTaken = _sabreFormTaken = _formLocked = 0;
            _slots[0] = (uint)starterPoolIndex < PelagKit.PoolSize ? starterPoolIndex : PelagKit.StarterPoolIndex;
            Version++;
        }

        public void CopyFrom(RunLoadout other)
        {
            System.Array.Copy(other._slots, _slots, Slots);
            System.Array.Copy(other._taken, _taken, _taken.Length);
            System.Array.Copy(other._form, _form, _form.Length);
            System.Array.Copy(other._formTaken, _formTaken, _formTaken.Length);
            _sabreForm = other._sabreForm;
            _sabreTaken = other._sabreTaken;
            _sabreFormTaken = other._sabreFormTaken;
            _formLocked = other._formLocked;
            Version++;
        }

        // ---- слоты ----

        public int PoolIndexAt(int slot) => (uint)slot < Slots ? _slots[slot] : EmptySlot;
        public AbilityDefinition DefinitionAt(int slot) => PelagKit.PoolDefinition(PoolIndexAt(slot));
        public bool IsEmpty(int slot) => PoolIndexAt(slot) == EmptySlot;

        /// <summary>Первый пустой слот или −1.</summary>
        public int FreeSlot()
        {
            for (int i = 0; i < Slots; i++)
                if (_slots[i] == EmptySlot) return i;
            return -1;
        }

        public bool IsFull => FreeSlot() < 0;

        /// <summary>Сколько слотов занято навыками (сабля не считается).</summary>
        public int SkillCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Slots; i++)
                    if (_slots[i] != EmptySlot) count++;
                return count;
            }
        }

        /// <summary>Слот, где лежит способность пула, или −1. У сабли слота нет — тоже −1.</summary>
        public int SlotOf(int poolIndex)
        {
            if (poolIndex == EmptySlot) return -1;
            for (int i = 0; i < Slots; i++)
                if (_slots[i] == poolIndex) return i;
            return -1;
        }

        /// <summary>Линия во владении: способность в слоте; сабля — всегда.</summary>
        public bool Owns(int poolIndex) => poolIndex == PelagKit.SabreLine || SlotOf(poolIndex) >= 0;

        /// <summary>
        /// Кладёт способность в слот (EmptySlot — освобождает). Прежняя способность
        /// уходит вместе со своими талантами, формой и талантами формы. Способность,
        /// которая уже лежит в другом слоте, положить нельзя: две одинаковые кнопки — не выбор.
        /// </summary>
        public bool Put(int slot, int poolIndex)
        {
            if ((uint)slot >= Slots) return false;
            if (poolIndex != EmptySlot && (uint)poolIndex >= PelagKit.PoolSize) return false;
            if (_slots[slot] == poolIndex) return true;
            if (Owns(poolIndex)) return false;

            int old = _slots[slot];
            if (old != EmptySlot) ForgetLine(old);
            _slots[slot] = poolIndex;
            Version++;
            return true;
        }

        /// <summary>Навык ушёл из набора: его таланты, форма и таланты формы пропадают.</summary>
        private void ForgetLine(int pool)
        {
            _taken[pool] = 0;
            if (FormRewardRules.FormLockedAfterRemoval && _form[pool] != PelagForm.None) _formLocked |= 1 << pool;
            _form[pool] = PelagForm.None;
            _formTaken[pool] = 0;
        }

        /// <summary>Кладёт новую способность в первый пустой слот. False — панель полна или способность уже есть.</summary>
        public bool Add(int poolIndex)
        {
            int free = FreeSlot();
            return free >= 0 && Put(free, poolIndex);
        }

        // ---- таланты (усиления) ----

        /// <summary>Сколько собственных усилений у линии: 8 у линий SabreTalents, у сабли и пула 8–9 — пока 0.</summary>
        public static int LineTalentCount(int line)
            => line == PelagKit.SabreLine ? PelagKit.SabreTalentCount
                : SabreTalents.TryLineOf(line, out _) ? SabreTalents.TalentsPerLine : 0;

        /// <summary>Сколько усилений способности взято (0…TalentsPerLine). Порядок не важен.</summary>
        public int TalentRank(int poolIndex) => TalentCount(poolIndex);

        public int TalentCount(int poolIndex) => BitCount(TalentMask(poolIndex));

        /// <summary>Набор взятых усилений: бит N — усиление N. Линия — индекс пула или сабля.</summary>
        public int TalentMask(int poolIndex)
            => (uint)poolIndex < PelagKit.PoolSize ? _taken[poolIndex]
                : poolIndex == PelagKit.SabreLine ? _sabreTaken : 0;

        /// <summary>Усиление взято. Номер от PelagForms.FormTalentBase — талант формы.</summary>
        public bool HasTalent(int poolIndex, int index)
        {
            if (index >= PelagForms.FormTalentBase) return HasFormTalent(poolIndex, index - PelagForms.FormTalentBase);
            return (uint)index < SabreTalents.TalentsPerLine && (TalentMask(poolIndex) & (1 << index)) != 0;
        }

        /// <summary>
        /// Сколько ещё можно взять у линии: свои усиления плюс, если форма выбрана,
        /// таланты формы. У линии без формы — ровно как до форм.
        /// </summary>
        public int TalentsLeft(int poolIndex)
            => LineTalentCount(poolIndex) - TalentCount(poolIndex) + FormTalentsLeft(poolIndex);

        /// <summary>Способность в руках, у неё есть усиления (или таланты формы), и взяты не все.</summary>
        public bool CanTakeTalent(int poolIndex) => Owns(poolIndex) && TalentsLeft(poolIndex) > 0;

        /// <summary>
        /// Это конкретное усиление можно взять: способность в руках, усиление ещё не взято.
        /// Номер от PelagForms.FormTalentBase — талант формы (только после формы).
        /// </summary>
        public bool CanTakeTalent(int poolIndex, int index)
        {
            if (index >= PelagForms.FormTalentBase) return CanTakeFormTalent(poolIndex, index - PelagForms.FormTalentBase);
            return Owns(poolIndex)
                   && (uint)index < (uint)LineTalentCount(poolIndex)
                   && !HasTalent(poolIndex, index);
        }

        /// <summary>Берёт конкретное усиление способности — в любом порядке. Номер от FormTalentBase — талант формы.</summary>
        public bool TakeTalent(int poolIndex, int index)
        {
            if (index >= PelagForms.FormTalentBase) return TakeFormTalent(poolIndex, index - PelagForms.FormTalentBase);
            if (!CanTakeTalent(poolIndex, index)) return false;
            if (poolIndex == PelagKit.SabreLine) _sabreTaken |= 1 << index;
            else _taken[poolIndex] |= 1 << index;
            Version++;
            return true;
        }

        /// <summary>Берёт первое ещё не взятое усиление. Для меню разработчика и тестов.</summary>
        public bool TakeTalent(int poolIndex)
        {
            for (int index = 0; index < SabreTalents.TalentsPerLine; index++)
                if (TakeTalent(poolIndex, index)) return true;
            return false;
        }

        /// <summary>
        /// Номер N-го (с нуля) ещё не взятого усиления линии или −1. Сначала свои
        /// усиления, потом таланты формы (номера от PelagForms.FormTalentBase).
        /// </summary>
        public int UntakenTalentAt(int poolIndex, int n)
        {
            int own = LineTalentCount(poolIndex);
            for (int index = 0; index < own; index++)
                if (!HasTalent(poolIndex, index) && n-- == 0) return index;
            PelagForm form = FormOf(poolIndex);
            int formTalents = PelagForms.FormTalentCount(form);
            for (int k = 0; k < formTalents; k++)
                if (!HasFormTalent(poolIndex, k) && n-- == 0) return PelagForms.FormTalentBase + k;
            return -1;
        }

        /// <summary>Есть ли у игрока хоть одно доступное усиление — от этого зависят веса карточек.</summary>
        public bool HasTalentToTake
        {
            get
            {
                for (int i = 0; i < Slots; i++)
                    if (_slots[i] != EmptySlot && CanTakeTalent(_slots[i])) return true;
                return CanTakeTalent(PelagKit.SabreLine);
            }
        }

        // ---- формы (план 02.10) ----

        /// <summary>Форма линии или None.</summary>
        public PelagForm FormOf(int line)
            => (uint)line < PelagKit.PoolSize ? _form[line]
                : line == PelagKit.SabreLine ? _sabreForm : PelagForm.None;

        /// <summary>Сколько линий (с саблей) уже с формой.</summary>
        public int FormsChosen
        {
            get
            {
                int count = _sabreForm != PelagForm.None ? 1 : 0;
                for (int i = 0; i < _form.Length; i++)
                    if (_form[i] != PelagForm.None) count++;
                return count;
            }
        }

        /// <summary>Форма линии ушла вместе с навыком, и правило запрещает выбрать новую.</summary>
        public bool IsFormLocked(int line) => (uint)line < PelagKit.PoolSize && (_formLocked & (1 << line)) != 0;

        /// <summary>
        /// Форму можно выбрать: линия во владении (сабля — всегда), формы ещё нет,
        /// форма этой линии и готова. unreadyAllowed — меню разработчика и тесты:
        /// зарезервированная, ещё не написанная форма тоже подходит.
        /// </summary>
        public bool CanChooseForm(int line, PelagForm form, bool unreadyAllowed = false)
            => Owns(line) && FormOf(line) == PelagForm.None && !IsFormLocked(line)
               && PelagForms.LineOf(form) == line && (unreadyAllowed || PelagForms.IsReady(form));

        /// <summary>Выбор формы с награды. Одна форма на линию за забег, сменить нельзя.</summary>
        public bool ChooseForm(int line, PelagForm form, bool unreadyAllowed = false)
        {
            if (!CanChooseForm(line, form, unreadyAllowed)) return false;
            SetForm(line, form);
            Version++;
            return true;
        }

        /// <summary>
        /// Меню разработчика: ставит или снимает (None) форму линии в обход запрета
        /// смены и готовности. Таланты формы при смене пропадают. Забег — тестовый.
        /// </summary>
        public bool DebugSetForm(int line, PelagForm form)
        {
            if (!Owns(line) || form != PelagForm.None && PelagForms.LineOf(form) != line) return false;
            if ((uint)line < PelagKit.PoolSize) _formLocked &= ~(1 << line);
            if (FormOf(line) != form) SetForm(line, form);
            Version++;
            return true;
        }

        private void SetForm(int line, PelagForm form)
        {
            if (line == PelagKit.SabreLine)
            {
                _sabreForm = form;
                _sabreFormTaken = 0;
                return;
            }
            _form[line] = form;
            _formTaken[line] = 0;
        }

        /// <summary>Набор взятых талантов формы линии: бит k — талант формы k.</summary>
        public int FormTalentMask(int line)
            => (uint)line < PelagKit.PoolSize ? _formTaken[line]
                : line == PelagKit.SabreLine ? _sabreFormTaken : 0;

        public int FormTalentCount(int line) => BitCount(FormTalentMask(line));

        public bool HasFormTalent(int line, int index)
            => (uint)index < PelagForms.MaxFormTalents && (FormTalentMask(line) & (1 << index)) != 0;

        /// <summary>Сколько талантов формы ещё не взято; без формы — ноль.</summary>
        public int FormTalentsLeft(int line)
        {
            PelagForm form = FormOf(line);
            return form == PelagForm.None ? 0 : PelagForms.FormTalentCount(form) - FormTalentCount(line);
        }

        /// <summary>Талант формы можно взять: линия во владении, форма выбрана, талант не взят.</summary>
        public bool CanTakeFormTalent(int line, int index)
        {
            PelagForm form = FormOf(line);
            return Owns(line) && form != PelagForm.None
                   && (uint)index < (uint)PelagForms.FormTalentCount(form) && !HasFormTalent(line, index);
        }

        public bool TakeFormTalent(int line, int index)
        {
            if (!CanTakeFormTalent(line, index)) return false;
            if (line == PelagKit.SabreLine) _sabreFormTaken |= 1 << index;
            else _formTaken[line] |= 1 << index;
            Version++;
            return true;
        }

        // ---- узлы и симуляция ----

        /// <summary>Дописывает узлы взятых усилений способности в слоте.</summary>
        public int AppendTalentNodes(int slot, AbilityNode[] buffer, int count)
        {
            int pool = PoolIndexAt(slot);
            if (!SabreTalents.TryLineOf(pool, out SabreTalentLine line)) return count;
            for (int index = 0; index < SabreTalents.TalentsPerLine; index++)
                if (HasTalent(pool, index)) count = SabreTalents.AppendNode(line, index, buffer, count);
            return count;
        }

        /// <summary>
        /// ВСЕ узлы слота: усиления линии, узлы формы и взятые таланты формы. Одна
        /// функция и для RiftRun (ApplyTo), и для вида (TickDriver) — иначе «в тестах
        /// форма есть, в игре нет». Буфер — MaxNodesPerSlot.
        /// </summary>
        public int AppendSlotNodes(int slot, AbilityNode[] buffer, int count)
        {
            count = AppendTalentNodes(slot, buffer, count);
            int pool = PoolIndexAt(slot);
            return pool == EmptySlot ? count : AppendFormNodes(pool, buffer, count);
        }

        /// <summary>
        /// Узлы сабли: её усиления (пока нет) и форма с талантами формы. Ноль узлов —
        /// симуляция серию не трогает (Simulation.SetBasicAttack).
        /// </summary>
        public int AppendSabreNodes(AbilityNode[] buffer, int count) => AppendFormNodes(PelagKit.SabreLine, buffer, count);

        private int AppendFormNodes(int line, AbilityNode[] buffer, int count)
        {
            PelagForm form = FormOf(line);
            if (form == PelagForm.None) return count;
            count = PelagForms.AppendFormNodes(form, buffer, count);
            int talents = PelagForms.FormTalentCount(form);
            for (int k = 0; k < talents; k++)
                if (HasFormTalent(line, k)) count = PelagForms.AppendFormTalentNode(form, k, buffer, count);
            return count;
        }

        /// <summary>Ставит набор в симуляцию: четыре слота, общий рывок и ветку сабли.</summary>
        public void ApplyTo(Simulation sim)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                int count = AppendSlotNodes(slot, _nodes, 0);
                sim.SetAbility(slot, DefinitionAt(slot), _nodes, count);
            }
            sim.SetAbility(PelagKit.DashSlot, AbilityDefinition.Dash(), _nodes, 0);
            sim.SetBasicAttack(_nodes, AppendSabreNodes(_nodes, 0));
            Applications++;
        }

        /// <summary>Сколько первых индексов пула хешируются всегда (пул до Броска якоря); дальше — только ненулевые.</summary>
        private const int HashedPoolPrefix = 10;

        public void HashInto(ref ulong hash)
        {
            for (int i = 0; i < Slots; i++) Hashing.Mix(ref hash, _slots[i]);
            // Индексы пула с 10 (Бросок якоря, 03.10) — только ненулевые и с номером: рост пула
            // не сдвигает хеш ни одного прежнего набора (FormPinTests прибит).
            for (int i = 0; i < _taken.Length; i++)
            {
                if (i < HashedPoolPrefix) Hashing.Mix(ref hash, _taken[i]);
                else if (_taken[i] != 0) { Hashing.Mix(ref hash, i); Hashing.Mix(ref hash, _taken[i]); }
            }

            // Формы и сабля — только когда есть хоть что-то: набор без форм хешируется как до них.
            if (!HasFormState()) return;
            Hashing.Mix(ref hash, 0x464F524D);   // "FORM"
            for (int i = 0; i < _form.Length; i++)
            {
                if (i >= HashedPoolPrefix && _form[i] == PelagForm.None && _formTaken[i] == 0) continue;
                if (i >= HashedPoolPrefix) Hashing.Mix(ref hash, i);
                Hashing.Mix(ref hash, (int)_form[i]);
                Hashing.Mix(ref hash, _formTaken[i]);
            }
            Hashing.Mix(ref hash, (int)_sabreForm);
            Hashing.Mix(ref hash, _sabreTaken);
            Hashing.Mix(ref hash, _sabreFormTaken);
            Hashing.Mix(ref hash, _formLocked);
        }

        private bool HasFormState()
        {
            if (_sabreForm != PelagForm.None || _sabreTaken != 0 || _sabreFormTaken != 0 || _formLocked != 0) return true;
            for (int i = 0; i < _form.Length; i++)
                if (_form[i] != PelagForm.None || _formTaken[i] != 0) return true;
            return false;
        }

        private static int BitCount(int mask)
        {
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }
    }
}
