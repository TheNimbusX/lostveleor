using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Имена и картинки основ вещей — одни на лагерь (палатка, лавки) и на награды забега.
    /// Раньше каталог жил только в палатке, и карточка награды подписывала любую вещь
    /// «Ржавый меч» или «Кожаная куртка» без картинки (аудит UI, 25 сентября).
    ///
    /// Первый набор (владелец, 21 сентября): у каждой основы своя картинка, редкие богаче внешне,
    /// редкость дополнительно показывает рамка. Файлы — Resources/UI/Items/{ключ}.png, исходники ART/itmes.
    /// Имя — ключ локализации item.{ключ}, русский текст здесь запасной.
    /// </summary>
    public static class ItemTexts
    {
        static readonly string[,] Catalog =
        {
            { "rusty_sword", "Старая сабля" }, { "boarding_cutlass", "Абордажный тесак" }, { "duelist_sabre", "Сабля дуэлянта" }, { "officer_sabre", "Офицерская сабля" },
            { "quilted_jacket", "Стёганая куртка" }, { "leather_jacket", "Кожаная куртка" }, { "scout_jacket", "Куртка разведчика" }, { "boarding_vest", "Абордажный жилет" },
            { "copper_ring", "Медное кольцо" }, { "smith_ring", "Кольцо кузнеца" }, { "marksman_ring", "Кольцо стрелка" }, { "lavidium_ring", "Кольцо с лавидием" },
            { "woodland_talisman", "Лесной талисман" }, { "fang_cord", "Клык на шнурке" }, { "sea_knot", "Морской узел" }, { "courier_token", "Жетон гонца" },
            { "memory_shard", "Осколок памяти" },
        };

        /// <summary>Сколько основ в каталоге; ключ основы по номеру — для атласа находок палатки.</summary>
        public static int Count => Catalog.GetLength(0);
        public static string KeyAt(int index) => Catalog[index, 0];

        static int[] _ids;
        static readonly Dictionary<int, Texture2D> Icons = new Dictionary<int, Texture2D>();

        /// <summary>Номер основы в каталоге по её StableId; -1 — не знаем такой.</summary>
        public static int IndexOf(int baseId)
        {
            if (_ids == null)
            {
                _ids = new int[Catalog.GetLength(0)];
                for (int i = 0; i < _ids.Length; i++) _ids[i] = StableId.Of("base." + Catalog[i, 0]);
            }
            return System.Array.IndexOf(_ids, baseId);
        }

        public static string Name(int baseId)
        {
            int i = IndexOf(baseId);
            if (i < 0) return "Предмет";
            string key = "item." + Catalog[i, 0];
            string text = CampServiceText.Get(key);
            return text != key ? text : Catalog[i, 1];
        }

        /// <summary>Картинка основы (Resources/UI/Items); null — картинки нет.</summary>
        public static Texture2D Icon(int baseId)
        {
            if (Icons.TryGetValue(baseId, out Texture2D icon)) return icon;
            int i = IndexOf(baseId);
            icon = i >= 0 ? Resources.Load<Texture2D>("UI/Items/" + Catalog[i, 0]) : null;
            Icons[baseId] = icon;
            return icon;
        }
    }
}
