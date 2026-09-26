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
        public enum Shot { Choice, Replace, Status, Summary, Artifact, Route }

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
            view.Choice.gameObject.SetActive(shot == Shot.Choice || shot == Shot.Artifact || shot == Shot.Route);
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
            }

            if (shot == Shot.Route)
            {
                // Выбор следующей арены после награды: обычная с улучшением, магазин, опасная с бонусом.
                // Тексты карточек — из игры (RunHud.RouteTexts), чтобы кадр не расходился с экраном.
                view.ChoiceTitle.text = "Выбери следующую арену";
                view.ChoiceSubtitle.text = "Арена 3 · награда — после зачистки";
                view.ChoiceHint.text = "1  2  3 — выбрать путь    ·    L — уйти с добычей";
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
                Offer(view.Offers[0], "Рассекающий удар", "Способность", "Сильный удар саблей сверху перед собой. Бьёт одну цель и не двигает героя.", "Лавидий", "30", "Cleave", false, "1");
                view.Offers[0].KindIcon.texture = view.KindIcons[0];
                Offer(view.Offers[1], "Длинный клинок", "Усиление · 3 из 8", "Дальность Рассекающего удара +50%. Удар достаёт врагов за спиной первого.", "Рассекающий удар", "", "Cleave", true, "2");
                view.Offers[1].KindIcon.texture = view.KindIcons[1];
                Offer(view.Offers[2], "Кожаная куртка", "Обычная вещь · ур. 4", "Броня +12 · Скорость движения +4% · Сопротивление огню +8%", "", "", null, false, "3");
                view.Offers[2].KindIcon.texture = view.KindIcons[2];
            }
            if (shot == Shot.Artifact)
            {
                // Награда босса: три артефакта набора акта I, у героя уже есть Обет Хранителя — открыт вопрос о замене.
                var held = Game.Sim.RunArtifact.GuardianVow;
                Game.Sim.RunArtifact[] offers = { Game.Sim.RunArtifact.SunSeal, Game.Sim.RunArtifact.Hourglass, Game.Sim.RunArtifact.CrimsonHeart };
                view.ChoiceTitle.text = "Выбери артефакт";
                view.ChoiceSubtitle.text = "Награда босса · сейчас у тебя «" + RunArtifactTexts.Name(held) + "» — слот один";
                view.ChoiceHint.text = "1  2  3 — выбрать    ·    «Отказаться» — оставить как есть";
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
                view.StatusExtra.text = "Тайники 0 / 2 · золото 36";
                if (view.SurvivalLabel != null) view.SurvivalLabel.text = "Выстоять 0:42";
                view.BossName.text = "Хранитель лугов";
                view.BossBar.Set(.64f);
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
