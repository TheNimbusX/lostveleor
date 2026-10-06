using System.IO;

namespace Game.Sim
{
    /// <summary>Итог покупки или переключения клятвы на доске.</summary>
    public enum OathResult : byte
    {
        Success = 0,
        /// <summary>Номера нет в наборе.</summary>
        InvalidOath = 1,
        /// <summary>Клятва уже на потолке ступеней.</summary>
        MaxRank = 2,
        /// <summary>Пепла меньше цены — кошелёк не тронут.</summary>
        NotEnoughAsh = 3,
        /// <summary>Включить можно только купленную клятву.</summary>
        NotOwned = 4,
        /// <summary>Все слоты заняты.</summary>
        NoFreeSlot = 5,
    }

    /// <summary>
    /// Доска клятв (решение 06.10): купленные за пепел ступени и включённые клятвы.
    ///
    /// Цена — общая лестница: каждая следующая покупка, включая ступени одной клятвы,
    /// на 20 дороже (60, 80, 100…). Так первая клятва — за забег, а ~9 — к ~15-му
    /// забегу, и порядок покупок цены не меняет. Включено не больше OathSlots
    /// (3/4/5/6 на ур. 1/6/12/18), все клятвы равны; новая сама встаёт в свободный слот.
    ///
    /// Лагерь не тикает и в хеш забега не входит: в забег клятвы уходят один раз
    /// снимком CreateRunBoons, дальше забег их не меняет.
    /// </summary>
    public sealed partial class Camp
    {
        public const int OathBasePrice = 60, OathPriceStep = 20;

        // Индекс — номер OathId (0 — None, не используется): так ступень читается без поиска.
        private readonly byte[] _oathRanks = new byte[OathIds.Count + 1];
        // Бит (int)OathId — клятва включена. Включённая всегда куплена.
        private uint _oathActive;

        /// <summary>Купленная ступень клятвы, 0 — не куплена или номер вне набора.</summary>
        public int OathRank(OathId id) => KnownOath(id) ? _oathRanks[(int)id] : 0;

        public bool OathActive(OathId id) => KnownOath(id) && (_oathActive & (1u << (int)id)) != 0;

        public int ActiveOathCount => CountBits(_oathActive);

        /// <summary>Сколько покупок сделано: ступень — это покупка (пробел №22).</summary>
        public int OathPurchases
        {
            get
            {
                int sum = 0;
                for (int id = 1; id <= OathIds.Count; id++) sum += _oathRanks[id];
                return sum;
            }
        }

        /// <summary>Цена следующей покупки любой клятвы: 60 + 20 × уже куплено.</summary>
        public int NextOathPrice => OathBasePrice + OathPriceStep * OathPurchases;

        /// <summary>
        /// Слоты: 3 / 4 / 5 / 6 на ур. 1 / 6 / 12 / 18. Sandbox (тесты, проверочные лагеря
        /// вида) — сразу 6, как и всё остальное в нём.
        /// </summary>
        public int OathSlots => !IsProgressive ? 6 : Level >= 18 ? 6 : Level >= 12 ? 5 : Level >= 6 ? 4 : 3;

        /// <summary>
        /// Купить следующую ступень за пепел. Первая ступень сама включает клятву, если
        /// есть свободный слот: купленная и молча выключенная клятва читалась бы как
        /// пропавший пепел. Ступени выше первой включённость не трогают.
        /// </summary>
        public OathResult BuyOath(OathId id)
        {
            if (!KnownOath(id)) return OathResult.InvalidOath;
            int rank = _oathRanks[(int)id];
            if (rank >= RunBoons.MaxRank(id)) return OathResult.MaxRank;
            if (!Spend(CurrencyType.Ash, NextOathPrice)) return OathResult.NotEnoughAsh;
            _oathRanks[(int)id] = (byte)(rank + 1);
            if (rank == 0 && ActiveOathCount < OathSlots) _oathActive |= 1u << (int)id;
            return OathResult.Success;
        }

        /// <summary>Включить или выключить купленную клятву. Выключение всегда можно и ничего не стоит.</summary>
        public OathResult SetOathActive(OathId id, bool on)
        {
            if (!KnownOath(id)) return OathResult.InvalidOath;
            if (_oathRanks[(int)id] == 0) return OathResult.NotOwned;
            uint bit = 1u << (int)id;
            if (!on) { _oathActive &= ~bit; return OathResult.Success; }
            if ((_oathActive & bit) != 0) return OathResult.Success;
            if (ActiveOathCount >= OathSlots) return OathResult.NoFreeSlot;
            _oathActive |= bit;
            return OathResult.Success;
        }

        /// <summary>
        /// Снимок на вход в забег: ступени включённых клятв и грани сердец надетых вещей.
        ///
        /// Слоты проверяются и здесь: уровень могли опустить через F8 — тогда в забег идут
        /// младшие по номеру клятвы, как и при чтении сохранения. Одна грань на двух вещах
        /// не складывается: маска граней это бит, а не счётчик (пробел №19).
        /// </summary>
        public RunBoons CreateRunBoons()
        {
            RunBoons boons = RunBoons.Empty;
            int slots = OathSlots, taken = 0;
            for (int id = 1; id <= OathIds.Count && taken < slots; id++)
            {
                if ((_oathActive & (1u << id)) == 0) continue;
                boons = boons.WithRank((OathId)id, _oathRanks[id]);
                taken++;
            }
            uint facets = WornHeartFacetMask();
            for (int f = 1; f <= (int)RunBoons.LastFacet; f++)
                if ((facets & (1u << f)) != 0) boons = boons.WithFacet((HeartFacet)f);
            return boons;
        }

        /// <summary>
        /// Снимок для героя лагеря (манекены): только шкура, шаг, глаз и запас (пробел №23).
        /// Его вешает GameSession.RefreshCampOaths.
        /// </summary>
        public RunBoons CreateCampBoons() => CreateRunBoons().CampStatsOnly();

        /// <summary>
        /// «Знаток рун»: +25% осколков при разборе у Эни. Разбор — в лагере, не в забеге,
        /// поэтому процент читает кузнец (пакет T1), а не снимок забега.
        /// </summary>
        public int RuneSageBonusPercent => OathActive(OathId.RuneSage) ? RunBoons.RuneSageBonusPercent : 0;

        /// <summary>
        /// Доска в хеше лагеря. По этому хешу CampSaveStore решает, писать ли сохранение в
        /// лагере: покупку выдаёт и пепел кошелька, а переключение — только этот хеш.
        /// Без купленных клятв не подмешивает ничего, хеш прежний.
        /// </summary>
        internal void HashOaths(ref ulong hash)
        {
            if (OathPurchases == 0) return;
            Hashing.Mix(ref hash, 0x4F415448);
            for (int id = 1; id <= OathIds.Count; id++) Hashing.Mix(ref hash, (int)_oathRanks[id]);
            Hashing.Mix(ref hash, (int)_oathActive);
        }

        // ---- сохранение v10, секция 9 ----
        //
        // byte count; count × { int32 ключ OathIds.Key, byte ступень ≥ 1, bool включена },
        // ключи строго по возрастанию. Клятва лежит под стабильным ключом, а не под номером:
        // снятая патчем клятва при чтении выпадает, остальные остаются на месте.
        // Без купленных клятв секция пустая и не пишется вовсе (CampSaveCodec, skipEmpty).

        private const int MaxSavedOaths = 64;

        // Номера клятв в порядке возрастания ключа: считается раз при загрузке типа.
        private static readonly OathId[] OathsByKey = SortOathsByKey();

        internal void WriteOaths(BinaryWriter w)
        {
            int count = 0;
            for (int id = 1; id <= OathIds.Count; id++) if (_oathRanks[id] > 0) count++;
            if (count == 0) return;
            w.Write((byte)count);
            for (int i = 0; i < OathsByKey.Length; i++)
            {
                OathId id = OathsByKey[i];
                if (_oathRanks[(int)id] == 0) continue;
                w.Write(OathIds.Key(id));
                w.Write(_oathRanks[(int)id]);
                w.Write(OathActive(id));
            }
        }

        /// <summary>
        /// Читает секцию клятв. Порча — длина списка вне предела, ступень 0, ключи не по
        /// возрастанию (в том числе повтор): InvalidDataException. Смена правил профиль не
        /// роняет: неизвестный ключ выпадает, ступень выше потолка подрезается, лишние
        /// включённые выключаются начиная со старшего номера. Уровень к этому моменту уже
        /// прочитан (секция ядра), поэтому слоты считаются по нему.
        /// </summary>
        internal void ReadOaths(BinaryReader r, int length)
        {
            if (length <= 0 || !CampSaveCodec.Has(r, 1)) return;
            int count = r.ReadByte();
            // Тот же жёсткий предел списков, что у кодека (боссы, навыки): запас на будущие клятвы.
            if (count > MaxSavedOaths) throw new InvalidDataException("Слишком длинный список клятв");
            if (!CampSaveCodec.Has(r, count * (4 + 1 + 1))) throw new InvalidDataException("Обрезан список клятв");
            bool first = true;
            int previous = 0;
            for (int i = 0; i < count; i++)
            {
                int key = r.ReadInt32();
                int rank = r.ReadByte();
                bool active = r.ReadBoolean();
                if (rank == 0) throw new InvalidDataException("Клятва без ступени");
                if (!first && key <= previous) throw new InvalidDataException("Клятвы не по порядку ключей");
                first = false;
                previous = key;
                OathId id = OathIds.FromKey(key);
                if (id == OathId.None) continue;
                int max = RunBoons.MaxRank(id);
                _oathRanks[(int)id] = (byte)(rank > max ? max : rank);
                if (active) _oathActive |= 1u << (int)id;
            }
            TrimActiveOaths();
        }

        /// <summary>Лишние включённые клятвы выключаются, начиная со старшего номера.</summary>
        private void TrimActiveOaths()
        {
            int slots = OathSlots;
            for (int id = OathIds.Count; id >= 1 && ActiveOathCount > slots; id--)
                _oathActive &= ~(1u << id);
        }

        private static bool KnownOath(OathId id) => id != OathId.None && (int)id <= OathIds.Count;

        private static int CountBits(uint bits)
        {
            int count = 0;
            for (; bits != 0; bits &= bits - 1) count++;
            return count;
        }

        private static OathId[] SortOathsByKey()
        {
            var order = new OathId[OathIds.Count];
            for (int id = 1; id <= OathIds.Count; id++)
            {
                int at = id - 1;
                while (at > 0 && OathIds.Key(order[at - 1]) > OathIds.Key((OathId)id)) { order[at] = order[at - 1]; at--; }
                order[at] = (OathId)id;
            }
            return order;
        }
    }
}
