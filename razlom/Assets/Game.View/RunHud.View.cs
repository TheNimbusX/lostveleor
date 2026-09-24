using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Экраны забега на Canvas (префаб RunHudWc, пак «Ночная акварель»): выбор награды,
    /// замена способности, панель состояния и полоса босса. Нет префаба — остаётся
    /// прежний IMGUI из RunHud.cs.
    /// </summary>
    public sealed partial class RunHud
    {
        private RunHudView _view;
        private RunPhase _shownPhase = (RunPhase)255;
        private int _shownDepth = -1, _shownLetters = -1;
        private string _statusShown;

        private void BindView()
        {
            var prefab = Resources.Load<GameObject>("UI/Prefabs/RunHudWc");
            if (prefab == null) return;
            _view = Instantiate(prefab, transform).GetComponentInChildren<RunHudView>(true);
            if (_view == null) return;
            _view.OfferClicked += i => _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseReward1 + i));
            _view.SlotClicked += i => _driver.QueueRunCommand((RunCommand)((int)RunCommand.ReplaceSlot1 + i));
            _view.SalvageClicked += () =>
            {
                GameSound.Play("salvage", .8f);
                _driver.QueueRunCommand(RunCommand.SalvageAbility);
            };
            _view.RepeatClicked += () => _driver.QueueSummaryCommand(true);
            _view.CampClicked += () => _driver.QueueSummaryCommand(false);
        }

        /// <summary>Итог забега рисует Canvas — CampHud свой текстовый итог не показывает.</summary>
        public bool CanvasSummary => _view != null && _view.Summary != null;

        private bool _summaryFilled;

        private void RefreshSummary()
        {
            GameSession session = _driver.Session;
            bool shown = !_driver.GameplayPaused && session != null && session.Mode == GameMode.Summary;
            _view.SetShown(_view.Summary, shown);
            if (!shown) { _summaryFilled = false; return; }
            if (_summaryFilled) return;
            _summaryFilled = true;

            RunSummary summary = session.LastRun;
            bool died = summary.Outcome == RunOutcome.Died, won = summary.Outcome == RunOutcome.Completed;
            RunHudView.SetText(_view.SummaryTitle, died ? "Гибель" : won ? "Победа" : "Ушёл с добычей");
            int outcome = died ? 0 : won ? 1 : 2;
            if (_view.OutcomeIcon != null && outcome < _view.OutcomeIcons.Length) _view.OutcomeIcon.texture = _view.OutcomeIcons[outcome];
            _view.SummaryTitle.GetComponent<ThemeColor>()?.SetRole(died ? UiTheme.Role.Health : won ? UiTheme.Role.Accent : UiTheme.Role.Text);
            RunHudView.SetText(_view.SummarySubtitle, died ? "Всё найденное в забеге осталось в Разломе"
                : won ? "Локация пройдена" : "Разлом отпустил тебя с тем, что ты унёс");
            int[] values = { summary.RiftsCleared, summary.Depth, summary.ItemsKept, summary.GoldKept };
            for (int i = 0; i < _view.SummaryValues.Length && i < values.Length; i++)
                RunHudView.SetText(_view.SummaryValues[i], values[i].ToString());

            string loss = string.Empty;
            if (summary.ItemsLeftBehind > 0 || summary.GoldLeftBehind > 0)
                loss = "Потеряно со смертью: предметов " + summary.ItemsLeftBehind + ", золота " + summary.GoldLeftBehind;
            if (summary.ItemsLost > 0)
                loss += (loss.Length > 0 ? "    ·    " : "") + "Не влезло в сумку: " + summary.ItemsLost;
            RunHudView.SetText(_view.SummaryLoss, loss);

            string camp = string.Empty;
            if (session.NewItemsToTry > 0) camp += "Новых вещей проверить на манекенах: " + session.NewItemsToTry;
            if (session.JunkToSalvage > 0) camp += (camp.Length > 0 ? "\n" : "") + "Мусора под разбор: " + session.JunkToSalvage;
            if (camp.Length == 0) camp = "Ничего. Значит, повторяй.";
            RunHudView.SetText(_view.SummaryCamp, camp);
            RunHudView.SetText(_view.RepeatLabel, "Повторить · " + GameKeyBindings.Label(GameAction.RepeatRun));
            RunHudView.SetText(_view.ToCampLabel, "В лагерь · " + GameKeyBindings.Label(GameAction.ReturnToCamp));
        }

        private void LateUpdate()
        {
            if (_view == null) return;
            RefreshSummary();
            RiftRun run = _driver.Run;
            bool live = !_driver.GameplayPaused && _driver.Session != null && _driver.Session.Mode == GameMode.Rift && run != null;
            RunPhase phase = live ? run.Phase : (RunPhase)255;

            bool choosing = phase == RunPhase.ChoosingReward, replacing = phase == RunPhase.ReplacingAbility;
            int letters = GameUserSettings.AbilityRowUsesLetters ? 1 : 0;
            if (live && (phase != _shownPhase || run.Depth != _shownDepth || letters != _shownLetters))
            {
                _shownPhase = phase; _shownDepth = run.Depth; _shownLetters = letters;
                if (choosing) FillChoice(run, letters == 1);
                if (replacing) FillReplace(run, letters == 1);
            }
            if (!live) _shownPhase = (RunPhase)255;
            _view.SetShown(_view.Choice, choosing);
            _view.SetShown(_view.Replace, replacing);

            bool status = phase == RunPhase.Clearing || phase == RunPhase.SeekingExit;
            RunHudView.SetActive(_view.Status, status);
            if (status) FillStatus(run);
            bool boss = status && run.BossId >= 0 && run.Sim.Entities.Alive[run.BossId];
            RunHudView.SetActive(_view.Boss, boss);
            if (boss)
            {
                int id = run.BossId;
                RunHudView.SetText(_view.BossName, run.BossEnraged ? "Хранитель лугов · ярость" : "Хранитель лугов");
                if (_view.BossBar != null) _view.BossBar.Set(run.Sim.Entities.Health[id] / (float)Mathf.Max(1, run.Sim.Entities.MaxHealth[id]));
            }
        }

        private void FillStatus(RiftRun run)
        {
            string text;
            if (run.Phase == RunPhase.SeekingExit) text = "Путь открыт\nСледуй по тропе к выходу\n";
            else
            {
                int fights = 0, cleared = 0;
                if (run.Encounters != null)
                    for (int e = 0; e < run.Encounters.Count; e++)
                    {
                        if (run.Encounters.Get(e).Role == EncounterRole.RewardBranch) continue;
                        fights++;
                        if (run.Encounters.Alive(e, run.Sim.Entities) == 0) cleared++;
                    }
                text = "Разлом " + run.Depth + (run.TotalLevels > 0 ? " / " + run.TotalLevels : "") + "\n"
                       + (fights > 0 ? "Встречи " + cleared + " / " + fights + " · целей " + run.CountRequiredEnemies() : "Целей " + run.CountRequiredEnemies()) + "\n"
                       + "Тайники " + run.BranchesClaimed + " / " + run.Map.RewardBranchCount + " · золото " + run.Gold;
            }
            if (text == _statusShown) return;
            _statusShown = text;
            string[] lines = text.Split('\n');
            RunHudView.SetText(_view.StatusTitle, lines[0]);
            RunHudView.SetText(_view.StatusLine, lines.Length > 1 ? lines[1] : string.Empty);
            RunHudView.SetText(_view.StatusExtra, lines.Length > 2 ? lines[2] : string.Empty);
        }

        private static string KeyLabel(int index, bool letters) => letters ? "QWER"[index].ToString() : (index + 1).ToString();

        private void FillChoice(RiftRun run, bool letters)
        {
            RunHudView.SetText(_view.ChoiceTitle, "Выберите награду");
            RunHudView.SetText(_view.ChoiceSubtitle, run.IsFinalLevel
                ? "Локация пройдена — последняя награда, дальше итоги"
                : "Разлом зачищен · дальше разлом " + (run.Depth + 1) + (run.TotalLevels > 0 ? " / " + run.TotalLevels : ""));
            RunHudView.SetText(_view.ChoiceHint, (letters ? "Q  W  E" : "1  2  3") + " — выбрать    ·    L — уйти с добычей");
            for (int i = 0; i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null) continue;
                bool shown = i < RiftRun.RewardChoices;
                card.gameObject.SetActive(shown);
                if (!shown) continue;
                Describe(run.GetOffer(i), run, out string title, out string kind, out string body,
                    out string valueLabel, out string value, out Texture2D icon, out bool rare);
                RunHudView.SetText(card.Title, title);
                RunHudView.SetText(card.Kind, kind);
                RewardKind rewardKind = run.GetOffer(i).Kind;
                int kindIcon = rewardKind == RewardKind.Ability ? 0 : rewardKind == RewardKind.Talent ? 1 : rewardKind == RewardKind.Item ? 2 : 3;
                if (card.KindIcon != null && kindIcon < _view.KindIcons.Length)
                {
                    card.KindIcon.texture = _view.KindIcons[kindIcon];
                    card.KindIcon.enabled = card.KindIcon.texture != null;
                }
                RunHudView.SetText(card.Description, body);
                RunHudView.SetText(card.ValueLabel, valueLabel);
                RunHudView.SetText(card.Value, value);
                RunHudView.SetText(card.Key, KeyLabel(i, letters));
                if (card.Icon != null) { card.Icon.texture = icon; card.Icon.enabled = icon != null; }
                // Вещь — её редкость (четыре цвета, как в палатке); способность и талант — «редкая» или обычная.
                WcRarity.Tier tier = run.GetOffer(i).Kind == RewardKind.Item ? WcRarity.FromItem((int)run.GetOffer(i).Item.Rarity)
                    : rare ? WcRarity.Tier.Rare : WcRarity.Tier.Common;
                if (card.Rarity != null) card.Rarity.Set(tier);
            }
        }

        private void FillReplace(RiftRun run, bool letters)
        {
            AbilityDefinition pending = PelagKit.PoolDefinition(run.PendingAbility);
            RunHudView.SetText(_view.ReplaceTitle, "Новая способность: " + (pending != null ? Capitalized(PlayerHud.AbilityName(pending.Id)) : "—"));
            RunHudView.SetText(_view.ReplaceSubtitle, "Панель полна. Замени одну из четырёх — её таланты пропадут — или разбери новую на золото.");
            RunHudView.SetText(_view.ReplaceHint, (letters ? "Q  W  E  R" : "1  2  3  4") + " — заменить слот    ·    L — уйти с добычей");
            RunHudView.SetText(_view.SalvageLabel, "Разобрать на " + run.SalvageGold + " золота");
            for (int slot = 0; slot < _view.Slots.Length; slot++)
            {
                RunSlotTile tile = _view.Slots[slot];
                if (tile == null) continue;
                AbilityDefinition current = run.Loadout.DefinitionAt(slot);
                Texture2D icon = current != null ? Icon(current.Id) : null;
                if (tile.Icon != null) { tile.Icon.texture = icon; tile.Icon.enabled = icon != null; }
                RunHudView.SetText(tile.Name, current != null ? Capitalized(PlayerHud.AbilityName(current.Id)) : "Пусто");
                int rank = run.Loadout.TalentRank(run.Loadout.PoolIndexAt(slot));
                RunHudView.SetText(tile.Note, rank > 0 ? "Талантов " + rank + " — пропадут" : "Талантов нет");
                RunHudView.SetText(tile.Key, KeyLabel(slot, letters));
            }
        }

        /// <summary>Тексты карточки награды — те же, что рисовал IMGUI, в разметке карточки пака.</summary>
        private void Describe(in RewardOffer offer, RiftRun run, out string title, out string kind, out string body,
            out string valueLabel, out string value, out Texture2D icon, out bool rare)
        {
            title = kind = body = valueLabel = value = string.Empty;
            icon = null;
            rare = false;
            switch (offer.Kind)
            {
                case RewardKind.Ability:
                {
                    AbilityDefinition definition = PelagKit.PoolDefinition(offer.PoolIndex);
                    if (definition == null) return;
                    icon = Icon(definition.Id);
                    title = Capitalized(PlayerHud.AbilityName(definition.Id));
                    kind = "Способность";
                    body = PlayerHud.AbilityDescription(definition.Id);
                    if (run.Loadout.IsFull) body += " Панель полна: придётся заменить способность или разобрать эту на " + run.SalvageGold + " золота.";
                    valueLabel = "Лавидий";
                    value = definition.GetBase(AbilityStatType.LavidiumCost).ToInt().ToString();
                    return;
                }
                case RewardKind.Talent:
                {
                    if (!SabreTalents.TryLineOf(offer.PoolIndex, out SabreTalentLine line)) return;
                    AbilityDefinition definition = PelagKit.PoolDefinition(offer.PoolIndex);
                    icon = definition != null ? Icon(definition.Id) : null;
                    title = SabreTalentTexts.Name(line, offer.TalentIndex);
                    kind = "Талант " + (offer.TalentIndex + 1) + " из " + SabreTalents.TalentsPerLine;
                    body = SabreTalentTexts.Description(line, offer.TalentIndex);
                    valueLabel = Capitalized(SabreTalentTexts.LineName(line));
                    rare = true;
                    return;
                }
                case RewardKind.StatBoost:
                    title = Capitalized(StatTitle(offer.Stat));
                    kind = "Характеристика";
                    body = "Постоянно до конца забега.";
                    valueLabel = Capitalized(StatTitle(offer.Stat));
                    value = offer.Op == ModifierOp.Flat ? "+" + offer.Value.ToFloat().ToString("0.##")
                        : "+" + (offer.Value.ToFloat() * 100f).ToString("0.#") + "%";
                    return;
            }
            if (!ItemGenerator.Generate(offer.Item, run.Items, _itemBuffer))
            {
                title = "Предмет не найден";
                return;
            }
            title = _itemBuffer.Category == ItemCategory.Weapon ? "Ржавый меч" : "Кожаная куртка";
            kind = "Предмет · ур. " + offer.Item.ItemLevel;
            body = ItemDetails(_itemBuffer).Replace("\n", " · ");
            rare = offer.Item.Rarity >= ItemRarity.Magic;
        }

        /// <summary>Названия в коде — капсом (так их рисовал IMGUI); на карточке антиква — с заглавной.</summary>
        private static string Capitalized(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string lower = text.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }
    }
}
