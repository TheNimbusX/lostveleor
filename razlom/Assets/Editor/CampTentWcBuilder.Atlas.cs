using Game.Sim;
using Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;
using TextStep = Game.View.UiTheme.TextStep;

namespace Game.EditorTools
{
    /// <summary>
    /// Атлас палатки v103 (06.10, концепт владельца atlas-a в нашем стиле ingame-style/atlas.png): «Открыто N из 8», сетка
    /// 3×4 круглых мест — восемь артефактов акта I и четыре тёмных места акта II, — и постоянная карточка справа вместо
    /// всплывающей. Найденный артефакт — расписная картинка в огненном кольце, неизвестный — силуэт и «?».
    /// Координаты — от левого верхнего угла панели «Сумка» (1084 × 860).
    /// </summary>
    public static partial class CampTentWcBuilder
    {
        const string RoundAtlasName = "Атлас 3×4";
        const float AtlasSealSize = 150f, AtlasStepX = 190f, AtlasStepY = 170f, AtlasLeft = 173f, AtlasTop = 160f;

        /// <summary>
        /// Прежняя «Страница атласа» (4 колонки квадратных ячеек и всплывающая карточка) удаляется вместе с шаблоном: её
        /// место занимает атлас 3×4. Ссылки вида на старую сетку обнуляются — окно берёт круглые места (CampTentView.AtlasSeals).
        /// </summary>
        static void BuildRoundAtlas(RectTransform content, CampTentView view)
        {
            if (content.Find(RoundAtlasName) != null) return;
            Transform old = content.Find("Страница атласа");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            view.AtlasGrid = null;
            view.AtlasTemplate = null;

            RectTransform page = Stretch(Node(RoundAtlasName, content));
            view.AtlasPage = page.gameObject;

            view.AtlasCount = CampInkParts.Text(page, "Открыто", "Открыто 0 из 8", 20f, 12f, 420f, 40f, FontRole.Heading, TextStep.Heading, Role.Text);
            view.AtlasAct = CampInkParts.Text(page, "Акт", "Акт I · Чаща", 400f, 12f, 306f, 40f, FontRole.Heading, TextStep.Body, Role.TextMuted,
                TextAlignmentOptions.MidlineRight);
            TopLeft(UiInkKit.Divider(page, "Нить", 690f, true, .55f), 16f, 56f, 690f, 16f);

            RectTransform grid = Stretch(Node("Сетка", page));
            float question = T.Size(TextStep.Display);
            for (int i = 0; i < CampOathRules.AtlasPlaces; i++)
            {
                bool future = CampOathRules.AtlasPlaceIsFuture(i);
                Texture art = future ? null : RunArtifactTexts.Icon(RunArtifacts.At(i));
                InkMedal medal = CampInkParts.IconMedallion(grid, future ? "Место акта II " + (i - RunArtifacts.Count + 1) : "Артефакт " + (i + 1),
                    AtlasLeft + (i % 3) * AtlasStepX, AtlasTop + (i / 3) * AtlasStepY, AtlasSealSize, art, false);
                CampOathSeal seal = Seal(medal, i, null);
                // Огонёк-ромб под кольцом, как на концепте.
                UiInkKit.LightAt(medal.Root, "Ромб", "light_gem", new Vector2(.5f, 0f), new Vector2(0f, -1f), new Vector2(12f, 13f), .75f, delay: .22f);
                seal.Question = UiInkKit.Label(medal.Root, "Вопрос", "?", FontRole.Heading, question, Role.TextMuted, TextAlignmentOptions.Center,
                    alpha: .6f, delay: .2f);
                seal.Question.gameObject.SetActive(future);
                RectTransform caption = At(Node("Подпись", medal.Root), new Vector2(.5f, .5f), Vector2.zero, new Vector2(AtlasSealSize, 40f));
                seal.HoverCaption = UiInkKit.Label(caption, "Надпись", future ? "Акт II" : "", FontRole.Heading, T.Size(TextStep.Heading), Role.Text,
                    TextAlignmentOptions.Center, 2f, delay: .15f);
                seal.HoverCaption.gameObject.SetActive(false);
                if (!future) seal.Show(i < 3 ? CampOathSeal.Look.Active : CampOathSeal.Look.Unknown, false);
                else seal.Show(CampOathSeal.Look.Empty, false);
                view.AtlasSeals[i] = seal;
            }

            BuildAtlasCard(page, view);
            page.gameObject.SetActive(false);
        }

        /// <summary>
        /// Карточка справа (концепт atlas-a): большая печать 210 в огне, имя (Title 32, до двух строк), нить, действие и строка
        /// применения, нить, «Источник» со знаком победы, нить, «Можно взять с собой на столе сборов» со знаком лагеря.
        /// </summary>
        static void BuildAtlasCard(RectTransform page, CampTentView view)
        {
            const float w = 340f, cx = w * .5f;
            RectTransform card = TopLeft(Node("Карточка артефакта", page), 724f, 0f, w, 860f);
            Texture first = RunArtifactTexts.Icon(RunArtifacts.At(0));
            InkMedal big = CampInkParts.IconMedallion(card, "Артефакт", cx, 126f, 210f, first, false, clickable: false);
            view.AtlasCardSeal = Seal(big, 0, null);
            view.AtlasCardSeal.Show(CampOathSeal.Look.Active, false);
            UiInkKit.LightAt(big.Root, "Ромб", "light_gem", new Vector2(.5f, 0f), new Vector2(0f, -1f), new Vector2(14f, 15f), .9f, delay: .22f);

            view.AtlasCardName = CampInkParts.Text(card, "Название", "", 0f, 246f, w, 84f, FontRole.Heading, TextStep.Title, Role.Text,
                TextAlignmentOptions.Center);
            view.AtlasCardName.enableAutoSizing = true;
            view.AtlasCardName.fontSizeMax = T.Size(TextStep.Title);
            view.AtlasCardName.fontSizeMin = T.Size(TextStep.Heading);
            var lines = new GameObject[3];
            lines[0] = TopLeft(UiInkKit.Divider(card, "Нить 1", w - 40f, true, .5f), 20f, 334f, w - 40f, 16f).gameObject;

            view.AtlasCardEffect = CampInkParts.Text(card, "Действие", "", 10f, 356f, w - 20f, 118f, FontRole.Body, TextStep.Body, Role.Text,
                TextAlignmentOptions.TopLeft);
            view.AtlasCardUse = CampInkParts.Text(card, "Применение", "", 10f, 478f, w - 20f, 24f, FontRole.Body, TextStep.Caption, Role.TextMuted,
                TextAlignmentOptions.TopLeft);
            lines[1] = TopLeft(UiInkKit.Divider(card, "Нить 2", w - 40f, true, .4f), 20f, 512f, w - 40f, 16f).gameObject;

            view.AtlasCardSourceRow = InfoRow(card, "Источник", 532f, CampInkParts.KitTexture("victory"),
                "Источник: награда босса · редко — тайник в Разломе", out view.AtlasCardSource).gameObject;
            lines[2] = TopLeft(UiInkKit.Divider(card, "Нить 3", w - 40f, true, .4f), 20f, 614f, w - 40f, 16f).gameObject;
            view.AtlasCardCarryRow = InfoRow(card, "Стол сборов", 634f, CampInkParts.KitTexture("camp"),
                "Можно взять с собой на столе сборов", out view.AtlasCardCarry).gameObject;
            view.AtlasCardLines = lines;
        }

        /// <summary>Строка карточки: белый знак 34 слева (краска темы) и текст Body до двух-трёх строк.</summary>
        static RectTransform InfoRow(RectTransform card, string name, float y, Texture icon, string text, out TMP_Text label)
        {
            RectTransform row = TopLeft(Node(name, card), 0f, y, 340f, 70f);
            RectTransform mark = At(Node("Значок", row), new Vector2(0f, .5f), new Vector2(30f, 0f), new Vector2(34f, 34f));
            var raw = mark.gameObject.AddComponent<RawImage>();
            raw.texture = icon;
            raw.enabled = icon != null;
            raw.raycastTarget = false;
            Tint(raw, Role.Text, .9f);
            raw.material = UiInkKit.Art;
            UiInkKit.Inked(raw, delay: .12f);
            label = CampInkParts.Text(row, "Надпись", text, 58f, 0f, 272f, 70f, FontRole.Body, TextStep.Body, Role.Text);
            label.enableAutoSizing = true;
            label.fontSizeMax = T.Size(TextStep.Body);
            label.fontSizeMin = T.Size(TextStep.Caption);
            return row;
        }
    }
}
