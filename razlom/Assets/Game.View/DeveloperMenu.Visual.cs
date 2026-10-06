#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace Game.View
{
    /// <summary>
    /// Вкладка «Визуал»: отложенные пробы 01.10 — комикс-рисовка и свет арены по глубине. По умолчанию выключены,
    /// владелец: «пока не удаляй из F8». Переключатели запоминаются (PlayerPrefs ComicStyle/ArenaMood — ключи не
    /// трогать); принудительный режим света — до выхода из игры. Съёмка ставит своё через -capture-style/-capture-mood.
    /// Ниже — A/B лунной кромки Хозяина Чащи и его лап на ходу (оба включены; до выхода из игры, не запоминаются).
    /// </summary>
    public sealed partial class DeveloperMenu
    {
        // Якорь: «Визуал · проба рисовки» цитируют STATE, ArenaMood, Bootstrap и память style-shift / light-arc.
        private const string StyleProbe = "Визуал · проба рисовки";
        private const string BossLook = "Визуал · Хозяин Чащи";
        private static readonly string[] ArenaMoodLabels = { "Авто", "День", "Туман", "Сумерки", "Босс" };
        private static readonly ArenaMoodMode[] ArenaMoodModes =
            { ArenaMoodMode.Auto, ArenaMoodMode.Day, ArenaMoodMode.Mist, ArenaMoodMode.Dusk, ArenaMoodMode.Boss };

        private void RegisterVisualEntries()
        {
            DevMenu.Section(DevTab.Visual, StyleProbe, 10, DevColumn.Left,
                hint: "Пробы 01.10 отложены, по умолчанию выключены. Выключено — игра выглядит как раньше.");

            DevMenu.Toggle("visual.comic", DevTab.Visual, StyleProbe, "Комикс-рисовка мира: тушь, кисть, диорама",
                c => ComicStyle.Enabled, (c, on) => ComicStyle.SetEnabled(on), DevFlags.Remembered,
                hint: "Модели, материалы и HUD не меняются.", order: 10);

            DevMenu.Toggle("visual.mood", DevTab.Visual, StyleProbe, "Свет арены по глубине: день → туман → сумерки",
                c => ArenaMood.Enabled, (c, on) => ArenaMood.SetEnabled(on), DevFlags.Remembered,
                hint: "Арены 1–3 — золотой день, 4–6 — туман, 7–8 и босс — сумерки. Лагерь и герои не меняются. " +
                      "Смотреть: «Начать с арены» 1, 5, 8.",
                order: 20);

            // Своё число оси (ключ съёмки) в сетке не показано: сетка рисуется на «Авто», но выбор не меняется, пока не
            // нажмут другую кнопку, — иначе первое же открытие F8 сбрасывало бы ось на авто.
            DevMenu.Choice("visual.mood-mode", DevTab.Visual, StyleProbe, "Режим света · до выхода из игры", ArenaMoodLabels,
                c => Math.Max(0, Array.IndexOf(ArenaMoodModes, ArenaMood.Mode)),
                (c, index) => ArenaMood.SetMode(ArenaMoodModes[index]),
                visible: c => ArenaMood.Enabled, order: 30);

            DevMenu.Custom("visual.mood-status", DevTab.Visual, StyleProbe, (c, ui) =>
            {
                var state = ArenaMood.Current;
                ui.Hint(ArenaMood.Active
                    ? "Сейчас: арена " + state.Depth + (state.Boss ? " (босс)" : "") + " · " + ArenaMoodRules.AxisName(state.Axis)
                    : ArenaMood.Prepared ? "Свет ждёт конца сборки арены." : "Свет только на аренах — в лагере как всегда.");
            }, visible: c => ArenaMood.Enabled, order: 40);

            // Лунная кромка Хозяина Чащи (находка 9 ревью 02.10) — A/B для владельца до его выбора (проверка находок 03.10).
            DevMenu.Section(DevTab.Visual, BossLook, 20, DevColumn.Left,
                hint: "Сравнить в бою с боссом: тёмная половина поляны, кромка вкл / выкл.");
            DevMenu.Toggle("visual.boss-rim", DevTab.Visual, BossLook, "Хозяин Чащи: лунная кромка в тени",
                c => ThicketMasterPhaseDressing.RimShown, (c, on) => ThicketMasterPhaseDressing.RimShown = on,
                hint: "Холодный край силуэта только там, где тело тёмное. До выхода из игры.", order: 10);
            // Лапы на ходу (владелец 08.10: «ноги немного проскальзывают») — A/B шага под поворот и замка стоп.
            DevMenu.Toggle("visual.boss-feet", DevTab.Visual, BossLook, "Хозяин Чащи: лапы держат землю на ходу",
                c => ThicketMasterAnimatorView.FootLock, (c, on) => ThicketMasterAnimatorView.FootLock = on,
                hint: "Выкл — ход как до 08.10: на повороте стоящие лапы едут по земле. До выхода из игры.", order: 20);
        }
    }
}
#endif
