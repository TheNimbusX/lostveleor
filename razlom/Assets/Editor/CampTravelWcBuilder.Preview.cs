using System.IO;
using Game.Sim;
using Game.View;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;

namespace Game.EditorTools
{
    /// <summary>
    /// Кадр стола «Перед походом» без запуска игры: превью-сцена UiKitShowcase поверх кадра игры (ART/no-ui.png), открытая
    /// сцена не трогается. Данные — настоящий лагерь Sim, показ — тот же CampTravelPanel.Show, что в игре: кадр не врёт.
    /// Пример: CampTravelWcBuilder.Capture("…/table.png", CampTravelWcBuilder.Shot.AfterBoss).
    /// </summary>
    public static partial class CampTravelWcBuilder
    {
        public enum Shot
        {
            /// <summary>После первого босса: ранг 1, умения выбраны, в «с собой» дары и артефакты, часть рецептов Лео закрыта.</summary>
            AfterBoss,
            /// <summary>То же с наведением: тонкая тлеющая кромка на втором умении и третьем варианте, их описания.</summary>
            Hover,
            /// <summary>Первый подход к столу: два умения, «с собой» пусто («Отправиться» неактивна), все большие рецепты закрыты.</summary>
            FirstVisit,
        }

        public static string Capture(string outPath, Shot shot = Shot.AfterBoss)
        {
            Build(false);
            return UiKitShowcase.Capture(outPath, PrefabPath, 1920, 1080, inst => Preview(inst, shot));
        }

        static void Preview(GameObject inst, Shot shot)
        {
            inst.SetActive(true);
            var panel = inst.GetComponent<CampTravelPanel>();
            if (panel.Group != null) panel.Group.alpha = 1f;
            var root = (RectTransform)inst.transform;
            const string backdrop = "../ART/no-ui.png";
            if (File.Exists(backdrop))
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(backdrop));
                var back = Stretch(Node("Кадр игры", root)).gameObject.AddComponent<RawImage>();
                back.texture = tex;
                back.uvRect = new Rect(.035f, 0f, .965f, .955f);
                back.transform.SetSiblingIndex(0);
            }

            Camp camp = shot == Shot.FirstVisit ? FirstVisitCamp() : AfterBossCamp();
            bool hover = shot == Shot.Hover;
            panel.Show(camp, 0, hover ? 1 : -1, -1, hover ? 2 : -1);

            // Кромка наведения — только у показанного наведения; у остальных её держит прозрачной UiHoverMotion (в кадре без Play — сами).
            foreach (var motion in inst.GetComponentsInChildren<UiHoverMotion>(true))
            {
                if (motion.HighlightGroup != null) motion.HighlightGroup.alpha = 0f;
                if (motion.Highlight != null) motion.Highlight.canvasRenderer.SetAlpha(0f);
            }
            if (hover)
            {
                Hovered(panel.Skills[1]);
                Hovered(panel.CarryOffers[2]);
            }
        }

        static void Hovered(CampTravelMedal medal)
        {
            if (medal?.Root == null || !medal.Root.gameObject.activeSelf) return;
            if (medal.Root.Find("Наведение") is Transform rim && rim.TryGetComponent(out CanvasGroup group)) group.alpha = 1f;
            medal.Root.localScale = new Vector3(1.05f, 1.05f, 1f);
        }

        /// <summary>Профиль после первого босса: ур. 6 и босс Чащи — ранг 1 (большие зелья Лео открыты), четыре взятых умения, три артефакта.</summary>
        static Camp AfterBossCamp()
        {
            var camp = PrototypeContent.NewCamp();
            camp.DeveloperSetLevel(6);
            camp.DeveloperCreditBoss(0);
            TakeSkills(camp, 4);
            for (int i = 0; i < 3 && i < RunArtifacts.Count; i++) camp.OpenArtifact(RunArtifacts.At(i));
            camp.RecordRealAttemptEnded(3, 1, true);
            camp.GrantPotions(PotionKind.LargeHealth, 2);
            camp.GrantPotions(PotionKind.SmallLavidium, 1);
            camp.SelectPotionForSlot(0, PotionKind.LargeHealth);
            // Взят артефакт, если он среди вариантов, иначе первый дар: кадр показывает и кольцо уникальной вещи.
            int pick = 0;
            for (int i = 0; i < camp.CarryOfferCount; i++) if (camp.CarryOfferAt(i).Kind == CarryKind.Artifact) { pick = i; break; }
            camp.SelectCarry(camp.CarryOfferAt(pick));
            if (camp.SkillOfferCount > 1) camp.SelectStarterSkill(camp.SkillOfferAt(1));
            return camp;
        }

        /// <summary>Первый подход: стартовый профиль (две малые по 3), два взятых умения, «с собой» ещё не выбрано.</summary>
        static Camp FirstVisitCamp()
        {
            var camp = PrototypeContent.NewCamp();
            TakeSkills(camp, 2);
            camp.RecordRealAttemptEnded(1, 0);
            return camp;
        }

        static void TakeSkills(Camp camp, int count)
        {
            for (int pool = 0; pool < PelagKit.PoolSize && count > 0; pool++)
                if (PelagKit.InRewardPool(pool)) { camp.RecordSkillTaken(PelagKit.PoolDefinition(pool).Id); count--; }
        }
    }
}
