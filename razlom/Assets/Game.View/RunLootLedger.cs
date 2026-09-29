using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Строка добычи под панелью забега (выбор владельца 30.09 — кадр b1-loot-strip-under-panel:
    /// «примерно так, только доработать для нас с максимальным удобством»). Что унесёшь из Разлома:
    /// золото и вещи. Здесь чистая логика без Unity (проверяется в Combat.Presentation.Tests): какие
    /// награды уносятся, в каком порядке стоят значки, сколько их влезает до середины экрана и сколько
    /// уходит в «+K», какие вещи не поместятся в сумку лагеря. Вид — RunHud.Loot, раскладка —
    /// RunHudWcBuilder (миграции v1–v2).
    ///
    /// Уносятся только вещи и золото — ровно то, что GameSession.FinishRun кладёт в лагерь.
    /// Артефакт, способность, усиление, родник и прибавка характеристики живут только в забеге.
    /// </summary>
    public sealed class RunLootLedger
    {
        /// <summary>Столько взятых наград помнит забег (RiftRun.MaxTakenRewards): больше вещей не бывает.</summary>
        public const int MaxEntries = 64;

        /// <summary>Вещь в строке: основа, редкость (как ItemRarity: 0 обычная … 3 уникальная), уровень.</summary>
        public struct Entry
        {
            public int BaseId;
            public int Rarity;
            public int Level;
            /// <summary>Номер среди всех взятых наград забега (RiftRun.GetTaken).</summary>
            public int TakenIndex;
        }

        readonly Entry[] _entries = new Entry[MaxEntries];

        /// <summary>Вещей в строке — по порядку взятия, первая — самая старая.</summary>
        public int Count { get; private set; }

        /// <summary>Сколько взятых наград уже просмотрено (и вещей, и остального).</summary>
        public int Scanned { get; private set; }

        public Entry this[int index] => _entries[index];

        /// <summary>Новый забег: строка пустая.</summary>
        public void Reset()
        {
            Count = 0;
            Scanned = 0;
        }

        /// <summary>Уходит ли награда в лагерь: только вещь (GameSession.FinishRun).</summary>
        public static bool Carried(in RewardOffer offer) => offer.Kind == RewardKind.Item && !offer.Item.IsEmpty;

        /// <summary>
        /// Следующая взятая награда забега по порядку. true — это вещь, строка выросла. Звать подряд
        /// для RiftRun.GetTaken(Scanned), пока Scanned меньше TakenRewardCount.
        /// </summary>
        public bool Take(in RewardOffer offer)
        {
            int index = Scanned++;
            if (!Carried(offer) || Count >= MaxEntries) return false;
            _entries[Count++] = new Entry
            {
                BaseId = offer.Item.BaseId,
                Rarity = Math.Max(0, Math.Min(3, (int)offer.Item.Rarity)),
                Level = offer.Item.ItemLevel,
                TakenIndex = index,
            };
            return true;
        }

        /// <summary>
        /// Поместится ли вещь <paramref name="entry"/> в сумку лагеря: FinishRun кладёт вещи по порядку
        /// взятия, и всё сверх свободных мест теряется. <paramref name="bagFree"/> меньше нуля — не знаем
        /// (сумки нет), считаем, что влезет.
        /// </summary>
        public static bool Fits(int entry, int bagFree) => bagFree < 0 || entry < bagFree;

        /// <summary>Сколько вещей строки не влезет в сумку при <paramref name="bagFree"/> свободных местах.</summary>
        public int Overflowing(int bagFree) => bagFree < 0 ? 0 : Math.Max(0, Count - bagFree);

        /// <summary>
        /// Что видно в ряду: <see cref="First"/> — первая показанная вещь, <see cref="Shown"/> — сколько
        /// значков, <see cref="Hidden"/> — сколько самых старых ушло в «+K» (0 — плашки нет). Новые — справа.
        /// </summary>
        public readonly struct Window
        {
            public readonly int First, Shown, Hidden;

            public Window(int first, int shown, int hidden)
            {
                First = first;
                Shown = shown;
                Hidden = hidden;
            }

            public bool HasMore => Hidden > 0;
            /// <summary>Мест в ряду: значки и плашка «+K».</summary>
            public int Slots => Shown + (Hidden > 0 ? 1 : 0);
        }

        /// <summary>Меньше двух мест не бывает: без этого «+K» не с чем было бы показать.</summary>
        public const int MinSlots = 2;

        /// <summary>
        /// Ряд из <paramref name="count"/> вещей на <paramref name="slots"/> мест. Влезают все — все по
        /// порядку. Не влезают — последние (slots − 1), а первое место занимает «+K» со старыми.
        /// </summary>
        public static Window WindowFor(int count, int slots)
        {
            count = Math.Max(0, count);
            slots = Math.Max(MinSlots, slots);
            if (count <= slots) return new Window(0, count, 0);
            int shown = slots - 1;
            return new Window(count - shown, shown, count - shown);
        }

        /// <summary>
        /// Сколько мест (значков и «+K») помещается в ряд, чтобы строка не заходила в середину экрана:
        /// там полоса босса и плашка «Новый уровень» шириной до 680 единиц, по <see cref="Layout.CentreHalf"/>
        /// в обе стороны от середины. <paramref name="canvasWidth"/> — ширина холста в единицах после
        /// масштаба интерфейса (1920 при 100% и 16:9, 1440 при 120% и 16:10), <paramref name="rowStart"/> —
        /// левый край первого значка от левого края экрана. Справа за последним значком — место под «+1».
        /// </summary>
        public static int Capacity(float canvasWidth, float rowStart, int maxSlots)
        {
            float room = canvasWidth * .5f - Layout.CentreHalf - rowStart - Layout.ArrivalReserve;
            int fit = room < Layout.Icon ? 0 : 1 + (int)Math.Floor((room - Layout.Icon) / Layout.Pitch);
            return Math.Max(MinSlots, Math.Min(Math.Max(MinSlots, maxSlots), fit));
        }

        /// <summary>
        /// Раскладка строки в единицах холста 1920×1080 — общая у миграции префаба (RunHudWcBuilder)
        /// и у вида (RunHud.Loot). Строка — от левого верхнего угла, под панелью забега.
        /// </summary>
        public static class Layout
        {
            /// <summary>Значок вещи — круг; шаг ряда — значок и зазор.</summary>
            public const float Icon = 40f, Gap = 8f, Pitch = Icon + Gap;
            /// <summary>Высота строки: круги и дым вокруг.</summary>
            public const float Height = 44f;
            /// <summary>Знак золота от левого края строки: центр и размер; число — от <see cref="GoldX"/>.</summary>
            public const float CoinX = 16f, Coin = 28f, GoldX = 36f;
            /// <summary>Число золота не уже трёх цифр — ряд не прыгает на 9 → 10 → 100.</summary>
            public const float GoldMinWidth = 34f;
            /// <summary>Черта между золотом и вещами: отступ от числа и от черты до первого круга.</summary>
            public const float DividerPad = 12f, RowPad = 12f;
            /// <summary>За последним кругом — «+1» новой вещи.</summary>
            public const float ArrivalReserve = 26f;
            /// <summary>Поле справа от содержимого: дым строки тает за последним кругом.</summary>
            public const float RightPad = 18f;
            /// <summary>
            /// Запретная середина экрана — по столько единиц в обе стороны от центра: полоса босса (680)
            /// и «Новый уровень» (до 680) плюс поле 16.
            /// </summary>
            public const float CentreHalf = 340f + 16f;
            /// <summary>Сколько кругов в префабе (пул): больше в ряд не ставится при любом масштабе.</summary>
            public const int PoolSlots = 8;

            /// <summary>Левый край первого круга от левого края строки при ширине числа <paramref name="goldWidth"/>.</summary>
            public static float RowStart(float goldWidth) => GoldX + Math.Max(GoldMinWidth, goldWidth) + DividerPad + 1f + RowPad;

            /// <summary>Центр черты от левого края строки.</summary>
            public static float DividerX(float goldWidth) => GoldX + Math.Max(GoldMinWidth, goldWidth) + DividerPad;

            /// <summary>Ширина строки с <paramref name="slots"/> местами (0 — только золото).</summary>
            public static float Width(float goldWidth, int slots) => slots <= 0
                ? GoldX + Math.Max(GoldMinWidth, goldWidth) + RightPad
                : RowStart(goldWidth) + (slots - 1) * Pitch + Icon + RightPad;
        }

        /// <summary>
        /// Число золота досчитывает до нового за <see cref="Duration"/> с, быстро в начале и мягко в
        /// конце — как итоги забега. Меньше, чем было, бывает только у нового забега: сразу.
        /// </summary>
        public sealed class GoldCounter
        {
            public float Duration = .6f;
            int _from, _to;
            float _t = 1f;

            /// <summary>Показанное сейчас число.</summary>
            public int Shown { get; private set; }

            /// <summary>Цель счёта.</summary>
            public int Target => _to;

            public bool Counting => _t < 1f;

            /// <summary>Сразу показать <paramref name="value"/>, без счёта.</summary>
            public void Snap(int value)
            {
                _from = _to = Shown = value;
                _t = 1f;
            }

            /// <summary>Новая цель. true — счёт начался (было меньше); меньше прежнего — сразу, без счёта.</summary>
            public bool Retarget(int target)
            {
                if (target == _to) return false;
                if (target < Shown) { Snap(target); return false; }
                _from = Shown;
                _to = target;
                _t = 0f;
                return true;
            }

            /// <summary>Шаг часов интерфейса; возвращает показанное число.</summary>
            public int Advance(float dt)
            {
                if (_t >= 1f) return Shown;
                _t = Duration <= 0f ? 1f : Math.Min(1f, _t + Math.Max(0f, dt) / Duration);
                float k = 1f - (1f - _t) * (1f - _t) * (1f - _t);
                Shown = _t >= 1f ? _to : _from + (int)Math.Round((_to - _from) * k);
                return Shown;
            }
        }
    }
}
