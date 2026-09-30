using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Полировка экранов забега (выбор владельца 30.09, доска concepts-2026-09-30-hud-polish: 1a с подписью из 1b,
    /// 3a, 4). Узлы строит миграция v3 (RunHudWcBuilder.Polish), смысл — RunHud.Boss, RunHud.Coins, RunHud.Reward,
    /// RunHud.Summary. Пустая ссылка (префаб до v3) — у части нет полировки, экран работает по-старому.
    /// </summary>
    public sealed partial class RunHudView
    {
        [Header("Босс: засечки, подпись фазы, числа (v3)")]
        [Tooltip("«Босс · фаза 2» под именем: меняется на засечках подмоги, «· ярость» — на половине")] public TMP_Text BossSubtitle;
        [Tooltip("«2900 / 5000» под полосой")] public TMP_Text BossNumbers;
        [Tooltip("Засечки на полосе по порядку прохождения: 66% (подмога), 50% (ярость), 33% (подмога)")]
        public BossMark[] BossMarks = new BossMark[0];

        [Header("Монеты в строку добычи (v3)")]
        [Tooltip("Слой монет во весь экран поверх строки добычи: мышь не ловит")] public RectTransform CoinLayer;
        [Tooltip("Пул монет: белый знак золота в краске монет")] public RectTransform[] Coins = new RectTransform[0];

        [Header("Подсказка карточки награды (v3)")]
        [Tooltip("Справа от наведённой карточки: название и полное описание с ключевыми словами")] public RectTransform OfferTip;
        public TMP_Text OfferTipTitle;
        public TMP_Text OfferTipBody;
        [Tooltip("Вложенная подсказка ключевого слова под основной (как в Hades)")] public RectTransform KeywordTip;
        public TMP_Text KeywordTipTitle;
        public TMP_Text KeywordTipBody;

        [Header("Панель состояния (v3)")]
        [Tooltip("Значок третьей строки (тайники): пустая строка — значок скрыт («Путь открыт»)")] public Graphic StatusExtraIcon;

        [Header("Итоги со статистикой (v3)")]
        [Tooltip("Стоп-кадр последнего удара в тлеющем круге (RunEndBeat)")] public RawImage SummaryFreeze;
        [Tooltip("«Последний удар · Лесной вендиго · 64»")] public TMP_Text SummaryFreezeCaption;
        [Tooltip("Строки статистики по порядку RunHudSummary.Rows")] public TMP_Text[] SummaryStats = new TMP_Text[0];
        [Tooltip("Круги «Убито»: портрет, «×N», имя")] public SummaryKill[] SummaryKills = new SummaryKill[0];
        [Tooltip("«Убито» с чертами: скрыт, если никого не убили")] public RectTransform SummaryKillsHeader;
        public SummaryBand SummaryLost;
        public SummaryBand SummaryKept;
        [Tooltip("Клавиша на «Повторить» (кейкап набора) и кольцо блокировки")] public KeyLock RepeatKey;
        [Tooltip("Клавиша на «В лагерь» (кейкап набора) и кольцо блокировки")] public KeyLock CampKey;

        /// <summary>Засечка на полосе босса: черта поперёк полосы и кружок, который загорается, когда засечку прошли.</summary>
        [Serializable]
        public sealed class BossMark
        {
            public RectTransform Rect;
            [Tooltip("Кружок: тёмный, пройденная — цветом заливки")] public Image Dot;
            [Tooltip("Кольцо кружка")] public Image Ring;
            [Tooltip("Вспышка, когда засечку прошли (сила — FlashScale)")] public Image Flash;
        }

        /// <summary>Круг «Убито»: портрет вида, «×38», имя.</summary>
        [Serializable]
        public sealed class SummaryKill
        {
            public RectTransform Rect;
            public RawImage Portrait;
            [Tooltip("Белый знак, пока нет портрета вида")] public RawImage Glyph;
            public TMP_Text Count;
            public TMP_Text Name;
        }

        /// <summary>Полоса «Потеряно» или «Остаётся»: круги вещей и строки.</summary>
        [Serializable]
        public sealed class SummaryBand
        {
            public RectTransform Rect;
            public TMP_Text Title;
            [Tooltip("Круги вещей (у потерянных — красный крест)")] public BandItem[] Items = new BandItem[0];
            public TMP_Text Lines;
        }

        [Serializable]
        public sealed class BandItem
        {
            public RectTransform Rect;
            public RawImage Art;
            public WcSlotState State;
            [Tooltip("Красный крест потерянной вещи")] public Graphic Cross;
        }

        /// <summary>Кейкап набора с кольцом блокировки: кольцо наполняется, пока ввод закрыт, потом кейкап загорается.</summary>
        [Serializable]
        public sealed class KeyLock
        {
            public RectTransform Cap;
            public TMP_Text Letter;
            [Tooltip("Кольцо блокировки: заливка по кругу")] public Image Ring;
            [Tooltip("Вспышка, когда ввод открылся")] public Image Glow;
            [Tooltip("Кромка кейкапа: тусклая, пока ввод закрыт")] public Graphic Edge;
        }
    }
}
