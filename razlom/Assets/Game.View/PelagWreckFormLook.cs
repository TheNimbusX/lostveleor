using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Цвета Крушения по формам — ОДНА ТАБЛИЦА (Крушение v2, 03.10). База — бирюза #2EC4C9
    /// с белой пеной и тонким тёмным обводом (семья Вихря, Шквала, Абордажа; кадр A-base).
    /// Формы — цвета их иконок (ART/UI/icons-wreck-2026-10-03/sheet.png) и кадров:
    /// Волнорез — морская зелень #1FB37E (B-breakwater-seagreen), Девятый вал — индиго
    /// #4B3FD0 (C-ninth-wave), Водяной панцирь — жемчуг #E4EEF6 (D-shell). Красного,
    /// оранжевого и золота нет: это цвета телеграфов врагов. Править только <see cref="For"/>.
    ///
    /// Свои материалы Крушения (PelagWreckFoamVfxSetup) собраны в базе этой таблицы
    /// (<see cref="BaseDeep"/>…): у базы блок свойств СНИМАЕТСЯ — объект рисуется своими
    /// материалами, а принятые префабы семьи (всплеск, корона) — своими авторскими цветами.
    /// Формы — блоком свойств: у воды (Razlom/Whirlwind Form Water) _Deep/_Water/_Shallow,
    /// у капель и комьев (Razlom/Sabre Foam Blob) — _Shade; капли и комья принятых префабов —
    /// цветом частиц экземпляра пула (<see cref="ApplyDrops"/>, приём Шквала v2). Металл якоря
    /// не красится. Капли, которые выбрасывает вид, берут <see cref="DropColor"/>. Персонажи
    /// не трогаются (razlom-no-character-whitening): оболочка Панциря по телу не рисуется
    /// (тела пишут трафарет, вода по ним не ложится) и не ярче тела.
    /// </summary>
    public static class PelagWreckFormLook
    {
        public struct Palette
        {
            public Color Deep, Water, Shallow, Shade;
            public bool IsBase;
        }

        /// <summary>База Крушения: бирюза #2EC4C9 в тоне воды, глубина и светлая вода семьи.</summary>
        public static readonly Color BaseDeep = new Color(.03f, .24f, .32f);
        public static readonly Color BaseWater = new Color(.18f, .77f, .79f);
        public static readonly Color BaseShallow = new Color(.50f, .93f, .94f);
        public static readonly Color BaseShade = new Color(.74f, .90f, .95f);

        /// <summary>Светлая вода принятых префабов семьи (серия сабли, рывок, Вихрь) — от неё сдвиг капель формы.</summary>
        private static readonly Color FamilyShallow = new Color(.42f, .90f, .92f);

        private static Palette Make(Color deep, Color water, Color shallow, Color shade) => new Palette
        {
            Deep = deep, Water = water, Shallow = shallow, Shade = shade,
            IsBase = deep == BaseDeep && water == BaseWater && shallow == BaseShallow && shade == BaseShade
        };

        /// <summary>Цвета формы Крушения. ТАБЛИЦА ДЛЯ ПРАВКИ (те же цвета, что у иконок форм).</summary>
        public static Palette For(PelagForm form)
        {
            switch (form)
            {
                // Волнорез — морская зелень (#1FB37E, кадр B-breakwater-seagreen; тот же тон, что Гейзер Абордажа).
                case PelagForm.WreckBreakwater: return Make(new Color(.02f, .30f, .20f), new Color(.12f, .70f, .49f), new Color(.52f, .95f, .76f), new Color(.64f, .95f, .82f));
                // Девятый вал — индиго (#4B3FD0, кадр C-ninth-wave).
                case PelagForm.WreckNinthWave: return Make(new Color(.07f, .05f, .32f), new Color(.29f, .25f, .82f), new Color(.62f, .58f, 1f), new Color(.76f, .74f, .98f));
                // Водяной панцирь — жемчуг (#E4EEF6, кадр D-shell): светлая, но не белая вода, глубина серо-голубая.
                case PelagForm.WreckShell: return Make(new Color(.34f, .40f, .50f), new Color(.70f, .78f, .86f), new Color(.89f, .93f, .96f), new Color(.86f, .91f, .96f));
                default: return Make(BaseDeep, BaseWater, BaseShallow, BaseShade);
            }
        }

        /// <summary>Цвет капли, которую выбрасывает вид: белая пена (0) … светлая вода формы (1).</summary>
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
        private static readonly System.Collections.Generic.HashSet<GameObject> Tinted = new System.Collections.Generic.HashSet<GameObject>();

        /// <summary>Окрасить объект пула Крушения в цвет формы (база — снять окраску).</summary>
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

        // ---- капли принятых префабов семьи (всплеск серии сабли, корона рывка и Вихря, веер Пенных волн)

        private static readonly System.Collections.Generic.Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> AuthoredStart
            = new System.Collections.Generic.Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();
        private static readonly System.Collections.Generic.HashSet<GameObject> Recast = new System.Collections.Generic.HashSet<GameObject>();
        private static readonly System.Collections.Generic.List<ParticleSystem> Systems = new System.Collections.Generic.List<ParticleSystem>();

        /// <summary>
        /// Капли и комья принятого префаба семьи из пула Крушения — в цвет формы (бирюзовый отлив →
        /// светлая вода формы, белое остаётся белым: PelagSquallFoamRules.RecastDrop). Звать до
        /// PelagVfxElement.Begin. База — авторские цвета (объект пула мог прийти окрашенным).
        /// </summary>
        public static void ApplyDrops(GameObject go, PelagForm form)
        {
            if (go == null) return;
            Palette palette = For(form);
            if (palette.IsBase && !Recast.Remove(go)) return;
            if (!palette.IsBase) Recast.Add(go);
            Color shift = palette.Shallow - FamilyShallow;
            go.GetComponentsInChildren(true, Systems);
            foreach (ParticleSystem system in Systems)
            {
                if (system == null) continue;
                if (!AuthoredStart.TryGetValue(system, out ParticleSystem.MinMaxGradient authored))
                {
                    if (palette.IsBase) continue;
                    if (AuthoredStart.Count >= 256) ForgetDestroyedSystems();
                    authored = system.main.startColor;
                    AuthoredStart[system] = authored;
                }
                ParticleSystem.MainModule main = system.main;
                main.startColor = palette.IsBase ? authored : RecastGradient(authored, shift);
            }
            Systems.Clear();
        }

        private static ParticleSystem.MinMaxGradient RecastGradient(ParticleSystem.MinMaxGradient authored, Color shift)
        {
            switch (authored.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(RecastColor(authored.color, shift));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(RecastColor(authored.colorMin, shift), RecastColor(authored.colorMax, shift));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(RecastGradient(authored.gradient, shift));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(RecastGradient(authored.gradientMin, shift), RecastGradient(authored.gradientMax, shift));
                case ParticleSystemGradientMode.RandomColor:
                    return new ParticleSystem.MinMaxGradient(RecastGradient(authored.gradient, shift)) { mode = ParticleSystemGradientMode.RandomColor };
                default:
                    return authored;
            }
        }

        private static Color RecastColor(Color c, Color shift)
        {
            float r = c.r, g = c.g, b = c.b;
            PelagSquallFoamRules.RecastDrop(ref r, ref g, ref b, shift.r, shift.g, shift.b);
            return new Color(r, g, b, c.a);
        }

        private static Gradient RecastGradient(Gradient authored, Color shift)
        {
            if (authored == null) return null;
            GradientColorKey[] keys = authored.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = RecastColor(keys[i].color, shift);
            var gradient = new Gradient { mode = authored.mode };
            gradient.SetKeys(keys, authored.alphaKeys);
            return gradient;
        }

        /// <summary>Пулы пересоздаются с ареной: уничтоженные системы из памяти авторских цветов убрать.</summary>
        private static void ForgetDestroyedSystems()
        {
            var dead = new System.Collections.Generic.List<ParticleSystem>();
            foreach (ParticleSystem system in AuthoredStart.Keys)
                if (system == null) dead.Add(system);
            foreach (ParticleSystem system in dead) AuthoredStart.Remove(system);
            Recast.RemoveWhere(go => go == null);
        }
    }
}
