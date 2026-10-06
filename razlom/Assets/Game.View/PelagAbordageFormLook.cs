using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Цвета Абордажа по формам — ОДНА ТАБЛИЦА (Абордаж v2, 02.10). База — та же
    /// бирюзовая морская пена, что у базового Вихря, Шквала и рывка (кадр A-base).
    /// Формы — цвета их иконок и кадров (Icon_AnchorLeap_*): Обвал — кобальт #2D5BE3
    /// (D-quake), Гейзер — морская зелень #1FB37E (F-geyser-seagreen), Пробоина —
    /// маджента #D23C9C (G-breach). Красного, оранжевого и золота нет: это цвета
    /// телеграфов врагов. Чтобы развести формы, править только <see cref="For"/>.
    ///
    /// Применение — блоком свойств на объектах пулов Абордажа (как PelagSquallFormLook):
    /// у воды (Razlom/Whirlwind Form Water) — _Deep/_Water/_Shallow, у капель и комьев
    /// (Razlom/Sabre Foam Blob) — _Shade. Пока цвет равен базе, блок СНИМАЕТСЯ: объект
    /// рисуется своими материалами, а объект из пула, пришедший окрашенным, — базой.
    /// Металл якоря и цепи не красится (у его материалов этих свойств нет).
    /// Капли, которые выбрасывает вид (EmitParams), берут цвет <see cref="DropColor"/>.
    /// </summary>
    public static class PelagAbordageFormLook
    {
        public struct Palette
        {
            public Color Deep, Water, Shallow, Shade;
            public bool IsBase;
        }

        // База — бирюза Вихря и серии сабли (как PelagSquallFormLook).
        private static readonly Color BaseDeep = new Color(.03f, .24f, .32f);
        private static readonly Color BaseWater = new Color(.06f, .60f, .68f);
        private static readonly Color BaseShallow = new Color(.42f, .90f, .92f);
        private static readonly Color BaseShade = new Color(.74f, .90f, .95f);

        private static Palette Make(Color deep, Color water, Color shallow, Color shade) => new Palette
        {
            Deep = deep, Water = water, Shallow = shallow, Shade = shade,
            IsBase = deep == BaseDeep && water == BaseWater && shallow == BaseShallow && shade == BaseShade
        };

        /// <summary>Цвета формы Абордажа. ТАБЛИЦА ДЛЯ ПРАВКИ (те же цвета, что у Icon_AnchorLeap_*).</summary>
        public static Palette For(PelagForm form)
        {
            switch (form)
            {
                // Обвал — кобальт (#2D5BE3, кадр D-quake).
                case PelagForm.AbordageQuake: return Make(new Color(.04f, .10f, .38f), new Color(.18f, .36f, .89f), new Color(.58f, .74f, 1f), new Color(.74f, .84f, .98f));
                // Гейзер — морская зелень (#1FB37E, кадр F-geyser-seagreen).
                case PelagForm.AbordageGeyser: return Make(new Color(.02f, .30f, .20f), new Color(.12f, .70f, .49f), new Color(.52f, .95f, .76f), new Color(.64f, .95f, .82f));
                // Пробоина — маджента (#D23C9C, кадр G-breach).
                case PelagForm.AbordageBreach: return Make(new Color(.30f, .04f, .20f), new Color(.82f, .24f, .61f), new Color(.98f, .60f, .85f), new Color(.98f, .74f, .90f));
                default: return Make(BaseDeep, BaseWater, BaseShallow, BaseShade);
            }
        }

        /// <summary>
        /// Цвет капли, которую выбрасывает вид: белая пена, подмешанная к светлой воде формы
        /// (<paramref name="mix"/> 0 — белая, 1 — цвет формы). Капли кадра G розовые, D — голубые.
        /// </summary>
        public static Color DropColor(PelagForm form, float mix)
        {
            Palette palette = For(form);
            Color white = new Color(1.08f, 1.16f, 1.16f, 1f);
            Color c = Color.Lerp(white, palette.Shallow * 1.08f, Mathf.Clamp01(mix));
            c.a = 1f;
            return c;
        }

        private static readonly int DeepId = Shader.PropertyToID("_Deep");
        private static readonly int WaterId = Shader.PropertyToID("_Water");
        private static readonly int ShallowId = Shader.PropertyToID("_Shallow");
        private static readonly int ShadeId = Shader.PropertyToID("_Shade");
        private static MaterialPropertyBlock _block;
        /// <summary>Объекты пулов, на которых сейчас блок цвета формы (база блок снимает).</summary>
        private static readonly System.Collections.Generic.HashSet<GameObject> Tinted = new System.Collections.Generic.HashSet<GameObject>();

        /// <summary>Окрасить объект пула Абордажа в цвет формы (база — снять окраску).</summary>
        public static void Apply(GameObject go, PelagForm form)
        {
            if (go == null) return;
            Palette palette = For(form);
            // База и объект без блока — ничего не делать (без обхода рендеров и без выделений памяти).
            if (palette.IsBase && !Tinted.Remove(go)) return;
            if (!palette.IsBase) Tinted.Add(go);
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null) continue;
                bool water = m.HasProperty(WaterId), blob = m.HasProperty(ShadeId);
                if (!water && !blob) continue;
                if (palette.IsBase) { r.SetPropertyBlock(null); continue; }
                _block.Clear();
                if (water)
                {
                    _block.SetColor(DeepId, palette.Deep);
                    _block.SetColor(WaterId, palette.Water);
                    _block.SetColor(ShallowId, palette.Shallow);
                }
                if (blob) _block.SetColor(ShadeId, palette.Shade);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
