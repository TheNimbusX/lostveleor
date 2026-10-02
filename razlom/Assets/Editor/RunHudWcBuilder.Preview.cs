using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;

namespace Game.EditorTools
{
    /// <summary>Кадры экранов забега без запуска игры, поверх кадра игры (ART/no-ui.png), с примером данных.</summary>
    public static partial class RunHudWcBuilder
    {
        public enum Shot { Choice, Replace, Status, Summary, Artifact, Route, Form }

        public static string Capture(string outPath, Shot shot)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst => Preview(inst, shot));
        }

        /// <summary>Картинка артефакта для кадра редактора — та же, что в игре (Resources/UI/Artifacts).</summary>
        static Texture2D ArtifactIcon(Game.Sim.RunArtifact artifact) => RunArtifactTexts.Icon(artifact);

        static Texture2D Ability(string key) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Abilities/Icon_" + key + ".png");

        static void Preview(GameObject inst, Shot shot)
        {
            var view = inst.GetComponent<RunHudView>();
            var root = (RectTransform)inst.transform;
            if (System.IO.File.Exists("../ART/no-ui.png"))
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(System.IO.File.ReadAllBytes("../ART/no-ui.png"));
                var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                back.texture = tex;
                back.uvRect = new Rect(.035f, 0f, .965f, .955f);
                back.transform.SetSiblingIndex(0);
            }
            view.Choice.gameObject.SetActive(shot == Shot.Choice || shot == Shot.Artifact || shot == Shot.Route || shot == Shot.Form);
            view.ArtifactReplace.gameObject.SetActive(shot == Shot.Artifact);
            view.Skip.gameObject.SetActive(shot == Shot.Artifact);
            view.Replace.gameObject.SetActive(shot == Shot.Replace);
            view.Status.gameObject.SetActive(shot == Shot.Status);
            view.Boss.gameObject.SetActive(shot == Shot.Status);
            // Таймер выживания — на том же кадре, под панелью (в игре с боссом не встречается).
            if (view.Survival != null) view.Survival.gameObject.SetActive(shot == Shot.Status);
            view.Summary.gameObject.SetActive(shot == Shot.Summary);
            if (shot == Shot.Summary)
            {
                view.SummaryTitle.text = "Гибель";
                view.OutcomeIcon.texture = view.OutcomeIcons[0];
                // Краски гибели — те же, что ставит RunHud.RefreshSummary (префаб до пересборки их не держит).
                ThemeColor iconTint = view.OutcomeIcon.GetComponent<ThemeColor>();
                if (iconTint != null) iconTint.SetRole(UiTheme.Role.Health, .78f);
                ThemeColor haloTint = view.OutcomeHalo != null ? view.OutcomeHalo.GetComponent<ThemeColor>() : null;
                if (haloTint != null) haloTint.SetRole(UiTheme.Role.Health, .12f);
                view.SummaryTitle.GetComponent<ThemeColor>().SetRole(UiTheme.Role.Health);
                view.SummarySubtitle.text = "Всё найденное в забеге осталось в Разломе";
                int[] values = { 3, 4, 2, 118 };
                for (int i = 0; i < 4; i++) view.SummaryValues[i].text = values[i].ToString();
                view.SummaryLoss.text = "Потеряно со смертью: предметов 3, золота 64";
                PreviewStatsSummary(view);
            }

            if (shot == Shot.Route)
            {
                // Выбор следующей арены после награды: обычная с улучшением, магазин, опасная с бонусом.
                // Тексты карточек — из игры (RunHud.RouteTexts), чтобы кадр не расходился с экраном.
                view.ChoiceTitle.text = "Выбери следующую арену";
                view.ChoiceSubtitle.text = "Арена 3 · награда — после зачистки";
                view.ChoiceHint.text = UiKeyHint.Join(UiKeyHint.Hint("выбрать путь", "1", "2", "3"), UiKeyHint.Hint("уйти с добычей", "L"));
                Game.Sim.ArenaRouteOffer[] routes =
                {
                    new Game.Sim.ArenaRouteOffer(Game.Sim.ArenaReward.Upgrade, 3, false, 0),
                    new Game.Sim.ArenaRouteOffer(Game.Sim.ArenaReward.Shop, 2, false, 0),
                    new Game.Sim.ArenaRouteOffer(Game.Sim.ArenaReward.Upgrade, 4, true, 70),
                };
                for (int i = 0; i < routes.Length; i++)
                {
                    RunOfferCard card = view.Offers[i];
                    RunHud.RouteTexts(routes[i], out string title, out string kind, out string body, out string valueLabel, out string value);
                    card.Title.text = title;
                    card.Kind.text = kind;
                    card.KindIcon.texture = view.KindIcons[3];
                    card.Description.text = body;
                    card.ValueLabel.text = valueLabel;
                    card.Value.text = value;
                    card.Key.text = (i + 1).ToString();
                    card.SetIcon(i < view.RouteIcons.Length ? view.RouteIcons[i] : null, true);
                    card.Rarity.Set(routes[i].Hard ? WcRarity.Tier.Rare : WcRarity.Tier.Common);
                }
            }

            if (shot == Shot.Choice)
            {
                view.ChoiceSubtitle.text = "Арена зачищена · дальше арена 3";
                Offer(view.Offers[0], "Рассекающий удар", "Способность", "Сильный удар саблей сверху перед собой. Бьёт одну цель и не двигает героя.", "Концентрация", "30", "Cleave", false, "1");
                view.Offers[0].KindIcon.texture = view.KindIcons[0];
                Offer(view.Offers[1], "Длинный клинок", "Усиление · 3 из 8", "Дальность Рассекающего удара +50%. Удар достаёт врагов за спиной первого.", "Рассекающий удар", "", "Cleave", true, "2");
                view.Offers[1].KindIcon.texture = view.KindIcons[1];
                Offer(view.Offers[2], "Кожаная куртка", "Обычная вещь · ур. 4", "Броня +12 · Скорость движения +4% · Сопротивление огню +8%", "", "", null, false, "3");
                view.Offers[2].KindIcon.texture = view.KindIcons[2];
                PreviewRewardPolish(view);
            }
            if (shot == Shot.Form) PreviewForm(view);
            if (shot == Shot.Artifact)
            {
                // Награда босса: три артефакта набора акта I, у героя уже есть Обет Хранителя — открыт вопрос о замене.
                var held = Game.Sim.RunArtifact.GuardianVow;
                Game.Sim.RunArtifact[] offers = { Game.Sim.RunArtifact.SunSeal, Game.Sim.RunArtifact.Hourglass, Game.Sim.RunArtifact.CrimsonHeart };
                view.ChoiceTitle.text = "Выбери артефакт";
                view.ChoiceSubtitle.text = "Награда босса · сейчас у тебя «" + RunArtifactTexts.Name(held) + "» — слот один";
                view.ChoiceHint.text = UiKeyHint.Join(UiKeyHint.Hint("выбрать", "1", "2", "3"), "«Отказаться» — оставить как есть");
                for (int i = 0; i < 3; i++)
                {
                    RunOfferCard card = view.Offers[i];
                    int cooldown = Game.Sim.Simulation.ArtifactCooldownTicks(offers[i]) / Game.Sim.Simulation.TicksPerSecond;
                    card.Title.text = RunArtifactTexts.Name(offers[i]);
                    card.Kind.text = "Уникальный артефакт";
                    card.KindIcon.texture = view.KindIcons[3];
                    card.Description.text = RunArtifactTexts.Effect(offers[i]);
                    card.ValueLabel.text = "Клавиша F";
                    card.Value.text = "раз в " + cooldown + " с";
                    card.Key.text = (i + 1).ToString();
                    card.Icon.texture = ArtifactIcon(offers[i]);
                    card.Icon.enabled = true;
                    card.Rarity.Set(WcRarity.Tier.Unique);
                }
                view.ArtifactReplace.alpha = 1f;
                view.ArtifactOld.texture = ArtifactIcon(held);
                view.ArtifactNew.texture = ArtifactIcon(offers[1]);
                view.ArtifactReplaceText.text = "«" + RunArtifactTexts.Name(held) + "» уйдёт, на его место встанет «" + RunArtifactTexts.Name(offers[1]) + "».";
            }
            if (shot == Shot.Replace)
            {
                view.ReplaceTitle.text = "Новая способность: Взрывная смесь";
                view.ReplaceSubtitle.text = "Панель полна. Замени одну из четырёх — её усиления пропадут — или разбери новую на золото.";
                string[] names = { "Вихрь", "Рассекающий удар", "Шквал", "Абордаж" };
                string[] icons = { "Whirlwind", "Cleave", "Squall", "AnchorLeap" };
                string[] notes = { "Усилений 2 — пропадут", "Усилений нет", "Усилений 1 — пропадут", "Усилений нет" };
                for (int i = 0; i < 4; i++)
                {
                    view.Slots[i].Name.text = names[i];
                    view.Slots[i].Note.text = notes[i];
                    // Цвет строки — как у RunHud.FillReplace: усиления пропадут — красным.
                    ThemeColor noteTint = view.Slots[i].Note.GetComponent<ThemeColor>();
                    if (noteTint != null) noteTint.SetRole(notes[i].EndsWith("пропадут") ? UiTheme.Role.Bad : UiTheme.Role.TextMuted);
                    view.Slots[i].Icon.texture = Ability(icons[i]);
                    view.Slots[i].Icon.enabled = true;
                }
                view.SalvageLabel.text = "Разобрать на 40 золота";
            }
            if (shot == Shot.Status)
            {
                view.StatusTitle.text = "Арена 2";
                view.StatusLine.text = "Волна 2 / 3 · целей 14";
                // Золото — в строке добычи под панелью (RunHud.FillStatus с v1).
                view.StatusExtra.text = view.Loot != null ? "Тайники 1 / 2" : "Тайники 0 / 2 · золото 36";
                PreviewLoot(view);
                if (view.SurvivalLabel != null) view.SurvivalLabel.text = "Выстоять 0:42";
                view.BossName.text = "Хранитель лугов";
                view.BossBar.Set(.64f);
                PreviewBossPolish(view);
            }

            // В игре подсветки наведения гасит UiHoverMotion.OnEnable; в предпросмотре его нет.
            foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
                if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
            // На выборе награды вторая карточка — под мышью, как на кадре из игры: видно кремовый
            // свет наведения и яркую нить рядом с тусклыми нитями соседей.
            if (shot == Shot.Choice)
            {
                var hovered = view.Offers[1].GetComponent<UiHoverMotion>();
                if (hovered != null && hovered.HighlightGroup != null) hovered.HighlightGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// Строка добычи на кадре состояния — как на выбранном кадре b1: золото 128, пять вещей разной
        /// редкости и «+1» у последней. Раскладка — та же, что ставит RunHud.Loot.
        /// </summary>
        static void PreviewLoot(RunHudView view)
        {
            if (view.Loot == null) return;
            view.Loot.gameObject.SetActive(true);
            view.LootGold.text = "128";
            float goldWidth = view.LootGold.GetPreferredValues("128").x;
            string[] items = { "officer_sabre", "leather_jacket", "lavidium_ring", "woodland_talisman", "scout_jacket" };
            int[] rarity = { 1, 0, 2, 1, 0 };
            int shown = Mathf.Min(items.Length, view.LootSlots.Length);
            for (int i = 0; i < view.LootSlots.Length; i++)
            {
                RunHudView.LootSlot slot = view.LootSlots[i];
                if (slot?.Rect == null) continue;
                slot.Rect.gameObject.SetActive(i < shown);
                if (i >= shown) continue;
                slot.Art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + items[i] + ".png");
                slot.Art.enabled = slot.Art.texture != null;
                slot.State.Set(rarity[i], false);
            }
            view.LootDivider.gameObject.SetActive(true);
            view.LootDivider.anchoredPosition = new Vector2(RunLootLedger.Layout.DividerX(goldWidth), view.LootDivider.anchoredPosition.y);
            view.LootRow.anchoredPosition = new Vector2(RunLootLedger.Layout.RowStart(goldWidth), view.LootRow.anchoredPosition.y);
            view.Loot.sizeDelta = new Vector2(RunLootLedger.Layout.Width(goldWidth, shown), view.Loot.sizeDelta.y);
            view.LootArrival.gameObject.SetActive(true);
            ((RectTransform)view.LootArrival.transform.parent).anchoredPosition =
                new Vector2((shown - 1) * RunLootLedger.Layout.Pitch + RunLootLedger.Layout.Icon + 4f, 0f);
        }

        /// <summary>
        /// Полоса босса как на кадре 1b: «Босс · фаза 2», 2900 / 5000, засечка 66% пройдена (горит), 50% и 33% — нет,
        /// светлый след недавнего урона и круглая голова заливки.
        /// </summary>
        static void PreviewBossPolish(RunHudView view)
        {
            if (view.BossSubtitle == null) return;
            view.BossSubtitle.text = RunHudBossMarks.Subtitle(2, false);
            if (view.BossNumbers != null) view.BossNumbers.text = "2900 / 5000";
            view.BossBar.TrailValue = .66f;
            view.BossBar.Set(.58f);
            for (int i = 0; i < view.BossMarks.Length; i++)
            {
                RunHudView.BossMark mark = view.BossMarks[i];
                if (mark?.Dot == null) continue;
                bool lit = RunHudBossMarks.Passed(i, 2900, 5000);
                mark.Dot.color = lit ? UiTheme.Current.Get(UiTheme.Role.Accent) : UiTheme.Current.Get(UiTheme.Role.SmokeDeep);
                if (mark.Ring != null && lit) mark.Ring.color = new Color(1f, .9f, .74f, .95f);
            }
        }

        /// <summary>
        /// Экран награды как на кадре 3a: сравнение «было → станет» на усилении и вещи (с «Надето: …»), кейкапы набора —
        /// у третьей ещё наполняется кольцо блокировки, вторая карточка под мышью приподнята, справа её подсказка с
        /// ключевыми словами и вложенная подсказка «Корни».
        /// </summary>
        static void PreviewRewardPolish(RunHudView view)
        {
            string good = "#" + ColorUtility.ToHtmlStringRGB(UiTheme.Current.Get(UiTheme.Role.Good));
            string muted = "#" + ColorUtility.ToHtmlStringRGB(UiTheme.Current.Get(UiTheme.Role.TextMuted));
            view.Offers[1].Description.text = RunHudCompare.Line("Дальность", "2 м", "3 м", 1, 1, good, good) + "\n"
                + UiKeywords.Themed("Удар достаёт врагов за спиной первого.");
            view.Offers[2].Description.text = RunHudCompare.Line("Здоровье", "120", "150", 1, 1, good, good) + "\n"
                + RunHudCompare.Line("Броня", "4", "6", 1, 1, good, good) + "\n<color=" + muted + "><size=90%>"
                + RunHudCompare.Worn("Потёртая куртка") + "</size></color>";
            if (view.Offers[2].Description.transform.parent is RectTransform body && body.name == "Описание")
                body.offsetMin = new Vector2(body.offsetMin.x, 0f);
            for (int i = 0; i < view.Offers.Length; i++)
            {
                RunHudView.KeyLock key = view.Offers[i].KeyLock;
                if (key?.Edge == null) continue;
                bool locked = i == 2;
                Color edge = UiTheme.Current.Get(locked ? UiTheme.Role.PanelLine : UiTheme.Role.Accent);
                edge.a = locked ? .3f : .85f;
                key.Edge.color = edge;
                if (key.Ring != null)
                {
                    key.Ring.gameObject.SetActive(locked);
                    key.Ring.fillAmount = .6f;
                }
            }
            var hovered = (RectTransform)view.Offers[1].transform;
            hovered.anchoredPosition += new Vector2(0f, 8f);
            hovered.localRotation = Quaternion.Euler(0f, 0f, .8f);
            if (view.OfferTip == null) return;
            view.OfferTip.gameObject.SetActive(true);
            view.OfferTipTitle.text = "Длинный клинок";
            view.OfferTipTitle.color = UiTheme.Current.Get(UiTheme.Role.Rare);
            view.OfferTipBody.text = UiKeywords.Themed("Дальность Рассекающего удара +50%. Удар достаёт врагов за спиной первого; "
                + "враги в корнях получают оглушение на 1 с.");
            view.OfferTip.anchoredPosition = new Vector2(510f, -(CardTop + CardPitch));
            if (view.KeywordTip == null) return;
            view.KeywordTip.gameObject.SetActive(true);
            UiKeywords.Entry roots = UiKeywords.Get(UiKeywords.Id.Roots);
            view.KeywordTipTitle.text = roots.Title;
            view.KeywordTipTitle.color = UiTheme.Current.Get(UiKeywords.ThemeRole(roots.Tone));
            view.KeywordTipBody.text = UiKeywords.ThemedDefinition(UiKeywords.Id.Roots);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(view.OfferTip);
            view.KeywordTip.anchoredPosition = new Vector2(546f, view.OfferTip.anchoredPosition.y - view.OfferTip.rect.height - 12f);
        }

        /// <summary>Итоги со статистикой как на кадре 4: числа, «Убито», «Потеряно» / «Остаётся», подпись стоп-кадра, кейкапы.</summary>
        static void PreviewStatsSummary(RunHudView view)
        {
            if (view.SummaryStats == null || view.SummaryStats.Length == 0) return;
            long[] values = { 767, 1, 2, 3, 18420, 2960, 612, 47, 4 };
            for (int i = 0; i < view.SummaryStats.Length && i < values.Length; i++)
                view.SummaryStats[i].text = RunHudSummary.Format(RunHudSummary.Rows[i], values[i]);
            string[] names = { "Корнеполз", "Шипомет", "Расщепень", "Корнехват", "Камнекопыт", "Лесной вендиго" };
            int[] kills = { 38, 6, 5, 4, 2, 1 };
            for (int i = 0; i < view.SummaryKills.Length && i < names.Length; i++)
            {
                RunHudView.SummaryKill slot = view.SummaryKills[i];
                if (slot?.Rect == null) continue;
                slot.Rect.gameObject.SetActive(true);
                slot.Name.text = names[i];
                slot.Count.text = RunHudSummary.KillCount(kills[i]);
            }
            if (view.SummaryFreezeCaption != null)
                view.SummaryFreezeCaption.text = RunHudSummary.FreezeCaption(Game.Sim.RunOutcome.Died, "Лесной вендиго", 64, 3);
            if (view.SummaryLost?.Lines != null) view.SummaryLost.Lines.text = "3 вещи в Разломе\n−64 золота";
            if (view.SummaryKept?.Lines != null) view.SummaryKept.Lines.text = "+3 уровня · опыт сохранён";
            string[] lost = { "officer_sabre", "lavidium_ring", "scout_jacket" };
            for (int i = 0; view.SummaryLost?.Items != null && i < view.SummaryLost.Items.Length; i++)
            {
                RunHudView.BandItem item = view.SummaryLost.Items[i];
                if (item?.Rect == null) continue;
                item.Rect.gameObject.SetActive(i < lost.Length);
                if (i >= lost.Length || item.Art == null) continue;
                item.Art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/" + lost[i] + ".png");
                item.Art.enabled = item.Art.texture != null;
            }
            if (view.SummaryLost?.Lines != null && view.SummaryLost.Lines.transform.parent is RectTransform area)
                area.offsetMin = new Vector2(18f + lost.Length * 46f + 8f, area.offsetMin.y);
            view.RepeatLabel.text = "Повторить";
            view.ToCampLabel.text = "В лагерь";
            if (view.RepeatKey?.Letter != null) view.RepeatKey.Letter.text = "R";
            if (view.CampKey?.Letter != null) view.CampKey.Letter.text = "C";
            foreach (RunHudView.KeyLock key in new[] { view.RepeatKey, view.CampKey })
                if (key?.Edge != null)
                {
                    Color edge = UiTheme.Current.Get(UiTheme.Role.Accent);
                    edge.a = .85f;
                    key.Edge.color = edge;
                }
        }

        static void Offer(RunOfferCard card, string title, string kind, string body, string valueLabel, string value, string icon, bool rare, string key)
        {
            card.Title.text = title;
            card.Kind.text = kind;
            card.Description.text = body;
            card.ValueLabel.text = valueLabel;
            card.Value.text = value;
            card.Key.text = key;
            card.Icon.texture = icon != null ? Ability(icon) : AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Items/leather_jacket.png");
            card.Icon.enabled = card.Icon.texture != null;
            card.Rarity.Set(rare ? WcRarity.Tier.Rare : WcRarity.Tier.Common);
        }
    }
}
