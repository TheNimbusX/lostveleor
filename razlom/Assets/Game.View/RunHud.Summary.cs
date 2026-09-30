using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Итог забега на Canvas. С v3 (выбор владельца 30.09, кадр 4 — «ок») — итоги со статистикой: слева
    /// стоп-кадр последнего удара в тлеющем круге (RunEndBeat) с подписью; справа — время, арены, глубина,
    /// уровни, урон нанесён и получен, лучший удар, криты, зелья (GameSession.LastRun.Stats, только чтение);
    /// «Убито» — круги видов с портретом из боя (RunHud.Portraits) и «×N»; полосы «Потеряно» и «Остаётся» —
    /// что осталось в Разломе и что уходит в лагерь. Числа досчитываются по очереди, когда цифры проявились из
    /// дыма. Ввод закрыт ещё ~1,2 с после показа (RunEndBeat.Holding): кольцо на кейкапах «Повторить» и
    /// «В лагерь» наполняется, потом кейкапы загораются.
    ///
    /// Префаб до v3 — прежние четыре числа (арены, глубина, предметы, золото) и строка потерь.
    /// </summary>
    public sealed partial class RunHud
    {
        private bool _summaryFilled;

        // Числа итогов досчитываются от нуля, по очереди — как в концепте итогов (аудит UI, этап 2).
        // Счёт — когда цифры уже проявились из дыма (владелец 26 сентября: итоги тлеют медленно,
        // и счёт, начатый с показа, кончался раньше, чем цифры становились видны).
        private readonly int[] _summaryTargets = new int[4];
        /// <summary>Часы итогов с показа, шагом не больше 0,1 с (как у UiInkGroup); меньше нуля — счёт не идёт.</summary>
        private float _summaryClock = -1f, _summaryLast;
        private const float CountDelay = .9f, CountDuration = .6f, CountStagger = .12f;

        private readonly long[] _statTargets = new long[RunHudSummary.Rows.Length];
        private readonly long[] _statShown = new long[RunHudSummary.Rows.Length];
        private readonly EnemyKind[] _killKinds = new EnemyKind[RunHudSummary.KillSlots];
        private readonly int[] _killShown = new int[RunHudSummary.KillSlots];
        private int _killCount;
        private readonly string[] _bandLines = new string[4];
        private bool _summaryKeysOpen;

        /// <summary>Префаб с итогами v3: строки статистики есть.</summary>
        private bool StatsSummary => _view.SummaryStats != null && _view.SummaryStats.Length > 0;

        private void RefreshSummary()
        {
            GameSession session = _driver.Session;
            // Итоги — после паузы конца забега (RunEndBeat): смерть и победа успевают прозвучать.
            bool shown = !_driver.GameplayPaused && session != null && session.Mode == GameMode.Summary && RunEndBeat.ScreenDue;
            _view.SetShown(_view.Summary, shown);
            if (!shown) { _summaryFilled = false; _summaryClock = -1f; return; }
            if (_summaryFilled)
            {
                CountSummary();
                SummaryKeys();
                return;
            }
            _summaryFilled = true;

            RunSummary summary = session.LastRun;
            bool died = summary.Outcome == RunOutcome.Died, won = summary.Outcome == RunOutcome.Completed;
            RunHudView.SetText(_view.SummaryTitle, died ? "Гибель" : won ? "Победа" : "Ушёл с добычей");
            int outcome = died ? 0 : won ? 1 : 2;
            if (_view.OutcomeIcon != null && outcome < _view.OutcomeIcons.Length) _view.OutcomeIcon.texture = _view.OutcomeIcons[outcome];
            // Знаки исхода белые: гибель — приглушённый красный, победа и уход — тёплые.
            Paint(_view.OutcomeIcon, died ? UiTheme.Role.Health : won ? UiTheme.Role.Accent : UiTheme.Role.Coins, died ? .78f : 1f);
            Paint(_view.OutcomeHalo, died ? UiTheme.Role.Health : UiTheme.Role.Accent, died ? .12f : .18f);
            _view.SummaryTitle.GetComponent<ThemeColor>()?.SetRole(died ? UiTheme.Role.Health : won ? UiTheme.Role.Accent : UiTheme.Role.Text);
            RunHudView.SetText(_view.SummarySubtitle, died ? "Всё найденное в забеге осталось в Разломе"
                : won ? "Локация пройдена" : "Разлом отпустил тебя с тем, что ты унёс");
            _summaryClock = 0f;
            _summaryLast = UiMotion.Now;
            _summaryKeysOpen = false;

            if (StatsSummary) FillStatsSummary(session, in summary);
            else FillPlainSummary(in summary);

            // Клавиши — в кейкапах набора на кнопках (v3); у префаба до v3 — в самой надписи.
            bool keycaps = _view.RepeatKey != null && _view.RepeatKey.Letter != null;
            RunHudView.SetText(_view.RepeatLabel, keycaps ? "Повторить" : "Повторить · " + GameKeyBindings.Label(GameAction.RepeatRun));
            RunHudView.SetText(_view.ToCampLabel, keycaps && _view.CampKey != null && _view.CampKey.Letter != null
                ? "В лагерь" : "В лагерь · " + GameKeyBindings.Label(GameAction.ReturnToCamp));
            if (_view.RepeatKey != null) RunHudView.SetKey(_view.RepeatKey.Letter, GameKeyBindings.Label(GameAction.RepeatRun));
            if (_view.CampKey != null) RunHudView.SetKey(_view.CampKey.Letter, GameKeyBindings.Label(GameAction.ReturnToCamp));
            CountSummary();
            SummaryKeys();
        }

        /// <summary>Префаб до v3: четыре числа и строка потерь, как было.</summary>
        private void FillPlainSummary(in RunSummary summary)
        {
            _summaryTargets[0] = summary.RiftsCleared;
            _summaryTargets[1] = summary.Depth;
            _summaryTargets[2] = summary.ItemsKept;
            _summaryTargets[3] = summary.GoldKept;
            string loss = string.Empty;
            if (summary.ItemsLeftBehind > 0 || summary.GoldLeftBehind > 0)
                loss = "Потеряно со смертью: предметов " + summary.ItemsLeftBehind + ", золота " + summary.GoldLeftBehind;
            if (summary.ItemsLost > 0)
                loss += (loss.Length > 0 ? "    ·    " : "") + "Не влезло в сумку: " + summary.ItemsLost;
            RunHudView.SetText(_view.SummaryLoss, loss);
        }

        private void FillStatsSummary(GameSession session, in RunSummary summary)
        {
            RunStats stats = summary.Stats;
            for (int i = 0; i < _statTargets.Length; i++)
            {
                _statTargets[i] = RunHudSummary.Value(RunHudSummary.Rows[i], in summary);
                _statShown[i] = -1;
                if (i < _view.SummaryStats.Length) RunHudView.SetText(_view.SummaryStats[i], RunHudSummary.Format(RunHudSummary.Rows[i], 0));
            }
            FillKills(stats);
            FillBands(session, in summary);
            FillFreeze(in summary);
        }

        /// <summary>«Убито»: чаще убитые раньше; портрет из боя, без него — белый знак встречи.</summary>
        private void FillKills(RunStats stats)
        {
            _killCount = RunHudSummary.KillOrder(stats, _killKinds);
            RunHudView.SetActive(_view.SummaryKillsHeader, _killCount > 0);
            RunHudView.SummaryKill[] slots = _view.SummaryKills;
            for (int i = 0; slots != null && i < slots.Length; i++)
            {
                RunHudView.SummaryKill slot = slots[i];
                if (slot == null || slot.Rect == null) continue;
                bool shown = i < _killCount;
                RunHudView.SetActive(slot.Rect, shown);
                if (i < _killShown.Length) _killShown[i] = -1;
                if (!shown) continue;
                EnemyKind kind = _killKinds[i];
                Texture portrait = Portrait(kind);
                if (slot.Portrait != null)
                {
                    slot.Portrait.texture = portrait;
                    slot.Portrait.enabled = portrait != null;
                }
                if (slot.Glyph != null) slot.Glyph.enabled = portrait == null;
                bool boss = kind == EnemyKind.ForestGuardian && stats.BossKills > 0;
                RunHudView.SetText(slot.Name, boss ? EnemyTexts.BossName(kind) : EnemyTexts.Name(kind));
                RunHudView.SetText(slot.Count, RunHudSummary.KillCount(0));
            }
        }

        /// <summary>
        /// «Потеряно» и «Остаётся»: строки (RunHudSummary) и круги вещей из строки добычи этого забега — при гибели
        /// все в «Потеряно» с красным крестом; при уходе первые, что влезли в сумку, — в «Остаётся», остальные — в
        /// «Потеряно».
        /// </summary>
        private void FillBands(GameSession session, in RunSummary summary)
        {
            bool developer = session.IsDeveloperRun;
            RiftRun run = session.Run;
            // Последняя награда могла прийти, когда строка добычи уже скрыта: дочитать взятое.
            if (run != null && run == _lootRun)
                while (_loot.Scanned < run.TakenRewardCount) _loot.Take(run.GetTaken(_loot.Scanned));
            bool ledger = run != null && run == _lootRun && !developer;
            int items = ledger ? _loot.Count : 0;
            bool died = summary.Outcome == RunOutcome.Died;
            int kept = died ? 0 : Mathf.Min(items, summary.ItemsKept);

            int lost = RunHudSummary.LostLines(in summary, developer, _bandLines);
            FillBand(_view.SummaryLost, lost, kept, items, true, lost == 1 && _bandLines[0] == "Ничего");
            int keptLines = RunHudSummary.KeptLines(in summary, developer, _bandLines);
            FillBand(_view.SummaryKept, keptLines, 0, kept, false, false);
        }

        /// <summary>Шаг кругов вещей в полосе (сборщик: круг 40 и зазор 6).</summary>
        private const float BandOrbPitch = 46f;

        private void FillBand(RunHudView.SummaryBand band, int lines, int first, int last, bool lost, bool nothing)
        {
            if (band == null || band.Rect == null) return;
            var text = new System.Text.StringBuilder();
            if (nothing) text.Append("<color=").Append(Hex(UiTheme.Role.TextMuted)).Append('>');
            for (int i = 0; i < lines; i++) text.Append(i > 0 ? "\n" : "").Append(_bandLines[i]);
            if (nothing) text.Append("</color>");
            RunHudView.SetText(band.Lines, text.ToString());
            RunHudView.BandItem[] slots = band.Items;
            // Строки — правее кругов вещей; вещей нет — с левого края полосы.
            int orbs = slots != null ? Mathf.Clamp(last - first, 0, slots.Length) : 0;
            if (band.Lines != null && band.Lines.transform.parent is RectTransform area && area != band.Rect)
                area.offsetMin = new Vector2(orbs > 0 ? 18f + orbs * BandOrbPitch + 8f : 18f, area.offsetMin.y);
            for (int i = 0; slots != null && i < slots.Length; i++)
            {
                RunHudView.BandItem slot = slots[i];
                if (slot == null || slot.Rect == null) continue;
                int entry = first + i;
                bool shown = entry < last;
                RunHudView.SetActive(slot.Rect, shown);
                if (!shown) continue;
                RunLootLedger.Entry item = _loot[entry];
                if (slot.Art != null)
                {
                    Texture art = ItemTexts.Icon(item.BaseId);
                    slot.Art.texture = art != null ? art : _view.LootFallbackIcon;
                    slot.Art.enabled = slot.Art.texture != null;
                }
                if (slot.State != null) slot.State.Set(item.Rarity, false);
                if (slot.Cross != null) RunHudView.SetActive(slot.Cross, lost);
            }
        }

        /// <summary>Стоп-кадр конца забега в круге: гибель — в тёплой сепии, победа и уход — в своём цвете.</summary>
        private void FillFreeze(in RunSummary summary)
        {
            if (_view.SummaryFreeze != null)
            {
                Texture frame = RunEndBeat.FreezeFrame;
                _view.SummaryFreeze.texture = frame;
                _view.SummaryFreeze.enabled = frame != null;
                _view.SummaryFreeze.uvRect = RunEndBeat.FreezeRect;
                _view.SummaryFreeze.color = summary.Outcome == RunOutcome.Died ? new Color(1f, .83f, .66f, 1f) : new Color(1f, .97f, .93f, 1f);
            }
            if (_view.SummaryFreezeCaption == null) return;
            RunStats stats = summary.Stats;
            EnemyKind foe = RunEndBeat.LastBlowFoe;
            if (foe == EnemyKind.None && summary.Outcome == RunOutcome.Died) foe = stats.KilledBy;
            string name = foe == EnemyKind.None ? null
                : RunEndBeat.LastBlowBoss || summary.Outcome == RunOutcome.Died && stats.KilledByRank == RunFoeRank.Boss ? EnemyTexts.BossName(foe)
                : EnemyTexts.Name(foe);
            RunHudView.SetText(_view.SummaryFreezeCaption, RunHudSummary.FreezeCaption(summary.Outcome, name, RunEndBeat.LastBlow, summary.Depth));
        }

        private void CountSummary()
        {
            if (_summaryClock < 0f) return;
            float now = UiMotion.Now;
            _summaryClock += Mathf.Clamp(now - _summaryLast, 0f, .1f);
            _summaryLast = now;
            if (StatsSummary) { CountStats(); return; }
            // До CountDelay — нули: цифры проявляются вместе с подписями.
            float since = _summaryClock - CountDelay;
            bool done = true;
            for (int i = 0; i < _view.SummaryValues.Length && i < _summaryTargets.Length; i++)
            {
                float k = Mathf.Clamp01((since - i * CountStagger) / CountDuration);
                if (k < 1f) done = false;
                float eased = 1f - (1f - k) * (1f - k) * (1f - k);
                RunHudView.SetText(_view.SummaryValues[i], Mathf.RoundToInt(_summaryTargets[i] * eased).ToString());
            }
            if (done) _summaryClock = -1f;
        }

        /// <summary>Строки по очереди, за ними «×N» убитых; текст — только когда число сменилось.</summary>
        private void CountStats()
        {
            int rows = _statTargets.Length;
            for (int i = 0; i < rows && i < _view.SummaryStats.Length; i++)
            {
                long value = RunHudSummary.Counted(_statTargets[i], RunHudSummary.CountK(_summaryClock, i));
                if (value == _statShown[i]) continue;
                _statShown[i] = value;
                RunHudView.SetText(_view.SummaryStats[i], RunHudSummary.Format(RunHudSummary.Rows[i], value));
            }
            RunHudView.SummaryKill[] slots = _view.SummaryKills;
            RunStats stats = _driver.Session != null ? _driver.Session.LastRun.Stats : null;
            for (int i = 0; stats != null && slots != null && i < _killCount && i < slots.Length; i++)
            {
                int target = stats.KillsOf(_killKinds[i]);
                int value = (int)RunHudSummary.Counted(target, RunHudSummary.CountK(_summaryClock, rows + i));
                if (value == _killShown[i] || slots[i] == null) continue;
                _killShown[i] = value;
                RunHudView.SetText(slots[i].Count, RunHudSummary.KillCount(value));
            }
            if (RunHudSummary.CountDone(_summaryClock, rows + _killCount)) _summaryClock = -1f;
        }

        /// <summary>Кольцо блокировки на кейкапах «Повторить» и «В лагерь»; открылся ввод — кейкапы загораются.</summary>
        private void SummaryKeys()
        {
            if (_summaryKeysOpen) return;
            if (!RunEndBeat.Holding)
            {
                _summaryKeysOpen = true;
                KeyLockRelease(_view.RepeatKey);
                KeyLockRelease(_view.CampKey);
                return;
            }
            float progress = RunEndBeat.LockProgress;
            KeyLockState(_view.RepeatKey, progress, false);
            KeyLockState(_view.CampKey, progress, false);
        }
    }
}
