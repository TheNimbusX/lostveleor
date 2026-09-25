using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Состояние забега и центральный экран выбора награды.</summary>
    [RequireComponent(typeof(TickDriver))]
    public sealed partial class RunHud : MonoBehaviour
    {
        private TickDriver _driver;
        private GUIStyle _title;
        private GUIStyle _subtitle;
        private GUIStyle _body;
        private GUIStyle _eyebrow;
        private GUIStyle _cardButton;
        private Texture2D _white;
        private readonly GeneratedItem _itemBuffer = new GeneratedItem();

        // Цвета пака «Ночная акварель» (23 сентября 2026): тёмное стекло, светлый текст,
        // оранжевый акцент. Ими рисуются подписи в мире и меню над добычей; экраны
        // выбора и замены — на Canvas (RunHud.View), здесь они только запасные.
        private static readonly Color Panel = new Color32(0x11, 0x16, 0x20, 0xDC);
        private static readonly Color Card = new Color32(0x16, 0x1C, 0x28, 0xF5);
        private static readonly Color CardHover = new Color32(0x22, 0x2A, 0x3A, 0xFF);
        private static readonly Color Ink = new Color32(0xF4, 0xF7, 0xFB, 0xFF);
        private static readonly Color Coral = new Color32(0xFD, 0x74, 0x42, 0xFF);
        private static readonly Color Gold = new Color32(0xFA, 0x88, 0x3C, 0xFF);
        private static readonly Color Cyan = new Color32(0x3B, 0xF0, 0xF5, 0xFF);
        private static readonly Color Muted = new Color32(0xC9, 0xD2, 0xE0, 0xFF);

        private void Awake()
        {
            _driver = GetComponent<TickDriver>();
            BindView();
        }

        private void OnGUI()
        {
            if (_driver.GameplayPaused) return;
            if (_driver.Session == null || _driver.Session.Mode != GameMode.Rift) return;

            RiftRun run = _driver.Run;
            if (run == null) return;

            // Боевая строка — чистая отрисовка, и на не-Repaint события её
            // гонять незачем (см. PlayerHud). Экран награды пропускать нельзя:
            // там живые GUI.Button, и без Layout и событий мыши карточки
            // перестанут нажиматься.
            // Мини-меню над добычей — тоже живые кнопки, и события мыши ему нужны.
            int menuDrop = DropMenuTarget(run);
            if (run.Phase != RunPhase.ChoosingReward && run.Phase != RunPhase.ReplacingAbility && run.Phase != RunPhase.ChoosingRoute
                && menuDrop < 0 && Event.current.type != EventType.Repaint) return;

            EnsureStyles();

            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Rect safe = Screen.safeArea;
            float canvasWidth = Screen.width / scale;
            float canvasHeight = Screen.height / scale;
            float safeLeft = safe.xMin / scale;
            float safeRight = canvasWidth - safe.xMax / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                bool menuShown = false;
                // Метки мира и мини-меню добычи рисует пак (RunWorldView); здесь — только запасной вид.
                if (_world == null && (run.Phase == RunPhase.Clearing || run.Phase == RunPhase.SeekingExit))
                {
                    DrawRouteLandmarks(run, scale);
                    DrawDrops(run, scale);
                    menuShown = DrawDropMenu(run, menuDrop, scale);
                }
                if (Event.current.type == EventType.Repaint) _menuShown = menuShown;
                // Экраны и панели на Canvas (RunHudWc) рисует RunHud.View; здесь — только запасной вид.
                if (_view != null) { }
                else if (run.Phase == RunPhase.Clearing)
                    DrawCombatStatus(run, safeLeft);
                else if (run.Phase == RunPhase.SeekingExit)
                    DrawSeekingExit(safeLeft);
                else if (run.Phase == RunPhase.ChoosingRoute)
                    DrawRouteChoice(run, canvasWidth, canvasHeight, safeLeft, safeRight);
                else if (run.Phase == RunPhase.ChoosingReward)
                    DrawRewardChoice(run, canvasWidth, canvasHeight, safeLeft, safeRight);
                else if (run.Phase == RunPhase.ReplacingAbility)
                    DrawReplaceChoice(run, canvasWidth, canvasHeight, safeLeft, safeRight);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        // ---- добыча с элит ----

        private bool _menuShown, _menuReplacing;
        private int _menuDrop = -1;
        private Rect _menuScreenRect;
        private GUIStyle _menuButton, _menuCaption;

        /// <summary>
        /// Курсор над мини-меню добычи. TickDriver проверяет это до шага симуляции,
        /// чтобы клик по кнопке меню не стал ударом или приказом идти.
        /// </summary>
        public bool PointerOverDropMenu(Vector2 screenPosition)
            => _menuShown && _menuScreenRect.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y))
               || _world != null && _world.PointerOverMenu(screenPosition);

        /// <summary>Меню нужно только при полной панели: иначе способность поднимается сама.</summary>
        private static int DropMenuTarget(RiftRun run)
            => (run.Phase == RunPhase.Clearing || run.Phase == RunPhase.SeekingExit) && run.Loadout.IsFull
                ? run.NearestAbilityDrop(RiftRun.DropMenuRadius)
                : -1;

        private void DrawDrops(RiftRun run, float scale)
        {
            var camera = Camera.main;
            if (camera == null) return;
            for (int d = 0; d < run.DropCount; d++)
            {
                RunDrop drop = run.GetDrop(d);
                if (drop.Claimed) continue;
                AbilityDefinition definition = drop.Offer.Kind == RewardKind.Ability
                    ? PelagKit.PoolDefinition(drop.Offer.PoolIndex) : null;
                DrawLandmark(drop.Position, definition != null
                    ? "СПОСОБНОСТЬ · " + PlayerHud.AbilityName(definition.Id)
                    : "ПРЕДМЕТ · подойди", camera, scale, 0.9f);
            }
        }

        /// <summary>
        /// Мини-меню над способностью при полной панели. Бой не останавливается.
        /// Первый уровень — разобрать или заменить; «Заменить» раскрывает слоты.
        /// Передумал или отошёл — меню закрывается, способность лежит дальше:
        /// команда уходит в симуляцию только по выбору слота или разбору.
        /// </summary>
        private bool DrawDropMenu(RiftRun run, int index, float scale)
        {
            if (index != _menuDrop)
            {
                _menuDrop = index;
                _menuReplacing = false;
            }
            var camera = Camera.main;
            if (index < 0 || camera == null) return false;

            RunDrop drop = run.GetDrop(index);
            AbilityDefinition incoming = PelagKit.PoolDefinition(drop.Offer.PoolIndex);
            if (incoming == null) return false;
            Vector3 projected = camera.WorldToScreenPoint(
                new Vector3(drop.Position.X.ToFloat(), 1.6f, drop.Position.Y.ToFloat()));
            if (projected.z <= 0) return false;
            float cx = projected.x / scale;
            float cy = (Screen.height - projected.y) / scale;

            Rect panel = _menuReplacing
                ? new Rect(cx - 180f, cy - 150f, 360f, 132f)
                : new Rect(cx - 140f, cy - 96f, 280f, 84f);
            _menuScreenRect = new Rect(panel.x * scale, panel.y * scale, panel.width * scale, panel.height * scale);
            Fill(panel, Panel);
            Frame(panel, Ink, 1f);
            Fill(new Rect(panel.x, panel.y, panel.width, 4f), Coral);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, 20f),
                "ПАНЕЛЬ ПОЛНА · " + PlayerHud.AbilityName(incoming.Id), _eyebrow);

            if (!_menuReplacing)
            {
                float width = (panel.width - 30f) * 0.5f;
                if (GUI.Button(new Rect(panel.x + 10f, panel.y + 34f, width, 40f),
                        "РАЗОБРАТЬ +" + run.SalvageGold, _menuButton))
                {
                    GameSound.Play("salvage", .8f);
                    _driver.QueueRunCommand(RunCommand.PickupSalvage);
                }
                if (GUI.Button(new Rect(panel.x + 20f + width, panel.y + 34f, width, 40f), "ЗАМЕНИТЬ…", _menuButton))
                    _menuReplacing = true;
                return true;
            }

            const float gap = 6f;
            float tile = (panel.width - 20f - gap * (RunLoadout.Slots - 1)) / RunLoadout.Slots;
            for (int slot = 0; slot < RunLoadout.Slots; slot++)
            {
                Rect box = new Rect(panel.x + 10f + slot * (tile + gap), panel.y + 32f, tile, 64f);
                if (GUI.Button(box, GUIContent.none, _menuButton))
                {
                    _driver.QueueRunCommand((RunCommand)((int)RunCommand.PickupReplaceSlot1 + slot));
                    _menuReplacing = false;
                }
                AbilityDefinition current = run.Loadout.DefinitionAt(slot);
                Texture2D icon = current != null ? Icon(current.Id) : null;
                if (icon != null)
                    GUI.DrawTexture(new Rect(box.center.x - 16f, box.y + 4f, 32f, 32f), icon, ScaleMode.ScaleToFit);
                int rank = run.Loadout.TalentRank(run.Loadout.PoolIndexAt(slot));
                GUI.Label(new Rect(box.x + 2f, box.y + 40f, box.width - 4f, 20f),
                    (slot + 1) + (rank > 0 ? " · −" + rank + " тал." : ""), _menuCaption);
            }
            if (GUI.Button(new Rect(panel.x + 10f, panel.y + 102f, panel.width - 20f, 24f), "НАЗАД", _menuButton))
                _menuReplacing = false;
            return true;
        }

        private void DrawCombatStatus(RiftRun run, float safeLeft)
        {
            Rect panel = new Rect(safeLeft + 18f, 18f, 300f, 88f);
            Fill(panel, Panel);
            Fill(new Rect(panel.x, panel.y, 4f, panel.height), Coral);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 6f, 205f, 26f),
                "РАЗЛОМ  " + run.Depth + (run.TotalLevels > 0 ? "/" + run.TotalLevels : ""), _title);
            int fights = 0, cleared = 0;
            if (run.Encounters != null)
                for (int e = 0; e < run.Encounters.Count; e++)
                {
                    if (run.Encounters.Get(e).Role == EncounterRole.RewardBranch) continue;
                    fights++;
                    if (run.Encounters.Alive(e, run.Sim.Entities) == 0) cleared++;
                }
            GUI.Label(new Rect(panel.x + 16f, panel.y + 34f, 280f, 20f),
                fights > 0 ? $"ВСТРЕЧИ: {cleared}/{fights} · ЦЕЛЕЙ: {run.CountRequiredEnemies()}"
                    : "ЦЕЛЕЙ: " + run.CountRequiredEnemies(), _subtitle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 60f, 280f, 22f),
                run.ArenaFlow ? (run.CurrentRoute.Hard ? "ОПАСНАЯ АРЕНА" : run.CurrentRoute.Reward == ArenaReward.Shop
                    ? "МАГАЗИН · СКОРО" : "УЛУЧШЕНИЕ") + $" · золото: {run.Gold}"
                    : $"Тайники: {run.BranchesClaimed}/{run.Map.RewardBranchCount} · золото забега: {run.Gold}", _subtitle);
            if (run.BossId >= 0 && run.Sim.Entities.Alive[run.BossId])
            {
                int id = run.BossId;
                var bar = new Rect(panel.x, panel.yMax + 8, 360, 50);
                Fill(bar, Panel);
                GUI.Label(new Rect(bar.x + 8, bar.y + 3, 344, 24),
                    run.BossEnraged ? "ХРАНИТЕЛЬ ЛУГОВ · ЯРОСТЬ" : "ХРАНИТЕЛЬ ЛУГОВ · БОСС", _subtitle);
                Fill(new Rect(bar.x + 8, bar.y + 30, 344, 10), Ink);
                Fill(new Rect(bar.x + 8, bar.y + 30,
                    344f * run.Sim.Entities.Health[id] / run.Sim.Entities.MaxHealth[id], 10), Coral);
            }
        }

        private void DrawSeekingExit(float safeLeft)
        {
            Rect panel = new Rect(safeLeft + 18f, 18f, 260f, 62f);
            Fill(panel, Panel);
            Fill(new Rect(panel.x, panel.y, 4f, panel.height), Gold);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 6f, 235f, 26f),
                "ПУТЬ ОТКРЫТ", _title);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 34f, 235f, 20f),
                "Подойди к порталу", _subtitle);
        }

        private void DrawRouteLandmarks(RiftRun run, float scale)
        {
            var camera = Camera.main;
            if (camera == null || run.Map.Routes == null) return;
            DrawLandmark(run.Map.EntryPoint, "ВХОД", camera, scale);
            if (run.Encounters != null)
                for (int i = 1; i < run.Sim.Entities.Count; i++)
                    if (run.Encounters.IsElite(i) && run.Sim.Entities.Alive[i])
                        DrawLandmark(run.Sim.Entities.Position[i], i == run.BossId ? "ХРАНИТЕЛЬ ЛУГОВ" : "УСИЛЕННЫЙ ХРАНИТЕЛЬ", camera, scale, 3.2f);
            for (int e = 0; e < run.Map.ExitCount; e++)
                DrawLandmark(run.Map.ExitPoint(e), run.Phase == RunPhase.SeekingExit
                    ? "ВЫХОД · подойди" : "ВЫХОД · победи цели", camera, scale);
            for (int b = 0; b < run.Map.RewardBranchCount; b++)
            {
                string label = run.IsBranchClaimed(b) ? "ТАЙНИК · собран"
                    : run.BranchGuardsAlive(b) > 0 ? $"ТАЙНИК · охрана {run.BranchGuardsAlive(b)}"
                    : "ТАЙНИК · подойди за предметом";
                DrawLandmark(run.Map.CenterOf(run.Map.GetRewardBranch(b)), label, camera, scale);
            }
        }

        private void DrawLandmark(FixVec2 point, string text, Camera camera, float scale, float height = 0.6f)
        {
            var projected = camera.WorldToScreenPoint(new Vector3(point.X.ToFloat(), height, point.Y.ToFloat()));
            if (projected.z <= 0 || projected.x < 0 || projected.x > Screen.width ||
                projected.y < 0 || projected.y > Screen.height) return;
            var box = new Rect(projected.x / scale - 110, (Screen.height - projected.y) / scale - 26, 220, 24);
            Fill(box, Panel);
            GUI.Label(new Rect(box.x + 6, box.y + 2, box.width - 12, 20), text, _subtitle);
        }

        private void DrawRouteChoice(RiftRun run, float canvasWidth, float canvasHeight,
            float safeLeft, float safeRight)
        {
            float width = Mathf.Min(920f, canvasWidth - safeLeft - safeRight - 32f);
            float height = Mathf.Min(440f, canvasHeight - 34f);
            var panel = new Rect((canvasWidth - width) * .5f, (canvasHeight - height) * .5f, width, height);
            Fill(panel, Panel); Frame(panel, Ink, 1f);
            Fill(new Rect(panel.x, panel.y, width, 5f), Cyan);
            GUI.Label(new Rect(panel.x + 28, panel.y + 20, width - 56, 30), "ВЫБЕРИ СЛЕДУЮЩУЮ АРЕНУ", _title);
            GUI.Label(new Rect(panel.x + 28, panel.y + 54, width - 56, 26),
                "Разлом " + (run.Depth + 1) + (run.TotalLevels > 0 ? " / " + run.TotalLevels : "")
                + " · награда ждёт после зачистки", _subtitle);
            float cardWidth = (width - 80) / 3;
            for (int i = 0; i < 3; i++)
            {
                var offer = run.GetRoute(i);
                var card = new Rect(panel.x + 28 + i * (cardWidth + 12), panel.y + 90, cardWidth, height - 142);
                bool selected = GUI.Button(card, GUIContent.none, _cardButton);
                Fill(card, card.Contains(Event.current.mousePosition) ? CardHover : Card);
                Frame(card, offer.Hard ? Coral : Cyan, 2);
                string size = offer.Size == 2 ? "Малая" : offer.Size == 3 ? "Средняя" : "Большая";
                GUI.Label(new Rect(card.x + 14, card.y + 14, card.width - 28, 28),
                    (i + 1) + ". " + (offer.Reward == ArenaReward.Shop ? "МАГАЗИН · СКОРО" : "УЛУЧШЕНИЕ"), _eyebrow);
                string text = size + " лесная арена\n" + (offer.Hard ? "Повышенная сложность" : "Обычная сложность")
                    + "\n\n" + (offer.Reward == ArenaReward.Shop
                        ? "Место для будущего торговца.\nПока — обычная награда за бой."
                        : "Новая способность или талант\nпосле победы.")
                    + (offer.Hard ? "\n\nВраги: +25% здоровья и урона\nБонус: +" + offer.BonusGold + " золота за зачистку" : "");
                GUI.Label(new Rect(card.x + 14, card.y + 48, card.width - 28, card.height - 54), text, _body);
                if (selected)
                    _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseRoute1 + i));
            }
            GUI.Label(new Rect(panel.x + 28, panel.yMax - 35, width - 56, 24),
                (GameUserSettings.AbilityRowUsesLetters ? "Q W E" : "1 2 3") + "  выбрать путь     L  уйти с добычей", _subtitle);
        }

        private void DrawRewardChoice(RiftRun run, float canvasWidth, float canvasHeight,
            float safeLeft, float safeRight)
        {
            float panelWidth = Mathf.Min(920f, canvasWidth - safeLeft - safeRight - 32f);
            float panelHeight = Mathf.Min(410f, canvasHeight - 34f);
            Rect panel = new Rect((canvasWidth - panelWidth) * 0.5f, (canvasHeight - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            Fill(panel, Panel);
            Frame(panel, Ink, 1f);
            Fill(new Rect(panel.x, panel.y, panel.width, 5f), Coral);

            GUI.Label(new Rect(panel.x + 28f, panel.y + 20f, panel.width - 56f, 30f),
                "РАЗЛОМ ЗАЧИЩЕН", _title);
            GUI.Label(new Rect(panel.x + 28f, panel.y + 51f, panel.width - 56f, 22f),
                run.IsFinalLevel ? "Локация пройдена — выбери последнюю награду и перейди к итогам"
                    : "Выбери награду — дальше разлом " + (run.Depth + 1)
                        + (run.TotalLevels > 0 ? "/" + run.TotalLevels : ""), _subtitle);

            float gap = 12f;
            float cardsTop = panel.y + 88f;
            float cardsHeight = panel.height - 136f;
            float cardWidth = (panel.width - 56f - gap * 2f) / RiftRun.RewardChoices;
            for (int i = 0; i < RiftRun.RewardChoices; i++)
            {
                Rect card = new Rect(panel.x + 28f + i * (cardWidth + gap), cardsTop,
                    cardWidth, cardsHeight);
                DrawOfferCard(card, i, run.GetOffer(i), run);
                if (GUI.Button(card, GUIContent.none, _cardButton))
                    _driver.QueueRunCommand((RunCommand)((int)RunCommand.ChooseReward1 + i));
            }

            GUI.Label(new Rect(panel.x + 28f, panel.yMax - 35f, panel.width - 56f, 22f),
                (GameUserSettings.AbilityRowUsesLetters ? "Q W E" : "1 2 3")
                + "  выбрать награду     L  уйти с добычей", _subtitle);
        }

        /// <summary>
        /// Новая способность при полной панели: четыре слота на замену и разбор.
        /// Таланты заменённой способности пропадают — это написано прямо на плитке.
        /// </summary>
        private void DrawReplaceChoice(RiftRun run, float canvasWidth, float canvasHeight,
            float safeLeft, float safeRight)
        {
            float panelWidth = Mathf.Min(920f, canvasWidth - safeLeft - safeRight - 32f);
            float panelHeight = Mathf.Min(330f, canvasHeight - 34f);
            Rect panel = new Rect((canvasWidth - panelWidth) * 0.5f, (canvasHeight - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            Fill(panel, Panel);
            Frame(panel, Ink, 1f);
            Fill(new Rect(panel.x, panel.y, panel.width, 5f), Coral);

            AbilityDefinition pending = PelagKit.PoolDefinition(run.PendingAbility);
            GUI.Label(new Rect(panel.x + 28f, panel.y + 20f, panel.width - 56f, 30f),
                "НОВАЯ СПОСОБНОСТЬ: " + (pending != null ? PlayerHud.AbilityName(pending.Id) : "—"), _title);
            GUI.Label(new Rect(panel.x + 28f, panel.y + 51f, panel.width - 56f, 22f),
                "Панель полна. Замени одну из четырёх — её усиления пропадут — или разбери новую на золото.", _subtitle);

            float gap = 12f;
            float top = panel.y + 88f;
            const float tileHeight = 120f;
            float tileWidth = (panel.width - 56f - gap * (RunLoadout.Slots - 1)) / RunLoadout.Slots;
            for (int slot = 0; slot < RunLoadout.Slots; slot++)
            {
                Rect tile = new Rect(panel.x + 28f + slot * (tileWidth + gap), top, tileWidth, tileHeight);
                if (GUI.Button(tile, GUIContent.none, _cardButton))
                    _driver.QueueRunCommand((RunCommand)((int)RunCommand.ReplaceSlot1 + slot));
                Frame(tile, Coral, 1f);
                AbilityDefinition current = run.Loadout.DefinitionAt(slot);
                Texture2D icon = current != null ? Icon(current.Id) : null;
                if (icon != null) GUI.DrawTexture(new Rect(tile.x + 12f, tile.y + 12f, 48f, 48f), icon, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(tile.x + 12f, tile.y + 66f, tile.width - 24f, 22f),
                    (slot + 1) + ". " + (current != null ? PlayerHud.AbilityName(current.Id) : "ПУСТО"), _eyebrow);
                int rank = run.Loadout.TalentRank(run.Loadout.PoolIndexAt(slot));
                GUI.Label(new Rect(tile.x + 12f, tile.y + 90f, tile.width - 24f, 20f),
                    rank > 0 ? "усилений " + rank + " — пропадут" : "усилений нет", _subtitle);
            }

            Rect salvage = new Rect(panel.x + 28f, top + tileHeight + 16f, panel.width - 56f, 40f);
            if (GUI.Button(salvage, GUIContent.none, _cardButton))
            {
                GameSound.Play("salvage", .8f);
                _driver.QueueRunCommand(RunCommand.SalvageAbility);
            }
            Fill(salvage, Gold);
            GUI.Label(salvage, "РАЗОБРАТЬ НА " + run.SalvageGold + " ЗОЛОТА", _cardButton);

            GUI.Label(new Rect(panel.x + 28f, panel.yMax - 35f, panel.width - 56f, 22f),
                (GameUserSettings.AbilityRowUsesLetters ? "Q W E R" : "1 2 3 4")
                + "  заменить слот     L  уйти с добычей", _subtitle);
        }

        private readonly System.Collections.Generic.Dictionary<int, Texture2D> _icons =
            new System.Collections.Generic.Dictionary<int, Texture2D>();

        private Texture2D Icon(int definitionId)
        {
            if (_icons.TryGetValue(definitionId, out Texture2D texture)) return texture;
            string file = PlayerHud.IconFile(definitionId);
            texture = file != null ? Resources.Load<Texture2D>("UI/Abilities/" + file) : null;
            _icons[definitionId] = texture;
            return texture;
        }

        private void DrawAbilityCard(Rect card, in RewardOffer offer, RiftRun run)
        {
            AbilityDefinition definition = PelagKit.PoolDefinition(offer.PoolIndex);
            if (definition == null) return;
            Texture2D icon = Icon(definition.Id);
            if (icon != null) GUI.DrawTexture(new Rect(card.x + 16f, card.y + 60f, 64f, 64f), icon, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(card.x + 92f, card.y + 64f, card.width - 108f, 56f), PlayerHud.AbilityName(definition.Id), _title);
            GUI.Label(new Rect(card.x + 16f, card.y + 134f, card.width - 32f, 20f),
                "ЛАВИДИЙ " + definition.GetBase(AbilityStatType.LavidiumCost).ToInt(), _eyebrow);
            string text = PlayerHud.AbilityDescription(definition.Id);
            if (run.Loadout.IsFull)
                text += "\n\nПанель полна: придётся заменить способность или разобрать эту на " + run.SalvageGold + " золота.";
            GUI.Label(new Rect(card.x + 16f, card.y + 160f, card.width - 32f, card.height - 174f), text, _body);
        }

        private void DrawTalentCard(Rect card, in RewardOffer offer)
        {
            if (!SabreTalents.TryLineOf(offer.PoolIndex, out SabreTalentLine line)) return;
            AbilityDefinition definition = PelagKit.PoolDefinition(offer.PoolIndex);
            Texture2D icon = definition != null ? Icon(definition.Id) : null;
            if (icon != null) GUI.DrawTexture(new Rect(card.x + 16f, card.y + 60f, 48f, 48f), icon, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(card.x + 76f, card.y + 60f, card.width - 92f, 22f),
                SabreTalentTexts.LineName(line).ToUpperInvariant(), _eyebrow);
            GUI.Label(new Rect(card.x + 76f, card.y + 82f, card.width - 92f, 30f),
                SabreTalentTexts.Name(line, offer.TalentIndex), _title);
            GUI.Label(new Rect(card.x + 16f, card.y + 124f, card.width - 32f, card.height - 138f),
                SabreTalentTexts.Description(line, offer.TalentIndex), _body);
        }

        private void DrawOfferCard(Rect card, int index, in RewardOffer offer, RiftRun run)
        {
            Color accent = offer.Kind == RewardKind.Item ? Gold
                : offer.Kind == RewardKind.StatBoost || offer.Kind == RewardKind.Talent ? Cyan : Coral;
            Fill(card, Card);
            Frame(card, accent, 1f);
            Fill(new Rect(card.x, card.y, card.width, 4f), accent);

            Rect badge = new Rect(card.x + 14f, card.y + 14f, 34f, 34f);
            Fill(badge, accent);
            GUI.Label(badge, (index + 1).ToString(), _cardButton);

            string kind = offer.Kind == RewardKind.Item ? "ПРЕДМЕТ"
                : offer.Kind == RewardKind.Ability ? "СПОСОБНОСТЬ"
                : offer.Kind == RewardKind.Talent ? "УСИЛЕНИЕ"
                : offer.Kind == RewardKind.StatBoost ? "СТАТ"
                : "УЗЕЛ СПОСОБНОСТИ";
            GUI.Label(new Rect(card.x + 58f, card.y + 15f, card.width - 72f, 18f), kind, _eyebrow);

            if (offer.Kind == RewardKind.Ability)
            {
                DrawAbilityCard(card, offer, run);
                return;
            }

            if (offer.Kind == RewardKind.Talent)
            {
                DrawTalentCard(card, offer);
                return;
            }

            if (offer.Kind == RewardKind.StatBoost)
            {
                GUI.Label(new Rect(card.x + 16f, card.y + 72f, card.width - 32f, 54f),
                    StatTitle(offer.Stat), _title);
                GUI.Label(new Rect(card.x + 16f, card.y + 132f, card.width - 32f, card.height - 150f),
                    (offer.Op == ModifierOp.Flat ? "+" + offer.Value.ToFloat().ToString("0.##")
                        : "+" + (offer.Value.ToFloat() * 100f).ToString("0.#") + "%") + " к характеристике", _body);
                return;
            }

            if (!ItemGenerator.Generate(offer.Item, run.Items, _itemBuffer))
            {
                GUI.Label(new Rect(card.x + 16f, card.y + 76f, card.width - 32f, 70f),
                    "Предмет не найден", _body);
                return;
            }

            string itemName = _itemBuffer.Category == ItemCategory.Weapon ? "РЖАВЫЙ МЕЧ" : "КОЖАНАЯ КУРТКА";
            GUI.Label(new Rect(card.x + 16f, card.y + 72f, card.width - 32f, 32f), itemName, _title);
            GUI.Label(new Rect(card.x + 16f, card.y + 110f, card.width - 32f, 20f),
                "УР. " + offer.Item.ItemLevel + "  /  " + offer.Item.Rarity.ToString().ToUpperInvariant(), _eyebrow);
            string details = ItemDetails(_itemBuffer);
            GUI.Label(new Rect(card.x + 16f, card.y + 144f, card.width - 32f, card.height - 158f),
                details, _body);
        }

        private static string ItemDetails(GeneratedItem item)
        {
            string text = item.HasImplicit
                ? StatTitle(item.ImplicitStat) + "  " + ValueText(item.ImplicitOp, item.ImplicitValue)
                : string.Empty;
            for (int i = 0; i < item.AffixCount; i++)
            {
                RolledAffix affix = item.GetAffix(i);
                if (text.Length > 0) text += "\n";
                bool percentage = affix.Stat == StatType.AbilitySpeed || affix.Stat == StatType.CooldownRecovery;
                text += StatTitle(affix.Stat) + "  " + ValueText(percentage ? ModifierOp.Increased : affix.Op, affix.Value);
            }
            return text.Length == 0 ? "Без дополнительных свойств" : text;
        }

        private static string ValueText(ModifierOp op, Fix64 value)
            => op == ModifierOp.Flat ? "+" + value.ToFloat().ToString("0.##")
                : "+" + (value.ToFloat() * 100f).ToString("0.#") + "%";

        private static string StatTitle(StatType stat)
        {
            switch (stat)
            {
                case StatType.MaxHealth: return "МАКС. ЗДОРОВЬЕ";
                case StatType.Damage: return "УРОН";
                case StatType.AttackSpeed: return "СКОРОСТЬ АТАКИ";
                case StatType.AbilitySpeed: return "СКОРОСТЬ ИСПОЛНЕНИЯ";
                case StatType.CooldownRecovery: return "ВОССТАНОВЛЕНИЕ НАВЫКОВ";
                case StatType.LavidiumRegen: return "ЛАВИДИЙ В СЕКУНДУ";
                case StatType.MoveSpeed: return "СКОРОСТЬ ДВИЖЕНИЯ";
                case StatType.CritChance: return "ШАНС КРИТА";
                case StatType.CritMultiplier: return "МНОЖИТЕЛЬ КРИТА";
                case StatType.Armor: return "БРОНЯ";
                case StatType.FireResist: return "СОПРОТИВЛЕНИЕ ОГНЮ";
                default: return "ХАРАКТЕРИСТИКА";
            }
        }

        private void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
        }

        private void Frame(Rect rect, Color color, float width)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, width), color);
            Fill(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Fill(new Rect(rect.x, rect.y, width, rect.height), color);
            Fill(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private void EnsureStyles()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            if (_title != null) return;

            _title = new GUIStyle(GameTypography.Label)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
            };
            _title.normal.textColor = Ink;
            _subtitle = new GUIStyle(_title) { fontSize = 13, fontStyle = FontStyle.Normal };
            _subtitle.normal.textColor = Muted;
            _body = new GUIStyle(_subtitle) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft };
            _body.normal.textColor = Muted;
            _eyebrow = new GUIStyle(_subtitle) { fontSize = 11, fontStyle = FontStyle.Bold };
            _eyebrow.normal.textColor = Coral;
            _cardButton = new GUIStyle(GameTypography.Button)
            {
                fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            _cardButton.normal.background = MakeTexture(new Color(0f, 0f, 0f, 0f));
            _cardButton.hover.background = MakeTexture(CardHover);
            _cardButton.active.background = MakeTexture(new Color(0.18f, 0.20f, 0.27f, 1f));
            _cardButton.normal.textColor = Color.white;

            _menuButton = new GUIStyle(GameTypography.Button)
            {
                fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true,
            };
            _menuButton.normal.background = MakeTexture(CardHover);
            _menuButton.hover.background = MakeTexture(Coral);
            _menuButton.active.background = MakeTexture(new Color32(0xDF, 0x5E, 0x30, 0xFF));
            _menuButton.normal.textColor = _menuButton.hover.textColor = _menuButton.active.textColor
                = Ink;
            _menuCaption = new GUIStyle(_eyebrow) { alignment = TextAnchor.MiddleCenter };
            _menuCaption.normal.textColor = Ink;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
