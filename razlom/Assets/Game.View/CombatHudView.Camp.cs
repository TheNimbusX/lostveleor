using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// HUD в лагере (владелец, 29 сентября). Вне полигона боевая часть — способности, кувырок,
    /// значки эффектов зелий и дым под способностями — уходит; остаются портрет с полосами,
    /// миникарта, кошелёк лагеря и зелья. В зоне полигона (та же граница, по которой Sim
    /// запрещает бой, — <see cref="GameSession.CampCombatAllowed"/>) боевая часть возвращается
    /// проявлением «Дыма и света». Общее появление HUD одноразовое (FirstTimeOnly), поэтому у
    /// каждой такой части своя группа проявления, и она играет заново на каждом входе.
    /// В Разломе ничего не меняется, кошелёк скрыт.
    ///
    /// Части прячутся прозрачностью (CanvasGroup), а не выключением: DashPanel включает сам
    /// Refresh. Чтобы мышь не находила спрятанное, после ухода выключается то, что ловит
    /// наведение: весь ряд способностей и плитка кувырка.
    /// </summary>
    public sealed partial class CombatHudView
    {
        sealed class CampPart
        {
            public CanvasGroup Group;
            /// <summary>Группа проявления части; null — часть только гаснет прозрачностью.</summary>
            public UiInkGroup Ink;
            /// <summary>Что выключить после ухода, чтобы наведение и клик его не находили; может быть null.</summary>
            public GameObject Hover;
        }

        const float CampShowFade = .35f, CampHideFade = .3f;
        CampPart[] _campParts;
        bool _campCombatShown = true, _campRefreshed;
        int _walletGold = -1, _walletShards = -1;

        /// <summary>Боевая часть спрятана: мышь над её местом — это мир, а не HUD.</summary>
        internal bool CampCombatHidden => _campParts != null && !_campCombatShown;

        /// <summary>После Refresh: видимость боевой части в лагере и кошелёк лагеря.</summary>
        internal void RefreshCamp(GameSession session)
        {
            bool camp = session.Mode == GameMode.Camp;
            bool combat = !camp || session.CampCombatAllowed;
            bool first = !_campRefreshed;
            _campRefreshed = true;
            // Части собираются в первый уход боевой части: пока она на месте (Разлом, полигон),
            // HUD не трогается вовсе и появляется ровно как раньше.
            if (_campParts == null && !combat) SetupCampParts(first);
            if (_campParts != null && combat != _campCombatShown)
            {
                _campCombatShown = combat;
                foreach (CampPart part in _campParts)
                    if (combat) ShowCampPart(part);
                    else HideCampPart(part);
            }
            RefreshCampWallet(camp ? session.Camp : null);
        }

        /// <summary>
        /// Мышь над HUD, пока боевая часть спрятана: портрет с полосами, зелья, карта и кошелёк.
        /// Полоса под всем HUD сюда не входит — по пустому месту способностей клик идёт в мир.
        /// </summary>
        internal bool HitTestCampCalm(Vector2 screen)
            => Contains(HeroPanel, screen) || Contains(PotionPanel, screen) || Contains(MinimapFrame, screen)
               || Contains(MinimapCaptionPanel, screen) || Contains(CampWallet, screen);

        /// <summary>
        /// Собирает части. HUD только появляется (первый кадр или ещё идёт его общее проявление)
        /// — боевая часть в лагере вне полигона просто не появляется, без движения. Иначе части
        /// остаются видимыми (<see cref="_campCombatShown"/> — true), и уводит их RefreshCamp
        /// обычным уходом.
        /// </summary>
        void SetupCampParts(bool appearing)
        {
            var parts = new System.Collections.Generic.List<CampPart>(5);
            UiInkGroup root = GetComponent<UiInkGroup>();
            AddCampPart(parts, AbilityPanel, root, AbilityPanel != null ? AbilityPanel.gameObject : null, true);
            AddCampPart(parts, DashPanel, root, Dash != null && Dash.Hit != null && Dash.Hit != DashPanel ? Dash.Hit.gameObject : null, true);
            // Строка эффектов над портретом (с 30.09; раньше — ряд значков зелий) гаснет целиком: у каждого
            // круга своя прозрачность, её ведёт он сам.
            Transform buffs = EffectRow != null ? EffectRow.transform
                : ResinChip != null ? ResinChip.transform.parent : SurgeChip != null ? SurgeChip.transform.parent : null;
            if (buffs != null && buffs != transform) AddCampPart(parts, buffs as RectTransform, root, null, false);
            // Дым под способностями и огонёк-разделитель у кувырка без самих плиток — пустая подложка.
            if (Strip != null)
            {
                AddCampPart(parts, Strip.Find("Дым под способностями") as RectTransform, root, null, true);
                AddCampPart(parts, Strip.Find("Разделитель") as RectTransform, root, null, true);
            }
            _campParts = parts.ToArray();
            // Части ушли из общей группы появления в свои: общая больше не трогает их чернила.
            if (root != null) root.Collect();

            bool hidden = appearing || root != null && root.Running;
            _campCombatShown = !hidden;
            foreach (CampPart part in _campParts)
            {
                if (hidden) { HideCampPartInstant(part); continue; }
                // Добавленные группы уже начали свой показ — встают сразу видимыми, как были.
                part.Group.alpha = 1f;
                if (part.Ink != null) part.Ink.ShowInstant();
            }
        }

        void AddCampPart(System.Collections.Generic.List<CampPart> parts, RectTransform node, UiInkGroup root, GameObject hover, bool ink)
        {
            if (node == null) return;
            var part = new CampPart { Hover = hover };
            part.Group = node.GetComponent<CanvasGroup>();
            if (part.Group == null) part.Group = node.gameObject.AddComponent<CanvasGroup>();
            // HUD не ловит лучи Canvas (мышь разбирает HitTest): группа их тоже не перехватывает.
            part.Group.blocksRaycasts = false;
            part.Group.interactable = false;
            if (ink) part.Ink = CampInkGroup(node, root);
            parts.Add(part);
        }

        /// <summary>
        /// Своя группа проявления части в темпе общего появления HUD. Добавленная группа сразу
        /// начинает показ (PlayOnEnable); дальше её ведёт только лагерь.
        /// </summary>
        static UiInkGroup CampInkGroup(RectTransform node, UiInkGroup root)
        {
            UiInkGroup ink = node.GetComponent<UiInkGroup>();
            if (ink == null)
            {
                ink = node.gameObject.AddComponent<UiInkGroup>();
                ink.Direction = UiInkGroup.Sweep.LeftToRight;
                if (root != null)
                {
                    ink.Duration = root.Duration;
                    ink.InkDuration = root.InkDuration;
                    ink.Curve = root.Curve;
                    ink.HideDuration = root.HideDuration;
                    ink.Burn = root.Burn;
                    ink.EdgeScale = root.EdgeScale;
                    // Лесенка общего появления рассчитана на всю ширину HUD; на один ряд — короче.
                    ink.Stagger = Mathf.Min(root.Stagger, .3f);
                }
            }
            ink.PlayOnEnable = false;
            ink.FirstTimeOnly = false;
            return ink;
        }

        void ShowCampPart(CampPart part)
        {
            UiMotion.Stop(part.Group);
            if (part.Hover != null && !part.Hover.activeSelf) part.Hover.SetActive(true);
            UiMotion.FadeTo(part.Group, 1f, CampShowFade);
            if (part.Ink == null) return;
            // Выключенная часть (кувырка ещё нет — DashPanel выключает Refresh) проявиться не может:
            // встаёт сразу, иначе её чернила так и остались бы спрятанными, когда она включится.
            if (part.Ink.isActiveAndEnabled) part.Ink.Show();
            else part.Ink.ShowInstant();
        }

        void HideCampPart(CampPart part)
        {
            UiMotion.Stop(part.Group);
            GameObject hover = part.Hover;
            // Вернулись в зону раньше, чем часть догасла, — выключать уже нечего.
            UiMotion.FadeTo(part.Group, 0f, CampHideFade, () => { if (!_campCombatShown && hover != null) hover.SetActive(false); });
            if (part.Ink != null && part.Ink.isActiveAndEnabled) part.Ink.Hide();
        }

        static void HideCampPartInstant(CampPart part)
        {
            UiMotion.Stop(part.Group);
            part.Group.alpha = 0f;
            if (part.Ink != null)
            {
                // Hide у выключенной группы прячет сразу, без движения; включение её не показывает (PlayOnEnable снят).
                bool enabled = part.Ink.enabled;
                part.Ink.enabled = false;
                part.Ink.Hide();
                part.Ink.enabled = enabled;
            }
            if (part.Hover != null) part.Hover.SetActive(false);
        }

        /// <summary>Кошелёк лагеря «золото · осколки»: только в лагере, числа — из Camp.</summary>
        void RefreshCampWallet(Camp camp)
        {
            if (CampWallet == null) return;
            bool show = camp != null;
            if (CampWallet.gameObject.activeSelf != show) CampWallet.gameObject.SetActive(show);
            if (!show) return;
            SetWalletValue(WalletGold, camp.Money(CurrencyType.Gold), ref _walletGold);
            SetWalletValue(WalletShards, camp.Money(CurrencyType.Shards), ref _walletShards);
        }

        static void SetWalletValue(TMPro.TMP_Text label, int value, ref int shown)
        {
            if (label == null || value == shown) return;
            // Первое число встаёт молча; дальше покупка или добыча толкает цифру.
            bool changed = shown >= 0;
            shown = value;
            label.text = value.ToString();
            if (changed) HudFx.Punch(label.transform, 1.18f, .35f);
        }
    }
}
