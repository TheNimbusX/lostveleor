using System.Collections.Generic;
using System.Linq;
using Game.View;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Собирает «Бросок якоря» из трёх покупных Mixamo-клипов.
///
/// Клипы выгружены Mixamo НА ЭТОТ ЖЕ риг (Pelag_v6_MixamoRig@...), поэтому
/// bind pose источника и цели совпадают, и никакого переноса поз не нужно:
/// локальные повороты копируются один в один. Именно этим сборщик отличается
/// от RazlomPelagAuthoredClips, который вычитает чужой Blender-bind.
///
/// Раскладка по фазам жёстко следует симуляции:
///
///   0                LeapWindup      LeapArrival        LeapRecovery
///   |----- бросок ----|--- полёт ----|--- посадка ------|
///          9 тиков        15 тиков        остаток
///
/// Моменты стыковки НЕ задаются на глаз: кадр выпуска в броске и кадр контакта
/// в посадке измеряются по самим клипам (см. FindRelease/FindContact) и
/// сажаются ровно на границы фаз. Меняешь клип — пересчитается само.
/// </summary>
public static class RazlomPelagLeapClips
{
    private const string MixamoFolder = "Assets/Resources/Characters/Pelag_v5/Mixamo/";
    private const string TargetRig = "Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx";
    private const string OutputName = "Pelag_AN_AnchorLeap";

    /// <summary>
    /// Доля тяги, за которую бросок полностью уступает свободному падению.
    ///
    /// До начала тяги бросок держится целиком: якорь должен воткнуться раньше,
    /// чем герой полетит.
    /// </summary>
    /// <summary>
    /// За сколько до втыкания якоря герой начинает браться за цепь.
    ///
    /// Рывок обязан начаться ДО того, как якорь закрепился: сначала упор и
    /// натяжение, потом срыв с места. Без этого опережения рывку остаётся
    /// только время тяги, а его там слишком мало, чтобы прочитаться.
    /// </summary>
    private const float HaulLead = 0.20f;

    /// <summary>Доля тяги, за которую рывок цепи выходит на полную силу.</summary>
    private const float HaulRise = 0.02f;

    /// <summary>
    /// Доля тяги, после которой рывок начинает уступать полёту.
    ///
    /// РЫВОК — ТОЛЧОК, А НЕ ПОЗА.
    ///
    /// Сначала он держался три кадра и не читался, потом его растянули до 45%
    /// тяги — и герой полетел, держа руки натянутыми, будто продолжает тянуть
    /// уже в воздухе. Тяга должна кончиться раньше, чем начнётся полёт: усилие
    /// приложено, дальше несёт инерция.
    /// </summary>
    private const float HaulFall = 0.12f;

    private const float AirRise = 0.35f;

    /// <summary>
    /// Во сколько раз рывок проигрывается быстрее исходника.
    ///
    /// «Pull Heavy Object» — это размеренное перетягивание тяжести. Нам нужен
    /// короткий резкий дёрг, и в реальном времени он читается вяло. Ускорение
    /// правит характер движения, а не только его длину.
    /// </summary>
    private const float HaulSpeed = 1.5f;

    /// <summary>
    /// Доля тяги, после которой начинает набирать вес посадка.
    ///
    /// Между AirRise и AirFall свободное падение звучит в чистом виде — это
    /// и есть та «серединка», где оно уместно. Дальше поза уже готовится к
    /// земле, и клип посадки заходит своим предконтактным участком, где ноги
    /// тянутся к опоре.
    ///
    /// Посадка выглядит лучше свободного падения и занимает больше тяги, чем
    /// оно. С появлением рывка цепи падение окончательно стало короткой
    /// связкой между рывком и посадкой, а не самостоятельной фазой.
    ///
    /// 0.50: посадка вступает с середины тяги, поэтому её нарастание идёт
    /// длинно и внахлёст с угасающим рывком. Короткое нарастание читалось как
    /// топорный стык между полётом и приземлением.
    /// </summary>
    private const float AirFall = 0.50f;

    /// <summary>Кадров в секунду итогового клипа. Как у остальных производных.</summary>
    private const float Fps = 30f;

    /// <summary>
    /// Высота дуги полёта в метрах.
    ///
    /// Тяга за цепь — не прыжок: дуга должна быть низкой. Больше 0.5 читается
    /// как прыжок и спорит с тем, что героя тянет вперёд, а не подбрасывает.
    /// </summary>
    private const float ArcMeters = 0.35f;

    /// <summary>
    /// Глубина приседа на посадке, в метрах.
    ///
    /// Ориентир взят из соседнего сборщика якорных поз: 0.16 м при длине
    /// корпуса около 0.53 м. Глубже читается как падение, мельче — как будто
    /// удара о землю не было.
    /// </summary>
    private const float LandCrouchMeters = 0.16f;

    private sealed class Source
    {
        public GameObject Instance;
        public AnimationClip Clip;
        public Dictionary<string, Transform> Bones;
        public Transform Hips;
        public float Length => Clip.length;

        public void Sample(float time)
        {
            Clip.SampleAnimation(Instance, Mathf.Clamp(time, 0f, Clip.length));
        }
    }

    private static Source Load(string name)
    {
        string path = MixamoFolder + name + ".fbx";
        // В FBX из Mixamo ДВА такта: служебный «Armature|mixamo.com» длиной
        // ровно в один кадр и настоящий «mixamo.com» на всю анимацию. Порядок
        // подассетов не гарантирован, и служебный обычно идёт первым — брать
        // первый попавшийся нельзя, иначе запекается одна поза (T-поза в игре).
        // Берём самый длинный: он и есть анимация.
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .OrderByDescending(c => c.length)
            .FirstOrDefault();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (clip == null || model == null)
            throw new System.InvalidOperationException("Нет исходного клипа прыжка: " + path);

        // Вырожденный источник молча запекается в статичную позу, и персонаж
        // летит в T-позе — ошибка обнаруживается только глазами в игре.
        // Проверка стоит здесь, чтобы это падало на сборке, а не в кадре.
        if (clip.length < 0.2f)
            throw new System.InvalidOperationException(
                $"Клип {path} длиной {clip.length:F3}с — источник обрезан импортёром. " +
                "Проверьте firstFrame/lastFrame в .meta: полный такт должен быть длиннее.");

        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        foreach (var animator in instance.GetComponentsInChildren<Animator>()) animator.enabled = false;
        var bones = instance.GetComponentsInChildren<Transform>()
            .Where(t => t.name.StartsWith("mixamorig:")).ToDictionary(t => t.name);
        return new Source
        {
            Instance = instance,
            Clip = clip,
            Bones = bones,
            Hips = bones["mixamorig:Hips"]
        };
    }

    /// <summary>
    /// Кадр выпуска снаряда: рука максимально вынесена вперёд относительно таза.
    ///
    /// Ищем по обеим рукам и берём ту, что прошла больший путь — так сборщик
    /// не зависит от того, какой рукой бросает конкретный купленный клип.
    /// </summary>
    private static float FindRelease(Source throwClip)
    {
        // ВСЁ МЕРЯЕТСЯ В РОСТАХ, НЕ В МЕТРАХ.
        //
        // Первая версия сравнивала абсолютный вынос кисти в мировых единицах и
        // получала 0.008 — масштаб инстанса FBX неизвестен, порог бессмыслен, и
        // выбирался произвольный кадр. Рост тела берётся из того же инстанса,
        // поэтому отношение к нему верно при любом масштабе.
        float height = BodyHeight(throwClip);
        string[] hands = { "mixamorig:LeftHand", "mixamorig:RightHand" };
        float bestSpeed = -1f, release = throwClip.Length * 0.5f;
        string chosen = hands[0];
        foreach (string hand in hands)
        {
            Vector3 previous = Vector3.zero;
            bool first = true;
            for (int frame = 0; frame <= Mathf.RoundToInt(throwClip.Length * 60f); frame++)
            {
                float time = frame / 60f;
                throwClip.Sample(time);
                // Относительно таза: убирает перемещение всего тела и оставляет
                // собственно работу руки.
                Vector3 point = throwClip.Bones[hand].position - throwClip.Hips.position;
                if (!first)
                {
                    // Выпуск — момент максимальной скорости кисти. Это физика
                    // броска, а не подобранная константа.
                    float speed = (point - previous).magnitude * 60f / height;
                    if (speed > bestSpeed) { bestSpeed = speed; release = time; chosen = hand; }
                }
                previous = point;
                first = false;
            }
        }
        Debug.Log($"[Pelag leap] выпуск на {release:F3}с по {chosen}, пик скорости кисти {bestSpeed:F2} роста/с");
        return release;
    }

    /// <summary>Рост инстанса в его собственных единицах: таз до головы.</summary>
    private static float BodyHeight(Source source)
    {
        source.Sample(0f);
        return Mathf.Max(0.000001f,
            Vector3.Distance(source.Hips.position, source.Bones["mixamorig:Head"].position));
    }

    /// <summary>
    /// Начало окна заданной длины, в котором движения больше всего.
    ///
    /// Запасной вариант, когда точечный детектор находит почти ноль: лучше
    /// взять заведомо насыщенный кусок, чем произвольный тихий.
    /// </summary>
    private static float FindBusiestWindow(Source source, float window)
    {
        string[] watched = { "mixamorig:RightArm", "mixamorig:LeftUpLeg", "mixamorig:Spine" };
        float best = -1f, start = 0f;
        float last = Mathf.Max(0f, source.Length - window);
        for (float from = 0f; from <= last; from += 1f / 30f)
        {
            float total = 0f;
            foreach (string boneName in watched)
            {
                if (!source.Bones.TryGetValue(boneName, out var bone)) continue;
                Quaternion previous = Quaternion.identity;
                bool first = true;
                for (float t = from; t <= from + window; t += 1f / 30f)
                {
                    source.Sample(t);
                    if (!first) total += Quaternion.Angle(previous, bone.localRotation);
                    previous = bone.localRotation;
                    first = false;
                }
            }
            if (total > best) { best = total; start = from; }
        }
        return start;
    }

    /// <summary>
    /// Кадр контакта с землёй: таз идёт вниз быстрее всего.
    ///
    /// Именно контакт, а не нижняя точка приседа — присед наступает позже и
    /// является уже гашением удара, а не его моментом.
    /// </summary>
    private static float FindContact(Source landing, float window)
    {
        float height = BodyHeight(landing);
        int frames = Mathf.RoundToInt(landing.Length * 60f);
        float fastest = 0f, contact = 0f, previous = 0f;
        for (int frame = 0; frame <= frames; frame++)
        {
            float time = frame / 60f;
            landing.Sample(time);
            float y = landing.Hips.position.y / height;
            if (frame > 0)
            {
                float speed = (previous - y) * 60f;
                if (speed > fastest) { fastest = speed; contact = time; }
            }
            previous = y;
        }

        // Порог в ростах: настоящая посадка роняет таз быстрее половины роста
        // в секунду. Меньше — значит вертикали в клипе нет (её мог срезать
        // импортёр или её изначально нет в исходнике), и точку контакта искать
        // по высоте бессмысленно. Тогда берём самый насыщенный участок.
        if (fastest < 0.5f)
        {
            contact = FindBusiestWindow(landing, window);
            Debug.Log($"[Pelag leap] вертикали в посадке нет (пик {fastest:F2} роста/с) — " +
                      $"взят самый насыщенный участок с {contact:F3}с");
            return contact;
        }

        Debug.Log($"[Pelag leap] контакт на {contact:F3}с, падение таза {fastest:F2} роста/с");
        return contact;
    }

    /// <summary>
    /// Что именно берётся из источников: найденные моменты и — главное —
    /// сколько движения есть в самих взятых окнах.
    ///
    /// Замер по собранному клипу на этот вопрос не отвечает: сшивка фаз сама
    /// по себе даёт большой угол, и статичное окно выглядит как насыщенное.
    /// Здесь окна меряются ДО сборки, по исходным клипам.
    /// </summary>
    public static string DescribeWindows()
    {
        Source throwClip = null, air = null, land = null;
        var text = new System.Text.StringBuilder();
        try
        {
            throwClip = Load("Pelag_AN_LeapThrow");
            air = Load("Pelag_AN_LeapAir");
            land = Load("Pelag_AN_LeapLand");

            float windup = PelagAbilityTiming.LeapWindup;
            float arrival = PelagAbilityTiming.LeapArrival;
            float duration = PelagAbilityTiming.LeapRecovery;

            float landSpan = duration - arrival;
            float release = FindRelease(throwClip);
            float contact = FindContact(land, landSpan);
            float throwStart = Mathf.Clamp(release - PelagAbilityTiming.LeapRelease,
                0f, Mathf.Max(0f, throwClip.Length - windup));
            float landStart = contact;

            text.AppendLine("==== окна источников ====");
            text.AppendLine($"  бросок:  длина {throwClip.Length:F2}с, выпуск на {release:F3}с, окно {throwStart:F3}..{throwStart + windup:F3}");
            text.AppendLine($"  полёт:   длина {air.Length:F2}с, окно 0.000..{PelagAbilityTiming.LeapTravel:F3}");
            text.AppendLine($"  посадка: длина {land.Length:F2}с, контакт на {contact:F3}с, окно {landStart:F3}..{landStart + landSpan:F3}");
            text.AppendLine();
            text.AppendLine("  движение ВНУТРИ взятых окон (без сшивки):");
            text.AppendLine("    бросок:  " + Motion(throwClip, throwStart, throwStart + windup));
            text.AppendLine("    полёт:   " + Motion(air, 0f, PelagAbilityTiming.LeapTravel));
            text.AppendLine("    посадка: " + Motion(land, landStart, landStart + landSpan));
            text.AppendLine();
            text.AppendLine("  движение по ВСЕЙ длине источника (для сравнения):");
            text.AppendLine("    бросок:  " + Motion(throwClip, 0f, throwClip.Length));
            text.AppendLine("    полёт:   " + Motion(air, 0f, air.Length));
            text.AppendLine("    посадка: " + Motion(land, 0f, land.Length));
            return text.ToString();
        }
        finally
        {
            if (throwClip != null) Object.DestroyImmediate(throwClip.Instance);
            if (air != null) Object.DestroyImmediate(air.Instance);
            if (land != null) Object.DestroyImmediate(land.Instance);
        }
    }

    private static string Motion(Source source, float from, float to)
    {
        string[] watched = { "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:LeftUpLeg" };
        var text = new System.Text.StringBuilder();
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (string boneName in watched)
        {
            if (!source.Bones.TryGetValue(boneName, out var bone)) continue;
            float total = 0f;
            Quaternion previous = Quaternion.identity;
            bool first = true;
            for (float t = from; t <= to; t += 1f / 30f)
            {
                source.Sample(t);
                if (!first) total += Quaternion.Angle(previous, bone.localRotation);
                previous = bone.localRotation;
                first = false;
                minY = Mathf.Min(minY, source.Hips.localPosition.y);
                maxY = Mathf.Max(maxY, source.Hips.localPosition.y);
            }
            text.Append($"{boneName.Replace("mixamorig:", "")}={total:F1}° ");
        }
        text.Append($"тазY={(maxY - minY):F4}");
        return text.ToString();
    }

    [MenuItem("Разлом/Пелаг/Пересобрать Бросок якоря из Mixamo")]
    public static void BuildMenu()
    {
        Build();
        AssetDatabase.SaveAssets();
        Debug.Log("[Pelag leap] готово. Пересоберите аниматор, если меняли состав клипов.");
    }

    public static AnimationClip Build()
    {
        Source throwClip = null, haul = null, air = null, land = null;
        GameObject target = null;
        try
        {
            throwClip = Load("Pelag_AN_LeapThrow");
            haul = Load("Pelag_AN_LeapHaul");
            air = Load("Pelag_AN_LeapAir");
            land = Load("Pelag_AN_LeapLand");

            var targetModel = AssetDatabase.LoadAssetAtPath<GameObject>(TargetRig);
            if (targetModel == null) throw new System.InvalidOperationException("Нет игрового рига: " + TargetRig);
            target = Object.Instantiate(targetModel);
            target.hideFlags = HideFlags.HideAndDontSave;
            foreach (var animator in target.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var targetBones = target.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("mixamorig:")).ToArray();
            var targetHips = targetBones.First(t => t.name == "mixamorig:Hips");
            Vector3 restHips = targetHips.localPosition;

            // Скелеты обязаны совпадать: на этом держится прямое копирование поз.
            foreach (var bone in targetBones)
                if (!throwClip.Bones.ContainsKey(bone.name) || !air.Bones.ContainsKey(bone.name) || !land.Bones.ContainsKey(bone.name))
                    throw new System.InvalidOperationException(
                        "Кость " + bone.name + " есть в игровом риге, но не во всех клипах Mixamo. " +
                        "Клипы должны быть выгружены на этот же риг.");

            float windup = PelagAbilityTiming.LeapWindup;      // 18 тиков
            float arrival = PelagAbilityTiming.LeapArrival;     // 18 + 15 тиков
            float duration = PelagAbilityTiming.LeapRecovery;   // прибытие + 10 тиков

            float release = FindRelease(throwClip);
            float contact = FindContact(land, duration - arrival);

            // Найденный в мокапе выпуск сажается ровно на PelagAbilityTiming
            // .LeapRelease — тот же момент, из которого представление запускает
            // якорь. Пока здесь стояла своя доля (0.75), рука отпускала якорь
            // на четверть секунды позже, чем он улетал.
            float throwStart = Mathf.Clamp(release - PelagAbilityTiming.LeapRelease,
                0f, Mathf.Max(0f, throwClip.Length - windup));
            // Посадка выравнивается так, чтобы контакт лёг ровно на arrival.
            float landOffset = arrival - contact;

            // РЫВОК ВЫРАВНИВАЕТСЯ ПО ПИКУ УСИЛИЯ, А НЕ ПО НАСЫЩЕННОСТИ.
            //
            // Самый насыщенный участок протяжки приходится на её середину, где
            // корпус уже откинут назад. Посаженный так рывок означал, что к
            // моменту втыкания якоря герой уже лежит в воздухе.
            //
            // Пик скорости кистей относительно таза — тот же признак, по
            // которому находится выпуск в броске, — здесь означает момент
            // самого дёрга. Сажаем его ровно на втыкание: до него виден упор
            // и захват, после — срыв с места.
            float haulPeak = FindRelease(haul);
            Debug.Log($"[Pelag leap] пик рывка на {haulPeak:F3}с из {haul.Length:F2}с, " +
                      $"посажен на втыкание якоря ({windup:F3}с), скорость {HaulSpeed:F2}x");

            var curves = new Dictionary<string, AnimationCurve[]>();
            foreach (var bone in targetBones)
                curves[bone.name] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var hipsCurve = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

            int frames = Mathf.RoundToInt(duration * Fps);

            // ШАГ ТАЗА В ЗАМАХЕ ПЕРЕНОСИТСЯ, В ОСТАЛЬНЫХ ФАЗАХ — НЕТ.
            //
            // В замахе герой стоит на месте, а клип броска делает шаг. Без
            // переноса ноги шагают, тело не едет, и стопы скользят по полу.
            // В тяге и посадке всё наоборот: там героя двигает симуляция, и
            // запечённый XZ оторвал бы тело от собственного корня.
            //
            // Единицы приводятся отношением ростов, измеренных на тех же двух
            // инстансах. Просто копировать localPosition нельзя: у клипов
            // Mixamo свои единицы, и однажды это уже уронило тело сквозь пол.
            float targetHeight = Vector3.Distance(targetHips.position,
                targetBones.First(b => b.name == "mixamorig:Head").position);
            float throwToTarget = targetHeight / BodyHeight(throwClip);
            throwClip.Sample(throwStart);
            Vector3 throwHipsBase = throwClip.Hips.position;

            // ВЫСОТА ТАЗА НЕ БЕРЁТСЯ ИЗ ИСТОЧНИКОВ ВООБЩЕ.
            //
            // localPosition таза живёт в единицах своего FBX, и у клипов Mixamo
            // они не совпадают с единицами игрового рига. Перенос давал дельту
            // 0.75 — больше метра в игре, — и тело проваливалось сквозь пол.
            // Приводить единицы отношением ростов можно, но это ещё один
            // молчаливый множитель там, где он уже один раз соврал.
            //
            // Мокап даёт ПОЗУ, то есть повороты костей. Вертикаль задаётся
            // здесь явно, в метрах: дуга тяги и присед посадки — величины
            // дизайна, а не свойство купленного клипа.
            const float unit = 1f / 1.82f;
            float arcUnits = ArcMeters * unit;
            float crouchUnits = LandCrouchMeters * unit;

            for (int frame = 0; frame <= frames; frame++)
            {
                float t = frame / Fps;

                // ЧЕТЫРЕ ФАЗЫ, И РЫВОК ЦЕПИ — ПРИЧИНА ПОЛЁТА.
                //
                // Без него последовательность была «бросил — и магическим
                // образом полетел»: между втыканием якоря и срывом с места не
                // происходило ничего. Рывок занимает начало тяги и делает
                // полёт следствием действия, а не эффектом.
                //
                // ФАЗЫ ПЕРЕТЕКАЮТ ДРУГ В ДРУГА, А СВОБОДНОЕ ПАДЕНИЕ ЖИВЁТ
                // ТОЛЬКО В СЕРЕДИНЕ ТЯГИ.
                //
                // Стык встык с короткой сшивкой давал две беды. Поза полёта
                // подмешивалась ДО того, как якорь воткнулся, — герой начинал
                // лететь раньше, чем ему было за что тянуться. А свободное
                // падение занимало всю тягу целиком, хотя выглядит оно хуже
                // и броска, и посадки.
                //
                // Теперь бросок держится до самого втыкания якоря, падение
                // набирает силу к середине тяги и сразу начинает уступать
                // посадке. Отдельного «перехода» писать не нужно: он и есть
                // перекрытие соседних вкладов.
                float travel = PelagAbilityTiming.LeapTravel;
                float haulIn = windup - travel * HaulLead;
                float haulFull = windup + travel * HaulRise;
                float haulEnd = windup + travel * HaulFall;
                float airFull = windup + travel * AirRise;
                float landRise = windup + travel * AirFall;

                float haulWeight = Smooth((t - haulIn) / (haulFull - haulIn));
                float wThrow = 1f - haulWeight;
                float wLand = Smooth((t - landRise) / (arrival - landRise));
                float wHaul = haulWeight * (1f - Smooth((t - haulEnd) / (airFull - haulEnd)));
                float wAir = Mathf.Max(0f, 1f - wThrow - wHaul - wLand);
                float sum = wThrow + wHaul + wAir + wLand;
                wThrow /= sum; wHaul /= sum; wAir /= sum; wLand /= sum;

                // Времена внутри каждого исходника.
                throwClip.Sample(Mathf.Min(throwStart + t, throwClip.Length));
                haul.Sample(Mathf.Clamp(haulPeak + (t - windup) * HaulSpeed, 0f, haul.Length));
                float airTime = t - windup;
                if (air.Length > 0.001f) airTime = Mathf.Repeat(Mathf.Max(0f, airTime), air.Length);
                air.Sample(airTime);
                land.Sample(Mathf.Clamp(t - landOffset, 0f, land.Length));

                foreach (var bone in targetBones)
                {
                    Quaternion q = Blend4(
                        throwClip.Bones[bone.name].localRotation, wThrow,
                        haul.Bones[bone.name].localRotation, wHaul,
                        air.Bones[bone.name].localRotation, wAir,
                        land.Bones[bone.name].localRotation, wLand);
                    var channels = curves[bone.name];
                    channels[0].AddKey(t, q.x); channels[1].AddKey(t, q.y);
                    channels[2].AddKey(t, q.z); channels[3].AddKey(t, q.w);
                }

                // ТАЗ: X/Z НЕ ПИШЕМ.
                //
                // Перемещение героя считает симуляция (ForcedMotion), а
                // applyRootMotion = false. Запечённый XZ не сдвинет сущность,
                // он оторвёт тело от собственного корня — ровно так мобы
                // «телепортировались» до того, как это вычистили.
                //
                // Y оставляем: присед посадки и дуга — это поза.
                float flight = Mathf.Clamp01((t - windup) / PelagAbilityTiming.LeapTravel);
                float arc = arcUnits * Mathf.Sin(flight * Mathf.PI);

                // Присед посадки: быстрый провал сразу после контакта и мягкий
                // выход. Степень 0.55 смещает низшую точку к началу — гасить
                // удар после того, как он произошёл, поздно.
                float settle = Mathf.Clamp01((t - arrival) / (duration - arrival));
                float crouch = -crouchUnits * Mathf.Sin(Mathf.Pow(settle, 0.55f) * Mathf.PI);

                float y = restHips.y + arc + crouch;

                // Шаг замаха гаснет к началу тяги: дальше героя ведёт симуляция.
                Vector3 step = Vector3.zero;
                if (t < windup)
                {
                    Vector3 raw = (throwClip.Hips.position - throwHipsBase) * throwToTarget;
                    // Полметра — предел шага. Больше означает, что источник несёт
                    // не шаг, а перемещение, и переносить его сюда нельзя.
                    step = Vector3.ClampMagnitude(new Vector3(raw.x, 0f, raw.z), 0.5f / 1.82f)
                           * (1f - Smooth((t - windup * 0.75f) / (windup * 0.25f)));
                }

                hipsCurve[0].AddKey(t, restHips.x + step.x);
                hipsCurve[1].AddKey(t, y);
                hipsCurve[2].AddKey(t, restHips.z + step.z);
            }

            string path = "Assets/Resources/Characters/Pelag_v5/" + OutputName + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            clip.name = OutputName;
            clip.frameRate = Fps;

            foreach (var bone in targetBones)
            {
                string bonePath = AnimationUtility.CalculateTransformPath(bone, target.transform);
                var channels = curves[bone.name];
                for (int c = 0; c < 4; c++)
                {
                    Linear(channels[c]);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                        bonePath, typeof(Transform), "m_LocalRotation." + "xyzw"[c]), channels[c]);
                }
            }
            clip.EnsureQuaternionContinuity();

            string hipsPath = AnimationUtility.CalculateTransformPath(targetHips, target.transform);
            for (int c = 0; c < 3; c++)
            {
                Linear(hipsCurve[c]);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                    hipsPath, typeof(Transform), "m_LocalPosition." + "xyz"[c]), hipsCurve[c]);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, System.Array.Empty<AnimationEvent>());
            EditorUtility.SetDirty(clip);

            Debug.Log($"[Pelag leap] {OutputName}: {frames + 1} кадров, {targetBones.Length} костей, " +
                      $"замах {windup:F3}с, прибытие {arrival:F3}с, всего {duration:F3}с. " +
                      $"Источники: бросок {throwClip.Length:F2}с, полёт {air.Length:F2}с, посадка {land.Length:F2}с.");
            return clip;
        }
        finally
        {
            if (throwClip != null) Object.DestroyImmediate(throwClip.Instance);
            if (haul != null) Object.DestroyImmediate(haul.Instance);
            if (air != null) Object.DestroyImmediate(air.Instance);
            if (land != null) Object.DestroyImmediate(land.Instance);
            if (target != null) Object.DestroyImmediate(target);
        }
    }

    /// <summary>
    /// Смешение четырёх поз последовательными slerp.
    ///
    /// Каждый шаг подмешивает следующую позу с её долей от уже накопленного
    /// веса. Способ устойчив к нулевым весам и не требует логарифмов, в отличие
    /// от усреднения кватернионов «по-честному».
    /// </summary>
    private static Quaternion Blend4(Quaternion a, float wa, Quaternion b, float wb,
        Quaternion c, float wc, Quaternion d, float wd)
    {
        float sum = wa + wb;
        Quaternion result = sum < 0.0001f ? a : Quaternion.Slerp(a, b, wb / sum);
        sum += wc;
        if (sum > 0.0001f) result = Quaternion.Slerp(result, c, wc / sum);
        sum += wd;
        if (sum > 0.0001f) result = Quaternion.Slerp(result, d, wd / sum);
        return result;
    }

    private static void Linear(AnimationCurve curve)
    {
        for (int k = 0; k < curve.length; k++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
        }
    }

    private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
}
