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
            material.SetColor("_GridColor", new Color(0.025f, 0.032f, 0.065f, 1f));
            material.SetFloat("_GridScale", 0.5f);
            material.SetFloat("_GridWidth", 0.055f);
            return material;
        }

        private static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }
    }
}
