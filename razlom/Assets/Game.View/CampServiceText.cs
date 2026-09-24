using System;
using UnityEngine;

namespace Game.View
{
    // Стабильные ключи позволяют добавлять переводы без изменения логики взаимодействия.
    public static class CampServiceText
    {
        public static readonly string[] SupportedLocales={"ru","en","es","pt","it","de","zh"};
        public static string Locale { get; private set; }="ru";
        [Serializable] public sealed class Entry { public string key,value; }
        [Serializable] public sealed class Table { public Entry[] entries; }
        static Table _translation;
        public static void SetLocale(string locale)
        {
            Locale=Array.IndexOf(SupportedLocales,locale)>=0?locale:"ru";
            var asset=Resources.Load<TextAsset>("Localization/Camp/"+Locale);
            _translation=asset!=null?JsonUtility.FromJson<Table>(asset.text):null;
        }
        public static string Get(string key)
        {
            if(_translation?.entries!=null)foreach(var e in _translation.entries)if(e.key==key && !string.IsNullOrEmpty(e.value))return e.value;
            switch(key)
            {
                case "npc.smith":return "Эни";
                case "npc.trader":return "Вен";
                case "npc.alchemist":return "Лео";
                case "npc.tent":return "Палатка";
                case "dialogue.smith.open":return "Ну, что тебе сковать?";
                case "dialogue.smith.reforge":return "Так то лучше...!";
                case "dialogue.smith.dismantle":return "Туда его, в металлолом";
                case "dialogue.trader.open":return "Ну, чего тебе?";
                case "dialogue.trader.buy":return "Будешь должен, отдал за бесценок";
                case "dialogue.trader.sell":return "Не хотелось бы за это платить... ну ладно";
                case "dialogue.trader.refresh":return "Посмотри ещё раз. Кое-что нашлось.";
                case "dialogue.alchemist.open":return "Ты ради меня даже реку перешел?";
                case "dialogue.alchemist.buy":return "Не тряси. Я серьёзно.";
                case "dialogue.alchemist.order.accept":return "Договорились. Возвращайся, когда справишься.";
                case "dialogue.alchemist.order.complete":return "Вот теперь можно и за работу.";
                case "dialogue.alchemist.recipe.unlock":return "Теперь смогу варить это постоянно.";
                case "open.hint":return "E / A · Открыть";
                case "talk.hint":return "E / A · Поговорить";
                case "approach.hint":return "ПКМ · Подойти";
                case "service.smith":return "Перековка и разбор снаряжения";
                case "service.trader":return "Покупка и продажа снаряжения";
                case "service.alchemist":return "Зелья здоровья и лавидия";
                case "interact":return "Поговорить";
                case "approach":return "Подойти";
                case "unreachable":return "Нет прохода к собеседнику";
                case "close":return "Завершить разговор";
                case "close.hint":return "Esc / B — закрыть";
                case "smith.reforge":return "Перековка";
                case "smith.dismantle":return "Разбор";
                case "smith.gold":return "золото";
                case "smith.shards":return "осколки";
                case "smith.bag.hint":return "Надетое можно перековать прямо здесь.\nРазбирать — только из сумки.";
                case "smith.choose":return "Выбери предмет";
                case "smith.level":return "Уровень";
                case "smith.attempts":return "Перековки";
                case "smith.protected":return "Предмет помечен «беречь».\nСними отметку в палатке для разбора.";
                case "smith.yield":return "Получишь:";
                case "smith.destroy.warning":return "Предмет будет уничтожен";
                case "smith.confirm":return "Подтвердить разбор";
                case "smith.received":return "Получено:";
                case "smith.done":return "Так то лучше...!";
                case "smith.error.InvalidItem":return "Выбери предмет";
                case "smith.error.Protected":return "Предмет защищён от разбора";
                case "smith.error.NoAffix":return "У этого предмета нет аффиксов.\nДля перековки нужна редкая вещь.";
                case "smith.error.AtMaximum":return "Характеристика уже на максимуме";
                case "smith.error.Exhausted":return "Все три перековки использованы";
                case "smith.error.InsufficientFunds":return "Не хватает золота или осколков";
                // Характеристики: полное имя, строка листа героя (.short) и пояснение (.hint) — StatText.
                case "stat.MaxHealth":return "Здоровье";
                case "stat.Damage":return "Урон";
                case "stat.AttackSpeed":return "Скорость атаки";
                case "stat.MoveSpeed":return "Скорость бега";
                case "stat.CritChance":return "Шанс крита";
                case "stat.CritMultiplier":return "Сила крита";
                case "stat.Armor":return "Броня";
                case "stat.FireResist":return "Сопротивление огню";
                case "stat.MaxLavidium":return "Запас лавидия";
                case "stat.LavidiumRegen":return "Восстановление лавидия";
                case "stat.AbilitySpeed":return "Скорость приёмов";
                case "stat.CooldownRecovery":return "Ускорение перезарядки";
                case "stat.MaxHealth.short":return "Здоровье";
                case "stat.Damage.short":return "Урон";
                case "stat.AttackSpeed.short":return "Скор. атаки";
                case "stat.MoveSpeed.short":return "Скор. бега";
                case "stat.CritChance.short":return "Шанс крита";
                case "stat.CritMultiplier.short":return "Сила крита";
                case "stat.Armor.short":return "Броня";
                case "stat.FireResist.short":return "Сопр. огню";
                case "stat.MaxLavidium.short":return "Лавидий";
                case "stat.LavidiumRegen.short":return "Лавидий/с";
                case "stat.AbilitySpeed.short":return "Скор. приёмов";
                case "stat.CooldownRecovery.short":return "Перезарядка";
                case "stat.MaxHealth.hint":return "Сколько урона Пелаг выдержит. Растёт с уровнем и от брони и талисманов.";
                case "stat.Damage.hint":return "Урон обычной атаки и основа урона способностей.";
                case "stat.AttackSpeed.hint":return "Сколько обычных ударов саблей в секунду.";
                case "stat.MoveSpeed.hint":return "Скорость бега в метрах в секунду.";
                case "stat.CritChance.hint":return "Шанс, что удар станет критическим.";
                case "stat.CritMultiplier.hint":return "Во сколько раз критический удар сильнее обычного: 150% — в полтора раза.";
                case "stat.Armor.hint":return "Снижает физический урон. Чем сильнее удар, тем меньше броня от него спасает.";
                case "stat.FireResist.hint":return "Снижает урон от огня. Предел — 75%.";
                case "stat.MaxLavidium.hint":return "Запас лавидия — ресурса способностей. Растёт с уровнем.";
                case "stat.LavidiumRegen.hint":return "Сколько лавидия возвращается каждую секунду.";
                case "stat.AbilitySpeed.hint":return "Способности исполняются быстрее: замах, удар и завершение. Перезарядку не меняет. Предел — вдвое быстрее.";
                case "stat.CooldownRecovery.hint":return "Способности перезаряжаются быстрее.";
                case "stat.sources":return "Из чего складывается";
                case "stat.level":return "Уровень";
                case "stat.buffs":return "Зелья и эффекты";
                case "trader.buy.tab":return "Товары";
                case "trader.sell.tab":return "Продать из сумки";
                case "trader.buy":return "Купить";
                case "trader.sell":return "Продать";
                case "trader.sell.confirm":return "Подтвердить продажу";
                case "trader.bag":return "Сумка";
                case "trader.sell.info":return "Надетое сначала сними в палатке.\nПродажа требует подтверждения.";
                case "trader.rare.chance":return "Шанс редкого товара при обновлении:";
                case "trader.stock.info":return "После босса — бесплатное обновление, шанс 25%.";
                case "trader.refresh":return "Обновить товары";
                case "trader.refresh.confirm":return "Обновить · шанс редкого 10%";
                case "trader.normal":return "Обычный";
                case "trader.rare":return "Редкий";
                case "trader.epic":return "Эпический";
                case "trader.unique":return "Уникальный";
                case "trader.compare":return "По сравнению с надетым:";
                case "trader.unchanged":return "Характеристики не меняются";
                case "trader.protected":return "Предмет помечен «беречь»";
                case "trader.full":return "Сумка заполнена";
                case "trader.funds":return "Не хватает золота";
                case "trader.sold":return "Не хотелось бы за это платить... ну ладно";
                case "trader.bought":return "Будешь должен, отдал за бесценок";
                case "trader.failed":return "Сделка не состоялась";
                case "trader.refreshed":return "Посмотри ещё раз. Кое-что нашлось.";
                case "potion.SmallHealth":return "Малое зелье здоровья";
                case "potion.LargeHealth":return "Большое зелье здоровья";
                case "potion.SmallLavidium":return "Малое зелье лавидия";
                case "potion.LargeLavidium":return "Большое зелье лавидия";
                case "potion.restore":return "Восстановит";
                case "potion.buy":return "Купить";
                case "potion.stock":return "В запасе";
                case "potion.selected":return "В быстром слоте";
                case "potion.select":return "В быстрый слот";
                case "potion.bought":return "Не тряси. Я серьёзно.";
                case "potion.failed":return "Не хватает золота или запас заполнен";
                case "potion.use.hint":return "ЛКМ · Выпить одну бутылку";
                case "potion.empty":return "Бутылки закончились";
                case "potion.switch.hint":return "ПКМ · Сменить размер";
                case "back":return "Назад";
                case "smith.bag.caption":return "Предметы в сумке";
                case "shop.worn":return "Надето";
                case "shop.worn.tag":return "надето";
                case "trader.worn.note":return "Надетое не продаётся.\nЧтобы продать, сними в палатке.";
                case "smith.affixes":return "Что перековать";
                case "smith.cost":return "Стоимость";
                case "trader.stock.caption":return "Товары";
                case "trader.bag.caption":return "Сумка";
                case "trader.price":return "Цена";
                case "potion.LivingResin":return "Живица";
                case "potion.LavidiumSurge":return "Лавидиевый порыв";
                case "potion.LivingResin.effect":return "20% здоровья и −25% входящего урона на 6 с";
                case "potion.LavidiumSurge.effect":return "20% лавидия и +20% скорости на 6 с";
                case "potion.health":return "здоровья";
                case "potion.lavidium":return "лавидия";
                case "potion.locked":return "Откроется заказом алхимика";
                case "close.action":return "Закрыть";
                case "role.smith":return "Кузнец";
                case "role.trader":return "Торговец";
                case "role.alchemist":return "Алхимик";
                case "role.tent":return "Снаряжение и сумка";
                case "action.open":return "Открыть";
                case "action.talk":return "Поговорить";
                case "action.approach":return "Подойти";
                case "order.resin.goal":return "Убей Лесного бутона в забеге или отдай редкую вещь";
                case "order.surge.goal":return "Пройди уровень Разлома без зелий или отдай 12 осколков";
                case "order.inwork":return "В работе:";
                case "order.ready":return "Заказ выполнен — сдай его Лео";
                case "order.accept":return "Взять заказ";
                case "order.turnin":return "Сдать заказ";
                case "order.resin.exchange":return "Отдать редкую вещь";
                case "order.surge.exchange":return "Отдать 12 осколков";
                case "order.confirm":return "Подтвердить обмен";
                case "order.norare":return "В сумке нет редкой вещи без отметки «беречь»";
                case "order.failed":return "Сейчас это не выйдет";
                case "order.done":return "Рецепт у Лео — зелье теперь продаётся во вкладке «Зелья».";
                case "order.state.available":return "Можно взять";
                case "order.state.inwork":return "В работе";
                case "order.state.ready":return "Выполнен";
                case "order.state.done":return "Рецепт открыт";
                case "alchemy.tab.potions":return "Зелья";
                case "alchemy.tab.recipes":return "Рецепты";
                case "potion.recipe":return "Нужен рецепт · вкладка «Рецепты»";
                default:return key;
            }
        }
    }
}
