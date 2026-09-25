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
        public enum Shot { Choice, Replace, Status, Summary, Artifact }

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
            view.Choice.gameObject.SetActive(shot == Shot.Choice || shot == Shot.Artifact);
            view.ArtifactReplace.gameObject.SetActive(shot == Shot.Artifact);
            view.Skip.gameObject.SetActive(shot == Shot.Artifact);
            view.Replace.gameObject.SetActive(shot == Shot.Replace);
            view.Status.gameObject.SetActive(shot == Shot.Status);
            view.Boss.gameObject.SetActive(shot == Shot.Status);
            view.Summary.gameObject.SetActive(shot == Shot.Summary);
            if (shot == Shot.Summary)
            {
                view.SummaryTitle.text = "Гибель";
                view.OutcomeIcon.texture = view.OutcomeIcons[0];
                view.SummaryTitle.GetComponent<ThemeColor>().SetRole(UiTheme.Role.Health);
                view.SummarySubtitle.text = "Всё найденное в забеге осталось в Разломе";
                int[] values = { 3, 4, 2, 118 };
                for (int i = 0; i < 4; i++) view.SummaryValues[i].text = values[i].ToString();
                view.SummaryLoss.text = "Потеряно со смертью: предметов 3, золота 64";
                view.SummaryCamp.text = "Новых вещей проверить на манекенах: 2\nМусора под разбор: 5";
            }

            if (shot == Shot.Choice)
            {
                view.ChoiceSubtitle.text = "Разлом зачищен · дальше разлом 3 / 5";
                Offer(view.Offers[0], "Рассекающий удар", "Способность", "Сильный удар саблей сверху перед собой. Бьёт одну цель и не двигает героя.", "Лавидий", "30", "Cleave", false, "1");
                view.Offers[0].KindIcon.texture = view.KindIcons[0];
                Offer(view.Offers[1], "Длинный клинок", "Усиление · 3 из 8", "Дальность Рассекающего удара +50%. Удар достаёт врагов за спиной первого.", "Рассекающий удар", "", "Cleave", true, "2");
                view.Offers[1].KindIcon.texture = view.KindIcons[1];
                Offer(view.Offers[2], "Кожаная куртка", "Предмет · ур. 4", "Броня +12 · Скорость движения +4% · Сопротивление огню +8%", "", "", null, false, "3");
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
                    view.Slots[i].Icon.texture = Ability(icons[i]);
                    view.Slots[i].Icon.enabled = true;
                }
                view.SalvageLabel.text = "Разобрать на 40 золота";
            }
            if (shot == Shot.Status)
            {
                view.StatusTitle.text = "Разлом 2 / 5";
                view.StatusLine.text = "Встречи 1 / 3 · целей 14";
                view.StatusExtra.text = "Тайники 0 / 2 · золото 36";
                view.BossName.text = "Хранитель лугов";
                view.BossBar.Set(.64f);
            }

            // В игре подсветки наведения гасит UiHoverMotion.OnEnable; в предпросмотре его нет.
            foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
                if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
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
