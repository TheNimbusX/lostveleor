using Game.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Экраны забега на Canvas (префаб RunHudWc, «Дым и свет» с 26 сентября): выбор награды и
    /// следующей арены, замена способности, панель состояния, полоса босса и итог забега.
    /// Нет префаба — остаётся прежний IMGUI из RunHud.cs.
    /// </summary>
    public sealed partial class RunHud
    {
        private RunHudView _view;
        private RunPhase _shownPhase = (RunPhase)255;
        private int _shownDepth = -1, _shownLetters = -1;
        private string _statusShown;

        /// <summary>Метки мира, мини-меню добычи и подсказка цели на паке (RunWorldWc); без него — IMGUI.</summary>
        private RunWorldView _world;

        private void BindView()
        {
            var world = Resources.Load<GameObject>("UI/Prefabs/RunWorldWc");
            if (world != null && _world == null)
            {
                _world = Instantiate(world, transform).GetComponentInChildren<RunWorldView>(true);
                if (_world != null) UiScaleFollower.Attach(_world.gameObject);
                if (_world != null) _world.Initialize(_driver);
            }
            var prefab = Resources.Load<GameObject>("UI/Prefabs/RunHudWc");
            if (prefab == null) return;
            _view = Instantiate(prefab, transform).GetComponentInChildren<RunHudView>(true);
            if (_view == null) return;
            // Блок ввода экранов выбора (RunHud.Reward) спрашивает этот экземпляр: с первого экрана сессии.
            _choiceOwner = this;
            _view.OfferClicked += RequestOffer;
            _view.SkipClicked += () => { if (ChoiceLocked) return; _replaceOffer = -1; _driver.QueueRunCommand(_driver.Run.ChoosingArtifact?RunCommand.SkipReward:RunCommand.RerollReward); };
            _view.ArtifactConfirmClicked += () =>
            {
                int offer = _replaceOffer;
                _replaceOffer = -1;
                if (offer >= 0) _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseReward1 + offer));
            };
            _view.ArtifactKeepClicked += () => _replaceOffer = -1;
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

        // Итоги забега — RunHud.Summary (статистика, стоп-кадр, «Убито», «Потеряно» / «Остаётся», блок ввода).

        /// <summary>Цвет белого знака по роли темы; у префаба без ThemeColor на детали — прямо в цвет.</summary>
        private static void Paint(UnityEngine.UI.Graphic graphic, UiTheme.Role role, float alpha)
        {
            if (graphic == null) return;
            var theme = graphic.GetComponent<ThemeColor>();
            if (theme != null) { theme.SetRole(role, alpha); return; }
            Color color = UiTheme.Current.Get(role);
            color.a *= alpha;
            graphic.color = color;
        }

        private void LateUpdate()
        {
            if (_view == null) return;
            RefreshSummary();
            RiftRun run = _driver.Run;
            bool live = !_driver.GameplayPaused && _driver.Session != null && _driver.Session.Mode == GameMode.Rift && run != null;
            RunPhase phase = live ? run.Phase : (RunPhase)255;
            // Выбег с арены (TickDriver.ArenaRun): экран награды открывается, когда герой убежал в лес.
            if (live && _driver.ExitRunPlaying) phase = RunPhase.SeekingExit;

            // Выбор арены (ArenaFlow) — тот же экран с тремя карточками, что и награда.
            bool reward = phase == RunPhase.ChoosingReward, route = phase == RunPhase.ChoosingRoute;
            bool choosing = reward || route, replacing = phase == RunPhase.ReplacingAbility;
            int letters = TickDriver.GamepadLastUsed ? 2 : GameUserSettings.WasdMovement ? 3
                : GameUserSettings.AbilityRowUsesLetters ? 1 : 0;
            // Карточки сменились без смены фазы — «Резервный план» перебросил награду: заполнить заново.
            bool rerolled = live && reward && phase == _shownPhase && OffersSignature(run) != _shownOffers;
            if (live && (phase != _shownPhase || run.Depth != _shownDepth || letters != _shownLetters || rerolled))
            {
                // Награда взята — сразу выбор арены: экран тот же, но вопрос новый, и карточки
                // проявляются заново, а не меняют текст под рукой.
                if (route && _shownPhase == RunPhase.ChoosingReward) _view.SetShown(_view.Choice, false, true);
                // Блок ввода — только когда экран открылся или карточки сменились, не на смене раскладки клавиш.
                bool opened = phase != _shownPhase || run.Depth != _shownDepth || rerolled;
                _shownPhase = phase; _shownDepth = run.Depth; _shownLetters = letters;
                if (reward) FillChoice(run, letters == 1);
                if (route) FillRoute(run, letters == 1);
                if (replacing) FillReplace(run, letters == 1);
                if (reward) _shownOffers = OffersSignature(run);
                if (choosing && opened) OpenChoice(rerolled);
            }
            if (!live) _shownPhase = (RunPhase)255;
            _view.SetShown(_view.Choice, choosing);
            if (!reward) _replaceOffer = -1;
            HandleReplaceKeys();
            _view.SetShown(_view.ArtifactReplace, reward && _replaceOffer >= 0);
            ReplaceOpen = reward && _replaceOffer >= 0;
            _view.SetShown(_view.Replace, replacing);
            ModalOpen = live && (choosing || replacing);
            // Наклон, перелив, подсказка справа и кольцо блокировки на кейкапах (RunHud.Reward).
            UpdateChoice(choosing);

            bool status = phase == RunPhase.Clearing || phase == RunPhase.SeekingExit;
            RunHudView.SetActive(_view.Status, status);
            if (status) FillStatus(run);
            // Источник монет и портреты убитых для итогов — по событиям кадра (RunHud.Coins, RunHud.Portraits).
            if (live) TrackFrameEvents(run);
            // Строка добычи — под панелью и вместе с ней (RunHud.Loot).
            RefreshLoot(live ? run : null, status);
            RefreshSurvival(run, status);
            if (live) AnnounceWaves(run);
            // Полоса босса: появление, засечки, фаза, след (RunHud.Boss). Забег — и в паузе: пауза не делает
            // босса «новым», появление не повторяется.
            RefreshBoss(run, status);
        }

        private void FillStatus(RiftRun run)
        {
            string text;
            if (run.Phase == RunPhase.SeekingExit) text = "Путь открыт\nСледуй по тропе к выходу\n";
            else
            {
                // Префаб до пересборки без своего таймера — время выживания в строке панели.
                int survival = _view.Survival == null ? run.Sim.SurvivalTicksLeft : 0;
                text = "Арена " + run.Depth + "\n"
                       + (survival > 0 ? SurvivalText(survival) + " · " : "") + FightsLine(run) + "\n"
                       + "Тайники " + run.BranchesClaimed + " / " + run.Map.RewardBranchCount
                       // Золото — в строке добычи под панелью; у префаба до v2 её нет (или она не подключена) — золото здесь.
                       + (_view.Loot == null ? " · золото " + run.Gold : "");
            }
            if (text == _statusShown) return;
            // Новый заголовок («Путь открыт», следующий разлом) — дым и буквы проявляются заново;
            // смена счётчиков под ним панель не перерисовывает.
            string oldTitle = _statusShown != null ? _statusShown.Split('\n')[0] : null;
            _statusShown = text;
            string[] lines = text.Split('\n');
            if (oldTitle != null && oldTitle != lines[0] && _view.Status != null)
            {
                var ink = _view.Status.GetComponent<UiInkGroup>();
                if (ink != null && ink.isActiveAndEnabled) ink.Show();
            }
            RunHudView.SetText(_view.StatusTitle, lines[0]);
            RunHudView.SetText(_view.StatusLine, lines.Length > 1 ? lines[1] : string.Empty);
            string extra = lines.Length > 2 ? lines[2] : string.Empty;
            RunHudView.SetText(_view.StatusExtra, extra);
            // «Путь открыт»: третьей строки нет — и значка тайников без неё тоже (одинокий сундук под двумя строками).
            UnityEngine.UI.Graphic extraIcon = StatusExtraIcon();
            if (extraIcon != null) RunHudView.SetActive(extraIcon, extra.Length > 0);
        }

        private UnityEngine.UI.Graphic _statusExtraIcon;
        private bool _statusExtraIconSought;

        /// <summary>Значок третьей строки: ссылка миграции v3, у префаба до неё — узел «Значок тайников» по имени.</summary>
        private UnityEngine.UI.Graphic StatusExtraIcon()
        {
            if (_view.StatusExtraIcon != null) return _view.StatusExtraIcon;
            if (_statusExtraIconSought || _view.Status == null) return _statusExtraIcon;
            _statusExtraIconSought = true;
            Transform node = _view.Status.Find("Значок тайников");
            _statusExtraIcon = node != null ? node.GetComponent<UnityEngine.UI.Graphic>() : null;
            return _statusExtraIcon;
        }

        /// <summary>
        /// Счёт боя в панели состояния. Встреча по шаблону идёт волнами, и каждая волна
        /// добавляет в план своё размещение: прежний счёт «Встречи 0 / 1» рос на глазах
        /// (1 → 2 → 3) и ничего не значил. Теперь — «Волна 2 / 3 · целей 7»: сколько волн
        /// уже вышло из скольких. У босса подмога тоже встаёт волнами — там только цели,
        /// сам босс на своей полосе. Без шаблона — прежний счёт встреч.
        /// </summary>
        public static string FightsLine(RiftRun run)
        {
            int targets = run.CountRequiredEnemies();
            ArenaEncounterTemplate encounter = run.Sim.ActiveEncounter;
            if (encounter != null && encounter.WaveCount > 1)
                return "Волна " + Mathf.Clamp(run.Sim.EncounterWavesSpawned, 1, encounter.WaveCount) + " / " + encounter.WaveCount
                       + " · целей " + targets;
            if (run.BossId >= 0) return "Целей " + targets;
            int fights = 0, cleared = 0;
            if (run.Encounters != null)
                for (int e = 0; e < run.Encounters.Count; e++)
                {
                    if (run.Encounters.Get(e).Role == EncounterRole.RewardBranch) continue;
                    fights++;
                    if (run.Encounters.Alive(e, run.Sim.Entities) == 0) cleared++;
                }
            return fights > 0 ? "Встречи " + cleared + " / " + fights + " · целей " + targets : "Целей " + targets;
        }

        /// <summary>«Выстоять 0:42» — секунды вверх: ноль виден, только когда время вышло.</summary>
        public static string SurvivalText(int ticks)
        {
            int seconds = (ticks + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond;
            return "Выстоять " + seconds / 60 + ":" + (seconds % 60).ToString("00");
        }

        private int _survivalShown = -1;

        /// <summary>
        /// Таймер выживания — свой клуб дыма под панелью состояния, в её манере. Текст
        /// меняется раз в секунду; последние десять — тёплым акцентом: конец близко.
        /// </summary>
        private void RefreshSurvival(RiftRun run, bool status)
        {
            if (_view.Survival == null) return;
            int ticks = status && run.Phase == RunPhase.Clearing ? run.Sim.SurvivalTicksLeft : 0;
            RunHudView.SetActive(_view.Survival, ticks > 0);
            if (ticks <= 0) { _survivalShown = -1; return; }
            int seconds = (ticks + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond;
            if (seconds == _survivalShown) return;
            _survivalShown = seconds;
            RunHudView.SetText(_view.SurvivalLabel, SurvivalText(ticks));
            ThemeColor tint = _view.SurvivalLabel != null ? _view.SurvivalLabel.GetComponent<ThemeColor>() : null;
            if (tint != null) tint.SetRole(seconds <= 10 ? UiTheme.Role.Accent : UiTheme.Role.Text);
        }

        // Объявления волн: не чаще раза в WaveAnnounceSpacing секунд и не поверх чужого баннера.
        private float _waveAnnouncedAt = -100f;
        private const float WaveAnnounceSpacing = 6f;

        /// <summary>
        /// Короткий баннер боевого HUD (HudAnnounce) на выход волны из земли: «Засада!» —
        /// первая волна засады за спиной приманки, «Новая волна» — остальные. Молчат: стартовая
        /// волна (она стоит с начала арены), волны выживания (есть таймер, волна каждые 11 с —
        /// это был бы спам), подмога босса (баннер лёг бы на полосу босса), волна раньше
        /// WaveAnnounceSpacing после прошлой и волна поверх ещё видимого баннера.
        /// </summary>
        private void AnnounceWaves(RiftRun run)
        {
            var events = _driver.FrameEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Type != SimEventType.EncounterWave || e.Source >= 0 || e.Amount < 1 || e.Flag) continue;
                ArenaEncounterTemplate encounter = run.Sim.ActiveEncounter;
                if (encounter == null || encounter.Type == ArenaEncounterType.Survival) continue;
                float now = Time.unscaledTime;
                if (now - _waveAnnouncedAt < WaveAnnounceSpacing) continue;
                HudAnnounce announce = HudAnnounce.Find();
                if (announce == null || announce.Showing) continue;
                _waveAnnouncedAt = now;
                bool ambush = encounter.Type == ArenaEncounterType.Ambush && e.Amount == 1;
                announce.Show(ambush ? "ЗАСАДА!" : "НОВАЯ ВОЛНА",
                    ambush ? "Враги встают из земли вокруг" : "Волна " + (e.Amount + 1) + " из " + e.ActionVariant);
            }
        }

        private static string KeyLabel(int index, bool letters)
            => TickDriver.GamepadLastUsed
                ? (index == 0 ? "←" : index == 1 ? "↑" : index == 2 ? "→" : "↓")
                : GameKeyBindings.Label((GameAction)index);

        /// <summary>Карточка артефакта, для которой открыт вопрос «Заменить артефакт?»; −1 — вопроса нет.</summary>
        private int _replaceOffer = -1;

        /// <summary>Открыт вопрос «Заменить артефакт?» — пауза по Escape в этот момент не открывается.</summary>
        public static bool ReplaceOpen { get; private set; }

        /// <summary>Открыт экран выбора забега (награда, арена, замена): плашка уровня ждёт, пока он закроется.</summary>
        public static bool ModalOpen { get; private set; }
        /// <summary>Кадр, в который вопрос закрыли Escape'ом: пауза в этот кадр тоже молчит.</summary>
        public static int ReplaceClosedFrame { get; private set; } = -1;

        /// <summary>
        /// Вопрос «Заменить артефакт?» с клавиатуры: Enter или пробел — заменить, Escape — оставить.
        /// Раньше его открывали клавишами 1–3, а закрыть можно было только мышью (аудит UI, 25 сентября).
        /// </summary>
        private void HandleReplaceKeys()
        {
            if (_replaceOffer < 0) return;
            bool confirm, keep;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            confirm = keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame);
            keep = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
            keep = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (keep)
            {
                _replaceOffer = -1;
                ReplaceClosedFrame = Time.frameCount;
                return;
            }
            if (!confirm) return;
            int offer = _replaceOffer;
            _replaceOffer = -1;
            _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseReward1 + offer));
        }

        /// <summary>
        /// Выбор карточки мышью или клавишей. Если на экране артефакты, а артефакт уже есть, —
        /// сначала вопрос «Заменить артефакт?» (концепт 2-artifact-sheet): слот один.
        /// </summary>
        public void RequestOffer(int index)
        {
            RiftRun run = _driver != null ? _driver.Run : null;
            if (run == null || index < 0) return;
            // Экран только открылся: клик или клавиша, пришедшие вместе с последним ударом, карточку не берут.
            if (ChoiceLocked) return;
            // Те же карточки на выборе арены: клик — путь, без вопросов.
            if (run.Phase == RunPhase.ChoosingRoute)
            {
                if (index < RouteChoices) _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseRoute1 + index));
                return;
            }
            if (run.Phase != RunPhase.ChoosingReward || index >= RiftRun.RewardChoices) return;
            if (run.ChoosingArtifact && run.GetOffer(index).Artifact == RunArtifact.None) return;
            if (run.ChoosingArtifact && run.Artifact != RunArtifact.None && _view != null && _view.ArtifactReplace != null)
            {
                _replaceOffer = index;
                if (_view.ArtifactOld != null) _view.ArtifactOld.texture = RunArtifactTexts.Icon(run.Artifact);
                if (_view.ArtifactNew != null) _view.ArtifactNew.texture = RunArtifactTexts.Icon(run.GetOffer(index).Artifact);
                RunHudView.SetText(_view.ArtifactReplaceText, "«" + RunArtifactTexts.Name(run.Artifact) + "» уйдёт, на его место встанет «"
                    + RunArtifactTexts.Name(run.GetOffer(index).Artifact) + "».");
                return;
            }
            _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseReward1 + index));
        }

        private void FillChoice(RiftRun run, bool letters)
        {
            // Выбор формы навыка (RunHud.Forms): свой экран из трёх отмеченных карточек.
            FormCardsOff();
            if (run.ChoosingForm) { FillFormChoice(run, letters); return; }
            if (run.ChoosingArtifact) { FillArtifactChoice(run, letters); return; }
            // «Второй взгляд» тратится раньше дара (RiftRun.TryFreeReroll) — подпись по тому, что уйдёт.
            bool canReroll = run.CanFreeReroll || run.CanRerollReward;
            RunHudView.SetActive(_view.Skip, canReroll);
            if(_view.Skip!=null && canReroll)_view.Skip.GetComponentInChildren<TMPro.TMP_Text>().text=run.CanFreeReroll?"Второй взгляд · перебросить":"Резервный план · перебросить";
            RunHudView.SetText(_view.ChoiceTitle, "Выбери награду");
            RunHudView.SetText(_view.ChoiceSubtitle, run.IsFinalLevel
                ? "Локация пройдена — последняя награда, дальше итоги"
                : "Арена зачищена · дальше арена " + (run.Depth + 1));
            // Подсказки клавиш — один формат на все окна: «[1] [2] [3] Выбрать   ·   [L] Уйти с добычей» (лист 5, UiKeyHint).
            RunHudView.SetText(_view.ChoiceHint, UiKeyHint.Join(
                UiKeyHint.Hint("выбрать", KeyLabel(0, letters), KeyLabel(1, letters), KeyLabel(2, letters)),
                UiKeyHint.Hint("уйти с добычей", GameKeyBindings.Label(GameAction.LeaveRift))));
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
                bool spring = rewardKind == RewardKind.Spring;
                int kindIcon = rewardKind == RewardKind.Ability ? 0 : rewardKind == RewardKind.Talent ? 1 : rewardKind == RewardKind.Item ? 2 : 3;
                // Родник: и в строке вида, и в круге — белый знак здоровья.
                Texture kindTexture = spring && _view.SpringIcon != null ? _view.SpringIcon
                    : kindIcon < _view.KindIcons.Length ? _view.KindIcons[kindIcon] : null;
                if (card.KindIcon != null && kindTexture != null)
                {
                    card.KindIcon.texture = kindTexture;
                    card.KindIcon.enabled = true;
                }
                RunHudView.SetText(card.Description, body);
                RunHudView.SetText(card.ValueLabel, valueLabel);
                RunHudView.SetText(card.Value, value);
                RunHudView.SetKey(card.Key, KeyLabel(i, letters));
                card.SetIcon(icon, spring);
                // Белый знак красится краской здоровья темы, как полоса здоровья героя.
                if (spring && card.Icon != null) card.Icon.color = UiTheme.Current.Get(UiTheme.Role.Health);
                // Вещь — её редкость (четыре цвета, как в палатке); способность и талант — «редкая» или обычная.
                WcRarity.Tier tier = run.GetOffer(i).Kind == RewardKind.Item ? WcRarity.FromItem((int)run.GetOffer(i).Item.Rarity)
                    : rare ? WcRarity.Tier.Rare : WcRarity.Tier.Common;
                if (card.Rarity != null) card.Rarity.Set(tier);
                // Сравнение «было → станет» на карточке и подсказка справа с ключевыми словами (RunHud.Reward).
                PolishOffer(i, card, run.GetOffer(i), run, body);
            }
        }

        /// <summary>Награда босса: три артефакта на этот забег, можно отказаться (концепт 2-artifact-sheet).</summary>
        private void FillArtifactChoice(RiftRun run, bool letters)
        {
            RunHudView.SetText(_view.ChoiceTitle, "Выбери артефакт");
            RunHudView.SetText(_view.ChoiceSubtitle, run.Artifact == RunArtifact.None
                ? "Награда босса · действует только в этом забеге"
                : "Награда босса · сейчас у тебя «" + RunArtifactTexts.Name(run.Artifact) + "» — слот один");
            RunHudView.SetText(_view.ChoiceHint, UiKeyHint.Join(
                UiKeyHint.Hint("выбрать", KeyLabel(0, letters), KeyLabel(1, letters), KeyLabel(2, letters)), "«Отказаться» — оставить как есть"));
            RunHudView.SetActive(_view.Skip, true);
            if(_view.Skip!=null)_view.Skip.GetComponentInChildren<TMPro.TMP_Text>().text="Отказаться";
            for (int i = 0; i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null) continue;
                RunArtifact artifact = i < RiftRun.RewardChoices ? run.GetOffer(i).Artifact : RunArtifact.None;
                bool shown = artifact != RunArtifact.None;
                card.gameObject.SetActive(shown);
                if (!shown) continue;
                RunHudView.SetText(card.Title, RunArtifactTexts.Name(artifact));
                RunHudView.SetText(card.Kind, "Уникальный артефакт");
                if (card.KindIcon != null && _view.KindIcons.Length > 3)
                {
                    card.KindIcon.texture = _view.KindIcons[3];
                    card.KindIcon.enabled = card.KindIcon.texture != null;
                }
                RunHudView.SetText(card.Description, RunArtifactTexts.Effect(artifact));
                // Активный — клавиша и перезарядка; пассивный (Обет Хранителя) срабатывает сам.
                int cooldown = Simulation.ArtifactCooldownTicks(artifact) / Simulation.TicksPerSecond;
                RunHudView.SetText(card.ValueLabel, cooldown > 0 ? "Клавиша " + GameKeyBindings.Label(GameAction.UseArtifact) : "Срабатывает");
                RunHudView.SetText(card.Value, cooldown > 0 ? "раз в " + cooldown + " с" : "сам, раз за забег");
                RunHudView.SetKey(card.Key, KeyLabel(i, letters));
                card.SetIcon(RunArtifactTexts.Icon(artifact), false);
                if (card.Rarity != null) card.Rarity.Set(WcRarity.Tier.Unique);
                PolishArtifact(i, card, RunArtifactTexts.Effect(artifact));
            }
        }

        /// <summary>
        /// Выбор следующей арены (ArenaFlow): те же три карточки, что у награды. Раньше этот экран был
        /// только в IMGUI, а Canvas его глушил — после награды игра стояла без экрана, и казалось, что
        /// переход на следующий разлом сломан (владелец 26 сентября). Клавиши те же, что у награды
        /// (TickDriver.LatchSlots), клик — RequestOffer, L — уйти с добычей.
        /// </summary>
        private void FillRoute(RiftRun run, bool letters)
        {
            FormCardsOff();
            PolishRoute();
            RunHudView.SetActive(_view.Skip, false);
            RunHudView.SetText(_view.ChoiceTitle, "Выбери следующую арену");
            RunHudView.SetText(_view.ChoiceSubtitle, RouteSubtitle(run));
            RunHudView.SetText(_view.ChoiceHint, UiKeyHint.Join(
                UiKeyHint.Hint("выбрать путь", KeyLabel(0, letters), KeyLabel(1, letters), KeyLabel(2, letters)),
                UiKeyHint.Hint("уйти с добычей", GameKeyBindings.Label(GameAction.LeaveRift))));
            for (int i = 0; i < _view.Offers.Length; i++)
            {
                RunOfferCard card = _view.Offers[i];
                if (card == null) continue;
                bool shown = i < RouteChoices;
                card.gameObject.SetActive(shown);
                if (!shown) continue;
                ArenaRouteOffer offer = run.GetRoute(i);
                RouteTexts(offer, out string title, out string kind, out string body, out string valueLabel, out string value);
                RunHudView.SetText(card.Title, title);
                RunHudView.SetText(card.Kind, kind);
                // В плашке — знак разлома: это арена, а не вещь.
                if (card.KindIcon != null && _view.KindIcons.Length > 3)
                {
                    card.KindIcon.texture = _view.KindIcons[3];
                    card.KindIcon.enabled = card.KindIcon.texture != null;
                }
                RunHudView.SetText(card.Description, body);
                RunHudView.SetText(card.ValueLabel, valueLabel);
                RunHudView.SetText(card.Value, value);
                RunHudView.SetKey(card.Key, KeyLabel(i, letters));
                card.SetIcon(RouteIcon(offer.Hard ? 2 : offer.Reward == ArenaReward.Shop ? 1 : 0), true);
                // Опасная арена — рамкой «редкой»: там бонус золота и враги сильнее.
                if (card.Rarity != null) card.Rarity.Set(offer.Hard ? WcRarity.Tier.Rare : WcRarity.Tier.Common);
            }
        }

        /// <summary>Знак пути: улучшение, магазин, опасная; у префаба до пересборки — значки вида награды.</summary>
        private Texture RouteIcon(int index)
        {
            if (_view.RouteIcons != null && index < _view.RouteIcons.Length && _view.RouteIcons[index] != null)
                return _view.RouteIcons[index];
            // «Улучшение» — свиток способности, не ромб кристалла усиления (контейнеры и знаки — не ромбы).
            int kind = index == 0 ? 0 : index == 1 ? 2 : 3;
            return kind < _view.KindIcons.Length ? _view.KindIcons[kind] : null;
        }

        private void FillReplace(RiftRun run, bool letters)
        {
            AbilityDefinition pending = PelagKit.PoolDefinition(run.PendingAbility);
            RunHudView.SetText(_view.ReplaceTitle, "Новая способность: " + (pending != null ? Capitalized(PlayerHud.AbilityName(pending.Id)) : "—"));
            RunHudView.SetText(_view.ReplaceSubtitle, "Панель полна. Замени одну из четырёх — её усиления пропадут — или разбери новую на золото.");
            RunHudView.SetText(_view.ReplaceHint, UiKeyHint.Join(
                UiKeyHint.Hint("заменить слот", KeyLabel(0, letters), KeyLabel(1, letters), KeyLabel(2, letters), KeyLabel(3, letters)),
                UiKeyHint.Hint("уйти с добычей", GameKeyBindings.Label(GameAction.LeaveRift))));
            RunHudView.SetText(_view.SalvageLabel, "Разобрать на " + run.SalvageGold + " золота");
            for (int slot = 0; slot < _view.Slots.Length; slot++)
            {
                RunSlotTile tile = _view.Slots[slot];
                if (tile == null) continue;
                AbilityDefinition current = run.Loadout.DefinitionAt(slot);
                Texture2D icon = current != null ? Icon(current.Id, run.Loadout.FormOf(run.Loadout.PoolIndexAt(slot))) : null;
                if (tile.Icon != null) { tile.Icon.texture = icon; tile.Icon.enabled = icon != null; }
                RunHudView.SetText(tile.Name, current != null ? Capitalized(PlayerHud.AbilityName(current.Id)) : "Пусто");
                int rank = run.Loadout.TalentRank(run.Loadout.PoolIndexAt(slot));
                RunHudView.SetText(tile.Note, rank > 0 ? "Усилений " + rank + " — пропадут" : "Усилений нет");
                // Что пропадёт — красным: замена стирает усиления.
                ThemeColor noteTint = tile.Note != null ? tile.Note.GetComponent<ThemeColor>() : null;
                if (noteTint != null) noteTint.SetRole(rank > 0 ? UiTheme.Role.Bad : UiTheme.Role.TextMuted);
                RunHudView.SetKey(tile.Key, KeyLabel(slot, letters));
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
                    // Цена способности — ресурс, игроку «Концентрация» (владелец 01.10); 136 px при 20 pt — до числа на 150 px.
                    valueLabel = "Концентрация";
                    value = definition.GetBase(AbilityStatType.LavidiumCost).ToInt().ToString();
                    return;
                }
                case RewardKind.Talent:
                {
                    if (!SabreTalents.TryLineOf(offer.PoolIndex, out SabreTalentLine line)) return;
                    AbilityDefinition definition = PelagKit.PoolDefinition(offer.PoolIndex);
                    // Иконка навыка — с его формой, если она уже выбрана (AbilityIconRules).
                    PelagForm form = run.Loadout.FormOf(offer.PoolIndex);
                    icon = definition != null ? Icon(definition.Id, form) : null;
                    if (offer.IsFormTalent)
                    {
                        // Таланты формы (номер от PelagForms.FormTalentBase) своих текстов ещё не имеют: таблица
                        // талантов линии на такой номер упала бы и сорвала экран награды.
                        title = PelagFormTexts.TalentName(form, offer.FormTalentIndex);
                        kind = "Талант формы · " + (run.Loadout.FormTalentCount(offer.PoolIndex) + 1) + " из " + PelagForms.FormTalentCount(form);
                        body = PelagFormTexts.TalentPending;
                        valueLabel = PelagFormTexts.Title(form);
                        rare = true;
                        return;
                    }
                    title = SabreTalentTexts.Name(line, offer.TalentIndex);
                    // Уровней у усилений нет (владелец, 24 сентября) — только «сколько уже взято у способности».
                    kind = "Усиление · " + (run.Loadout.TalentCount(offer.PoolIndex) + 1) + " из " + RunLoadout.MaxUpgrades;
                    body = SabreTalentTexts.Description(line, offer.TalentIndex);
                    valueLabel = Capitalized(SabreTalentTexts.LineName(line));
                    rare = true;
                    return;
                }
                case RewardKind.Spring:
                    // Родник (стадия 6): лечение сразу при выборе. Число — сколько вернётся
                    // именно сейчас (не больше недостающего), как в RiftRun.SpringHealAmount.
                    icon = _view.SpringIcon;
                    title = "Родник";
                    kind = "Лечение";
                    body = "Сразу возвращает " + offer.HealPercent + "% здоровья";
                    valueLabel = "Здоровье";
                    value = "+" + run.SpringHealAmount;
                    return;
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
            // Имя и картинка — из общего каталога основ (как в палатке), а не две заглушки на всё.
            title = ItemTexts.Name(offer.Item.BaseId);
            icon = ItemTexts.Icon(offer.Item.BaseId);
            kind = WcRarity.Name(WcRarity.FromItem((int)offer.Item.Rarity)) + " вещь · ур. " + offer.Item.ItemLevel;
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
