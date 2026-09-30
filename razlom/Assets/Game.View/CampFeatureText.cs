using Game.Sim;

namespace Game.View
{
    public static class CampFeatureText
    {
        public static string Starter(int pool) => pool == 1 ? "Рассекающий" : pool == 3 ? "Шквал" : pool == 2 ? "Ладно смазал" : "Вихрь";
        public static string GiftName(CampGift gift)
        {
            switch ((int)gift)
            {
                case 1: return "Сухой паёк";
                case 2: return "Точило Эни";
                case 3: return "Лёгкая поклажа Вена";
                case 4: return "Морской узел";
                case 5: return "Резервный план";
                case 6: return "Запасная фляга Лео";
                default: return "Выбери дар";
            }
        }
        public static string GiftEffect(CampGift gift)
        {
            switch ((int)gift)
            {
                case 1: return "+12% максимального здоровья, −6% движения на этот поход.";
                case 2: return "+12% урона на первых трёх аренах.";
                case 3: return "+10% движения, −8% урона на этот поход.";
                case 4: return "Следующая обычная атака, начатая в течение двух секунд после кувырка, получает +25% урона.";
                case 5: return "Один бесплатный переброс обычной награды за поход.";
                case 6: return "Первое применение выбранного зелья не уменьшает запас.";
                default: return "Дар действует в этом походе и не занимает слот артефакта.";
            }
        }
        public static string PotionName(PotionKind kind) => (int)kind == 6 ? "Смешанный отвар" : (int)kind == 7 ? "Ясный настой" : CampServiceText.Get("potion." + kind);
        public static string PotionEffect(PotionKind kind)
        {
            switch ((int)kind)
            {
                case 0: return "Восстанавливает 10% здоровья.";
                case 1: return "Восстанавливает 30% здоровья.";
                case 2: return "Восстанавливает 10% лавидия.";
                case 3: return "Восстанавливает 30% лавидия.";
                case 4: return "20% здоровья и −25% входящего урона на шесть секунд.";
                case 5: return "20% лавидия и +20% скорости движения и навыков на шесть секунд.";
                case 6: return "Восстанавливает 18% здоровья и 18% лавидия.";
                case 7: return "Снимает корни и замедление, защищает от них две секунды.";
                default: return "";
            }
        }
    }
}
