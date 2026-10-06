using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Цвета Шквала по формам — ОДНА ТАБЛИЦА (Шквал v2, 02.10). База — та же
    /// бирюзовая морская пена, что у базового Вихря (палитра серии сабли, вариант Б).
    /// Формы — цвета их иконок (владелец 02.10 вечер: «VFX шквала перекрасить под цвет иконок»):
    /// Охота — маджента, Пенный след — кобальт, Неуловимый — жемчуг с сиреневым отливом.
    /// Красного, оранжевого и золота нет: это цвета телеграфов врагов.
    /// Чтобы развести формы, править только <see cref="For"/>.
    ///
    /// Применение — блоком свойств на объектах пулов Шквала (как TintWhirlwindShared):
    /// у воды (Razlom/Whirlwind Form Water) и двойника — _Deep/_Water/_Shallow, у
    /// капель и комьев (Razlom/Sabre Foam Blob) — _Shade. Пока цвет формы равен базе,
    /// блок СНИМАЕТСЯ (SetPropertyBlock(null)): объект рисуется ровно своими
    /// материалами, а объект из пула, пришедший окрашенным, возвращается к базе.
    /// Капли принятых префабов семьи (всплеск, корона, волна-толчок) красятся цветом
    /// частиц экземпляра пула — <see cref="ApplyDrops"/>.
    /// </summary>
    public static class PelagSquallFormLook
    {
        public struct Palette
        {
            public Color Deep, Water, Shallow, Shade;
            public bool IsBase;
        }

        // База — бирюза Вихря и серии сабли (PelagWhirlwindFoamVfxSetup / PelagDashVfxSetup).
        private static readonly Color BaseDeep = new Color(.03f, .24f, .32f);
        private static readonly Color BaseWater = new Color(.06f, .60f, .68f);
        private static readonly Color BaseShallow = new Color(.42f, .90f, .92f);
        private static readonly Color BaseShade = new Color(.74f, .90f, .95f);

        private static Palette Make(Color deep, Color water, Color shallow, Color shade) => new Palette
        {
            Deep = deep, Water = water, Shallow = shallow, Shade = shade,
            IsBase = deep == BaseDeep && water == BaseWater && shallow == BaseShallow && shade == BaseShade
        };

        /// <summary>Цвета формы Шквала. ТАБЛИЦА ДЛЯ ПРАВКИ (те же цвета, что у Icon_Squall_*).</summary>
        public static Palette For(PelagForm form)
        {
            switch (form)
            {
                // Охота — маджента (#D23C9C, иконка Icon_Squall_Hunt). Раунд 2 (проверка 02.10):
                // светлая вода .98/.60/.85 читалась бледно-розовой — полоска маджента в белой пене,
                // бледнее иконки. Насыщенность поднята во всех трёх тонах воды и в кромке капель;
                // белые гребни пены (_Foam материала) не трогаются.
                case PelagForm.SquallHunt: return Make(new Color(.36f, .02f, .24f), new Color(.86f, .10f, .58f), new Color(.96f, .36f, .76f), new Color(.96f, .50f, .82f));
                // Пенный след — кобальт (#2D5BE3, иконка Icon_Squall_FoamTrail).
                case PelagForm.SquallFoamTrail: return Make(new Color(.04f, .10f, .38f), new Color(.16f, .36f, .88f), new Color(.58f, .74f, 1f), new Color(.74f, .84f, .98f));
                // Неуловимый — жемчуг с сиреневым отливом (иконка Icon_Squall_Elusive).
                case PelagForm.SquallElusive: return Make(new Color(.36f, .37f, .52f), new Color(.72f, .74f, .90f), new Color(.94f, .95f, 1f), new Color(.90f, .90f, .98f));
                default: return Make(BaseDeep, BaseWater, BaseShallow, BaseShade);
            }
        }

        private static readonly int DeepId = Shader.PropertyToID("_Deep");
        private static readonly int WaterId = Shader.PropertyToID("_Water");
        private static readonly int ShallowId = Shader.PropertyToID("_Shallow");
        private static readonly int ShadeId = Shader.PropertyToID("_Shade");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static MaterialPropertyBlock _block;
        /// <summary>Объекты пулов, на которых сейчас блок цвета формы (база блок снимает).</summary>
        private static readonly System.Collections.Generic.HashSet<GameObject> Tinted = new System.Collections.Generic.HashSet<GameObject>();

        /// <summary>
        /// Окрасить объект пула Шквала в цвет формы. Рендеры, которым блок нужен ещё
        /// для своих чисел (двойник: прозрачность и растворение), вызывают
        /// <see cref="Write"/> сами, поверх своего блока.
        /// </summary>
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
                // Двойник Неуловимого ведёт свой блок (прозрачность, растворение) — PelagSquallGhostParts.
                if (m == null || m.HasProperty(DissolveId)) continue;
                bool water = m.HasProperty(WaterId), blob = m.HasProperty(ShadeId);
                if (!water && !blob) continue;
                if (palette.IsBase) { r.SetPropertyBlock(null); continue; }
                _block.Clear();
                Write(_block, m, palette);
                r.SetPropertyBlock(_block);
            }
        }

        // ---- капли принятых префабов семьи ----
        //
        // Раунд 3 (проверка 02.10, Охота): всплеск на теле (VFX_Pelag_Sabre_Splash), корона у ноги
        // (VFX_Pelag_Dash_Splash) и волна-толчок (VFX_Pelag_Sabre_Crash) оставались бирюзово-белыми
        // в любой форме — тело их капель и комьев — цвет частицы (startColor), а блок свойств
        // Apply красит только кромку (_Shade) и воду волны. Эти префабы у Шквала в своих пулах
        // (SquallSplash/Crown/Surge), поэтому цвет частиц меняется у экземпляра пула; префабы и
        // материалы не трогаются, авторский цвет запоминается при первом касании, база его возвращает.

        private static readonly System.Collections.Generic.Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> AuthoredStart
            = new System.Collections.Generic.Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();
        /// <summary>Объекты пулов, у которых сейчас цвет частиц формы (база его возвращает).</summary>
        private static readonly System.Collections.Generic.HashSet<GameObject> Recast = new System.Collections.Generic.HashSet<GameObject>();
        private static readonly System.Collections.Generic.List<ParticleSystem> Systems = new System.Collections.Generic.List<ParticleSystem>();

        /// <summary>
        /// Капли и комья принятого префаба семьи из пула Шквала — в цвет формы (PelagSquallFoamRules.RecastDrop:
        /// бирюзовый отлив → Shallow формы, белое остаётся белым). Звать до PelagVfxElement.Begin:
        /// всплеск рождается уже в цвете. База — авторские цвета (объект пула мог прийти окрашенным).
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

        /// <summary>Записать цвета палитры в блок (только те свойства, что есть у материала).</summary>
        public static void Write(MaterialPropertyBlock block, Material material, Palette palette)
        {
            if (palette.IsBase || material == null) return;
            if (material.HasProperty(WaterId))
            {
                block.SetColor(DeepId, palette.Deep);
                block.SetColor(WaterId, palette.Water);
                block.SetColor(ShallowId, palette.Shallow);
            }
            if (material.HasProperty(ShadeId)) block.SetColor(ShadeId, palette.Shade);
        }
    }
}
