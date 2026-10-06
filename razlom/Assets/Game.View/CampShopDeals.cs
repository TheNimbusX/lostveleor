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
            /// <summary>Такого свойства у вещи нет (у обычной закаляется только базовое).</summary>
            NoAffix,
            /// <summary>Выбранное свойство уже на пределе.</summary>
            AtMaximum,
            /// <summary>Попытки закалки израсходованы и рискованный удар недоступен.</summary>
            Exhausted,
            /// <summary>Не хватает золота или осколков.</summary>
            Funds,
            BagFull,
            /// <summary>Товар с прилавка уже купили — до обновления товаров место пустое.</summary>
            SoldOut,
            Invalid,
            /// <summary>Три трещины: вещь расколота, больше не куётся.</summary>
            Shattered,
            /// <summary>Шедевр: дальше только сердце.</summary>
            Masterpiece,
            /// <summary>У Эни ждёт оплаченный выбор переплавки или добавления.</summary>
            Session,
        }

        /// <summary>Закалка выбранного свойства (06.10): цена, нехватка, попытки, трещины, рост и риск первого удара.</summary>
        public struct Temper
        {
            public Block Block;
            public int Gold, Shards;
            /// <summary>Сколько не хватает; 0 — хватает.</summary>
            public int GoldShort, ShardsShort;
            /// <summary>Попыток потрачено и всего по редкости (Camp.TemperAttempts), трещин на вещи.</summary>
            public int Used, Limit, Cracks;
            /// <summary>Рост первого удара в долях диапазона и шанс трещины этого удара.</summary>
            public Fix64 Growth;
            public int RiskPercent;
            /// <summary>Попытки кончились: это рискованный удар (шедевр или осколки).</summary>
            public bool Risky;
            public bool Allowed => Block == Block.None;
            /// <summary>Цену показывать: закалка в принципе возможна, пусть и не по карману.</summary>
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

        static ForgeTarget Target(int slot, bool worn) => worn ? ForgeTarget.Worn((EquipSlot)slot) : ForgeTarget.Bag(slot);

        /// <summary>
        /// Закалка свойства property (−1 — базовое свойство обычной вещи) по Camp.Quote:
        /// окно и кузница не могут разойтись в цене и запрете. Уровень вещи закалка не меняет.
        /// </summary>
        public static Temper PlanTemper(Camp camp, int slot, bool worn, int property)
        {
            var plan = new Temper();
            ItemInstance item = Pick(camp, slot, worn);
            if (item.IsEmpty) { plan.Block = Block.NoItem; return plan; }
            plan.Used = camp.AttemptsUsed(item);
            plan.Limit = Camp.TemperAttempts(item.Rarity);
            plan.Cracks = camp.CrackCount(item);
            var quote = camp.Quote(EniAction.Temper, Target(slot, worn), property);
            plan.Gold = quote.Gold; plan.Shards = quote.Shards;
            plan.GoldShort = System.Math.Max(0, plan.Gold - camp.Money(CurrencyType.Gold));
            plan.ShardsShort = System.Math.Max(0, plan.Shards - camp.Money(CurrencyType.Shards));
            plan.Growth = quote.NextGrowth; plan.RiskPercent = quote.RiskPercent; plan.Risky = quote.Risky;
            switch (quote.Status)
            {
                case SmithResult.Success: plan.Block = plan.GoldShort > 0 || plan.ShardsShort > 0 ? Block.Funds : Block.None; break;
                case SmithResult.NoAffix: plan.Block = Block.NoAffix; break;
                case SmithResult.AtMaximum: plan.Block = Block.AtMaximum; break;
                case SmithResult.Exhausted: plan.Block = Block.Exhausted; break;
                case SmithResult.Shattered: plan.Block = Block.Shattered; break;
                case SmithResult.Masterpiece: plan.Block = Block.Masterpiece; break;
                case SmithResult.SessionOpen: plan.Block = Block.Session; break;
                default: plan.Block = Block.Invalid; break;
            }
            return plan;
        }

        /// <summary>
        /// Свойство, выбранное при открытии вещи: у обычной — базовое (−1), у остальных — первое,
        /// которое ещё можно закалить (не на пределе), иначе первое. Раньше всегда бралось первое —
        /// и кнопка встречала отказом «уже на максимуме».
        /// </summary>
        public static int DefaultAffix(Camp camp, int slot, bool worn, int affixCount)
        {
            ItemInstance item = Pick(camp, slot, worn);
            if (!item.IsEmpty && item.Rarity == ItemRarity.Normal) return -1;
            for (int i = 0; i < affixCount; i++)
                if (camp.Quote(EniAction.Temper, Target(slot, worn), i).Status == SmithResult.Success) return i;
            return 0;
        }

        public static Dismantle PlanDismantle(Camp camp, int slot, bool worn)
        {
            var plan = new Dismantle();
            ItemInstance item = Pick(camp, slot, worn);
            if (item.IsEmpty) { plan.Block = Block.NoItem; return plan; }
            // Как Camp.Dismantle: с бонусом за закалку и «Знатоком рун».
            plan.Shards = camp.SalvageShards(item);
            if (worn) plan.Block = Block.Worn;
            else if (camp.Bag.IsKept(slot)) plan.Block = Block.Protected;
            else if (camp.Session.IsOpen && camp.Session.Target.Same(ForgeTarget.Bag(slot))) plan.Block = Block.Session;
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
