using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Строка добычи под панелью забега (выбор владельца 30.09, кадр b1): золото и круги вещей, которые
    /// унесёшь из Разлома, с кольцом и светом редкости. Узлы строит миграция v1, ссылку Loot подключает v2
    /// (RunHudWcBuilder.Migrations); смысл — в RunHud.Loot, чистые правила — в <see cref="RunLootLedger"/>.
    /// Loot пуст (префаб до v2) — строки нет, золото остаётся в строке панели.
    /// </summary>
    public sealed partial class RunHudView
    {
        [Header("Раскладка")]
        [Tooltip("Версия раскладки префаба: RunHudWcBuilder.Migrations доводит готовый префаб поверх ручных правок")]
        public int LayoutVersion;

        [Header("Добыча забега (строка под панелью состояния)")]
        [Tooltip("Строка: золото и вещи, которые унесёшь. Ширину ведёт RunHud по числу кругов")]
        public RectTransform Loot;
        [Tooltip("Знак золота (белый знак забега в краске монет)")] public RawImage LootCoin;
        public TMP_Text LootGold;
        [Tooltip("Черта между золотом и вещами")] public RectTransform LootDivider;
        [Tooltip("Ряд кругов вещей: едет целиком, когда старые уходят в «+K»")] public RectTransform LootRow;
        [Tooltip("Круги вещей по порядку ряда (пул)")] public LootSlot[] LootSlots = new LootSlot[0];
        [Tooltip("Круг «+K»: сколько старых вещей не влезло в ряд; стоит на первом месте, не едет с рядом")]
        public RectTransform LootMore;
        public TMP_Text LootMoreLabel;
        [Tooltip("Своя группа проявления круга «+K»: проявляется, когда он появляется")] public UiInkGroup LootMoreInk;
        [Tooltip("«+1» у новой вещи оранжевым акцентом: всплывает и гаснет")] public TMP_Text LootArrival;
        [Tooltip("Картинка вещи без своей (незнакомая основа): белый знак вещей")] public Texture LootFallbackIcon;

        [Header("Подсказка добычи")]
        [Tooltip("Малая подложка «Дыма и света» под строкой: имя и редкость вещи под мышью")] public RectTransform LootTip;
        public TMP_Text LootTipTitle;
        public TMP_Text LootTipLine;
        [Tooltip("Третья строка: «вещь пропадёт», «тестовый забег»; пусто — скрыта")] public TMP_Text LootTipNote;

        /// <summary>Круг вещи в строке добычи (UiInkKit.SlotOrb): только ссылки.</summary>
        [Serializable]
        public sealed class LootSlot
        {
            public RectTransform Rect;
            [Tooltip("Притушить вещь, которая не влезет в сумку лагеря")] public CanvasGroup Group;
            [Tooltip("Картинка вещи («Предмет»)")] public RawImage Art;
            [Tooltip("Кольцо и свет редкости")] public WcSlotState State;
            [Tooltip("Проявление круга: только у новой вещи, не при каждом показе строки")] public UiInkGroup Ink;
            [Tooltip("Вспышка цвета редкости у новой вещи")] public Image Flash;
        }
    }
}
