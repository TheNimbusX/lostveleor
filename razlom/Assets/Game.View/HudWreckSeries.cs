using Game.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Индикатор серии Крушения на плитке способности (06.10, целевой кадр series-ui.png): ряд железных звеньев под
    /// кейкапом и кольцо окна по кромке плитки. Только ссылки на части и их вид в этот кадр; что показывать, решают
    /// HudWreckSeriesTracker и HudWreckSeriesRules, зовёт CombatHudView (CombatHudView.Wreck) — у плитки без
    /// Крушения узел спрятан. Узлы ставит CombatHudWcBuilder (свежая сборка и миграция v5); правятся в префабе руками.
    /// </summary>
    public sealed class HudWreckSeries : MonoBehaviour
    {
        [Tooltip("Узел индикатора на плитке: прячется целиком, пока в слоте не Крушение")] public GameObject Root;
        [Tooltip("Ряд звеньев под кейкапом")] public RectTransform Row;
        [Tooltip("Звенья слева направо; четвёртое — только с «Четвёртым ударом»")] public RectTransform[] Links = new RectTransform[HudWreckSeriesRules.MaxLinks];
        [Tooltip("Свет звена (аддитивный, белый рисунок — цвет формы ставит вид)")] public Image[] Glows = new Image[HudWreckSeriesRules.MaxLinks];
        [Tooltip("Лучи финала серии за рядом (аддитивные)")] public Image Burst;
        [Tooltip("Кольцо окна следующего нажатия: Image Filled Radial360 по кромке плитки (аддитивное)")] public Image Window;

        readonly HudWreckSeriesTracker _tracker = new HudWreckSeriesTracker();
        int _links = -1;
        Vector2 _burstSize;

        /// <summary>У плитки нет Крушения: узел спрятан, серия забыта.</summary>
        public void Hide()
        {
            if (Root != null && Root.activeSelf) Root.SetActive(false);
            _tracker.Reset();
        }

        /// <summary>Кадр индикатора: снимок серии, слот плитки, звеньев в ряду, тик показа и доля кадра, форма.</summary>
        public void Show(in WreckState s, int slot, int links, int tick, float alpha, PelagForm form)
        {
            if (Root != null && !Root.activeSelf) Root.SetActive(true);
            HudWreckSeriesShow show = _tracker.Observe(s, slot, links, tick, alpha);
            if (links != _links) Arrange(links);

            for (int i = 0; i < Links.Length && i < links; i++)
            {
                RectTransform link = Links[i];
                if (link == null) continue;
                float scale = HudWreckSeriesRules.LinkScale(i, show);
                if (!Mathf.Approximately(link.localScale.x, scale)) link.localScale = new Vector3(scale, scale, 1f);
                Image glow = i < Glows.Length ? Glows[i] : null;
                if (glow == null) continue;
                float lit = HudWreckSeriesRules.LinkGlow(i, show);
                if (glow.enabled != lit > .001f) glow.enabled = lit > .001f;
                if (lit > .001f) Paint(glow, form, HudWreckSeriesRules.LinkHot(i, show), lit);
            }

            if (Burst != null)
            {
                float burst = HudWreckSeriesRules.BurstAlpha(show);
                if (Burst.enabled != burst > .001f) Burst.enabled = burst > .001f;
                if (burst > .001f)
                {
                    Paint(Burst, form, .35f, burst);
                    float k = HudWreckSeriesRules.BurstScale(show);
                    Burst.rectTransform.localScale = new Vector3(k, k, 1f);
                }
            }

            if (Window != null)
            {
                bool open = show.Window > 0f;
                if (Window.enabled != open) Window.enabled = open;
                if (open)
                {
                    if (!Mathf.Approximately(Window.fillAmount, show.Window)) Window.fillAmount = show.Window;
                    Paint(Window, form, 0f, HudWreckSeriesRules.WindowAlpha);
                }
            }
        }

        /// <summary>Звеньев в ряду стало другое число (талант «Четвёртый удар»): лишние спрятаны, ряд по центру.</summary>
        void Arrange(int links)
        {
            _links = links;
            for (int i = 0; i < Links.Length; i++)
            {
                RectTransform link = Links[i];
                if (link == null) continue;
                bool on = i < links;
                if (link.gameObject.activeSelf != on) link.gameObject.SetActive(on);
                if (on) link.anchoredPosition = new Vector2(HudWreckSeriesRules.LinkX(i, links), link.anchoredPosition.y);
            }
            if (Burst != null)
            {
                // Лучи — по ширине ряда: у четырёх звеньев шире на одно.
                if (_burstSize == Vector2.zero) _burstSize = Burst.rectTransform.sizeDelta;
                float grow = (links - Simulation.WreckStages) * HudWreckSeriesRules.LinkPitch;
                Burst.rectTransform.sizeDelta = _burstSize + new Vector2(grow, 0f);
            }
        }

        static void Paint(Image image, PelagForm form, float hot, float alpha)
        {
            HudWreckSeriesRules.FormColour(form, hot, out float r, out float g, out float b);
            var colour = new Color(r, g, b, Mathf.Clamp01(alpha));
            if (image.color != colour) image.color = colour;
        }
    }
}
