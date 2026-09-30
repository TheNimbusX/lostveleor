using System.IO;
using Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;
using FontRole = Game.View.UiTheme.FontRole;
using Role = Game.View.UiTheme.Role;

namespace Game.EditorTools
{
    /// <summary>
    /// Строка эффектов героя над портретом (этап 4, выбор владельца 30.09 — кадр 1a
    /// `ART/UI/concepts-2026-09-30-hud-polish/1a-fight-rings-notches.png`; из 1b — секунды и стаки
    /// «×2» в углу значка, без кромки мазком туши). Круги-медальоны с тонким кольцом-таймером
    /// строкой ТОЛЬКО НАД портретом: корни, оглушение, защита от контроля, замедление, Живица,
    /// Порыв, Ясный настой, артефакт, Blaze; лишние — в круг «+N». Значки вырезаны из того же кадра
    /// (tools/ui-kit/cut-effect-icons.py → Assets/UI/Kit/Watercolor/wc_effect_*.png).
    ///
    /// Строка — ребёнок панели героя и стоит от портрета: над его верхом (голова выходит за круг —
    /// считается и она), под столбиком всплывашек, слева чуть за кромкой портрета — как в кадре.
    /// Мыши не ловит. Подсказка — малая подложка кита без кромки, Nunito.
    /// Готовый префаб получает строку миграцией v2; значки зелий HudBuffChip уходят.
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        const string EffectRowName = "Эффекты", EffectTipName = "Подсказка эффекта", BuffRowName = "Эффекты зелий";
        /// <summary>Круг, шаг и места в строке, единицы холста (кадр 1a: круг ≈ 50 при портрете 132).</summary>
        const float EffectChip = 44f, EffectPitch = 52f;
        const int EffectMaxVisible = 7;
        /// <summary>Зазор между верхом портрета (с головой) и низом строки; слева строка выходит за портрет на столько.</summary>
        const float EffectLift = 8f, EffectLeftOut = 20f;

        static readonly string[] EffectIconNames =
            { "root", "stun", "immune", "slow", "resin", "surge", "clear", "artifact", "blaze" };
        static readonly string[] EffectChipNames =
            { "Корни", "Оглушение", "Защита от контроля", "Замедление", "Живица", "Порыв", "Ясный настой", "Артефакт", "Blaze" };

        /// <summary>Строка эффектов над портретом и её подсказка; ставит view.EffectRow.</summary>
        static void BuildEffects(RectTransform root, CombatHudView view)
        {
            RectTransform parent = view.HeroPanel != null ? view.HeroPanel : root;
            RectTransform row = Node(EffectRowName, parent);
            row.anchorMin = row.anchorMax = parent == root ? BottomCenter : Vector2.zero;
            row.pivot = new Vector2(0f, .5f);
            row.sizeDelta = new Vector2(EffectPitch * EffectMaxVisible, EffectChip);
            PlaceEffects(row, view, parent == root);
            var effects = row.gameObject.AddComponent<HudEffectRow>();
            effects.Pitch = EffectPitch;
            effects.MaxVisible = EffectMaxVisible;
            // Угроза — приглушённый красный (не оранжевый акцент), защита — холодная, помощь — кремовое золото.
            effects.ThreatRing = new Color(.8f, .37f, .33f, 1f);
            effects.GuardRing = new Color(.63f, .81f, .87f, 1f);
            effects.BoonRing = new Color(1f, .85f, .58f, 1f);
            effects.ThreatGlow = new Color(1f, .36f, .28f, 1f);
            effects.GuardGlow = new Color(.6f, .85f, 1f, 1f);
            effects.BoonGlow = new Color(1f, .8f, .5f, 1f);

            effects.Chips = new HudEffectChip[HudEffectList.KindCount];
            effects.Icons = new Texture[HudEffectList.KindCount];
            for (int i = 0; i < HudEffectList.KindCount; i++)
            {
                Texture icon = EffectIcon(EffectIconNames[i]);
                effects.Icons[i] = icon;
                effects.Chips[i] = EffectChipNode(row, EffectChipNames[i], icon, false);
            }
            effects.More = EffectChipNode(row, "Ещё", null, true);
            BuildEffectTooltip(root, effects);
            view.EffectRow = effects;
        }

        /// <summary>
        /// Место строки от портрета: низ — над верхом портрета (над головой, если она выше круга) с
        /// зазором, начало — чуть левее портрета. У старой раскладки без портрета — место из сборщика.
        /// </summary>
        static void PlaceEffects(RectTransform row, CombatHudView view, bool onRoot)
        {
            RectTransform hero = view.HeroPanel;
            RectTransform portrait = hero != null ? hero.Find("Портрет") as RectTransform : null;
            if (onRoot || portrait == null)
            {
                // Панели героя нет: от низа экрана, как сборщик ставит портрет.
                float left = StripLeft - 44f, bottom = Bottom - 6f;
                row.anchoredPosition = new Vector2(left - EffectLeftOut, bottom + PortraitTopFallback + EffectLift + EffectChip * .5f);
                if (!onRoot) row.anchoredPosition -= new Vector2(left, bottom);
                return;
            }
            // Координаты локальные (у Canvas в сцене префаба нулевой масштаб): поворотов и масштабов в
            // панели героя сборщик не ставит.
            Vector2 corner = (Vector2)portrait.localPosition - Vector2.Scale(portrait.rect.size, portrait.pivot);
            float top = corner.y + portrait.rect.height;
            RawImage art = view.Portrait;
            if (art != null && art.rectTransform.parent == portrait)
            {
                RectTransform a = art.rectTransform;
                float artTop = portrait.localPosition.y + a.localPosition.y + a.rect.height * (1f - a.pivot.y) * a.localScale.y;
                top = Mathf.Max(top, artTop);
            }
            // anchoredPosition в панели героя — от её левого нижнего угла; localPosition — от её опоры.
            Vector2 heroPivot = Vector2.Scale(hero.rect.size, hero.pivot);
            row.anchoredPosition = new Vector2(corner.x - EffectLeftOut, top + EffectLift + EffectChip * .5f) + heroPivot;
        }

        /// <summary>Верх головы над низом портрета у свежей сборки (PortraitArtCentre + половина рисунка).</summary>
        const float PortraitTopFallback = Portrait * .5f + PortraitArtLift + PortraitArtSize * .5f;

        /// <summary>Значок из кита; только что вырезанный файл, которого база ещё не видела, импортируется сразу.</summary>
        static Texture EffectIcon(string name)
        {
            string path = UiKitImport.KitRoot + "/Watercolor/wc_effect_" + name + ".png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null && File.Exists(path))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Sprite sprite = Kit("wc_effect_" + name);
            if (sprite == null) Debug.LogWarning("[ui-kit] Нет значка эффекта " + path + ": круг будет без значка");
            return sprite != null ? sprite.texture : null;
        }

        /// <summary>
        /// Круг эффекта: тень, тёмный диск, значок, тусклая дорожка и кольцо-таймер; свет и клуб дыма
        /// появления; кружок секунд справа снизу и «×2» справа сверху. У круга «+N» вместо значка и
        /// кольца — число.
        /// </summary>
        static HudEffectChip EffectChipNode(RectTransform row, string name, Texture icon, bool more)
        {
            RectTransform node = Node(name, row);
            node.anchorMin = node.anchorMax = new Vector2(0f, .5f);
            node.pivot = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(EffectChip, EffectChip);
            node.anchoredPosition = new Vector2(EffectPitch * .5f, 0f);
            var chip = node.gameObject.AddComponent<HudEffectChip>();
            chip.Group = node.gameObject.AddComponent<CanvasGroup>();
            chip.Group.blocksRaycasts = false;
            chip.Group.interactable = false;
            RectTransform body = Stretch(Node("Тело", node));
            chip.Body = body;

            // Клуб дыма появления — за кругом, шире его; гасит и растит HudEffectChip.
            Image puff = Mark(body, "Дым", UiInkKit.Sprite("smoke_ring"), Role.SmokeDeep, 0f, new Vector2(.5f, .5f), Vector2.zero, EffectChip * 1.7f);
            Object.DestroyImmediate(puff.GetComponent<ThemeColor>());
            puff.color = new Color(.05f, .06f, .09f, 0f);
            puff.enabled = false;
            chip.Puff = puff;
            // Мягкая тень: круг читается и на светлой траве.
            Image shadow = Layer(body, "Тень", RoundShadow, Role.Veil, .8f, 9f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            Layer(body, "Диск", T.CircleFill, Role.SmokeDeep, .95f);
            if (!more)
            {
                var art = Stretch(Node("Значок", body), EffectChip * .17f).gameObject.AddComponent<RawImage>();
                art.texture = icon;
                art.raycastTarget = false;
                chip.Icon = art;
            }
            chip.Track = Layer(body, "Дорожка", T.CircleFrameBold, Role.PanelLine, .45f);
            if (!more)
            {
                // Кольцо-таймер: полное — только что наложен; убывает по часовой от верха.
                Image ring = Layer(body, "Кольцо", T.CircleFrameBold, Role.Text, 1f);
                Object.DestroyImmediate(ring.GetComponent<ThemeColor>());
                ring.color = new Color(1f, .85f, .58f, 1f);
                ring.type = Image.Type.Filled;
                ring.fillMethod = Image.FillMethod.Radial360;
                ring.fillOrigin = (int)Image.Origin360.Top;
                ring.fillClockwise = true;
                ring.fillAmount = .7f;
                chip.Ring = ring;
            }
            Image glow = Mark(body, "Свет", Kit("wc_fx_glow"), Role.Text, 0f, new Vector2(.5f, .5f), Vector2.zero, EffectChip * 2.3f);
            Additive(glow, new Color(1f, .8f, .5f, 0f));
            glow.enabled = false;
            chip.Glow = glow;

            if (more)
            {
                RectTransform countBox = Stretch(Node("Число", body));
                chip.Count = LabelOn(countBox, "+2", FontRole.Body, 16f, Role.Text, TextAlignmentOptions.Center);
                chip.Count.fontStyle = FontStyles.Bold;
                chip.Count.textWrappingMode = TextWrappingModes.NoWrap;
                Shadowed(chip.Count);
                node.gameObject.SetActive(false);
                return chip;
            }

            // Секунды — кружок на кромке справа снизу (кадр 1b).
            RectTransform badge = Box(Node("Секунды", body), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(EffectChip * .36f, -EffectChip * .36f), new Vector2(20f, 20f));
            Layer(badge, "Диск", T.CircleFill, Role.SmokeDeep, .97f);
            Layer(badge, "Кольцо", T.CircleFrame, Role.Text, .5f);
            RectTransform secondsBox = Stretch(Node("Число", badge));
            chip.Seconds = LabelOn(secondsBox, "3", FontRole.Body, 13f, Role.Text, TextAlignmentOptions.Center);
            chip.Seconds.fontStyle = FontStyles.Bold;
            chip.Seconds.textWrappingMode = TextWrappingModes.NoWrap;
            chip.Seconds.enableAutoSizing = true;
            chip.Seconds.fontSizeMin = 8f;
            chip.Seconds.fontSizeMax = 13f;
            chip.Seconds.margin = new Vector4(1f, 0f, 1f, 0f);
            chip.SecondsBadge = badge.gameObject;

            // Стаки — «×2» справа сверху, без подложки, с тенью (кадр 1b).
            RectTransform stacksBox = Box(Node("Стаки", body), new Vector2(.5f, .5f), new Vector2(0f, .5f),
                new Vector2(EffectChip * .3f, EffectChip * .38f), new Vector2(30f, 16f));
            chip.Stacks = LabelOn(stacksBox, "×2", FontRole.Body, 13f, Role.Text, TextAlignmentOptions.MidlineLeft);
            chip.Stacks.fontStyle = FontStyles.Bold;
            chip.Stacks.textWrappingMode = TextWrappingModes.NoWrap;
            Shadowed(chip.Stacks);
            stacksBox.gameObject.SetActive(false);
            node.gameObject.SetActive(false);
            return chip;
        }

        /// <summary>
        /// Подсказка круга: малая подложка «Дыма и света» без тлеющей кромки (частая всплывашка), одна
        /// надпись Nunito — имя, строка «что делает», секунды. Высота по тексту; место ставит HudEffectRow.
        /// </summary>
        static void BuildEffectTooltip(RectTransform root, HudEffectRow effects)
        {
            RectTransform tip = Box(Node(EffectTipName, root), BottomCenter, new Vector2(.5f, 0f), new Vector2(0f, 260f), new Vector2(300f, 80f));
            UiInkKit.Plate(tip, small: true);
            UiInkKit.Group(tip, UiInkGroup.Sweep.FromCenter, .3f, .08f).Burn = 0f;
            var column = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(16, 16, 10, 12);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform text = Node("Текст", tip);
            TMP_Text label = LabelOn(text, "<b>Корни</b>\nНи шага, ни рывка — бить на месте можно\n<size=88%>Ещё 2 с</size>",
                FontRole.Body, T.Size(UiTheme.TextStep.Caption), Role.Text, TextAlignmentOptions.Center);
            label.richText = true;
            label.lineSpacing = 2f;
            UiInkKit.Revealed(label, .06f);
            effects.Tooltip = tip;
            effects.TooltipText = label;
            effects.TooltipGap = 10f;
            tip.gameObject.SetActive(false);
        }

        /// <summary>
        /// v2 — строка эффектов над портретом: ряд значков зелий «Эффекты зелий» (HudBuffChip) уходит,
        /// на его место — строка кругов над портретом и её подсказка (перед подсказкой способности).
        /// </summary>
        static void MigrateTo2(GameObject root, CombatHudView view)
        {
            RemoveBuffChips(root, view);
            if (view.EffectRow != null) return;
            BuildEffects((RectTransform)root.transform, view);
            RectTransform tip = view.EffectRow.Tooltip;
            if (tip != null && view.PotionTooltip != null) tip.SetSiblingIndex(view.PotionTooltip.GetSiblingIndex());
            CheckEffectsBelowToasts(view);
        }

        /// <summary>
        /// Строка стоит под столбиком всплывашек; если их сдвинули руками ниже верха строки — предупредить
        /// (двигать всплывашки миграция не станет). Сравнение — в координатах «от низа по центру экрана».
        /// </summary>
        static void CheckEffectsBelowToasts(CombatHudView view)
        {
            RectTransform hero = view.HeroPanel, row = view.EffectRow != null ? (RectTransform)view.EffectRow.transform : null;
            RectTransform toasts = view.Toasts != null ? (RectTransform)view.Toasts.transform : null;
            if (hero == null || row == null || toasts == null || row.parent != hero) return;
            if (hero.anchorMin != BottomCenter || hero.anchorMax != BottomCenter || toasts.anchorMin != BottomCenter || toasts.anchorMax != BottomCenter) return;
            float heroBottom = hero.anchoredPosition.y - hero.rect.height * hero.pivot.y;
            float rowTop = heroBottom + row.anchoredPosition.y + EffectChip * .5f;
            float toastsBottom = toasts.anchoredPosition.y - toasts.rect.height * toasts.pivot.y;
            if (rowTop > toastsBottom)
                Debug.LogWarning("[ui-kit] Строка эффектов над портретом заходит под столбик всплывашек на "
                                 + Mathf.CeilToInt(rowTop - toastsBottom) + " ед.: всплывашки сдвинуты руками — поднять их или опустить строку");
        }

        /// <summary>
        /// Ряд значков зелий: целиком, если в нём только значки; иначе — только значки, а ряд с чужими
        /// детьми (правка руками) остаётся.
        /// </summary>
        static void RemoveBuffChips(GameObject root, CombatHudView view)
        {
            Transform row = view.ResinChip != null ? view.ResinChip.transform.parent
                : view.SurgeChip != null ? view.SurgeChip.transform.parent
                : root.transform.Find(BuffRowName);
            bool onlyChips = row != null && row != root.transform && row.name == BuffRowName;
            if (onlyChips)
                foreach (Transform child in row)
                    if (child.GetComponent<HudBuffChip>() == null) { onlyChips = false; break; }
            if (onlyChips) Object.DestroyImmediate(row.gameObject);
            else
            {
                if (view.ResinChip != null) Object.DestroyImmediate(view.ResinChip.gameObject);
                if (view.SurgeChip != null) Object.DestroyImmediate(view.SurgeChip.gameObject);
                if (row != null) Debug.LogWarning("[ui-kit] В ряду «" + row.name + "» боевого HUD есть не только значки зелий: ряд оставлен, значки убраны");
            }
            view.ResinChip = view.SurgeChip = null;
        }

        /// <summary>
        /// Предпросмотр строки: корни на 2 с, Живица, Порыв, Солнечная Печать и Blaze «×2» — круги стоят
        /// на местах, как их раскладывает HudEffectRow.
        /// </summary>
        static void PreviewEffects(CombatHudView view)
        {
            HudEffectRow row = view.EffectRow;
            if (row == null) return;
            var shown = new (HudEffectKind Kind, float Fill, int Seconds, int Stacks)[]
            {
                (HudEffectKind.Root, .5f, 2, 1),
                (HudEffectKind.Resin, .66f, 4, 1),
                (HudEffectKind.Surge, .33f, 2, 1),
                (HudEffectKind.Artifact, .75f, 3, 1),
                (HudEffectKind.Blaze, .8f, 3, 2),
            };
            for (int i = 0; i < shown.Length; i++)
            {
                HudEffectChip chip = row.Chips[(int)shown[i].Kind];
                if (chip == null) continue;
                chip.gameObject.SetActive(true);
                chip.Group.alpha = 1f;
                chip.Rect.anchoredPosition = new Vector2(row.Pitch * (i + .5f), 0f);
                HudEffectTone tone = HudEffectList.ToneOf(shown[i].Kind);
                chip.Ring.color = tone == HudEffectTone.Threat ? row.ThreatRing : tone == HudEffectTone.Guard ? row.GuardRing : row.BoonRing;
                chip.Ring.fillAmount = shown[i].Fill;
                chip.Seconds.text = HudEffectList.SecondsLabel(shown[i].Seconds);
                chip.SecondsBadge.SetActive(true);
                chip.Stacks.text = HudEffectList.StacksLabel(shown[i].Stacks);
                chip.Stacks.gameObject.SetActive(shown[i].Stacks > 1);
                if (shown[i].Kind == HudEffectKind.Artifact && chip.Icon != null)
                    chip.Icon.texture = RunArtifactTexts.Icon(Game.Sim.RunArtifact.SunSeal) ?? chip.Icon.texture;
            }
        }
    }
}
