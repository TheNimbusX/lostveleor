using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.UiKitBuilder;

namespace Game.EditorTools
{
    /// <summary>
    /// Индикатор серии Крушения на плитках способностей (06.10, целевой кадр ART/characters/pelag/wreck-look-2026-10-06/
    /// chatgpt-results/series-ui.png; решение владельца — «и там, и там», memory razlom-anchor-skills-own-look):
    /// у каждой плитки способности — узел «Серия Крушения» (виден, только пока в слоте Крушение; решает CombatHudView):
    /// * «Окно» — кольцо по кромке плитки, Image Filled Radial360 от верха по часовой, аддитивное: светится цветом формы
    ///   и гаснет по кругу, пока открыто окно следующего нажатия; лежит сразу над огненным кольцом, под наведением и кейкапом;
    /// * «Звенья» — ряд под кейкапом (клавиша сидит на нижней кромке плитки): четыре звена (четвёртое — только с
    ///   «Четвёртым ударом»), у каждого «Железо» (тёмное звено с контуром, не красится) и «Свет» (белый, аддитивный,
    ///   цвет ставит вид); за рядом «Лучи» финала. Дуга точек над плиткой — усиления, её не трогаем.
    /// Рисунки — Resources/UI/HUD/WreckSeries (их же берёт PelagWreckSeriesView над героем).
    ///
    /// Свежая сборка — <see cref="WreckSeriesNode"/> из BuildAbilities; готовый префаб — миграция v5 (только добавляет
    /// узлы; ручные правки и всё прочее в плитках не трогаются; узел уже есть — плитка пропускается).
    /// </summary>
    public static partial class CombatHudWcBuilder
    {
        const string SeriesFolder = "Assets/Resources/UI/HUD/WreckSeries/";
        const string SeriesIronPath = SeriesFolder + "Wreck_Link_Iron.png";
        const string SeriesGlowPath = SeriesFolder + "Wreck_Link_Glow.png";
        const string SeriesBurstPath = SeriesFolder + "Wreck_Link_Burst.png";
        const string SeriesRingPath = SeriesFolder + "Wreck_Window_Ring.png";
        const string SeriesNodeName = "Серия Крушения";

        /// <summary>Кольцо окна шире плитки на столько с каждой стороны: ядро рисунка (0,43 стороны) ложится на кромку плитки.</summary>
        const float SeriesRingExpand = 7f;

        /// <summary>Свет звена шире звена в полтора раза (рисунок 192 × 108 против 128 × 72), лучи — 256 × 160.</summary>
        const float SeriesGlowRatio = 1.5f, SeriesBurstWidth = 112f, SeriesBurstHeight = 70f;

        static Sprite SeriesSprite(string path)
        {
            // Импорт рисунков мог ещё не дойти до них (тот же Refresh, что и скрипты): довести сейчас.
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[ui-kit] Нет спрайта " + path + " (импорт Sprite): индикатор серии Крушения не поставлен");
            return sprite;
        }

        /// <summary>Узел индикатора серии у плитки способности; уже есть — он и возвращается. null — нет тела или рисунков.</summary>
        static HudWreckSeries WreckSeriesNode(HudSlotWidget widget)
        {
            if (widget == null) return null;
            var existing = widget.GetComponent<HudWreckSeries>();
            if (existing != null && existing.Root != null) return existing;
            RectTransform body = widget.Body;
            if (body == null)
            {
                Debug.LogWarning("[ui-kit] У плитки «" + widget.name + "» нет тела (HudSlotWidget.Body): индикатор серии Крушения не поставлен");
                return null;
            }
            Sprite iron = SeriesSprite(SeriesIronPath), glow = SeriesSprite(SeriesGlowPath);
            Sprite burst = SeriesSprite(SeriesBurstPath), ring = SeriesSprite(SeriesRingPath);
            if (iron == null || glow == null || burst == null || ring == null) return null;

            RectTransform root = Stretch(Node(SeriesNodeName, body));
            // Над огненным кольцом (иначе кольцо окна под ним), под наведением, секундами и кейкапом.
            Transform fire = widget.Frame != null ? widget.Frame.transform : null;
            if (fire != null && fire.parent == body) root.SetSiblingIndex(fire.GetSiblingIndex() + 1);
            else
            {
                Transform key = widget.Key != null ? widget.Key.transform.parent : null;
                if (key != null && key.parent == body) root.SetSiblingIndex(key.GetSiblingIndex());
                Debug.LogWarning("[ui-kit] У плитки «" + widget.name + "» огненное кольцо не в теле: индикатор серии поставлен перед кейкапом, проверить порядок руками");
            }

            var series = existing != null ? existing : widget.gameObject.AddComponent<HudWreckSeries>();
            series.Root = root.gameObject;

            Image window = Layer(root, "Окно", ring, UiTheme.Role.Text, 1f, SeriesRingExpand);
            window.type = Image.Type.Filled;
            window.fillMethod = Image.FillMethod.Radial360;
            window.fillOrigin = (int)Image.Origin360.Top;
            window.fillClockwise = true;
            window.fillAmount = 1f;
            Additive(window, new Color(.31f, .66f, 1f, 0f));
            window.enabled = false;
            series.Window = window;

            RectTransform row = Box(Node("Звенья", root), new Vector2(.5f, 0f), new Vector2(.5f, .5f), new Vector2(0f, -HudWreckSeriesRules.RowDrop),
                new Vector2(HudWreckSeriesRules.LinkPitch * HudWreckSeriesRules.MaxLinks, HudWreckSeriesRules.LinkHeight * SeriesGlowRatio));
            series.Row = row;

            RectTransform rays = Box(Node("Лучи", row), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(SeriesBurstWidth, SeriesBurstHeight));
            Image burstImage = rays.gameObject.AddComponent<Image>();
            burstImage.sprite = burst;
            burstImage.raycastTarget = false;
            Additive(burstImage, new Color(.31f, .66f, 1f, 0f));
            burstImage.enabled = false;
            series.Burst = burstImage;

            int count = HudWreckSeriesRules.LinkCount(false);
            series.Links = new RectTransform[HudWreckSeriesRules.MaxLinks];
            series.Glows = new Image[HudWreckSeriesRules.MaxLinks];
            for (int i = 0; i < HudWreckSeriesRules.MaxLinks; i++)
            {
                RectTransform link = Box(Node("Звено " + (i + 1), row), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(HudWreckSeriesRules.LinkX(i, i < count ? count : HudWreckSeriesRules.MaxLinks), 0f),
                    new Vector2(HudWreckSeriesRules.LinkWidth, HudWreckSeriesRules.LinkHeight));
                Image metal = Stretch(Node("Железо", link)).gameObject.AddComponent<Image>();
                metal.sprite = iron;
                metal.raycastTarget = false;

                RectTransform light = Node("Свет", link);
                light.anchorMin = Vector2.zero;
                light.anchorMax = Vector2.one;
                light.pivot = new Vector2(.5f, .5f);
                Vector2 pad = new Vector2(HudWreckSeriesRules.LinkWidth, HudWreckSeriesRules.LinkHeight) * ((SeriesGlowRatio - 1f) * .5f);
                light.offsetMin = -pad;
                light.offsetMax = pad;
                Image lit = light.gameObject.AddComponent<Image>();
                lit.sprite = glow;
                lit.raycastTarget = false;
                Additive(lit, new Color(.31f, .66f, 1f, 0f));
                lit.enabled = false;

                series.Links[i] = link;
                series.Glows[i] = lit;
                // Четвёртое звено — только с «Четвёртым ударом» (вид включает по сборке слота).
                if (i >= count) link.gameObject.SetActive(false);
            }
            // У плитки без Крушения узла не видно: вид включит его по сборке слота.
            root.gameObject.SetActive(false);
            EditorUtility.SetDirty(series);
            return series;
        }

        /// <summary>Индикаторы серии у всех плиток способностей (свежая сборка и миграция v5).</summary>
        static void BuildWreckSeries(CombatHudView view)
        {
            HudSlotWidget[] slots = view.Slots ?? new HudSlotWidget[0];
            var series = new HudWreckSeries[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    Debug.LogWarning("[ui-kit] В боевом HUD нет плитки способности " + (i + 1) + ": индикатор серии Крушения у неё не поставлен");
                    continue;
                }
                series[i] = WreckSeriesNode(slots[i]);
            }
            view.WreckSeries = series;
            EditorUtility.SetDirty(view);
        }

        /// <summary>
        /// v5 — индикатор серии Крушения (06.10): узлы у каждой плитки способности, остальное не тронуто. Рисунков ещё
        /// нет (скрипты пришли раньше картинок) — отказ исключением ДО правок: префаб не сохраняется, версия не растёт,
        /// миграция повторится после следующей перезагрузки, а не встанет навсегда без звеньев.
        /// </summary>
        static void MigrateTo5(GameObject root, CombatHudView view)
        {
            foreach (string path in new[] { SeriesIronPath, SeriesGlowPath, SeriesBurstPath, SeriesRingPath })
                if (SeriesSprite(path) == null)
                    throw new System.InvalidOperationException("[ui-kit] Боевой HUD v5: нет спрайта " + path + " — миграция отложена до импорта рисунков серии Крушения");
            BuildWreckSeries(view);
        }
    }
}
