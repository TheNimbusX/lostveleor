using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// Метки миникарты на холсте пака «Ночная акварель» (аудит UI, 25 сентября): место — знак
    /// в тёмном круге с оправой, враг — красная точка, герой — стрелка пака, туман — тёмные
    /// чернила, при наведении — плашка пака с именем и расстоянием. Раньше всё это рисовал IMGUI
    /// старым шрифтом поверх любых окон, а белая маска тумана делала карту Разлома белым квадратом.
    /// Что и где стоит, решает <see cref="HudMinimap"/>; здесь только показ и наведение.
    /// </summary>
    public sealed class HudMinimapMarks : MonoBehaviour
    {
        [Tooltip("Слой мест: растянут по карте; образец метки места лежит в нём выключенным")]
        public RectTransform PlaceLayer;
        [Tooltip("Слой врагов под местами; образец точки лежит в нём выключенным")]
        public RectTransform EnemyLayer;
        [Tooltip("Образец метки места: «Оправа» (Image с ThemeColor) и «Знак» (RawImage)")]
        public RectTransform PlaceTemplate;
        [Tooltip("Образец точки врага")]
        public RectTransform EnemyTemplate;
        [Tooltip("Стрелка героя: смотрит вверх, на север; опора по центру")]
        public RectTransform Player;
        [Tooltip("Туман войны: белая маска LayoutView, окрашенная цветом этого RawImage")]
        public RawImage Fog;
        [Tooltip("Подпись при наведении, опора — правый край: встаёт слева от метки, вне маски карты")]
        public RectTransform Hint;
        public TMP_Text HintText;
        [Tooltip("Знак алхимика: в атласе мест его нет")]
        public Texture2D AlchemistSymbol;
        /// <summary>Номер знака алхимика в метках (для кадра редактора).</summary>
        public const int AlchemistMark = HudMinimap.AlchemistSymbol;
        [Tooltip("Круг места на карте и прижатого к краю")]
        public float PlaceSize = 24f, EdgeSize = 17f;
        [Tooltip("Знак внутри круга, доля круга")]
        [Range(.3f, 1f)] public float SymbolFill = .68f;
        [Tooltip("Насколько круг под мышью крупнее")]
        public float HoverGrow = 1.18f;
        [Tooltip("Поля подписи слева и справа от текста")]
        public float HintPadding = 14f;

        sealed class PlaceMark
        {
            public RectTransform Root;
            public ThemeColor Rim;
            public RawImage Symbol;
            public int ShownSymbol = -1;
        }

        readonly List<PlaceMark> _places = new List<PlaceMark>();
        readonly List<RectTransform> _enemies = new List<RectTransform>();
        int _hovered = -1;
        string _hint;

        internal void Apply(HudMinimap map, Vector2 screen)
        {
            if (PlaceLayer == null) return;
            Rect area = PlaceLayer.rect;
            // Картинку карты HudMinimap рисует под её ширину на экране (холст поверх экрана:
            // мировые единицы — пиксели), чтобы кромка не мельчила и не мерцала при сжатии.
            map.PixelSize = area.width * Mathf.Abs(PlaceLayer.lossyScale.x);
            Vector2 pointer = new Vector2(float.MinValue, float.MinValue);
            if (RectTransformUtility.RectangleContainsScreenPoint(PlaceLayer, screen, null)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(PlaceLayer, screen, null, out Vector2 local))
                pointer = new Vector2(local.x - area.xMin, area.yMax - local.y);
            Show(map, pointer);
        }

        /// <summary>
        /// Кадр для съёмки префаба в редакторе: места (x, y — доли карты от левого верхнего угла,
        /// z — номер знака), враги, герой и место под мышью (−1 — никакое).
        /// </summary>
        public void Preview(Vector3[] places, Vector2[] enemies, Vector2 player, float angle, int hovered)
        {
            if (PlaceLayer == null) return;
            Vector2 size = PlaceLayer.rect.size;
            var map = new HudMinimap();
            foreach (Vector3 place in places)
            {
                Vector2 point = new Vector2(place.x * size.x, place.y * size.y);
                bool nearby = place.x > .1f && place.x < .9f && place.y > .1f && place.y < .9f;
                map.Marks.Add(new MapMark { Point = point, Symbol = (int)place.z, Nearby = nearby,
                    Kind = (int)place.z == 6 ? MapMarkKind.Boss : (int)place.z == 5 ? MapMarkKind.Reward : MapMarkKind.Place,
                    Name = (int)place.z == 4 ? "Выход" : (int)place.z == 5 ? "Награда" : "Лео", Distance = 23f });
            }
            foreach (Vector2 enemy in enemies)
                map.Marks.Add(new MapMark { Point = new Vector2(enemy.x * size.x, enemy.y * size.y), Kind = MapMarkKind.Enemy, Nearby = true });
            map.HasPlayer = true;
            map.PlayerPoint = new Vector2(player.x * size.x, player.y * size.y);
            map.PlayerAngle = angle;
            Vector2 pointer = hovered >= 0 && hovered < places.Length
                ? new Vector2(places[hovered].x * size.x, places[hovered].y * size.y) : new Vector2(float.MinValue, float.MinValue);
            Show(map, pointer);
        }

        void Show(HudMinimap map, Vector2 pointer)
        {
            Rect area = PlaceLayer.rect;
            int places = 0, enemies = 0, hovered = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < map.Marks.Count; i++)
            {
                MapMark mark = map.Marks[i];
                if (mark.Kind == MapMarkKind.Enemy)
                {
                    RectTransform dot = Take(_enemies, EnemyTemplate, EnemyLayer, enemies++);
                    if (dot != null) dot.anchoredPosition = new Vector2(mark.Point.x, -mark.Point.y);
                    continue;
                }
                float reach = (mark.Nearby ? PlaceSize : EdgeSize) * .5f + 3f;
                float distance = Vector2.Distance(pointer, mark.Point);
                if (distance <= reach && distance < nearest) { nearest = distance; hovered = places; }
                PlaceMark place = TakePlace(places++);
                if (place == null) continue;
                ShowPlace(place, mark);
            }
            for (int i = enemies; i < _enemies.Count; i++) SetActive(_enemies[i], false);
            for (int i = places; i < _places.Count; i++) SetActive(_places[i].Root, false);

            if (hovered != _hovered)
            {
                // Круг под мышью — поверх соседей и чуть крупнее.
                if (hovered >= 0) _places[hovered].Root.SetAsLastSibling();
                _hovered = hovered;
            }
            if (hovered >= 0)
            {
                MapMark mark = MarkOfPlace(map, hovered);
                float size = (mark.Nearby ? PlaceSize : EdgeSize) * HoverGrow;
                _places[hovered].Root.sizeDelta = new Vector2(size, size);
                ShowHint(mark, size, area);
            }
            else if (Hint != null && Hint.gameObject.activeSelf) Hint.gameObject.SetActive(false);

            if (Player != null)
            {
                SetActive(Player, map.HasPlayer);
                if (map.HasPlayer)
                {
                    Player.anchoredPosition = new Vector2(map.PlayerPoint.x, -map.PlayerPoint.y);
                    // HudMinimap считает поворот по часовой (ось Y вниз), холст — против.
                    Player.localEulerAngles = new Vector3(0f, 0f, -map.PlayerAngle);
                }
            }

            if (Fog != null)
            {
                bool fog = map.FogMask != null;
                if (Fog.enabled != fog) Fog.enabled = fog;
                if (fog)
                {
                    if (Fog.texture != map.FogMask) Fog.texture = map.FogMask;
                    Fog.uvRect = map.FogUv;
                }
            }
        }

        /// <summary>Места идут в <see cref="HudMinimap.Marks"/> вперемешку с врагами; n-е место по порядку.</summary>
        static MapMark MarkOfPlace(HudMinimap map, int place)
        {
            foreach (MapMark mark in map.Marks)
                if (mark.Kind != MapMarkKind.Enemy && place-- == 0) return mark;
            return default;
        }

        void ShowPlace(PlaceMark place, MapMark mark)
        {
            float size = mark.Nearby ? PlaceSize : EdgeSize;
            place.Root.sizeDelta = new Vector2(size, size);
            place.Root.anchoredPosition = new Vector2(mark.Point.x, -mark.Point.y);
            if (place.Rim != null)
            {
                UiTheme.Role role = RimRole(mark.Kind);
                if (place.Rim.Role != role) place.Rim.SetRole(role);
            }
            if (place.Symbol != null && place.ShownSymbol != mark.Symbol)
            {
                place.ShownSymbol = mark.Symbol;
                Texture2D texture;
                Rect uv;
                if (mark.Symbol == HudMinimap.AlchemistSymbol) { texture = AlchemistSymbol; uv = new Rect(0f, 0f, 1f, 1f); }
                else HudSymbols.MapCell(mark.Symbol, out texture, out uv);
                place.Symbol.texture = texture;
                place.Symbol.uvRect = uv;
                place.Symbol.enabled = texture != null;
                // Знак вписан в круг по своей пропорции: у атласа поля обрезаны неровно.
                float aspect = texture != null ? uv.width * texture.width / Mathf.Max(1f, uv.height * texture.height) : 1f;
                RectTransform symbol = place.Symbol.rectTransform;
                symbol.anchorMin = symbol.anchorMax = new Vector2(.5f, .5f);
                symbol.sizeDelta = Vector2.zero;
                Vector2 fill = aspect >= 1f ? new Vector2(1f, 1f / aspect) : new Vector2(aspect, 1f);
                symbol.anchorMin = new Vector2(.5f - fill.x * SymbolFill * .5f, .5f - fill.y * SymbolFill * .5f);
                symbol.anchorMax = new Vector2(.5f + fill.x * SymbolFill * .5f, .5f + fill.y * SymbolFill * .5f);
                symbol.anchoredPosition = Vector2.zero;
            }
        }

        void ShowHint(MapMark mark, float size, Rect area)
        {
            if (Hint == null || HintText == null) return;
            string text = mark.Name + " · " + Mathf.RoundToInt(mark.Distance) + " м";
            if (!Hint.gameObject.activeSelf) Hint.gameObject.SetActive(true);
            if (_hint != text)
            {
                _hint = text;
                HintText.text = text;
                float width = HintText.GetPreferredValues(text).x + HintPadding * 2f;
                Hint.sizeDelta = new Vector2(width, Hint.sizeDelta.y);
            }
            // Слева от метки: карта у правого края экрана, справа места нет.
            Vector3 at = PlaceLayer.TransformPoint(new Vector3(area.xMin + mark.Point.x - size * .5f - 6f, area.yMax - mark.Point.y, 0f));
            Hint.position = at;
        }

        static UiTheme.Role RimRole(MapMarkKind kind)
        {
            switch (kind)
            {
                case MapMarkKind.Boss: return UiTheme.Role.Bad;
                // Цель, к которой стоит идти, — оранжевым; остальное серебром.
                case MapMarkKind.Reward: case MapMarkKind.Drop: return UiTheme.Role.Accent;
                default: return UiTheme.Role.PanelLine;
            }
        }

        PlaceMark TakePlace(int index)
        {
            while (_places.Count <= index)
            {
                if (PlaceTemplate == null) return null;
                RectTransform root = Instantiate(PlaceTemplate, PlaceLayer);
                root.name = "Место " + _places.Count;
                Transform rim = root.Find("Оправа"), symbol = root.Find("Знак");
                _places.Add(new PlaceMark
                {
                    Root = root,
                    Rim = rim != null ? rim.GetComponent<ThemeColor>() : null,
                    Symbol = symbol != null ? symbol.GetComponent<RawImage>() : null,
                });
            }
            PlaceMark place = _places[index];
            SetActive(place.Root, true);
            return place;
        }

        static RectTransform Take(List<RectTransform> pool, RectTransform template, RectTransform parent, int index)
        {
            while (pool.Count <= index)
            {
                if (template == null || parent == null) return null;
                RectTransform copy = Instantiate(template, parent);
                copy.name = template.name + " " + pool.Count;
                pool.Add(copy);
            }
            SetActive(pool[index], true);
            return pool[index];
        }

        static void SetActive(Component item, bool active)
        {
            if (item != null && item.gameObject.activeSelf != active) item.gameObject.SetActive(active);
        }
    }
}
