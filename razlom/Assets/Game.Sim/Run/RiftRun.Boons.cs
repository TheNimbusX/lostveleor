namespace Game.Sim
{
    /// <summary>
    /// Клятвы и грани сердца в забеге: снимок лагеря, закреплённый до старта (решение 06.10).
    ///
    /// Забег читает здесь то, что касается его самого: золото («Звонкая монета»), Родник
    /// («Щедрый родник») и бесплатный переброс («Второй взгляд»). Бой — в Simulation.Oaths,
    /// снимок уходит туда в StartRun (ApplyRunBoons). Пустой снимок — тестовый забег и старые
    /// сохранения: проценты 100, бонусы 0, StateHash и Hash забега прежние бит в бит.
    /// </summary>
    public sealed partial class RiftRun
    {
        /// <summary>Снимок на этот забег. Пустой — как у тестового забега.</summary>
        public RunBoons Boons { get; private set; }

        /// <summary>
        /// Закрепляет снимок. Только до старта забега: сменить клятвы посреди Разлома
        /// значило бы переиграть уже начисленное. false — забег начат или снимок не мог
        /// собрать лагерь (ступень выше потолка, неизвестная клятва или грань).
        /// </summary>
        public bool SetBoons(in RunBoons b)
        {
            if (Phase != RunPhase.Idle || !b.IsValid) return false;
            Boons = b;
            return true;
        }

        /// <summary>Процент к найденному золоту («Звонкая монета» — 115), от суммы забега.</summary>
        int GoldPercent => Boons.GoldPercent;

        /// <summary>Прибавка к проценту лечения Родника и привала («Щедрый родник» — +15% от самого лечения).</summary>
        int SpringHealPercentBonus => Boons.SpringHealBonusPercent;

        /// <summary>
        /// Можно ли сейчас перебросить награду бесплатно («Второй взгляд», раз за забег).
        /// Экраны те же, что у дара «Резервный план»: обычные карточки, не артефакт босса и
        /// не форма. Отметка о трате живёт в симуляции (Simulation.SecondLookUsed): она одна
        /// на весь забег и уже в StateHash, а у забега своего места в хеше под клятвы нет.
        /// </summary>
        public bool CanFreeReroll => Boons.Rank(OathId.SecondLook) > 0 && !_sim.SecondLookUsed
            && Phase == RunPhase.ChoosingReward && !ChoosingArtifact && !ChoosingForm && BossId < 0;

        /// <summary>
        /// Бесплатный переброс тратится раньше дара (план T2): дар «Резервный план» остаётся
        /// на следующий экран. Поток бросков тот же, что у дара, — RollOffers.
        /// </summary>
        bool TryFreeReroll()
        {
            if (!CanFreeReroll) return false;
            _sim.SpendSecondLook();
            RollOffers();
            return true;
        }
    }
}
