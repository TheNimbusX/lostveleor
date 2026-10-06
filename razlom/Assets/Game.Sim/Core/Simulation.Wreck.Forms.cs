namespace Game.Sim
{
    /// <summary>
    /// ТРИ ФОРМЫ КРУШЕНИЯ (владелец 03.10; переделка 06.10 вечером — AGENTS/DESIGN.md «Формы Крушения»).
    /// Анимации форм — базовые (мах влево, мах вправо, выпад); формы различает механика и VFX.
    ///
    /// * ВОЛНОРЕЗ (стена) — как было: вместо вала стена воды до 8 м шириной 2 м, фронт 0,4 м/тик:
    ///   лёгких (правило AbordageLight) подхватывает и несёт до конца (ForcedMotion
    ///   Carried, до 6, ближние вдоль полосы первыми), там обрушение: 50 % всем несомым и
    ///   всем в 2 м у конца, о преграду — 100 %. Тяжёлых, элит и босса бьёт и проходит.
    /// * ДЕВЯТЫЙ ВАЛ (горб) — БЕЗ УДЕРЖАНИЯ: каждый из двух махов, задевший хоть одного врага, —
    ///   заряд выпада (WreckState.NinthCharges). 0 — обычный выпад; 1 — урон выпада (круг и вал) и
    ///   ширина полосы ×1,5; 2 — ×2 и полоса на 2 м длиннее (Simulation.Wreck.Numbers).
    /// * ПРИЗРАЧНЫЙ ЯКОРЬ (вместо «Якорной брони», та не выходила) — вала у выпада нет; через
    ///   WreckGhostDelayTicks после удара в точку выпада падает огромный призрачный якорь: круг 3 м
    ///   (+ тело), ×1,5 урона круга выпада, оглушение 0,6 с (тяжёлых и элит тоже, босса — нет).
    ///   Событие WreckGhostAnchor — вид ставит VFX ровно в его точку.
    ///
    /// Все числа — именованные ЗАГЛУШКИ под приёмку.
    /// </summary>
    public sealed partial class Simulation
    {
        // ---- Волнорез ----

        /// <summary>Стена: до 8 м от героя, ширина 2 м, фронт 0,4 м/тик → 15 шагов от 2,2 м.</summary>
        public static readonly Fix64 WreckBreakwaterLength = Fix64.FromInt(8);
        public static readonly Fix64 WreckBreakwaterHalfWidth = Fix64.One;
        public static readonly Fix64 WreckBreakwaterStep = Fix64.Ratio(4, 10);

        /// <summary>Стена гаснет перед преградой оси: конец = преграда − 0,3 м.</summary>
        public static readonly Fix64 WreckBreakwaterWallBackoff = Fix64.Ratio(3, 10);

        /// <summary>Сколько несёт стена одновременно.</summary>
        public const int WreckBreakwaterCarryLimit = 6;

        /// <summary>Несомый оглушён до обрушения и ещё столько тиков.</summary>
        public const int WreckBreakwaterCarryStunTicks = 15;

        /// <summary>Обрушение: радиус (+ тело) у конца стены, доля урона стены (о преграду — полный).</summary>
        public static readonly Fix64 WreckBreakwaterCrashRadius = Fix64.FromInt(2);
        public const int WreckBreakwaterCrashPercent = 50;

        /// <summary>Обрушение толкает лёгких рядом на 0,5 м за 4 тика.</summary>
        public static readonly Fix64 WreckBreakwaterCrashShove = Fix64.Ratio(1, 2);
        public const int WreckBreakwaterCrashShoveTicks = 4;

        /// <summary>Выход Волнореза: 9 тиков протяжки (кадры 13–21 Slam_Drag), герой стоит по удар + 9.</summary>
        public const int WreckBreakwaterExitTicks = 9, WreckBreakwaterExitLockedTicks = 6;

        // ---- прежние числа, которые ещё читает вид ----

        /// <summary>
        /// Прежний предел удержания Девятого вала (1 с). С 06.10 вечером Sim его не использует — число держится
        /// для правил VFX (PelagWreckVfxRules: доля заряда), пока вид не перейдёт на заряды махами.
        /// </summary>
        public const int WreckChargeMaxTicks = 30;

        /// <summary>
        /// Прежний взрыв «Якорной брони» (2,5 м). С 06.10 вечером Sim его не использует — число держится для
        /// VFX брони (PelagVfxController.WreckShell), пока вид не перейдёт на Призрачный якорь.
        /// </summary>
        public static readonly Fix64 WreckShellBurstRadius = Fix64.Ratio(5, 2);

        // ---------- Девятый вал: заряды махами ----------

        /// <summary>
        /// Мах stage серии serial задел врага (Simulation.Wreck.Sweep, до урона): у Девятого вала — заряд
        /// выпада, один за мах, сколько бы врагов он ни задел. Мах прошлой серии и «Четвёртый удар» не в счёт.
        /// </summary>
        private void WreckNinthSwingLanded(int stage, int serial)
        {
            if (serial != _wreck.Serial || stage < 0 || stage >= WreckNinthMaxCharges) return;
            if (!FormIs(_wreck.Slot, PelagForm.WreckNinthWave)) return;
            int bit = 1 << stage;
            if ((_wreck.NinthLanded & bit) != 0) return;
            _wreck.NinthLanded |= bit;
            _wreck.NinthCharges++;
        }

        // ---------- Призрачный якорь ----------

        /// <summary>Удар выпада Призрачного якоря: якорь заряжен — упадёт в точку выпада через WreckGhostDelayTicks.</summary>
        private void ArmWreckGhost(int circleDamage)
        {
            _wreck.GhostTick = Tick + WreckGhostDelayTicks;
            _wreck.GhostPoint = _wreck.ImpactPoint;
            _wreck.GhostDamage = circleDamage * WreckGhostDamagePercent / 100;
        }

        /// <summary>
        /// Каждый тик (после вала, до шага серии): Призрачный якорь падает в свой тик — событие WreckGhostAnchor,
        /// потом каждому, чьё тело касается круга WreckGhostRadius у точки выпада (босс — по корпусу), урон
        /// и оглушение WreckGhostStunTicks (StunByTalent: босс не оглушается, тяжёлые и элиты — да). Серия могла
        /// уже кончиться или быть сорвана после удара — якорь всё равно падает, как вал доживает своё.
        /// </summary>
        private void UpdateWreckGhost()
        {
            if (_wreck.GhostTick < 0 || Tick < _wreck.GhostTick) return;
            FixVec2 at = _wreck.GhostPoint;
            int serial = _wreck.Serial, damage = _wreck.GhostDamage;
            _wreck.GhostTick = -1;
            _events.Add(new SimEvent(SimEventType.WreckGhostAnchor, PlayerId, -1, (WreckGhostRadius * 100).ToInt(), false, at,
                DamageType.Physical, DamageOrigin.Ability, serial));
            AbilityBuild build = WreckBuildForWave;
            for (int i = PlayerId + 1; i < Entities.Count; i++)
            {
                if (!WreckEnemy(i)) continue;
                Fix64 r = WreckGhostRadius + ThicketBodyFrom(i, at);
                if (FixVec2.DistanceSq(Entities.Position[i], at) > r * r) continue;
                ApplyAbilityDamage(PlayerId, i, build != null ? WreckBigGame(build, i, damage) : damage, _wreck.Slot, DamageType.Physical);
                // Герой умер от отражения — расстановка сбросила и якорь.
                if (_wreck.Serial != serial || !Entities.Alive[PlayerId]) return;
                if (Entities.Alive[i]) StunByTalent(i, WreckGhostStunTicks);
            }
        }

        /// <summary>«Неудержимый» (−25 %): до удара оземь и в его удержании.</summary>
        private bool WreckUnstoppableNow
            => _wreck.Phase != WreckPhase.None && (_wreck.Strikes < WreckStages || _wreck.Phase == WreckPhase.Hold)
               && BuildHas(_wreck.Slot, AbilityFlag.WreckUnstoppable, AbilityDefinition.WreckId);
    }
}
