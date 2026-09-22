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
                case "npc.smith":return "Кузнец";
                case "npc.trader":return "Торговец";
                case "npc.alchemist":return "Алхимик";
                case "npc.tent":return "Палатка";
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
                case "smith.bag.hint":return "Предметы из сумки\nНадетое сначала сними в палатке";
                case "smith.choose":return "Выбери предмет";
                case "smith.level":return "Уровень";
                case "smith.attempts":return "Перековки";
                case "smith.protected":return "Предмет помечен «беречь».\nСними отметку в палатке для разбора.";
                case "smith.yield":return "Получишь:";
                case "smith.destroy.warning":return "Предмет будет уничтожен";
                case "smith.confirm":return "Подтвердить разбор";
                case "smith.received":return "Получено:";
                case "smith.done":return "Перековка завершена.";
                case "smith.error.InvalidItem":return "Выбери предмет из сумки";
                case "smith.error.Protected":return "Предмет защищён от разбора";
                case "smith.error.NoAffix":return "У этого предмета нет аффиксов.\nДля перековки нужна редкая вещь.";
                case "smith.error.AtMaximum":return "Характеристика уже на максимуме";
                case "smith.error.Exhausted":return "Все три перековки использованы";
                case "smith.error.InsufficientFunds":return "Не хватает золота или осколков";
                case "stat.MaxHealth":return "Здоровье";
                case "stat.Damage":return "Урон";
                case "stat.AttackSpeed":return "Скорость атаки";
                case "stat.MoveSpeed":return "Скорость движения";
                case "stat.CritChance":return "Шанс критического удара";
                case "stat.CritMultiplier":return "Критический урон";
                case "stat.Armor":return "Броня";
                case "stat.FireResist":return "Сопротивление огню";
                case "stat.MaxLavidium":return "Запас лавидия";
                case "stat.LavidiumRegen":return "Лавидий в секунду";
                case "stat.AbilitySpeed":return "Скорость способностей";
                case "stat.CooldownRecovery":return "Восстановление способностей";
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
                case "trader.sold":return "Предмет продан";
                case "trader.bought":return "Покупка в сумке";
                case "trader.failed":return "Сделка не состоялась";
                case "trader.refreshed":return "Ассортимент обновлён";
                case "potion.SmallHealth":return "Малое зелье здоровья";
                case "potion.LargeHealth":return "Большое зелье здоровья";
                case "potion.SmallLavidium":return "Малое зелье лавидия";
                case "potion.LargeLavidium":return "Большое зелье лавидия";
                case "potion.restore":return "Восстановит";
                case "potion.buy":return "Купить";
                case "potion.stock":return "В запасе";
                case "potion.selected":return "В быстром слоте";
                case "potion.select":return "В быстрый слот";
                case "potion.bought":return "Зелье добавлено в запас";
                case "potion.failed":return "Не хватает золота или запас заполнен";
                case "potion.use.hint":return "ЛКМ · Выпить одну бутылку";
                case "potion.empty":return "Бутылки закончились";
                case "potion.switch.hint":return "ПКМ · Сменить размер";
                default:return key;
            }
        }
    }
}
