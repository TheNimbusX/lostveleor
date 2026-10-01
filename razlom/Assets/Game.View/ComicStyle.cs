using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Переключатель «комикс-рисовки» мира (проба 01.10). По умолчанию ВЫКЛЮЧЕН: обычная игра владельца
    /// выглядит ровно как сегодня, пока он сам не включит рисовку. Включается в меню разработчика (F8),
    /// в редакторе — «Разлом → Комикс-рисовка», в изолированной съёмке — ключом -capture-style comic|off.
    /// Выбор из F8 и из меню редактора запоминается в PlayerPrefs; ключ съёмки действует только на запуск.
    /// Рисует ComicStyleFeature (PC_Renderer): при выключенной рисовке она не ставит ни одного прохода.
    /// </summary>
    public static class ComicStyle
    {
        public const string PrefsKey = "razlom.visual.comic-style";
        public const string CaptureFlag = "-capture-style";

        static bool _loaded, _enabled;

        /// <summary>Рисовка включена. Читается рендером каждый кадр — дёшево после первого чтения.</summary>
        public static bool Enabled
        {
            get
            {
                if (!_loaded) Load();
                return _enabled;
            }
        }

        /// <summary>Сообщает о смене — например, чтобы перерисовать окна редактора.</summary>
        public static event Action Changed;

        /// <summary>Включить или выключить и запомнить выбор.</summary>
        public static void SetEnabled(bool enabled)
        {
            _loaded = true;
            bool changed = _enabled != enabled;
            _enabled = enabled;
            try
            {
                PlayerPrefs.SetInt(PrefsKey, enabled ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[comic-style] не удалось сохранить выбор: " + e.Message); }
            if (changed) Changed?.Invoke();
        }

        /// <summary>
        /// Только на этот запуск, без записи в настройки — для съёмки: сравнение «было → стало» не должно
        /// оставлять после себя чужой выбор рисовки.
        /// </summary>
        public static void OverrideForSession(bool enabled)
        {
            bool changed = !_loaded || _enabled != enabled;
            _loaded = true;
            _enabled = enabled;
            Debug.Log("[comic-style] на этот запуск: " + (enabled ? "комикс-рисовка" : "обычная картинка"));
            if (changed) Changed?.Invoke();
        }

        /// <summary>Значение ключа съёмки: comic / on → true, off / none → false, иначе null.</summary>
        public static bool? ParseCaptureValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (string.Equals(value, "comic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        static void Load()
        {
            _loaded = true;
            try { _enabled = PlayerPrefs.GetInt(PrefsKey, 0) == 1; }
            catch (Exception) { _enabled = false; }
        }

        // Без перезагрузки домена (Enter Play Mode Options) статика пережила бы выход из Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _loaded = false;
    }
}
