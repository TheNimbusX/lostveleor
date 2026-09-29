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
    public static partial class CombatHudWcBuilder
    {
        const string WalletName = "Кошелёк лагеря";
        const float WalletWidth = 250f, WalletHeight = 44f, WalletGap = 14f;

        /// <summary>
        /// Кошелёк лагеря (владелец 29.09: в лагере HUD — портрет с полосами, карта, валюта и зелья):
        /// «осколки · золото» слева от карты, у её верхней кромки — там же, где кошелёк в окнах лагеря
        /// (CampShopsWcBuilder.Wallet: белые знаки в краске темы, число золота — цветом монет). Подложка
        /// — клуб дыма, как у подписи карты; числа — Nunito. В префабе выключен: показ и числа ведёт
        /// лагерь (CombatHudView.Camp), при каждом включении кошелёк проявляется своей группой.
        /// </summary>
        static void BuildCampWallet(RectTransform root, CombatHudView view)
        {
            var topRight = new Vector2(1f, 1f);
            RectTransform frame = view.MinimapFrame;
            // Левая и верхняя кромки карты от правого верхнего угла экрана.
            float mapLeft = frame != null ? frame.anchoredPosition.x - frame.pivot.x * frame.sizeDelta.x : -MapMargin - MapSize;
            float mapTop = frame != null ? frame.anchoredPosition.y + (1f - frame.pivot.y) * frame.sizeDelta.y : -MapMargin;
            RectTransform wallet = Box(Node(WalletName, root), topRight, topRight, new Vector2(mapLeft - WalletGap, mapTop - 4f),
                new Vector2(WalletWidth, WalletHeight));
            UiInkKit.SmokeLayer(wallet, "Дым", "smoke_band_2", 1f, 40f, 20f);
            view.WalletShards = WalletEntry(wallet, "Осколки", "shards", 14f, 108f, Role.Text);
            view.WalletGold = WalletEntry(wallet, "Золото", "gold", 132f, 108f, Role.Coins);
            UiInkGroup group = UiInkKit.Group(wallet, UiInkGroup.Sweep.LeftToRight, .4f, .16f);
            group.PlayOnEnable = true;
            // Кошелёк висит над ходьбой по лагерю: мышь над ним — HUD (CombatHudView.HitTestCampCalm).
            wallet.gameObject.SetActive(false);
            view.CampWallet = wallet;
        }

        /// <summary>Знак валюты и число справа от него; <paramref name="x"/> — от левого края кошелька.</summary>
        static TMP_Text WalletEntry(RectTransform wallet, string name, string art, float x, float width, Role number)
        {
            var left = new Vector2(0f, .5f);
            RectTransform box = Box(Node(name, wallet), left, left, new Vector2(x, 0f), new Vector2(width, 36f));
            RectTransform iconRect = Box(Node("Значок", box), left, left, Vector2.zero, new Vector2(30f, 30f));
            var icon = iconRect.gameObject.AddComponent<RawImage>();
            icon.texture = WalletArt(art);
            icon.raycastTarget = false;
            // С 26 сентября знаки — белые силуэты, краску даёт тема; материал рисунка — без дымки.
            Tint(icon, Role.Text);
            icon.material = UiInkKit.Art;
            UiInkKit.Inked(icon, delay: .08f);
            RectTransform numberRect = Box(Node("Число", box), left, left, new Vector2(36f, 0f), new Vector2(width - 36f, 36f));
            TMP_Text value = UiInkKit.Label(numberRect, "Надпись", "0", FontRole.Body, 20f, number, TextAlignmentOptions.MidlineLeft, 0f, 1f, .16f);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.fontStyle = FontStyles.Bold;
            Shadowed(value);
            return value;
        }

        /// <summary>Знаки валют — те же, что в окнах лагеря: осколки из CampShops, золото из значков забега.</summary>
        static Texture WalletArt(string name)
        {
            Texture art = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/CampShops/" + name + ".png")
                          ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/RunIcons/" + name + ".png");
            if (art == null) Debug.LogWarning("[ui-kit] Нет знака валюты «" + name + "» для кошелька лагеря");
            return art;
        }
    }
}
