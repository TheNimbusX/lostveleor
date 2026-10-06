using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Махи Крушения v4 «железо» на рисованной технике серии сабли (06.10; ассеты — Editor/PelagWreckSwingIronVfxSetup).
    /// Лента за головой якоря (.WreckSwing, V2) отвергнута владельцем и от пути Крушения отвязана (файлы на месте).
    /// Всё — от событий Sim в очереди до тика показа:
    ///  • контакт маха (WreckStage этапа 0, 1 и «Четвёртого») — полумесяц-«C» вокруг героя на высоте пояса (V3 по
    ///    целевым кадрам v4-swing-left/right.png 06.10): центр на WisAhead впереди, наружный радиус — доля сектора Sim
    ///    (Radius сборки), голова закручивается за спину. Мах 1 справа налево: голова полумесяца слева, нормаль вверх;
    ///    мах 2 — зеркало (корень повёрнут на 180° вокруг оси удара, как удар 2 сабли), толще и ярче;
    ///  • задетый махом (Damage маха) — звезда удара (у маха 2 крупная), голубые искры (у маха 2 — ещё «молнии»),
    ///    крупные тёмные сколы по ходу маха, клуб пыли у ног.
    /// Цвет формы — ядро полосы (Water/Shallow блоком свойств) и звенья: одна таблица PelagWreckSwingLook.
    /// </summary>
    public sealed partial class PelagVfxController
    {
        // Числа маха (индекс 0 — мах 1, 1 — мах 2 и «Четвёртый»): высота центра над землёй, крен плоскости вокруг оси удара
        // (+ — голова полумесяца выше хвоста). V3 по кадрам v4-swing-left/right.png: полумесяц почти плашмя на высоте пояса.
        private static readonly float[] WisHeight = { .86f, .90f };
        private static readonly float[] WisRoll = { -4f, 8f };
        /// <summary>
        /// Наружный радиус — доля сектора урона Sim (Radius сборки, 2,8 м): на кадрах полумесяц с центром чуть впереди
        /// героя достаёт задетых (≈2,2 и 2,4 м). Без сборки — 2,8 м.
        /// </summary>
        private static readonly float[] WisReach = { .80f, .86f };
        /// <summary>Центр впереди героя (кадры: голова закручивается за спину, фронт — у задетых).</summary>
        private const float WisAhead = .45f;
        // Знак на задетом: масштаб лёгкого и тяжёлого, высота груди как у знака «железа».
        private static readonly float[] WisHitScale = { 1f, 1.3f };

        private MaterialPropertyBlock _wisBlock;
        private static readonly int WisWaterId = Shader.PropertyToID("_Water"), WisShallowId = Shader.PropertyToID("_Shallow");

        /// <summary>Префабы махов на технике сабли собраны и в библиотеке: махи рисуются ими, лента не рождается.</summary>
        private bool WreckIronSwingReady => _pools != null && (int)PelagVfxId.WreckIronSwingHit < _pools.Length
            && _pools[(int)PelagVfxId.WreckIronSwing] != null && _pools[(int)PelagVfxId.WreckIronSwingHeavy] != null
            && _pools[(int)PelagVfxId.WreckIronSwingHit] != null;

        private static Color WisHdr(int hex, float gain)
        {
            PelagWreckSwingLook.Rgb(hex, out float r, out float g, out float b);
            return new Color(r * gain, g * gain, b * gain, 1f);
        }

        /// <summary>Контакт маха (тик показа): полумесяц по сектору этапа. False — префабов нет (тогда прежний путь).</summary>
        private bool PlayWreckIronSwing(in WkPending p)
        {
            if (!WreckIronSwingReady || !PelagWreckSwingRules.Draws(p.Stage)) return false;
            if (CaptureRig.NoVfx) return true;
            int side = Simulation.WreckSwingSide(p.Stage) < 0 || p.Stage == PelagWreckSwingRules.StageFourth ? 1 : 0;
            PelagVfxId id = p.Stage == PelagWreckSwingRules.StageSwing ? PelagVfxId.WreckIronSwing : PelagVfxId.WreckIronSwingHeavy;
            if (!TryAcquire(id, out GameObject go, out PelagVfxElement element)) return true;
            Vector3 forward = p.Wave.Dir;
            forward.y = 0f;
            forward = forward.sqrMagnitude > .01f ? forward.normalized : PlayerFacing();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 headSide = side == 1 ? right : -right;
            Vector3 normal = side == 1 ? Vector3.down : Vector3.up;
            // Крен вокруг оси удара: + поднимает сторону головы (у маха 2 голова справа, у маха 1 — слева).
            Quaternion roll = Quaternion.AngleAxis(side == 1 ? WisRoll[side] : -WisRoll[side], forward);
            Quaternion rotation = Quaternion.LookRotation(roll * normal, roll * headSide);
            Vector3 origin = p.At;
            Vector3 center = origin + Vector3.up * WisHeight[side] + forward * WisAhead;
            float radius = WreckSwingRadius() * WisReach[side];
            TintWreckIronSwing(go, _wsLastForm);
            int index = ReserveActive();
            element.Begin(center, rotation);
            go.transform.localScale = Vector3.one * radius;
            _active[index] = new ActiveFx
            {
                Active = true, Id = id, Object = go, Element = element,
                Duration = element.DefaultLifetime, Start = center, End = center, Motion = Motion.Static, FollowIndex = -1
            };
            if (CaptureRig.HasEnemyOverride)
                Debug.Log($"[wreck-iron-swing] stage={p.Stage} tick={p.Tick} side={side} center={center.ToString("F2")} forward={forward.ToString("F2")} form={_wsLastForm}");
            return true;
        }

        /// <summary>Сектор махов Sim (Radius сборки Крушения), м; без сборки — значение по умолчанию 2,8.</summary>
        private float WreckSwingRadius()
        {
            Simulation sim = _driver != null ? _driver.Sim : null;
            WreckState w = sim != null ? sim.Wreck : default;
            AbilityBuild build = sim != null && IsWreckSlot(sim, w.Slot) ? sim.GetAbility(w.Slot) : null;
            return build != null ? Mathf.Clamp(build.Get(AbilityStatType.Radius).ToFloat(), 1.6f, 4f) : 2.8f;
        }

        /// <summary>Цвет формы: ядро полосы (оба гребня) — блоком свойств, звенья — цветом частицы до старта.</summary>
        private void TintWreckIronSwing(GameObject go, PelagForm form)
        {
            PelagWreckSwingLook.Row look = PelagWreckSwingLook.For(form);
            if (_wisBlock == null) _wisBlock = new MaterialPropertyBlock();
            Transform root = go.transform;
            foreach (string name in new[] { "Wave" })
            {
                Renderer renderer = root.Find(name)?.GetComponent<Renderer>();
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_wisBlock);
                // Без усиления: ядро цвета формы не уходит в блум (V1 с 1,18 выцветала до белого).
                // V3: ядро на тон глубже цвета формы (кадр: яркий, но насыщенный голубой, а не голубой лёд).
                _wisBlock.SetColor(WisWaterId, WisHdr(look.Tint, .86f));
                _wisBlock.SetColor(WisShallowId, WisHdr(look.Light, 1f));
                renderer.SetPropertyBlock(_wisBlock);
            }
            // Звенья — светящиеся «призраки»: сердцевина почти белая с оттенком формы (кромку даёт материал).
            Color light = WisHdr(look.Light, 1f);
            Color links = Color.Lerp(light, Color.white, .55f) * 1.12f;
            links.a = 1f;
            foreach (string name in new[] { "LinksTail", "LinksHead" })
            {
                ParticleSystem strip = root.Find(name)?.GetComponent<ParticleSystem>();
                if (strip == null) continue;
                var main = strip.main;
                main.startColor = links;
            }
        }

        /// <summary>
        /// Знак на задетом махом (из PlayWreckSwingHit после отброса): звезда удара в груди, искры и сколы по ходу маха,
        /// клуб пыли у ног. False — префабов нет (тогда прежний росчерк).
        /// </summary>
        private bool PlayWreckIronSwingMark(Vector3 body, Vector3 dir, float radius, WreckSwingWeight weight)
        {
            if (!WreckIronSwingReady) return false;
            if (CaptureRig.NoVfx) return true;
            int heavy = weight == WreckSwingWeight.Heavy ? 1 : 0;
            float groundY = WreckGroundAt(body);
            var foot = new Vector3(body.x, groundY + .03f, body.z);
            Vector3 chest = foot + Vector3.up * Mathf.Clamp(.55f + .5f * radius, .7f, 1.4f);
            if (WiSpawn(PelagVfxId.WreckIronSwingHit, chest, Quaternion.LookRotation(dir, Vector3.up), 0f, 0f, out GameObject go) < 0) return true;
            // Звезда удара: у маха 1 — небольшая вспышка, у маха 2 — крупная бело-голубая звезда (v4-swing-right.png).
            go.transform.localScale = Vector3.one * (heavy == 1 ? 1.0f : .62f);
            Transform root = go.transform;
            PelagWreckSwingLook.Row look = PelagWreckSwingLook.For(_wsLastForm);
            Color light = WisHdr(look.Light, 1.2f), white = new Color(1.15f, 1.22f, 1.3f, 1f), tint = WisHdr(look.Tint, 1.1f);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            ParticleSystem sparks = root.Find("Sparks")?.GetComponent<ParticleSystem>();
            // Тонкие короткие штрихи по ходу маха; у маха 2 — ещё крупные «молнии» вверх и наружу.
            for (int i = 0; i < 7; i++)
            {
                Vector3 d = (dir * 1.2f + Vector3.up * Random.Range(.05f, .6f) + side * Random.Range(-.7f, .7f)).normalized;
                WiEmit(sparks, chest + Random.insideUnitSphere * .1f, d * Random.Range(5f, 9f), i % 3 == 0 ? white : light,
                    Random.Range(.03f, .05f), Random.Range(.10f, .18f));
            }
            for (int i = 0; heavy == 1 && i < 5; i++)
            {
                Vector3 d = (dir * .8f + Vector3.up * Random.Range(.7f, 1.3f) + side * Random.Range(-.9f, .9f)).normalized;
                WiEmit(sparks, chest + Random.insideUnitSphere * .08f, d * Random.Range(6f, 9.5f), i % 2 == 0 ? light : tint,
                    Random.Range(.10f, .15f), Random.Range(.16f, .24f));
            }
            // Сколы — крупные тёмные куски железа и камня (кадры: почти чёрные, с серой гранью), по ходу и вверх.
            ParticleSystem chips = root.Find("Chips")?.GetComponent<ParticleSystem>();
            Color iron = new Color(.24f, .24f, .27f, 1f), stone = new Color(.36f, .33f, .32f, 1f), steel = new Color(.46f, .52f, .62f, 1f);
            int big = heavy == 1 ? 5 : 3, small = heavy == 1 ? 4 : 4;
            for (int i = 0; i < big + small; i++)
            {
                bool large = i < big;
                Vector3 d = (dir * 1.1f + Vector3.up * .75f + Random.insideUnitSphere * .65f).normalized;
                WiEmit(chips, chest + Random.insideUnitSphere * .15f, d * Random.Range(2.6f, 5.2f), i % 4 == 0 ? steel : i % 2 == 0 ? stone : iron,
                    large ? Random.Range(.24f, .36f) * (heavy == 1 ? 1.15f : 1f) : Random.Range(.12f, .2f), Random.Range(.42f, .65f));
            }
            // Клуб пыли у ног и за задетым по ходу маха (светло-бурый, полупрозрачный).
            ParticleSystem dust = root.Find("Dust")?.GetComponent<ParticleSystem>();
            int dustCount = heavy == 1 ? 5 : 3;
            Color dirt = new Color(.70f, .58f, .45f, .8f);
            for (int i = 0; i < dustCount; i++)
                WiEmit(dust, foot + dir * Random.Range(.1f, .5f) + side * Random.Range(-.4f, .4f) + Vector3.up * .2f,
                    dir * Random.Range(.6f, 1.3f) + Vector3.up * Random.Range(.25f, .55f), dirt,
                    Random.Range(.8f, 1.1f) * WisHitScale[heavy], Random.Range(.5f, .7f));
            return true;
        }
    }
}
