namespace Game.Sim
{
    /// <summary>
    /// Золото забега и выход (решение владельца 06.10, план «Лагерь 06–10.10», S.2).
    ///
    /// Всё золото идёт через AddGold: процент «Звонкой монеты» (GoldPercent, клятвы —
    /// пакет T2) берётся от СУММЫ найденного, а не от каждой порции, иначе десяток
    /// мелких начислений терял бы по единице на округлении каждого. Без клятв
    /// GoldPercent = 100 и Gold == _goldBase.
    ///
    /// Числа — RunEconomy: зачистка арены 2×N (уровень босса 30), «Сложно» ×2,
    /// элита 25, разбор навыка 10.
    /// </summary>
    public sealed partial class RiftRun
    {
        // Битсет «за эту элиту уже заплачено» по номеру сущности: 8 слов = 512 номеров,
        // это вместимость симуляции забега (GameSession, simCapacity по умолчанию).
        private const int EliteGoldWords = 8;

        private int _goldBase;
        private readonly ulong[] _eliteGoldPaid = new ulong[EliteGoldWords];
        private int _eliteGoldDepth;

        /// <summary>
        /// «Уйти с добычей» — только между аренами: с экранов награды, замены и пути.
        /// В бою и по дороге к выходу Sim команду игнорирует (решение 06.10: уход — это
        /// решение на развилке, а не кнопка спасения посреди драки), TickDriver её и не шлёт.
        /// </summary>
        public bool CanLeave => Phase == RunPhase.ChoosingReward || Phase == RunPhase.ReplacingAbility
            || Phase == RunPhase.ChoosingRoute;

        /// <summary>
        /// Снимок клятв и граней (Boons, закреплён до старта) уходит в симуляцию один раз
        /// на забег. Пустой снимок — ничего не меняется, StateHash прежний.
        /// </summary>
        private void ApplyRunBoons() => _sim.SetBoons(Boons);

        private void AddGold(int amount)
        {
            _goldBase += amount;
            Gold = RunEconomy.Percent(_goldBase, GoldPercent);
        }

        private void ResetEconomy()
        {
            _goldBase = 0;
            _eliteGoldDepth = 0;
            System.Array.Clear(_eliteGoldPaid, 0, EliteGoldWords);
        }

        /// <summary>
        /// Золото с элит: 25 за каждую убитую элиту, кроме босса (он помечен элитой, его
        /// куш — золото уровня босса при зачистке). Читает смерти этого тика, поэтому
        /// зовётся сразу после шага симуляции — до проверки смерти героя: элита, павшая
        /// в один тик с героем, тоже в найденном (при смерти доезжает половина).
        ///
        /// Номера сущностей переиспользуются между аренами, поэтому битсет живёт одну
        /// арену: на новой глубине он очищается.
        /// </summary>
        private void CollectEliteGold()
        {
            if (Encounters == null) return;
            if (_eliteGoldDepth != Depth)
            {
                _eliteGoldDepth = Depth;
                System.Array.Clear(_eliteGoldPaid, 0, EliteGoldWords);
            }
            var events = _sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                int target = e.Target;
                if (e.Type != SimEventType.Death || target <= 0 || target == BossId
                    || target >= EliteGoldWords * 64 || !Encounters.IsElite(target)) continue;
                ulong bit = 1UL << (target & 63);
                if ((_eliteGoldPaid[target >> 6] & bit) != 0) continue;
                _eliteGoldPaid[target >> 6] |= bit;
                AddGold(RunEconomy.EliteGold);
            }
        }

        /// <summary>
        /// Экономика в хеше забега. Без клятв и без убитых на этой арене элит выходит
        /// сразу: Gold уже подмешан в Hash, а прочие хеши забега не сдвигаются.
        /// </summary>
        private void HashEconomy(ref ulong hash)
        {
            bool paid = false;
            for (int i = 0; i < EliteGoldWords; i++) paid |= _eliteGoldPaid[i] != 0;
            if (_goldBase == Gold && !paid) return;
            Hashing.Mix(ref hash, 0x45434F4E);
            Hashing.Mix(ref hash, _goldBase);
            Hashing.Mix(ref hash, _eliteGoldDepth);
            for (int i = 0; i < EliteGoldWords; i++)
                if (_eliteGoldPaid[i] != 0) { Hashing.Mix(ref hash, i); Hashing.Mix(ref hash, _eliteGoldPaid[i]); }
        }
    }
}
