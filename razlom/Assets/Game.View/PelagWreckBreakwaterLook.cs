using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Крушение · ВОЛНОРЕЗ (07.10; целевой кадр ART/characters/pelag/wreck-look-2026-10-06/chatgpt-results/v4-form-breakwater.png;
    /// ассеты — Editor/PelagWreckComboVfxSetup.Breakwater, рождение — PelagVfxController.WreckBreakwater). Чистые числа вида без
    /// Unity. Стена — НАСТОЯЩИЕ освещённые 3D-плиты (меши пака), наклонены вперёд, как вал перед обрушением, между ними —
    /// свет морской зелени; плиты встают по одной ровно там, где фронт стены Sim (TryGetWreckWave: шаг 0,4 м/тик, полоса
    /// от точки удара до WallEnd, полуширина 1 м) проходит их место, с перелётом за 2–3 кадра, пылью и комьями у подошвы;
    /// за фронтом оседают, хвост у якоря крошится раньше, остальное рушится в обломки в тик обрушения Sim
    /// (WreckBreakwaterCrash). Пены и воды нет.
    /// </summary>
    public static class PelagWreckBreakwaterLook
    {
        /// <summary>Морская зелень формы (sRGB) — как у HUD и махов формы.</summary>
        public const int SeaGreenHex = 0x1FB37E;

        /// <summary>Плита стены: место вдоль полосы и поперёк (м), размеры (м), наклоны (град), меш (индекс ребёнка префаба).</summary>
        public struct Slab
        {
            public float Along, Across, Height, Width, Thick, Lean, Yaw, Roll;
            public int Child;
            /// <summary>Меш развёрнут на 180° вокруг своей вертикали (другая грань к камере; наклон — тот же, вперёд).</summary>
            public bool Main, Flip;
        }

        // ---------------------------------------------------------------- раскладка

        /// <summary>Главные плиты — через столько метров вдоль полосы (± разброс).</summary>
        public const float MainSpacing = .50f, SpacingJitter = .07f;

        /// <summary>Главная плита: высота (задание 0,9–1,6 м), ширина поперёк, толщина вдоль.</summary>
        // Раунд 3: плиты толщиной .38–.52 при шаге .5 сходились без щелей — свет между ними не был виден; тоньше.
        public const float MinHeight = .9f, MaxHeight = 1.6f, MainWidthMin = 1.0f, MainWidthMax = 1.3f, ThickMin = .28f, ThickMax = .38f;

        /// <summary>Боковые плиты (добирают ширину стены 2 м): доля главных с ней, высота, ширина, толщина.</summary>
        public const float FillerChance = .72f, FillerHeightMin = .55f, FillerHeightMax = .88f, FillerWidthMin = .5f, FillerWidthMax = .72f;

        /// <summary>Наклон вперёд, град: у якоря меньше, к концу стены больше — гребень «валится» вперёд, как вал.</summary>
        public const float LeanNear = 22f, LeanFar = 36f;

        /// <summary>Пул плит в префабе (детей «Slab00…»): стены 8 м хватает с запасом.</summary>
        public const int PoolSize = 30;

        /// <summary>
        /// Раскладка стены от <paramref name="start"/> до <paramref name="end"/> (м вдоль полосы от героя) шириной
        /// 2 × <paramref name="halfWidth"/>: главные плиты по оси, боковые — к краям; всё внутри полосы Sim (край плиты не
        /// дальше полуширины + 0,12 м). Высота главных — горбом (выше всего в середине стены), 0,9–1,6 м.
        /// </summary>
        public static Slab[] Layout(float start, float end, float halfWidth, int seed)
        {
            var random = new Random(seed);
            var list = new System.Collections.Generic.List<Slab>(PoolSize);
            float length = Math.Max(.1f, end - start);
            float s = start + .18f;
            int child = 0, side = random.NextDouble() < .5 ? 1 : -1;
            while (s <= end - .05f && list.Count < PoolSize)
            {
                float t = Clamp01((s - start) / length);
                // Раунд 1: ровный ряд одинаковых плит читался позвоночником — высота и ширина с разбросом.
                float height = Clamp(Lerp(.95f, MaxHeight, Bell(t)) * Range(random, .82f, 1.1f), MinHeight, MaxHeight);
                float width = Range(random, MainWidthMin, MainWidthMax);
                float across = Range(random, -.2f, .2f) * halfWidth;
                across = Clamp(across, -(halfWidth + .12f - width * .5f), halfWidth + .12f - width * .5f);
                list.Add(new Slab
                {
                    Along = s, Across = across, Height = height, Width = width, Thick = Range(random, ThickMin, ThickMax),
                    Lean = Lerp(LeanNear, LeanFar, t) + Range(random, -5f, 5f), Yaw = Range(random, -20f, 20f), Flip = random.NextDouble() < .5,
                    Roll = Range(random, -9f, 9f), Child = child++ % PoolSize, Main = true
                });
                if (list.Count < PoolSize && random.NextDouble() < FillerChance)
                {
                    side = random.NextDouble() < .7 ? -side : side;
                    float fw = Range(random, FillerWidthMin, FillerWidthMax);
                    float edge = halfWidth + .12f - fw * .5f;
                    float fx = side * Math.Min(edge, Range(random, .58f, .78f) * halfWidth + fw * .2f);
                    list.Add(new Slab
                    {
                        Along = Math.Min(end - .05f, s + Range(random, -.12f, .2f)), Across = fx,
                        Height = height * Range(random, FillerHeightMin, FillerHeightMax), Width = fw, Thick = Range(random, .24f, .32f),
                        Lean = Lerp(LeanNear, LeanFar, t) + Range(random, 2f, 9f), Yaw = Range(random, -18f, 18f),
                        Roll = side * Range(random, 6f, 14f), Child = child++ % PoolSize, Main = false
                    });
                }
                s += MainSpacing + Range(random, -SpacingJitter, SpacingJitter);
            }
            return list.ToArray();
        }

        /// <summary>Горб высоты по доле стены: ниже всего у якоря и в конце, выше всего чуть до середины.</summary>
        public static float Bell(float t) => (float)Math.Pow(Math.Sin(Math.PI * Clamp01(t * .9f + .08f)), .8);

        // ---------------------------------------------------------------- сроки (тики показа, 30 в секунду)

        /// <summary>
        /// Тик показа, когда фронт Sim доходит до места <paramref name="along"/>: шаг 1 — в тик удара (фронт = start + step),
        /// как PelagWreckVfxRules.WaveFront.
        /// </summary>
        public static float PassTick(float along, int tick, float start, float step)
            => tick - 1f + Math.Max(0f, along - start) / Math.Max(.01f, step);

        /// <summary>Выход плиты: за столько секунд из земли с перелётом (2–3 кадра при 60 к/с), потом отдача к покою.</summary>
        public const float PopSeconds = .05f, SettleSeconds = .12f;

        /// <summary>Перелёт выхода: выше покоя на долю высоты; плита выходит из-под земли (низ — на этой доле высоты ниже).</summary>
        public const float PopOvershoot = .07f, PopFrom = -.85f;

        /// <summary>Смещение плиты по высоте (доля её высоты) через <paramref name="age"/> с после прохода фронта.</summary>
        public static float PopLift(float age)
        {
            if (age <= 0f) return PopFrom;
            if (age < PopSeconds) return Lerp(PopFrom, PopOvershoot, (float)Math.Sin(Math.PI * .5 * age / PopSeconds));
            if (age < PopSeconds + SettleSeconds) return Lerp(PopOvershoot, 0f, Smooth01((age - PopSeconds) / SettleSeconds));
            return -Sink * Smooth01((age - PopSeconds - SettleSeconds) / .35f);
        }

        /// <summary>Оседание за фронтом (доля высоты) и доворот вперёд (град).</summary>
        public const float Sink = .05f, SettleLean = 4f;

        /// <summary>Наклон в выходе: плита встаёт почти прямо и «падает» в наклон с перелётом.</summary>
        public static float LeanAt(float age, float lean)
        {
            if (age <= 0f) return lean * .35f;
            if (age < PopSeconds) return Lerp(lean * .35f, lean * 1.12f, age / PopSeconds);
            if (age < PopSeconds + SettleSeconds) return Lerp(lean * 1.12f, lean, Smooth01((age - PopSeconds) / SettleSeconds));
            return lean + SettleLean * Smooth01((age - PopSeconds - SettleSeconds) / .35f);
        }

        /// <summary>Масштаб в выходе: по высоте (сжата → вытянута → 1), поперёк — обратно (хлопок).</summary>
        public static float PopStretch(float age)
        {
            if (age <= 0f) return .78f;
            if (age < PopSeconds) return Lerp(.78f, 1.08f, age / PopSeconds);
            return Lerp(1.08f, 1f, Smooth01((age - PopSeconds) / SettleSeconds));
        }

        /// <summary>Сила света трещин: вспышка выхода, к покою — ровный свет.</summary>
        public static float GlowAt(float age) => age <= 0f ? 0f : Lerp(2.4f, 1.05f, Smooth01(age / .28f));

        /// <summary>Плиты у якоря крошатся через столько тиков после выхода (хвост), остальные — в обрушение.</summary>
        public const float StandTicks = 13f;

        /// <summary>Обрушение Sim: стоящие плиты рушатся за столько тиков — сначала у конца стены (у преграды).</summary>
        public const float CollapseRippleTicks = 3f;

        /// <summary>Тик начала распада плиты: хвост — по возрасту, прочие — в обрушение (тик события), ближние к концу раньше.</summary>
        public static float CrumbleTick(float passTick, float crashTick, float along, float start, float end)
        {
            float ripple = CollapseRippleTicks * Clamp01((end - along) / Math.Max(.1f, end - start));
            return Math.Min(passTick + StandTicks, crashTick + ripple);
        }

        /// <summary>Распад: за столько секунд плита наклоняется, оседает, рвётся по трещинам и уходит; обломки — в доле Break.</summary>
        public const float CrumbleSeconds = .2f, Break = .42f, CrumbleLean = 20f;

        /// <summary>
        /// Доля распада по шейдеру (_Crumble) на доле времени распада: проба 2 — дыры по сетке трещин с начала распада читались
        /// рваной бумагой; теперь плита цела до 0,7 и в самом конце уходит крупными кусками (обломки уже летят).
        /// </summary>
        public static float CrumbleCut(float k) => k <= .7f ? 0f : Lerp(0f, .75f, (k - .7f) / .3f);

        /// <summary>Масштаб плиты в распаде: цела до Break, потом сжимается к подошве (её тело ушло в обломки).</summary>
        public static float CrumbleScale(float k) => k <= Break ? 1f : Lerp(1f, .3f, Smooth01((k - Break) / (1f - Break)));

        /// <summary>Оседание в распаде, доля высоты (до Break — немного, потом быстро в землю).</summary>
        public static float CrumbleSink(float k) => k <= 0f ? 0f : k < Break ? .14f * (k / Break) : Lerp(.14f, .9f, (k - Break) / (1f - Break));

        // ---------------------------------------------------------------- обрушение и несомые

        /// <summary>Всплеск обрушения у конца стены: полузвезда (ширина по кругу Sim), о преграду — крупнее.</summary>
        public static float CrashHalfWidth(float radius, bool obstacle) => Clamp(radius, .8f, 3f) * (obstacle ? 1.8f : 1.3f);

        /// <summary>Свет трещин на земле обрушения: диаметр билборда, чтобы видимый край лёг на круг Sim (лёжа ≈ 0,72 размера).</summary>
        public static float CrashGroundDiameter(float radius) => Clamp(radius, .8f, 3f) * 2f * 1.38f;

        /// <summary>Обломков в обрушении: о преграду — каменный взрыв.</summary>
        public static int CrashRocks(bool obstacle) => obstacle ? 18 : 8;

        /// <summary>Несомый на гребне: крошка из-под тела раз в столько секунд.</summary>
        public const float CarryChipSeconds = .07f;

        public static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        public static float Clamp01(float x) => Clamp(x, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float Range(Random random, float a, float b) => a + (b - a) * (float)random.NextDouble();
    }
}
