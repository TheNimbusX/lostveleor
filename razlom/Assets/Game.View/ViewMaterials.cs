using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Материалы для программно собираемой сцены.
    ///
    /// Имя свойства цвета отличается у URP (_BaseColor) и встроенного конвейера
    /// (_Color). Проект на URP, но откат оставлен: сцена собирается кодом, и
    /// молча чёрные объекты на чужом конвейере — плохой способ это узнать.
    /// </summary>
    public static class ViewMaterials
    {
        /// <summary>
        /// Цвет теневой полосы тун-шейдера.
        ///
        /// Лежит ЗДЕСЬ, а не двумя копиями в импортёре и в сборке материала на
        /// лету: разъехавшись, копии дают персонажа, который в игре одного
        /// цвета, а после переимпорта другого, — и причину такого не находят.
        ///
        /// ТЕНЬ ХОЛОДНАЯ И СВЕТЛАЯ. Два правила, и они не спорят.
        ///
        /// Светлая — потому что у мультяшной картинки тень это приглушённый
        /// свет, а не его отсутствие. Ранний фиолетовый (0.36,0.20,0.32) был
        /// тёмным и съедал раскраску: персонаж читался сиреневым силуэтом.
        ///
        /// Холодная — потому что сменивший его (0.80,0.72,0.76) был тёплым
        /// серым, а все три источника в сцене тоже тёплые. Свет и тень
        /// отличались только яркостью, и кадр выходил бежевым и плоским.
        /// Синева даёт разделение ПО ТОНУ, на котором стиль и держится.
        /// </summary>
        public static readonly Color ToonShadow = new Color(0.58f, 0.66f, 0.88f, 1f);

        private static Shader _lit;
        private static Shader _characterToon;
        private static Shader _arenaFloor;

        public static Material CreateLit(Color color)
        {
            // Явные проверки, а не ??: у UnityEngine.Object переопределён ==,
            // и смешивать его с оператором объединения с null — источник сюрпризов.
            if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
            if (_lit == null) _lit = Shader.Find("Standard");
            if (_lit == null) _lit = Shader.Find("Sprites/Default");

            if (_lit == null)
            {
                Debug.LogError("[Разлом] Не найден ни один шейдер для материалов сцены.");
                return null;
            }

            Material m = new Material(_lit) { name = $"Разлом/{ColorUtility.ToHtmlStringRGB(color)}" };
            SetColor(m, color);
            return m;
        }

        public static Material CreateCharacterToon()
        {
            if (_characterToon == null) _characterToon = Shader.Find("Razlom/VertexColorToon");
            if (_characterToon == null)
            {
                Debug.LogError("[Разлом] Не найден Razlom/VertexColorToon; используется обычный Lit.");
                return CreateLit(Color.white);
            }

            Material m = new Material(_characterToon) { name = "Разлом/Character Vertex Color Toon" };
            m.SetColor("_Tint", Color.white);
            m.SetFloat("_ToonSteps", 3f);
            m.SetColor("_OutlineColor", new Color(0.018f, 0.020f, 0.025f, 1f));
            m.SetFloat("_OutlineWidth", 0.006f);
            return m;
        }

        public static Material CreateArenaFloor(Color baseColor, Color accentColor)
        {
            if (_arenaFloor == null) _arenaFloor = Shader.Find("Razlom/Arena Floor");
            if (_arenaFloor == null) return CreateLit(baseColor);

            Material material = new Material(_arenaFloor) { name = "Разлом/Arena Floor" };
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_AccentColor", accentColor);
            // Сетка сведена к слабому шву между плитами камня: она даёт полу
            // масштаб и фактуру, но перестаёт читаться как отладочный grid.
            material.SetColor("_GridColor", new Color(0.34f, 0.31f, 0.26f, 1f));

            // РАЗМЕР ПЛИТЫ. 0.35 давало плиту почти в ТРИ МЕТРА — шире самого
            // персонажа, и на записи 1 сентября пол читался как разлинованная
            // пустота с ромбами во весь экран.
            //
            // 0.62 даёт плиту около полутора метров: примерно шаг человека,
            // и именно поэтому она сообщает масштаб. Шейдер поверх кладёт
            // подплитку вчетверо чаще и зерно — деталь на трёх частотах.
            material.SetFloat("_GridScale", 0.62f);
            material.SetFloat("_GridWidth", 0.014f);
            return material;
        }

        private static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }
    }
}
