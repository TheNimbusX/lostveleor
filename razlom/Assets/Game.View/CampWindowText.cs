namespace Game.View
{
    /// <summary>
    /// Тексты новых окон лагеря (закалка Эни, «Клятвы» и «Атлас» палатки, стол «Перед походом»): перевод ключа из таблицы
    /// языка (CampServiceText.Translated), а без него — русский текст, который передаёт само место вызова. Так окна не
    /// дописывают ключи в общий switch CampServiceText.Get, который правят параллельно, а переводчик всё равно видит ключ.
    /// Пример: CampWindowText.Get("oath.title", "Клятвы").
    /// </summary>
    public static class CampWindowText
    {
        /// <summary>Перевод ключа или русский запасной <paramref name="ru"/>.</summary>
        public static string Get(string key, string ru) => CampServiceText.Translated(key) ?? ru;

        /// <summary>То же с подстановкой {0}, {1}… (string.Format) — «Найдено {0} из {1}».</summary>
        public static string Format(string key, string ru, params object[] args) => string.Format(Get(key, ru), args);
    }
}
