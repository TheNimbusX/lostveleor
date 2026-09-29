using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Правила окон кузнеца и торговца на одной странице (ревью владельца 29.09, кадры «Кузнец А» и
    /// «Торговец А»): что случится по нажатию, сколько это стоит и почему кнопка сейчас не нажимается.
    /// Окно пишет это до клика — цена, выход, нехватка, запреты. Без Unity: проверяется в
    /// Combat.Presentation.Tests. Сами сделки делает Camp, здесь только предпросмотр.
    /// </summary>
    public static class CampShopDeals
    {
        /// <summary>Почему действие недоступно; None — можно.</summary>
        public enum Block
        {
            None,
            /// <summary>Ничего не выбрано.</summary>
            NoItem,
            /// <summary>Надетое: разбирать и продавать можно только из сумки.</summary>
            Worn,
            /// <summary>Отметка «беречь».</summary>
            Protected,
            /// <summary>У обычной вещи нечего перековывать.</summary>
            NoAffix,
            /// <summary>Выбранное свойство уже на пределе.</summary>
            AtMaximum,
            /// <summary>Три перековки использованы.</summary>
            Exhausted,
            /// <summary>Не хватает золота или осколков.</summary>
            Funds,
            BagFull,
            /// <summary>Товар с прилавка уже купили — до обновления товаров место пустое.</summary>
            SoldOut,
            Invalid,
        }

        public const int ReforgeLimit = 3;

        /// <summary>Перековка выбранного свойства: цена, нехватка, предел и уровень вещи после.</summary>
        public struct Reforge
        {
            public Block Block;
            public int Gold, Shards;
            /// <summary>Сколько не хватает; 0 — хватает.</summary>
            public int GoldShort, ShardsShort;
            public int LevelFrom, LevelTo;
            /// <summary>Сколько перековок уже сделано (из <see cref="ReforgeLimit"/>).</summary>
            public int Used;
            /// <summary>Пределы нового значения свойства (есть при None и Funds).</summary>
            public Fix64 Lower, Upper;
            public bool Allowed => Block == Block.None;
            /// <summary>Цену показывать: перековка в принципе возможна, пусть и не по карману.</summary>
            public bool ShowsCost => Block == Block.None || Block == Block.Funds || Block == Block.AtMaximum;
        }

        /// <summary>Разбор: сколько осколков выйдет.</summary>
        public struct Dismantle
        {
            public Block Block;
            public int Shards;
            public bool Allowed => Block == Block.None;
        }

        /// <summary>Покупка или продажа: цена, нехватка и золото после сделки.</summary>
        public struct Deal
        {
            public Block Block;
            public bool Sell;
            public int Price;
            public int GoldShort;
            public int GoldAfter;
            public bool Allowed => Block == Block.None;
        }

        static ItemInstance Pick(Camp camp, int slot, bool worn)
        {
            if (slot < 0) return default;
            if (worn) return slot < (int)EquipSlot.Count ? camp.Worn.Worn((EquipSlot)slot) : default;
            return slot < camp.Bag.Capacity ? camp.Bag.At(slot) : default;
        }

        public static Reforge PlanReforge(Camp camp, int slot, bool worn, int affix)
        {
            var plan = new Reforge();
            ItemInstance item = Pick(camp, slot, worn);
            if (item.IsEmpty) { plan.Block = Block.NoItem; return plan; }
            plan.Used = item.ReforgeCount;
            plan.Gold = Camp.ReforgeGold(item);
            plan.Shards = Camp.ReforgeShards(item);
            plan.GoldShort = System.Math.Max(0, plan.Gold - camp.Money(CurrencyType.Gold));
            plan.ShardsShort = System.Math.Max(0, plan.Shards - camp.Money(CurrencyType.Shards));
            plan.LevelFrom = item.ItemLevel;
            // Как Camp.TryPayReforge: уровень растёт на 1 + редкость.
            plan.LevelTo = item.ItemLevel + 1 + (int)item.Rarity;
            SmithResult result = worn
                ? camp.ReforgeRange((EquipSlot)slot, affix, out plan.Lower, out plan.Upper)
                : camp.ReforgeRange(slot, affix, out plan.Lower, out plan.Upper);
            switch (result)
            {
                case SmithResult.Success: plan.Block = plan.GoldShort > 0 || plan.ShardsShort > 0 ? Block.Funds : Block.None; break;
                case SmithResult.NoAffix: plan.Block = Block.NoAffix; break;
                case SmithResult.AtMaximum: plan.Block = Block.AtMaximum; break;
                case SmithResult.Exhausted: plan.Block = Block.Exhausted; break;
                default: plan.Block = Block.Invalid; break;
            }
            return plan;
        }

        /// <summary>
        /// Свойство, выбранное при открытии вещи: первое, которое ещё можно перековать (не на пределе),
        /// иначе первое. Раньше всегда бралось первое — и кнопка встречала отказом «уже на максимуме».
        /// </summary>
        public static int DefaultAffix(Camp camp, int slot, bool worn, int affixCount)
        {
            for (int i = 0; i < affixCount; i++)
            {
                SmithResult result = worn
                    ? camp.ReforgeRange((EquipSlot)slot, i, out _, out _)
                    : camp.ReforgeRange(slot, i, out _, out _);
                if (result == SmithResult.Success) return i;
            }
            return 0;
        }

        public static Dismantle PlanDismantle(Camp camp, int slot, bool worn)
        {
            var plan = new Dismantle();
            ItemInstance item = Pick(camp, slot, worn);
            if (item.IsEmpty) { plan.Block = Block.NoItem; return plan; }
            plan.Shards = Inventory.ShardsFor(item);
            if (worn) plan.Block = Block.Worn;
            else if (camp.Bag.IsKept(slot)) plan.Block = Block.Protected;
            return plan;
        }

        public static Deal PlanBuy(Camp camp, int index)
        {
            var deal = new Deal();
            int gold = camp.Money(CurrencyType.Gold);
            deal.GoldAfter = gold;
            if (index < 0 || index >= camp.TraderStockCount) { deal.Block = Block.NoItem; return deal; }
            ItemInstance item = camp.TraderStock(index);
            if (item.IsEmpty) { deal.Block = Block.SoldOut; return deal; }
            deal.Price = camp.BuyPriceOf(item);
            deal.GoldShort = System.Math.Max(0, deal.Price - gold);
            deal.GoldAfter = gold - deal.Price;
            // Как Camp.BuyFromTrader: место в сумке проверяется раньше денег.
            if (camp.Bag.IsFull) deal.Block = Block.BagFull;
            else if (deal.GoldShort > 0) deal.Block = Block.Funds;
            return deal;
        }

        public static Deal PlanSell(Camp camp, int slot, bool worn)
        {
            var deal = new Deal { Sell = true };
            int gold = camp.Money(CurrencyType.Gold);
            deal.GoldAfter = gold;
            ItemInstance item = Pick(camp, slot, worn);
            if (item.IsEmpty) { deal.Block = Block.NoItem; return deal; }
            deal.Price = Camp.PriceOf(item);
            deal.GoldAfter = gold + deal.Price;
            if (worn) deal.Block = Block.Worn;
            else if (camp.Bag.IsKept(slot)) deal.Block = Block.Protected;
            return deal;
        }

        /// <summary>
        /// Форма русского числительного: 0 — «1 осколок», 1 — «2 осколка», 2 — «5 осколков»
        /// (11–14 — всегда третья).
        /// </summary>
        public static int PluralForm(int n)
        {
            n = System.Math.Abs(n);
            int tens = n % 100, ones = n % 10;
            if (tens >= 11 && tens <= 14) return 2;
            if (ones == 1) return 0;
            if (ones >= 2 && ones <= 4) return 1;
            return 2;
        }

        /// <summary>
        /// Масштаб раскладки 1920×1080 в холсте <paramref name="width"/>×<paramref name="height"/>
        /// (единицы холста после масштаба интерфейса): больше единицы не растёт — при 80% окно меньше,
        /// как и весь интерфейс; при 120% и 16:10 ужимается целиком, чтобы ничего не уходило за край.
        /// </summary>
        public static float FitScale(float width, float height, float designWidth = 1920f, float designHeight = 1080f)
        {
            if (width <= 0f || height <= 0f || designWidth <= 0f || designHeight <= 0f) return 1f;
            float scale = System.Math.Min(width / designWidth, height / designHeight);
            return scale < 1f ? scale : 1f;
        }
    }
}
