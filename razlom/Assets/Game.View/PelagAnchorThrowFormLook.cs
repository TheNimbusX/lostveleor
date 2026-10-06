using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Цвета Броска якоря по формам — ОДНА ТАБЛИЦА (03.10, по целевым кадрам
    /// ART/characters/pelag/anchor-throw-2026-10-03/chatgpt-results A–D и листу иконок
    /// ART/UI/icons-anchor-throw-2026-10-03/sheet.png). База — та же бирюзовая морская пена,
    /// что у базового Вихря, Шквала и Абордажа (#2EC4C9 — средний тон между Water и Shallow
    /// семьи, белая пена). Невод — кобальт #2D5BE3 (широкая сеть пены с белыми узлами),
    /// Веер — индиго-фиолет #4B3FD0 (два полупрозрачных водяных якоря), Гарпун — маджента
    /// #D23C9C (насыщенная, как Охота Шквала после «бледно-розовой» проверки 02.10: укус и
    /// борозда волока). Красного, оранжевого и золота нет — цвета телеграфов врагов.
    /// Развести формы — править только <see cref="For"/>.
    ///
    /// Применение — как PelagSquallFormLook: блок свойств на объектах пулов Броска (вода
    /// Razlom/Whirlwind Form Water и призрак Razlom/Squall Foam Ghost — _Deep/_Water/_Shallow,
    /// капли и комья Razlom/Sabre Foam Blob — _Shade); пока цвет равен базе, блок снимается.
    /// Капли принятых префабов семьи (всплеск, корона, веер) — цветом частиц экземпляра пула
    /// (<see cref="ApplyDrops"/>), префабы и материалы не трогаются. Металл якоря не красится.
    /// </summary>
    public static class PelagAnchorThrowFormLook
    {
        public struct Palette
        {
            public Color Deep, Water, Shallow, Shade;
            public bool IsBase;
        }

        // База — бирюза Вихря, серии сабли, Шквала и Абордажа.
        private static readonly Color BaseDeep = new Color(.03f, .24f, .32f);
        private static readonly Color BaseWater = new Color(.06f, .60f, .68f);
        private static readonly Color BaseShallow = new Color(.42f, .90f, .92f);
        private static readonly Color BaseShade = new Color(.74f, .90f, .95f);

        private static Palette Make(Color deep, Color water, Color shallow, Color shade) => new Palette
        {
            Deep = deep, Water = water, Shallow = shallow, Shade = shade,
            IsBase = deep == BaseDeep && water == BaseWater && shallow == BaseShallow && shade == BaseShade
        };

        /// <summary>Цвета формы Броска якоря. ТАБЛИЦА ДЛЯ ПРАВКИ (те же цвета, что у Icon_AnchorThrow_*).</summary>
        public static Palette For(PelagForm form)
        {
            switch (form)
            {
                // Невод — кобальт (#2D5BE3, кадр B-net): вода сети глубокая синяя, узлы и гребни белые.
                case PelagForm.AnchorThrowNet: return Make(new Color(.04f, .10f, .38f), new Color(.18f, .36f, .89f), new Color(.58f, .74f, 1f), new Color(.74f, .84f, .98f));
                // Веер — индиго-фиолет (#4B3FD0, кадр C-fan): призраки якоря и водяные цепи.
                case PelagForm.AnchorThrowFan: return Make(new Color(.09f, .05f, .36f), new Color(.29f, .25f, .82f), new Color(.66f, .62f, 1f), new Color(.80f, .78f, .99f));
                // Гарпун — маджента (#D23C9C, кадр D-harpoon), насыщенная (как Охота Шквала, раунд 2).
                case PelagForm.AnchorThrowHarpoon: return Make(new Color(.36f, .02f, .24f), new Color(.86f, .10f, .58f), new Color(.96f, .36f, .76f), new Color(.96f, .50f, .82f));
                default: return Make(BaseDeep, BaseWater, BaseShallow, BaseShade);
            }
        }

        /// <summary>Капля, которую выбрасывает вид: белая пена, подмешанная к светлой воде формы (mix 0 — белая).</summary>
        public static Color DropColor(PelagForm form, float mix)
        {
            Palette palette = For(form);
            Color c = Color.Lerp(new Color(1.08f, 1.16f, 1.16f, 1f), palette.Shallow * 1.08f, Mathf.Clamp01(mix));
            c.a = 1f;
            return c;
        }

        private static readonly int DeepId = Shader.PropertyToID("_Deep");
        private static readonly int WaterId = Shader.PropertyToID("_Water");
        private static readonly int ShallowId = Shader.PropertyToID("_Shallow");
        private static readonly int ShadeId = Shader.PropertyToID("_Shade");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static MaterialPropertyBlock _block;
        private static readonly HashSet<GameObject> Tinted = new HashSet<GameObject>();

        /// <summary>Окрасить объект пула в цвет формы (база — снять окраску). Призрак Веера ведёт свой блок сам (<see cref="Write"/>).</summary>
        public static void Apply(GameObject go, PelagForm form)
        {
            if (go == null) return;
            Palette palette = For(form);
            if (palette.IsBase && !Tinted.Remove(go)) return;
            if (!palette.IsBase) Tinted.Add(go);
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null || m.HasProperty(DissolveId)) continue;
                bool water = m.HasProperty(WaterId), blob = m.HasProperty(ShadeId);
                if (!water && !blob) continue;
                if (palette.IsBase) { r.SetPropertyBlock(null); continue; }
                _block.Clear();
                Write(_block, m, palette);
                r.SetPropertyBlock(_block);
            }
        }

        /// <summary>Цвета палитры в блок (только те свойства, что есть у материала).</summary>
        public static void Write(MaterialPropertyBlock block, Material material, Palette palette)
        {
            if (material == null) return;
            if (material.HasProperty(WaterId))
            {
                block.SetColor(DeepId, palette.Deep);
                block.SetColor(WaterId, palette.Water);
                block.SetColor(ShallowId, palette.Shallow);
            }
            if (material.HasProperty(ShadeId)) block.SetColor(ShadeId, palette.Shade);
        }

        // ---- капли принятых префабов семьи (всплеск сабли, корона рывка, корона и веер Вихря)

        private static readonly Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> AuthoredStart
            = new Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();
        private static readonly HashSet<GameObject> Recast = new HashSet<GameObject>();
        private static readonly List<ParticleSystem> Systems = new List<ParticleSystem>();

        /// <summary>
        /// Капли префаба семьи из пула Броска — в цвет формы (бирюзовый отлив → Shallow формы, белое
        /// остаётся белым; правило PelagSquallFoamRules.RecastDrop). Звать до PelagVfxElement.Begin.
        /// </summary>
        public static void ApplyDrops(GameObject go, PelagForm form)
        {
            if (go == null) return;
            Palette palette = For(form);
            if (palette.IsBase && !Recast.Remove(go)) return;
            if (!palette.IsBase) Recast.Add(go);
            Color shift = palette.Shallow - BaseShallow;
            go.GetComponentsInChildren(true, Systems);
            foreach (ParticleSystem system in Systems)
            {
                if (!AuthoredStart.TryGetValue(system, out ParticleSystem.MinMaxGradient authored))
                {
                    if (palette.IsBase) continue;
                    if (AuthoredStart.Count >= 256) Forget();
                    authored = system.main.startColor;
                    AuthoredStart[system] = authored;
                }
                ParticleSystem.MainModule main = system.main;
                main.startColor = palette.IsBase ? authored : Recolor(authored, shift);
            }
            Systems.Clear();
        }

        private static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient g, Color shift)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Recolor(g.color, shift));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Recolor(g.colorMin, shift), Recolor(g.colorMax, shift));
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Recolor(g.gradient, shift));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Recolor(g.gradientMin, shift), Recolor(g.gradientMax, shift));
                case ParticleSystemGradientMode.RandomColor:
                    return new ParticleSystem.MinMaxGradient(Recolor(g.gradient, shift)) { mode = ParticleSystemGradientMode.RandomColor };
                default: return g;
            }
        }

        private static Color Recolor(Color c, Color shift)
        {
            float r = c.r, g = c.g, b = c.b;
            PelagSquallFoamRules.RecastDrop(ref r, ref g, ref b, shift.r, shift.g, shift.b);
            return new Color(r, g, b, c.a);
        }

        private static Gradient Recolor(Gradient authored, Color shift)
        {
            if (authored == null) return null;
            GradientColorKey[] keys = authored.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Recolor(keys[i].color, shift);
            var gradient = new Gradient { mode = authored.mode };
            gradient.SetKeys(keys, authored.alphaKeys);
            return gradient;
        }

        /// <summary>Пулы пересоздаются с ареной: уничтоженные системы из памяти авторских цветов убрать.</summary>
        private static void Forget()
        {
            var dead = new List<ParticleSystem>();
            foreach (ParticleSystem system in AuthoredStart.Keys) if (system == null) dead.Add(system);
            foreach (ParticleSystem system in dead) AuthoredStart.Remove(system);
            Recast.RemoveWhere(go => go == null);
        }
    }
}
