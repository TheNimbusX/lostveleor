using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Названия, действие и картинки артефактов забега. Набор акта I — список владельца
    /// (ui/unique-artifacts.md.md), картинки — вырезаны из его листа ui/unique-artefacts.png,
    /// лежат в Resources/UI/Artifacts. Меняется здесь и в <see cref="RunArtifact"/> одновременно.
    /// </summary>
    public static class RunArtifactTexts
    {
        public static string Name(RunArtifact artifact)
        {
            switch (artifact)
            {
                case RunArtifact.SunSeal: return "Солнечная Печать";
                case RunArtifact.ReturnDial: return "Циферблат Возврата";
                case RunArtifact.VengeanceMirror: return "Зеркало Возмездия";
                case RunArtifact.WinterHeart: return "Сердце Вечной Зимы";
                case RunArtifact.Hourglass: return "Песочные Часы Безвременья";
                case RunArtifact.VoidVisage: return "Лик Пустоты";
                case RunArtifact.CrimsonHeart: return "Багровое Сердце";
                case RunArtifact.GuardianVow: return "Обет Хранителя";
                default: return "Нет артефакта";
            }
        }

        /// <summary>Что делает — коротко, для карточки награды и подсказки.</summary>
        public static string Effect(RunArtifact artifact)
        {
            switch (artifact)
            {
                case RunArtifact.SunSeal: return "4 секунды полной неуязвимости. Атаковать можно.";
                case RunArtifact.ReturnDial: return "Все способности и кувырок сразу готовы снова.";
                case RunArtifact.VengeanceMirror: return "6 секунд весь урон по Пелагу возвращается атакующему, а сам Пелаг получает на 25% меньше.";
                case RunArtifact.WinterHeart: return "Враги в 8 м замерзают на 5 секунд; удар способностью по замёрзшему раскалывает лёд: +" + Simulation.WinterShatterDamage + " урона. Босс и элита — замедление на 40%.";
                case RunArtifact.Hourglass: return "4 секунды враги и их снаряды стоят. Весь урон по ним копится и приходит разом, когда время пойдёт.";
                case RunArtifact.VoidVisage: return "6 секунд Пелага не бьют и он проходит сквозь врагов, но атаковать нельзя. F ещё раз — выйти раньше со взрывом на " + Simulation.VoidBurstDamage + ".";
                case RunArtifact.CrimsonHeart: return "10 секунд: за каждые потерянные 10% здоровья +8% урона и скорости. В конце — взрыв силой вдвое больше потерянного здоровья.";
                case RunArtifact.GuardianVow: return "Смертельный удар не убивает: Пелаг встаёт с половиной здоровья, 3 секунды неуязвим, враги рядом отброшены. Один раз за забег.";
                default: return string.Empty;
            }
        }

        /// <summary>Как включается: «F · перезарядка 60 с» или «Срабатывает сам».</summary>
        public static string Use(RunArtifact artifact)
        {
            int cooldown = Simulation.ArtifactCooldownTicks(artifact);
            return cooldown > 0
                ? GameKeyBindings.Label(GameAction.UseArtifact) + " — включить · перезарядка " + cooldown / Simulation.TicksPerSecond + " с"
                : "Срабатывает сам";
        }

        static string File(RunArtifact artifact)
        {
            switch (artifact)
            {
                case RunArtifact.SunSeal: return "artifact_sun_seal";
                case RunArtifact.ReturnDial: return "artifact_return_dial";
                case RunArtifact.VengeanceMirror: return "artifact_vengeance_mirror";
                case RunArtifact.WinterHeart: return "artifact_winter_heart";
                case RunArtifact.Hourglass: return "artifact_hourglass";
                case RunArtifact.VoidVisage: return "artifact_void_visage";
                case RunArtifact.CrimsonHeart: return "artifact_crimson_heart";
                case RunArtifact.GuardianVow: return "artifact_guardian_vow";
                default: return null;
            }
        }

        public static Texture2D Icon(RunArtifact artifact)
        {
            string file = File(artifact);
            return file != null ? Resources.Load<Texture2D>("UI/Artifacts/" + file) : null;
        }
    }
}
