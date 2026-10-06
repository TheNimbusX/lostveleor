using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Последнее событие Абордажа, после которого пришёл Damage того же тика (PelagAbordageVfxRules.ClassifyHit).</summary>
    public enum AbordageVfxCue : byte { None = 0, Punch = 1, Quake = 2, Breach = 3, GeyserLift = 4, GeyserFall = 5 }

    /// <summary>Чей удар нарисовать по Damage слота Абордажа.</summary>
    public enum AbordageVfxHit : byte
    {
        /// <summary>Не опознан (старый каст, витрина) — простой всплеск на теле.</summary>
        Other = 0,
        /// <summary>Кулак по цели — его всплеск рисует AbordagePunch, сам Damage ничего не добавляет.</summary>
        Fist = 1,
        /// <summary>Талант «На абордаж!»: кулак достал соседа в 2 м — всплеск поменьше.</summary>
        Sweep = 2,
        /// <summary>Волна Обвала дошла до тела — корона у ног (сбит с ног).</summary>
        Quake = 3,
        /// <summary>Струя Пробоины дошла до тела — всплеск по ходу струи, след волока у отброшенных.</summary>
        Breach = 4,
        /// <summary>Вода Гейзера упала — корона у ног.</summary>
        Fall = 5,
    }

    /// <summary>
    /// Числа вида Абордажа v2 без Unity — проверяются тестами представления
    /// (tools/Combat.Presentation.Tests/AbordageVfxRulesTests.cs). Время — тик показа
    /// sim.Tick − 2 + Alpha: тело героя рисуется на нём, и всё, что касается тела
    /// (укус, удар, фронт формы от кулака), встаёт ровно в его тик (как удар Шквала v2
    /// и корона рывка). Видимый край воды форм = край урона Sim (правило владельца
    /// 02.10 после Водоворота): фронт гребня приходит к радиусу Sim к концу хода и
    /// дотекает не дальше <see cref="FrontDrift"/>.
    /// </summary>
    public static class PelagAbordageVfxRules
    {
        /// <summary>Тик, на котором нарисовано тело (как PelagSquallClipRules.ShownTick).</summary>
        public static float ShownTick(int simTick, float alpha) => simTick - 2 + alpha;

        public static float Seconds(float ticks) => ticks / Simulation.TicksPerSecond;

        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        // ---------------------------------------------------------------- якорь и цепь

        /// <summary>Скорость якоря на выпуске — доля средней: бросок от плеча, к укусу чуть тормозит.</summary>
        public const float AnchorLead = 1.4f;

        /// <summary>
        /// Доля пути якоря от руки до точки укуса на тике показа <paramref name="shown"/>:
        /// 0 в тик выпуска, 1 в тик зацепа. Без дуги (спека 5: «якорь летит прямо»), с
        /// резким выходом из руки: x·(1,4 − 0,4x).
        /// </summary>
        public static float AnchorTravel(float shown, int releaseTick, int biteTick)
        {
            float span = Math.Max(1, biteTick - releaseTick);
            float x = Clamp01((shown - releaseTick) / span);
            return x * (AnchorLead - (AnchorLead - 1f) * x);
        }

        /// <summary>Цепь сматывается после удара или срыва: якорь к руке за столько тиков (0,2 с, спека 5).</summary>
        public const int RetractTicks = 6;

        /// <summary>Доля пути назад к руке (S-кривая): 0 в тик начала, 1 через <paramref name="ticks"/>.</summary>
        public static float Retract(float shown, float startTick, int ticks)
            => Smooth01((shown - startTick) / Math.Max(1, ticks));

        /// <summary>Высота укуса на теле цели, м: грудь (Хранитель r 0,85 → 1,26 м).</summary>
        public static float BiteHeight(float bodyRadius)
        {
            float h = .75f + .6f * bodyRadius;
            return h < .9f ? .9f : h > 2f ? 2f : h;
        }

        /// <summary>
        /// Видимое тело уже радиуса Sim (Хранитель: r 0,85, грудь модели ~0,35 м от центра — укус
        /// на r висел в воздухе в ~0,5 м перед телом). Точка укуса в виде и начало струи Пробоины —
        /// на этой доле радиуса от центра: крюк входит в тело (кадр A). Время и точки Sim не меняются.
        /// </summary>
        public const float VisibleBodyShare = .42f;

        public static float VisibleBodyRadius(float bodyRadius) => Math.Max(0f, bodyRadius) * VisibleBodyShare;

        /// <summary>Насколько видимая поверхность тела глубже края тела Sim, м.</summary>
        public static float BodyInset(float bodyRadius) => Math.Max(0f, bodyRadius) - VisibleBodyRadius(bodyRadius);

        /// <summary>В полёте цепь выдаётся с запасом (доля длины) — читается «выдача»; натянутая — ровно в длину.</summary>
        public const float FlightSlack = .04f;

        public static float ChainPayout(float span, bool taut) => taut ? span : span * (1f + FlightSlack);

        /// <summary>
        /// Натяг цепи 0…1: в полёте 0,4 (цепь выдаётся), в зацеп за 2 тика — 1 (тело ещё
        /// стоит, цепь прямая), после удара или срыва (<paramref name="releaseTick"/>; −1 — ещё нет)
        /// за 3 тика — 0. По нему — ширина водяной ленты вдоль цепи и сколько капель с неё срывается.
        /// </summary>
        public static float Tension(float shown, int biteTick, float releaseTick)
        {
            float t = shown < biteTick ? TensionFlight : TensionFlight + (1f - TensionFlight) * Smooth01((shown - biteTick) / 2f);
            if (releaseTick >= 0f && shown >= releaseTick) t *= 1f - Smooth01((shown - releaseTick) / 3f);
            return t;
        }

        public const float TensionFlight = .4f;

        /// <summary>Полуширина водяной ленты вдоль цепи, м: тонкая нить в полёте, натянутая — шире.</summary>
        public static float SleeveHalfWidth(float tension) => .022f + .03f * Clamp01(tension);

        /// <summary>Капли с цепи, штук в секунду на метр: в полёте редкие, натянутая — сыплет.</summary>
        public static float SleeveDropRate(float tension) => 14f + 46f * Clamp01(tension);

        /// <summary>Лента рвётся на капли после удара: возраст распада идёт быстрее времени (0,30 → ~0,19 с).</summary>
        public const float SleeveBreakRate = 1.6f;

        public static float SleeveBreakAge(float shown, float releaseTick)
            => releaseTick < 0f || shown < releaseTick ? 0f : Seconds(shown - releaseTick) * SleeveBreakRate;

        /// <summary>Росчерк за головой якоря: не длиннее 1,2 м и 60 % пройденного пути.</summary>
        public const float StreakMax = 1.2f, StreakShare = .6f;

        public static float StreakLength(float flown)
        {
            float l = Math.Max(0f, flown) * StreakShare;
            return l < StreakMax ? l : StreakMax;
        }

        /// <summary>Хвост росчерка старше головы, с; после зацепа росчерк тает за ~0,15 с (возраст распада ×2,2).</summary>
        public const float StreakTailAge = .12f, StreakFadeRate = 2.2f;

        public static float StreakAge(float shown, int biteTick, float along01)
        {
            float age = (1f - Clamp01(along01)) * StreakTailAge;
            return shown <= biteTick ? age : age + Seconds(shown - biteTick) * StreakFadeRate;
        }

        // ---------------------------------------------------------------- кто бит

        /// <summary>
        /// Чей удар у Damage слота Абордажа. Sim пишет события в порядке: AbordagePunch →
        /// Damage цели (кулак) → Damage соседей («На абордаж!») → событие формы → Damage
        /// первого шага фронта; дальше фронт бьёт в следующих тиках без своего события,
        /// падение Гейзера — AbordageGeyserFall → Damage. Последнее событие того же тика
        /// решает; иначе — окно хода фронта Обвала/Пробоины; иначе — не опознан.
        /// </summary>
        public static AbordageVfxHit ClassifyHit(AbordageVfxCue lastCue, int lastCueTick, int fistTarget, int hitTick, int hitTarget,
            AbordageVfxCue waveCue, int waveStartTick, int waveTravelTicks)
        {
            if (lastCue != AbordageVfxCue.None && hitTick == lastCueTick)
            {
                switch (lastCue)
                {
                    case AbordageVfxCue.Punch: return hitTarget == fistTarget ? AbordageVfxHit.Fist : AbordageVfxHit.Sweep;
                    case AbordageVfxCue.Quake: return AbordageVfxHit.Quake;
                    case AbordageVfxCue.Breach: return AbordageVfxHit.Breach;
                    case AbordageVfxCue.GeyserFall: return AbordageVfxHit.Fall;
                }
            }
            if ((waveCue == AbordageVfxCue.Quake || waveCue == AbordageVfxCue.Breach) && waveStartTick >= 0
                && hitTick >= waveStartTick && hitTick < waveStartTick + Math.Max(1, waveTravelTicks))
                return waveCue == AbordageVfxCue.Quake ? AbordageVfxHit.Quake : AbordageVfxHit.Breach;
            return AbordageVfxHit.Other;
        }

        // ---------------------------------------------------------------- фронт воды форм

        /// <summary>Гребень выходит на полтика раньше шага Sim (шаг 1 бьёт уже в тик удара).</summary>
        public const float FrontLeadTicks = .5f;

        /// <summary>Сколько гребень дотекает за край урона после хода, м.</summary>
        public const float FrontDrift = .12f;

        /// <summary>Разгон гребня Обвала: выход в 1,85 средней скорости, к краю — 0,15 (не застывает, а дотекает).</summary>
        public const float QuakeEase = .85f;

        /// <summary>Разгон струи Пробоины.</summary>
        public const float BreachEase = .7f;

        /// <summary>Доля хода фронта на тике показа: 0 — до удара, 1 — фронт Sim дошёл до края.</summary>
        public static float FrontProgress(float shown, int startTick, int travelTicks)
            => (shown - startTick + FrontLeadTicks) / Math.Max(1, travelTicks);

        /// <summary>
        /// Гребень, м: на ходу reach·(x + e·x(1 − x)) — быстрый выход и замедление; после хода
        /// скорость конца хода гаснет экспонентой так, что гребень дотекает ровно на FrontDrift
        /// (скорость без излома — «не застывает», край урона не врёт).
        /// </summary>
        public static float Front(float shown, int startTick, int travelTicks, float reach, float ease)
        {
            float x = FrontProgress(shown, startTick, travelTicks);
            if (x <= 0f) return 0f;
            if (x <= 1f) return reach * (x + ease * x * (1f - x));
            float travelSeconds = Seconds(Math.Max(1, travelTicks));
            float endSpeed = reach * (1f - ease) / travelSeconds;
            if (endSpeed <= 1e-4f) return reach;
            float tau = FrontDrift / endSpeed;
            float after = (x - 1f) * travelSeconds;
            return reach + FrontDrift * (1f - (float)Math.Exp(-after / tau));
        }

        public static float QuakeCrest(float shown, int startTick, int travelTicks, float radius)
            => Front(shown, startTick, travelTicks, radius, QuakeEase);

        // ---------------------------------------------------------------- Обвал, круг 4 (всплеск кадра D)

        /// <summary>
        /// Круг 4 (ревью 03.10 в игре: ровное «солнце» с пилой по краю, сиреневая полоса у героя): Обвал —
        /// неровный всплеск кадра D. Крупных лопастей воды по кругу; каждая третья — до края урона Sim,
        /// остальные короче (<see cref="QuakeLobeShortest"/>…0,95), между крупными — малые.
        /// </summary>
        public const int QuakeLobes = 10;

        /// <summary>Самая короткая крупная лопасть — доля гребня (край урона Sim).</summary>
        public const float QuakeLobeShortest = .70f;

        /// <summary>Край воды в самом глубоком просвете между лопастями — доля гребня.</summary>
        public const float QuakeLobeValley = .55f;

        /// <summary>
        /// Край воды по углу, м: <paramref name="lobe"/> — профиль лопастей 0…1 (длина × круглый горб
        /// <see cref="FingerSoft"/>). Кончик длинной лопасти — ровно на гребне, наружу — никогда.
        /// </summary>
        public static float QuakeLobeEdge(float crest, float lobe)
            => Math.Max(0f, crest) * (QuakeLobeValley + (1f - QuakeLobeValley) * Clamp01(lobe));

        /// <summary>
        /// Мокрая земля у героя (кадр D — бурый «цветок» под кулаком): ядро не шире
        /// <see cref="QuakeEarthCoreMax"/> м (доля гребня у кулака), вокруг — круглые лепестки земли
        /// (<see cref="QuakePetals"/>, длина — доля гребня <see cref="QuakePetalMin"/>…<see cref="QuakePetalMax"/>),
        /// в просветах между лопастями — лучи земли с круглым концом чуть дальше лепестков (не звезда до края)
        /// (<see cref="QuakeRayMin"/>…<see cref="QuakeRayMax"/>).
        /// </summary>
        public const float QuakeEarthCoreMax = .65f, QuakeEarthCoreShare = .22f;
        public const int QuakePetals = 14;
        public const float QuakePetalMin = .28f, QuakePetalMax = .42f;
        public const float QuakeRayMin = .42f, QuakeRayMax = .58f;

        public static float QuakeEarthCore(float crest) => Math.Max(0f, Math.Min(QuakeEarthCoreMax, crest * QuakeEarthCoreShare));

        /// <summary>
        /// Круг 5 (проверка круга 4 в игре: лучи земли — длинные гладкие «доски» со светлыми полосами вдоль): земля —
        /// мокрая грязь, как в кадре D. Цвет — шум по месту, не полярный: пятна (мягкий шум пака «perlin mid» ×
        /// <see cref="QuakeMudScale"/> /м и мелкий × <see cref="QuakeMudFineScale"/> /м), три тона с чёткой кромкой —
        /// тёмный ниже <see cref="QuakeMudDark"/>, светлый выше <see cref="QuakeMudLight"/>; белые капли — пузыри
        /// пака × <see cref="QuakeMudDropScale"/> /м выше порога <see cref="QuakeMudDropCut"/>. Те же числа — в
        /// материале M_Abordage_Quake (_Mud, _MudTones).
        /// </summary>
        public const float QuakeMudScale = .8f, QuakeMudFineScale = 1.76f, QuakeMudDark = .42f, QuakeMudLight = .64f;
        public const float QuakeMudDropScale = 1f, QuakeMudDropCut = .62f;

        /// <summary>
        /// Пятно шума «perlin mid» выше порога — около 0,16 клетки текстуры (замер круга 5): поперечник пятна
        /// грязи, м, при масштабе <paramref name="scale"/> /м.
        /// </summary>
        public static float QuakeMudPatchSize(float scale) => .16f / Math.Max(.01f, scale);

        /// <summary>Пузырь «noise bubbles» выше порога ~0,6 — около 0,056 клетки: поперечник капли, м.</summary>
        public static float QuakeMudDropSize(float scale) => .056f / Math.Max(.01f, scale);

        /// <summary>
        /// Граница земли по углу, м: ядро, лепесток (<paramref name="petal"/> — доля гребня с профилем) или
        /// луч (<paramref name="ray"/>), что дальше. Земля непрозрачна — между героем и синим нет сиреневой полосы.
        /// </summary>
        public static float QuakeEarthEdge(float crest, float petal, float ray)
        {
            crest = Math.Max(0f, crest);
            return Math.Max(QuakeEarthCore(crest), crest * Math.Max(Clamp01(petal), Clamp01(ray)));
        }

        /// <summary>Вес белой пены на краю (кадр D — пена на кончиках лопастей): просвет 0,3, кончик длинной — 1.</summary>
        public static float QuakeTipFoam(float lobe) => .30f + .70f * Smooth01((lobe - .55f) / .45f);

        /// <summary>
        /// Круг 3: шум воды Обвала берётся в полярной координате с медленным радиусом — по кругу частый,
        /// по радиусу редкий, пятна и струи шейдера вытягиваются в лучи от центра (кадр D).
        /// Радиус шума для мирового радиуса <paramref name="r"/>, м.
        /// </summary>
        public const float StreakNoiseBase = 6f, StreakNoiseRate = .3f;

        public static float StreakNoiseRadius(float r) => StreakNoiseBase + StreakNoiseRate * r;

        /// <summary>Во сколько раз пятно шума длиннее вдоль луча, чем поперёк, на радиусе <paramref name="r"/>.</summary>
        public static float StreakStretch(float r) => StreakNoiseRadius(r) / Math.Max(.01f, r) / StreakNoiseRate;

        /// <summary>
        /// Распад Обвала (круг 4; круг 3 белил весь диск в кремовую звезду на +0,37…+0,50 с): по возрасту
        /// распада (0 — конец хода фронта) вода и земля уходят прозрачностью и дырами от центра наружу —
        /// фронт дыр с <see cref="QuakeHoleStart"/> за <see cref="QuakeHoleSweep"/> с проходит край лопасти;
        /// пена остаётся тонкой каймой и рвётся на капли с <see cref="QuakeRimDropsFrom"/> до
        /// <see cref="QuakeRimDropsGone"/>; страховка гасит всё к <see cref="QuakeFadeTo"/>. Те же числа — в
        /// материале M_Abordage_Quake (PelagAbordageFoamVfxSetup).
        /// </summary>
        public const float QuakeHoleStart = .15f, QuakeHoleSweep = .28f;

        /// <summary>
        /// Круг 5 (проверка круга 4 в игре: ~3 кадра сиреневые и рыжие точки по кромке дыр — мягкая кромка 0,22
        /// рисовала полупрозрачный кобальт и землю поверх охры): кромка дыры — жёсткий срез на столько впереди
        /// фронта (доли края), ровно там, где мягкая кромка была наполовину прозрачной, — срок дыр прежний.
        /// </summary>
        public const float QuakeHoleCut = .11f;

        /// <summary>
        /// Фронт дыр стартует за центром на столько (доли края): шум дыр (±<see cref="QuakeHoleNoise"/>) и срез
        /// (<see cref="QuakeHoleCut"/>) не съедают ядро раньше срока — до старта всплеск целый.
        /// </summary>
        public const float QuakeHoleLead = .5f, QuakeHoleNoise = .225f;
        public const float QuakeRimThinFrom = .12f, QuakeRimDropsFrom = .27f, QuakeRimDropsGone = .42f;
        public const float QuakeFadeFrom = .43f, QuakeFadeTo = .48f;

        /// <summary>Фронт дыр распада в долях края лопасти (как в шейдере): за ним вода и земля ушли.</summary>
        public static float QuakeHoleFront(float age)
            => -QuakeHoleLead + (1.3f + QuakeHoleLead) * Smooth01((age - QuakeHoleStart) / QuakeHoleSweep);

        /// <summary>Возраст распада Обвала на столько секунд после удара (ход 5 тиков, запас 0,02 с), с.</summary>
        public static float QuakeAgeAt(float secondsSinceCast, int travelTicks)
            => Math.Max(0f, secondsSinceCast + Seconds(FrontLeadTicks - Math.Max(1, travelTicks)) + .02f);

        /// <summary>Возраст распада воды формы (шейдер рвёт её на 0,30): ноль на ходу, после хода — секунды + запас.</summary>
        public static float FrontBreakAge(float shown, int startTick, int travelTicks, float lead)
        {
            float after = Seconds(shown - startTick + FrontLeadTicks - Math.Max(1, travelTicks));
            return Math.Max(0f, after + lead);
        }

        /// <summary>Сколько живёт объект волны: ход + распад (0,30) + капли + запас, с.</summary>
        public static float FrontLifeSeconds(int travelTicks) => Seconds(travelTicks) + .55f;

        public static float BreachFront(float shown, int startTick, int travelTicks, float length)
            => Front(shown, startTick, travelTicks, length, BreachEase);

        /// <summary>Полуугол конуса Пробоины, град — из Sim (AbordageBreachConeCos = cos 25°).</summary>
        public static float BreachHalfAngleDegrees
            => (float)(Math.Acos(Simulation.AbordageBreachConeCos.ToFloat()) * 180.0 / Math.PI);

        /// <summary>Ширина струи у вершины (прокол сквозь цель), м.</summary>
        public const float BreachApexHalf = .18f;

        /// <summary>Полуширина струи на расстоянии <paramref name="along"/> от вершины, м: край конуса Sim.</summary>
        public static float BreachHalfWidth(float along)
            => BreachApexHalf + Math.Max(0f, along) * (float)Math.Tan(BreachHalfAngleDegrees * Math.PI / 180.0);

        /// <summary>
        /// Ревью 03.10 (кадр G): дальний конец струи был прямым срезом поперёк оси. Ряд на
        /// расстоянии <paramref name="along"/> от вершины гнётся дугой вокруг вершины — сектор,
        /// как зона урона Sim (длина × полуугол): точка с боковым сдвигом <paramref name="across"/>
        /// отступает назад на столько, м, чтобы лечь на окружность радиуса <paramref name="along"/>.
        /// У вершины (сдвиг больше 0,8 пути) — не больше 0,4 пути: ряды не схлопываются.
        /// </summary>
        public static float BreachArcBack(float across, float along)
        {
            if (along <= 0f) return 0f;
            float a = Math.Min(Math.Abs(across), .8f * along);
            return along - (float)Math.Sqrt(along * along - a * a);
        }

        /// <summary>Нос струи белеет пеной и рвётся на капли: последние столько метров, до фронта.</summary>
        public const float BreachTipLength = .75f;

        /// <summary>
        /// Возраст распада у носа струи на ходу, с (шейдер белеет пеной с 0,24 и рвётся на капли
        /// с 0,30): у самого фронта 0,34 — капли и клочья, к <see cref="BreachTipLength"/> — живая вода.
        /// </summary>
        public static float BreachTipAge(float toFront)
            => .34f * (1f - Smooth01(Math.Max(0f, toFront) / BreachTipLength));

        // ---------------------------------------------------------------- лопасти и лучи (Обвал), розетка Гейзера

        /// <summary>
        /// Острый клин: 1 на оси, 0 на краю (|u| = 1) и дальше —
        /// (1 − |u|)²: лучи мокрой земли Обвала в просветах между лопастями (круг 4, кадр D).
        /// </summary>
        public static float Finger(float u)
        {
            float a = Math.Abs(u);
            if (a >= 1f) return 0f;
            float b = 1f - a;
            return b * b;
        }

        /// <summary>Клин (1 − |u|): лучи мокрой земли Обвала в просветах между лопастями (круг 4) — шире острого.</summary>
        public static float FingerWedge(float u)
        {
            float a = Math.Abs(u);
            return a >= 1f ? 0f : 1f - a;
        }

        /// <summary>
        /// Лепесток с круглым концом √(1 − u²): бурые лепестки земли у героя (круг 4, кадр D) — полные, с крутыми
        /// боками и круглым концом, а не тонкий шип.
        /// </summary>
        public static float FingerRound(float u)
        {
            float a = Math.Abs(u);
            return a >= 1f ? 0f : (float)Math.Sqrt(1f - a * a);
        }

        /// <summary>
        /// Гладкий горб (1 − u²)²: круглые лопасти воды и лепестки земли Обвала (круг 4, <see cref="QuakeLobeEdge"/>) и
        /// круглые лепестки розетки у основания Гейзера (круг 3, кадр F).
        /// </summary>
        public static float FingerSoft(float u)
        {
            float a = Math.Abs(u);
            if (a >= 1f) return 0f;
            float b = 1f - a * a;
            return b * b;
        }

        /// <summary>
        /// Внешний край розетки Гейзера по
        /// углу, м. Кончики лепестков (<paramref name="finger"/> = 1) — ровно на гребне;
        /// между лепестками край уходит внутрь до <paramref name="notch"/>. Наружу — никогда.
        /// </summary>
        public static float RaggedOuter(float crest, float finger, float notch)
            => crest - Math.Max(0f, notch) * (1f - Clamp01(finger));

        /// <summary>Комья земли Обвала (кадр D): множитель тяжести слоя «Clods» — по нему вид считает бросок.</summary>
        public const float QuakeClodGravity = 2.4f;

        /// <summary>
        /// Круг 3 (ревью 03.10: комьев не видно — маска растворения пака рвала их в крошку, бурый ≈ охра
        /// пола): ком — сплошной гранёный камень CFXR «debris unlit 3x3» без растворения, с тёмным обводом
        /// семьи. Размер частицы, м; камень занимает <see cref="QuakeClodFill"/> клетки.
        /// </summary>
        public const float QuakeClodSizeMin = .22f, QuakeClodSizeMax = .36f, QuakeClodFill = .75f;

        /// <summary>
        /// Круг 4 (ревью 03.10: первые 0,1 с комья лежали кучей на ногах героя): ком вылетает снаружи тела —
        /// с радиуса <see cref="QuakeClodStartMin"/>…<see cref="QuakeClodStartMax"/> м (видимое тело героя ≈0,35 м
        /// + полкома), дальность — доля радиуса Sim <see cref="QuakeClodReachMin"/>…<see cref="QuakeClodReachMax"/>.
        /// </summary>
        public const float QuakeClodStartMin = .50f, QuakeClodStartMax = .70f, QuakeClodReachMin = .20f, QuakeClodReachMax = .68f;

        /// <summary>
        /// Бросок кома без сопротивления: через <paramref name="flight"/> с он падает на высоту
        /// броска в <paramref name="reach"/> м по земле. Низкая быстрая дуга (0,3–0,46 с — пик ≤ 0,6 м).
        /// </summary>
        public static void ClodLaunch(float reach, float flight, out float horizontal, out float vertical)
        {
            flight = Math.Max(.05f, flight);
            horizontal = Math.Max(0f, reach) / flight;
            vertical = QuakeClodGravity * 9.81f * flight * .5f;
        }

        // ---------------------------------------------------------------- Гейзер

        /// <summary>
        /// Вершина подъёма лёгкой цели, м; доли срока: подъём до 0,28, падение с 0,78. Ревью 03.10
        /// (кадр F): столб 1,6 + 0,35 м читался низким «ящиком» — цель выше, шапка толще, столб ~2,4 м.
        /// Ревью 03.10, круг 2: с камеры 48° вертикаль сжата до 0,67 — 2,45 м при ширине ~1,7 м всё
        /// ещё «шатёр». Цель на 2,8 м, столб ~3,4 м (≈ два роста героя на экране), ствол уже.
        /// </summary>
        public const float GeyserApex = 2.8f, GeyserRiseShare = .28f, GeyserFallShare = .78f;

        /// <summary>Доля срока столба: 0 в тик подъёма, 1 в тик падения воды Sim.</summary>
        public static float GeyserT(float shown, int liftTick, int fallTick)
            => Clamp01((shown - liftTick) / Math.Max(1, fallTick - liftTick));

        /// <summary>
        /// Высота подброшенной цели в долях вершины: резкий взлёт (ease-out), зависание
        /// с лёгким всплытием, падение с ускорением точно к тику падения воды Sim.
        /// </summary>
        public static float GeyserLift01(float t)
        {
            t = Clamp01(t);
            if (t < GeyserRiseShare)
            {
                float u = 1f - t / GeyserRiseShare;
                return 1f - u * u * u;
            }
            if (t < GeyserFallShare)
                return 1f + .05f * (float)Math.Sin(Math.PI * (t - GeyserRiseShare) / (GeyserFallShare - GeyserRiseShare));
            float f = (t - GeyserFallShare) / (1f - GeyserFallShare);
            return 1f - f * f;
        }

        /// <summary>Поверх ног подброшенной цели столб выше на столько (пенная шапка — цель сидит в ней), м.</summary>
        public const float GeyserCrown = .6f;

        /// <summary>Столб у тяжёлых, элит и босса (не взлетают): бьёт вверх на 3,3 м и стоит до падения.</summary>
        public const float GeyserHeavyColumn = 3.3f;

        /// <summary>
        /// Полуширина ствола, м, по телу цели. Круг 3 (ревью 03.10: ствол ≈0,35 ширины героя и бледный,
        /// в кадре F ≈0,9): ствол шире (Хранитель 0,34 → 0,35 м, лёгкая цель 0,25 → 0,32 м), а белая пена по краям тонкая
        /// (<see cref="GeyserTrunkFoam"/>) — видна зелёная вода во всю ширину. Раструб и крона — <see cref="GeyserProfile"/>.
        /// </summary>
        public static float GeyserHalfWidth(float bodyRadius) => .29f + .07f * Math.Max(0f, bodyRadius);

        /// <summary>
        /// Белая пена и рваный край по каждой стороне ствола, м (материал столба v4: гребень 0,035 м,
        /// рваность 0,02 м, бурление ствола <see cref="GeyserTrunkChurn"/>).
        /// </summary>
        public const float GeyserTrunkFoam = .06f;

        /// <summary>Бурление ствола (TEXCOORD1.w): круг 2 — 0,45, белило треть ширины с каждой стороны.</summary>
        public const float GeyserTrunkChurn = .12f;

        /// <summary>Ширина героя на экране игры (с руками в стойке), м — мерка ствола (кадр F: ствол ≈0,9 героя).</summary>
        public const float HeroScreenWidth = .75f;

        /// <summary>Видимая ширина ствола с тонкой пеной по краям, м.</summary>
        public static float GeyserTrunkWidth(float bodyRadius) => 2f * GeyserHalfWidth(bodyRadius) - .04f;

        /// <summary>Ширина зелёной воды ствола без белой пены краёв, м.</summary>
        public static float GeyserTrunkWater(float bodyRadius) => 2f * (GeyserHalfWidth(bodyRadius) - GeyserTrunkFoam);

        /// <summary>
        /// Ширина столба по высоте в долях ствола, <paramref name="k"/> = 0 у земли, 1 на шапке.
        /// Круг 3: раструб у земли скромнее (×1,4, нижние 14 % — дальше розетка), ровный ствол, крона —
        /// округлая шапка ×1,6 в верхней четверти: обнимает ноги цели, а не плоский веер ×2,6.
        /// </summary>
        public static float GeyserProfile(float k)
        {
            k = Clamp01(k);
            float flare = .40f * (1f - Smooth01(k / .14f));
            float taper = .06f * Smooth01((k - .15f) / .55f);
            float crown = .70f * Smooth01((k - .74f) / .26f);
            return 1f + flare - taper + crown;
        }

        /// <summary>
        /// Розетка у основания (кадр F: круглая розетка брызг вокруг ствола), внешний край, м: в
        /// первые 8 % срока выбрасывается от ствола наружу, дальше стоит. Внутри кольца падения.
        /// Круг 3: шире (кадр F — розетка в 4–5 стволов) и с круглыми лепестками вместо шипов.
        /// </summary>
        public static float GeyserSkirt(float t, float bodyRadius)
        {
            float full = .95f + .35f * Math.Max(0f, Math.Min(bodyRadius, 1.2f));
            return full * (.35f + .65f * Smooth01(Clamp01(t) / .08f));
        }

        /// <summary>Лепестков розетки и глубина выемок между ними, м.</summary>
        public const int GeyserRosettePetals = 11;
        public const float GeyserRosetteNotch = .30f;

        /// <summary>
        /// Крона пены (круг 3, кадр F: цель сидит В пене): клубы вокруг подброшенного тела от ног до
        /// пояса — высота над ногами, м (доля 0…1 → −0,15…+0,55), и удаление от оси в долях видимого
        /// радиуса тела (0…1 → 0,55…1,25), не меньше 0,25 м.
        /// </summary>
        public static float GeyserCrownHeight(float share) => -.15f + .70f * Clamp01(share);

        public static float GeyserCrownReach(float bodyRadius, float share)
            => Math.Max(.25f, VisibleBodyRadius(bodyRadius)) * (.55f + .70f * Clamp01(share));

        /// <summary>
        /// Круг 4 (ревью 03.10: пена — россыпь отдельных кремовых «яиц», у каждого свой чёрный обвод): клубы
        /// пены — слитые массы. Каждый клуб рисуется дважды: тёмный силуэт, раздутый на
        /// <see cref="GeyserFoamOutline"/> маски (слой «CrownBack», очередь раньше), и заливка без обвода с тенью
        /// у края («Crown»): все силуэты ложатся до всех заливок, обвод остаётся только по краю объединения.
        /// Клубы крупнее и плотнее, чтобы соседи перекрывались: диаметр, м.
        /// </summary>
        public const float GeyserPuffMin = .30f, GeyserPuffMax = .46f, GeyserRimPuffMin = .28f, GeyserRimPuffMax = .40f;

        /// <summary>Раздувание силуэта клуба, доля маски (порог заливки минус столько) — обвод объединения.</summary>
        public const float GeyserFoamOutline = .09f;

        /// <summary>Клубов в секунду, пока стоит вода: доля кроны у тела, остальное — кромка розетки (по стволу клубов нет).</summary>
        public const float GeyserPuffRate = 160f, GeyserCrownPuffShare = .55f;

        /// <summary>Клубы кроны у подброшенной — с этой доли срока (конец взлёта, тело почти на вершине).</summary>
        public const float GeyserCrownFrom = .6f * GeyserRiseShare;

        /// <summary>
        /// Круг 4 (ревью: тёмное пятно пола у основания ствола): розетка — сплошной диск воды, внутренний край
        /// за центром на эту долю внешнего — у ствола нет дыры.
        /// </summary>
        public const float GeyserRosetteFill = .35f;

        /// <summary>
        /// Кольцо падения (круг 3, ревью: кольцо 2 м забивало кадр, первый кадр серо-хаки — полупрозрачная
        /// толстая пена): тонкая тихая полоса морской зелени, полуширина, м.
        /// </summary>
        public const float GeyserRingHalf = .07f;

        /// <summary>
        /// Проявление кольца падения по секундам с подъёма: непрозрачность за 0,06 с (2 кадра — без
        /// полупрозрачного «хаки»), полоса дорастает до полной ширины за 0,15 с.
        /// </summary>
        public static float GeyserRingAlpha(float seconds) => Smooth01(seconds / .06f);

        public static float GeyserRingGrow(float seconds) => .35f + .65f * Smooth01(seconds / .15f);

        /// <summary>Высота столба, м, до падения воды. Лёгкая цель — сидит на шапке столба.</summary>
        public static float GeyserColumn(float t, bool lifted)
        {
            t = Clamp01(t);
            if (lifted) return GeyserApex * GeyserLift01(t) + GeyserCrown * Smooth01(t / .06f);
            float rise = Math.Min(1f, t / .15f);
            float up = 1f - (1f - rise) * (1f - rise) * (1f - rise);
            return GeyserHeavyColumn * up * (1f - .15f * Smooth01((t - .15f) / .35f));
        }

        /// <summary>
        /// Круг 5 (проверка круга 4 в игре, каст 0 — лёгкая цель с 5 м: столб стоял в позиции тела, а поднятая модель
        /// висела на ~0,4 м ближе к герою): основание столба, розетки и пены — под тазом поднятой модели каждый
        /// кадр. Сдвиг таза от корня вида дальше этого, м, — не таз (сломанный риг, кость в нуле): столб остаётся
        /// в позиции тела.
        /// </summary>
        public const float GeyserPelvisReach = 1.2f;

        /// <summary>Сдвиг таза от корня вида по земле (<paramref name="dx"/>, <paramref name="dz"/>, м) годится для столба.</summary>
        public static bool GeyserPelvisUsable(float dx, float dz)
            => dx * dx + dz * dz <= GeyserPelvisReach * GeyserPelvisReach;

        /// <summary>
        /// Круг 5 (проверка круга 4: всплеск падения на +0,87…1,00 с — россыпь отдельных кремовых «яиц» веера пака и
        /// короны рывка): падение воды — слитая пена той же парой клубов («Crown»/«CrownBack»), что крона и кромка
        /// розетки. Горка <see cref="GeyserFallMoundPuffs"/> клубов там, где осел столб (до
        /// <see cref="GeyserFallMoundReach"/> м от оси), и вал <see cref="GeyserFallWavePuffs"/> клубов внахлёст: стартует
        /// с <see cref="GeyserFallWaveStart"/> м и бежит наружу <see cref="GeyserFallWaveSpeedMin"/>…<see cref="GeyserFallWaveSpeedMax"/>
        /// м/с, гаснет торможением слоя (<see cref="GeyserPuffDrag"/>). Задетым падением — пена у ног
        /// (<see cref="GeyserFallKnockPuffs"/> клубов по кругу тела) вместо короны Вихря; подброшенная лежит в горке.
        /// </summary>
        public const int GeyserFallMoundPuffs = 12, GeyserFallWavePuffs = 36, GeyserFallKnockPuffs = 10;
        public const float GeyserFallMoundReach = .35f, GeyserFallWaveStart = .52f;
        public const float GeyserFallWaveSpeedMin = 1.4f, GeyserFallWaveSpeedMax = 2f;

        /// <summary>Торможение слоёв клубов пены (PuffLayer, limitVelocity.drag), 1/с: путь клуба — скорость / торможение.</summary>
        public const float GeyserPuffDrag = 3f;

        /// <summary>Живых клубов в слое «Crown» (и «CrownBack») — предел частиц слоя (PuffLayer).</summary>
        public const int GeyserPuffLayerMax = 200;

        /// <summary>Где вал пены падения встаёт, м от оси: старт + скорость / торможение.</summary>
        public static float GeyserFallWaveReach(float speed) => GeyserFallWaveStart + Math.Max(0f, speed) / GeyserPuffDrag;

        /// <summary>После падения воды столб оседает за 0,18 с.</summary>
        public static float GeyserCollapse(float shown, int fallTick)
            => shown < fallTick ? 1f : 1f - Smooth01(Seconds(shown - fallTick) / .18f);
    }
}
