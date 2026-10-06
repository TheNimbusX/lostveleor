namespace Game.Sim
{
    /// <summary>
    /// Клятвы доски Пелага (решение владельца 06.10, 17 клятв).
    ///
    /// Номера входят в хеш забега, реплеи и упакованные ступени RunBoons:
    /// ТОЛЬКО ДОПИСЫВАТЬ, снятые номера не переиспользуются. В сохранении лагеря
    /// клятва лежит под стабильным ключом OathIds.Key, а не под номером.
    /// </summary>
    public enum OathId : byte
    {
        None = 0,

        // Герой, по 3 ступени.
        /// <summary>Крепкая шкура: +30 здоровья за ступень.</summary>
        ToughHide = 1,
        /// <summary>Тяжёлая рука: +8% урона серии за ступень.</summary>
        HeavyHand = 2,
        /// <summary>Лёгкий шаг: +5% бега за ступень.</summary>
        LightStep = 3,
        /// <summary>Острый глаз: +4% крита за ступень.</summary>
        KeenEye = 4,
        /// <summary>Глубокий запас: +20 лавидия за ступень.</summary>
        DeepReserve = 5,
        /// <summary>Быстрый кувырок: −10% перезарядки за ступень.</summary>
        QuickRoll = 6,

        // Выживание.
        /// <summary>Последний вдох: раз за забег встать с 30% здоровья.</summary>
        LastBreath = 7,
        /// <summary>Стойкость: −10% урона от элит и боссов.</summary>
        Steadfast = 8,
        /// <summary>Щедрый родник: +15% лечения Родника и привала.</summary>
        GenerousSpring = 9,
        /// <summary>Кровь врага: убийство элиты лечит 5% здоровья.</summary>
        EnemyBlood = 10,

        // Удача забега.
        /// <summary>Второй взгляд: один бесплатный переброс награды.</summary>
        SecondLook = 11,
        /// <summary>Чутьё: первый выбор навыка из 4.</summary>
        Instinct = 12,
        /// <summary>Благосклонность: +5% к шансу эпического таланта.</summary>
        Favor = 13,

        // Добыча.
        /// <summary>Цепкие руки: при смерти доезжает 75% золота.</summary>
        TenaciousHands = 14,
        /// <summary>Звонкая монета: +15% золота.</summary>
        RingingCoin = 15,
        /// <summary>Пепельный след: +20% пепла.</summary>
        AshTrail = 16,
        /// <summary>Знаток рун: +25% осколков при разборе.</summary>
        RuneSage = 17,
    }

    /// <summary>
    /// Грань сердца босса, вплавленного в вещь. Номер — бит в RunBoons:
    /// ТОЛЬКО ДОПИСЫВАТЬ. Грани боссов 2–3 встанут после граней Чащи.
    /// </summary>
    public enum HeartFacet : byte
    {
        None = 0,
        /// <summary>Чаща, «Корни»: кувырок связывает врагов рядом.</summary>
        ThicketRoots = 1,
        /// <summary>Чаща, «Пыльца»: убитый оставляет замедляющее облако.</summary>
        ThicketPollen = 2,
        /// <summary>Чаща, «Цветение»: раз в 20 с бутон лечит.</summary>
        ThicketBloom = 3,
    }

    /// <summary>
    /// Стабильные ключи клятв для сохранения лагеря. Строки после влития не меняются
    /// никогда: сменишь — купленные клятвы всех профилей пропадут.
    /// </summary>
    public static class OathIds
    {
        /// <summary>Клятв в наборе, без None.</summary>
        public const int Count = 17;

        // Индекс = (int)OathId − 1. Хеши считаются один раз при загрузке типа, не в бою.
        private static readonly int[] Keys =
        {
            StableId.Of("oath.tough-hide"), StableId.Of("oath.heavy-hand"), StableId.Of("oath.light-step"),
            StableId.Of("oath.keen-eye"), StableId.Of("oath.deep-reserve"), StableId.Of("oath.quick-roll"),
            StableId.Of("oath.last-breath"), StableId.Of("oath.steadfast"), StableId.Of("oath.generous-spring"),
            StableId.Of("oath.enemy-blood"),
            StableId.Of("oath.second-look"), StableId.Of("oath.instinct"), StableId.Of("oath.favor"),
            StableId.Of("oath.tenacious-hands"), StableId.Of("oath.ringing-coin"), StableId.Of("oath.ash-trail"),
            StableId.Of("oath.rune-sage"),
        };

        /// <summary>Ключ клятвы; 0 для None и неизвестных номеров.</summary>
        public static int Key(OathId id) => id == OathId.None || (int)id > Count ? 0 : Keys[(int)id - 1];

        /// <summary>Клятва по ключу; None, если ключ неизвестен (клятву сняли патчем).</summary>
        public static OathId FromKey(int key)
        {
            if (key == 0) return OathId.None;
            for (int i = 0; i < Count; i++) if (Keys[i] == key) return (OathId)(i + 1);
            return OathId.None;
        }
    }

    /// <summary>
    /// Снимок клятв и граней сердца на вход в забег. Лагерь собирает его один раз
    /// перед Разломом (Camp.CreateRunBoons), дальше забег его не меняет.
    ///
    /// Два числа вместо массивов: снимок копируется по значению и мешается в хеш
    /// без аллокаций. Ступень клятвы — 2 бита (0..3) по номеру OathId, грань — бит
    /// (1 &lt;&lt; HeartFacet).
    /// </summary>
    public readonly struct RunBoons
    {
        private readonly ulong _ranks;
        private readonly uint _facets;

        private RunBoons(ulong ranks, uint facets)
        { _ranks = ranks; _facets = facets; }

        /// <summary>Без клятв и сердец. Так идут тестовые забеги и старые сохранения.</summary>
        public static readonly RunBoons Empty = default;

        public bool IsEmpty => _ranks == 0 && _facets == 0;

        /// <summary>Ступень клятвы, 0 — не взята или неизвестна.</summary>
        public int Rank(OathId id) => !Known(id) ? 0 : (int)((_ranks >> (2 * (int)id)) & 3UL);

        /// <summary>
        /// Копия с другой ступенью. Зажимается в 0..3 — ёмкость двух бит; потолок
        /// конкретной клятвы (у героя 3, у остальных 1) проверяет доска клятв.
        /// </summary>
        public RunBoons WithRank(OathId id, int rank)
        {
            if (!Known(id)) return this;
            if (rank < 0) rank = 0; else if (rank > 3) rank = 3;
            int shift = 2 * (int)id;
            return new RunBoons((_ranks & ~(3UL << shift)) | ((ulong)rank << shift), _facets);
        }

        public bool Has(HeartFacet facet) => KnownFacet(facet) && (_facets & (1u << (int)facet)) != 0;

        public RunBoons WithFacet(HeartFacet facet)
            => KnownFacet(facet) ? new RunBoons(_ranks, _facets | (1u << (int)facet)) : this;

        /// <summary>Биты граней (1 &lt;&lt; HeartFacet) — в том же виде, что Camp.WornHeartFacetMask.</summary>
        public uint FacetMask => _facets;

        public void HashInto(ref ulong h)
        {
            Hashing.Mix(ref h, _ranks);
            Hashing.Mix(ref h, (ulong)_facets);
        }

        // ---- правила доски (06.10) ----

        /// <summary>Грань Чащи — последняя известная. Новые грани дописываются в HeartFacet, и сюда — последняя.</summary>
        public const HeartFacet LastFacet = HeartFacet.ThicketBloom;

        /// <summary>
        /// Потолок ступени: клятвы героя (шкура … кувырок) — 3, остальные — 1.
        /// 0 — номера нет в наборе.
        /// </summary>
        public static int MaxRank(OathId id)
            => !Known(id) ? 0 : id <= OathId.QuickRoll ? 3 : 1;

        /// <summary>
        /// Снимок, который мог собрать лагерь: ступени не выше потолка своей клятвы,
        /// без битов вне набора и без неизвестных граней. Чужой снимок (битый реплей,
        /// ручная сборка в тесте) забег не принимает, а не подрезает молча.
        /// </summary>
        public bool IsValid
        {
            get
            {
                // Биты 0–1 — место None, выше 2·Count+1 — номера вне набора.
                const ulong rankBits = ((1UL << (2 * (OathIds.Count + 1))) - 1) & ~3UL;
                if ((_ranks & ~rankBits) != 0) return false;
                for (int id = 1; id <= OathIds.Count; id++)
                    if (Rank((OathId)id) > MaxRank((OathId)id)) return false;
                uint facetBits = ((1u << ((int)LastFacet + 1)) - 1) & ~1u;
                return (_facets & ~facetBits) == 0;
            }
        }

        /// <summary>
        /// Только клятвы, которые работают и в лагере, на манекенах (пробел №23): шкура, шаг,
        /// глаз, запас. Остальные клятвы и грани сердца — забег, в лагере им нечего делать.
        /// </summary>
        public RunBoons CampStatsOnly()
        {
            ulong keep = (3UL << (2 * (int)OathId.ToughHide)) | (3UL << (2 * (int)OathId.LightStep))
                | (3UL << (2 * (int)OathId.KeenEye)) | (3UL << (2 * (int)OathId.DeepReserve));
            return new RunBoons(_ranks & keep, 0);
        }

        // ---- доход и лечение забега: проценты от суммы, округление вниз (пробел №6) ----

        public const int RingingCoinGoldPercent = 115, AshTrailPercent = 120, TenaciousHandsGoldPercent = 75,
            GenerousSpringBonusPercent = 15, RuneSageBonusPercent = 25;

        /// <summary>«Звонкая монета»: всё золото забега ×1,15 (RiftRun.AddGold, от суммы).</summary>
        public int GoldPercent => Rank(OathId.RingingCoin) > 0 ? RingingCoinGoldPercent : 100;

        /// <summary>«Пепельный след»: пепел ×1,2 (GameSession.CreditAsh, от суммы забега).</summary>
        public int AshPercent => Rank(OathId.AshTrail) > 0 ? AshTrailPercent : 100;

        /// <summary>«Цепкие руки»: при смерти доезжает 75% золота вместо 50%.</summary>
        public int DeathGoldPercent => Rank(OathId.TenaciousHands) > 0
            ? TenaciousHandsGoldPercent : RunEconomy.DeathGoldKeptPercent;

        /// <summary>
        /// «Щедрый родник»: +15% к самому лечению (40 → 46, привал 30 → 34), а не +15
        /// процентных пунктов (пробел №26).
        /// </summary>
        public int SpringHealBonusPercent => Rank(OathId.GenerousSpring) > 0 ? GenerousSpringBonusPercent : 0;

        // Номер за пределами набора не должен сдвигать чужие биты.
        private static bool Known(OathId id) => id != OathId.None && (int)id <= OathIds.Count;
        private static bool KnownFacet(HeartFacet facet) => facet != HeartFacet.None && (int)facet < 32;
    }
}
