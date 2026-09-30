using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.View
{
    /// <summary>
    /// Общее у кузнеца и торговца на одной странице (ревью 29.09): клавиши окна и их подписи внизу,
    /// отмена подтверждения по Esc, отклик на сделку (толчок кошелька и вещи), цвета темы в тексте.
    /// Enter — основная кнопка, Del — разбор у кузнеца, Esc при ждущем подтверждении отменяет его,
    /// а не закрывает окно.
    /// </summary>
    public sealed partial class CampServicesView
    {
        /// <summary>Клавиши открытого окна лавки; true — Esc ушёл на отмену подтверждения.</summary>
        bool ShopKeys(bool cancel)
        {
            if (_view == null) return false;
            bool smith = _view.Smith.Group.gameObject.activeSelf, trader = _view.Trader.Group.gameObject.activeSelf;
            if (!smith && !trader) return false;
            if (cancel && CancelShopConfirm()) return true;
            if (_openGroup == null || !_openGroup.interactable) return false;
            if (CampServicesProbe.IsRunning && !CampServicesProbe.AllowInteractionInput) return false;
            bool enter = false, delete = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
                delete = keyboard.deleteKey.wasPressedThisFrame;
            }
#else
            enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            delete = Input.GetKeyDown(KeyCode.Delete);
#endif
            CampShopScreen s = smith ? _view.Smith : _view.Trader;
            var selected=EventSystem.current?.currentSelectedGameObject;
            var auxiliary=selected!=null?selected.GetComponent<Button>():null;
            if(enter && auxiliary!=null && (auxiliary==s.Back || auxiliary==s.Extra || auxiliary.name=="Развитие лагеря" || auxiliary.name=="Резерв и заказ"))
            {Press(auxiliary);return true;}
            // Пока ждёт подтверждение разбора или обновления товаров, Enter основную кнопку не жмёт: он
            // перековал бы или купил вместо ответа на вопрос. Отвечают тем же — Del или вторым щелчком, Esc — отмена.
            // Продажа подтверждается Enter: её и начали основной кнопкой.
            bool asking = smith ? _confirmDismantle : _confirmRefresh;
            if (enter && !asking) Press(s.Action);
            else if (delete && smith) Press(s.Extra);
            return false;
        }

        /// <summary>
        /// Нажатие с клавиатуры. Выбор EventSystem снимается: иначе тот же Enter отправил бы Submit
        /// выбранной ячейке, и её выбор сбросил бы только что поставленное подтверждение.
        /// </summary>
        static void Press(Button button)
        {
            if (button == null || !button.isActiveAndEnabled || !button.interactable) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            button.onClick.Invoke();
        }

        /// <summary>Снять ждущее подтверждение (разбор, продажа, обновление товаров); true — было что снимать.</summary>
        internal bool CancelShopConfirm()
        {
            bool any = false;
            if (_confirmDismantle)
            {
                _confirmDismantle = false;
                any = true;
                if (_smithCamp != null) RefreshSmith();
            }
            if (_confirmSale || _confirmRefresh)
            {
                _confirmSale = _confirmRefresh = false;
                any = true;
                if (_traderCamp != null) RefreshTraderPanel();
            }
            return any;
        }

        /// <summary>
        /// Подписи клавиш внизу окна: пустая строка прячет пару. Пока ждёт подтверждение, у Esc —
        /// «Отменить»: окно не закроется посреди решения.
        /// </summary>
        static void KeyHints(CampShopScreen s, string main, string second, bool confirming)
        {
            if (s.MainKey != null) s.MainKey.SetActive(main.Length > 0);
            if (s.MainKeyLabel != null) s.MainKeyLabel.text = main;
            if (s.SecondKey != null) s.SecondKey.SetActive(second.Length > 0);
            if (s.SecondKeyLabel != null) s.SecondKeyLabel.text = second;
            if (s.CloseKeyLabel != null) s.CloseKeyLabel.text = CampServiceText.Get(confirming ? "key.cancel" : "close.action");
        }

        /// <summary>Старые вкладки не нужны: всё на одной странице. Прячутся и у префаба до миграции.</summary>
        static void HideTabs(CampShopScreen s)
        {
            if (s.Tabs == null) return;
            foreach (Button tab in s.Tabs)
                if (tab != null && tab.gameObject.activeSelf) tab.gameObject.SetActive(false);
        }

        /// <summary>
        /// Толчок после сделки: число кошелька, вещь, строка свойства. Канал 3 — тот же, что у
        /// UiMotion.ScaleTo и наведения (UiHoverMotion): толчок заменяет их и кончается на единице.
        /// </summary>
        static void Punch(Component target, float strength = .16f)
        {
            if (target == null) return;
            Transform t = target.transform;
            UiMotion.Play(t, 3, .32f, k => t.localScale = Vector3.one * (1f + strength * Mathf.Sin(k * Mathf.PI)), AnimationCurve.Linear(0f, 0f, 1f, 1f));
        }

        static string Hex(UiTheme.Role role)
        {
            UiTheme theme = UiTheme.Current;
            return theme == null ? "#F4F7FB" : "#" + ColorUtility.ToHtmlStringRGB(theme.Get(role));
        }

        static string Paint(string text, UiTheme.Role role) => "<color=" + Hex(role) + ">" + text + "</color>";

        /// <summary>Цвет надписи — ролью темы (ThemeColor), чтобы смена темы не затёрла его.</summary>
        static void Tone(TMP_Text label, UiTheme.Role role)
        {
            if (label == null) return;
            var tint = label.GetComponent<ThemeColor>();
            if (tint != null) tint.SetRole(role);
            else if (UiTheme.Current != null) label.color = UiTheme.Current.Get(role);
        }

        static void Say(TMP_Text label, string text)
        {
            if (label != null) label.text = text;
        }

        static void Show(GameObject part, bool shown)
        {
            if (part != null && part.activeSelf != shown) part.SetActive(shown);
        }

        static string Format(string key, params object[] values) => string.Format(CampServiceText.Get(key), values);

        static string ShardsText(int count) => count + " " + CampServiceText.Get("unit.shards." + CampShopDeals.PluralForm(count));

        static string GoldText(int count) => count + " " + CampServiceText.Get("unit.gold");

        static string RarityName(ItemRarity rarity) => CampServiceText.Get(rarity == ItemRarity.Normal ? "trader.normal"
            : rarity == ItemRarity.Magic ? "trader.rare" : rarity == ItemRarity.Rare ? "trader.epic" : "trader.unique");

        /// <summary>«Редкий · Уровень 9»: редкость — её цветом.</summary>
        static string RarityAndLevel(in ItemInstance item) =>
            Paint(RarityName(item.Rarity), WcSlotState.RoleFor((int)item.Rarity)) + "  ·  " + CampServiceText.Get("smith.level") + " " + item.ItemLevel;

        /// <summary>Свойства вещи строками: неявное и аффиксы.</summary>
        static string Properties(GeneratedItem roll)
        {
            string text = "";
            if (roll.HasImplicit) text += StatLabel(roll.ImplicitStat) + "  " + Paint(AffixValue(roll.ImplicitValue, roll.ImplicitOp, roll.ImplicitStat), UiTheme.Role.Text) + "\n";
            for (int i = 0; i < roll.AffixCount; i++)
            {
                var affix = roll.GetAffix(i);
                text += StatLabel(affix.Stat) + "  " + Paint(AffixValue(affix.Value, affix.Op, affix.Stat), UiTheme.Role.Text) + "\n";
            }
            return text.TrimEnd('\n');
        }
    }
}
