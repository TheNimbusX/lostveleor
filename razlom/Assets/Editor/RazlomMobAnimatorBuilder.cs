using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Собирает боевой контроллер моба из клипов, лежащих рядом с его телом.
///
/// Контроллер здесь одноразовый: правда — это FBX и этот рецепт. Ровно так же
/// устроен контроллер Пелага, и по той же причине: собранный руками граф
/// невозможно ни отревьюить, ни повторить на следующем мобе.
///
/// РАСКЛАДКА ФАЙЛОВ. Клип опознаётся по суффиксу после «@» — это конвенция
/// самой Unity (`модель@клип.fbx`) и родной формат выгрузки Mixamo:
///
///     Resources/Characters/&lt;Моб&gt;/
///         &lt;Моб&gt;.fbx                тело
///         &lt;Моб&gt;@Idle.fbx           стойка
///         &lt;Моб&gt;@Run.fbx            бег
///         &lt;Моб&gt;@AttackA.fbx        удар, нечётный
///         &lt;Моб&gt;@AttackB.fbx        удар, чётный
///         &lt;Моб&gt;@Hit.fbx            реакция на попадание   (необязательно)
///         &lt;Моб&gt;@Death.fbx          смерть
///
/// Недостающие клипы — не ошибка сборки: контроллер соберётся из того, что
/// есть, а чего не хватает, будет названо в консоли. Так моба можно завозить
/// по частям и видеть его в игре с первого же клипа.
/// </summary>
public static class RazlomMobAnimatorBuilder
{
    private const string CharactersFolder = "Assets/Resources/Characters";
    private const string AutoBuildSessionKey = "Razlom.MobAnimator.AutoBuild.v2";

    /// <summary>Мобы, у которых контроллер собирается этим рецептом.</summary>
    private static readonly string[] Mobs = { "Forest_Guardian", "Forest_RootSwarm" };

    // ИМЕНА ИЗ КОНТРАКТА. CharacterAnimatorView дёргает ровно их; переименуешь
    // здесь — Unity молча проглотит вызов, и моб замрёт без единой ошибки.
    private const string MoveSpeed = "MoveSpeed";
    private const string Stunned = "Stunned";
    private const string AttackATrigger = "SwordAttack";
    private const string AttackBTrigger = "ShieldBash";
    private const string HighBlock = "HighBlock";
    private const string GuardBreak = "GuardBreak";
    private const string HitLeft = "HitLeft";
    private const string HitRight = "HitRight";
    private const string Knockback = "Knockback";
    private const string Death = "Death";

    // Состояние смерти адресуется ИЗ КОДА по имени: CharacterAnimatorView делает
    // CrossFadeInFixedTime("Base Layer.DeathBack"). Имя нельзя менять, не меняя
    // код, и молчаливая поломка тут — «моб перестал умирать в кадре».
    private const string DeathState = "DeathBack";

    // Скорости проигрывания, выставленные по живому просмотру владельцем.
    //
    // Стойка идёт БЕЗ замедления: под первый клип она была замедлена в два с
    // половиной раза, потому что Mixamo-стойка шла слишком бодро для существа
    // такой массы. Владелец завёз другой клип, и подпорка стала лишней. Ручка
    // осталась — следующему мобу может понадобиться.
    private const float IdleTimeScale = 1f;

    // Смерть идёт СВОИМ темпом. Здесь стояло 0.67 — «в полтора раза
    // медленнее», чтобы падение туши имело вес. Замедление ставили вслепую:
    // показ смерти длился 0.46 с, и до падения клип на такой скорости не
    // доходил вовсе — в кадре его просто не было. Теперь падение играется
    // целиком (22 кадра, 0.73 с), и растягивать его нечем: на 0.67 те же
    // кадры заняли бы 1.1 с, и труп лежал бы дольше, чем жил.
    private const float DeathSpeed = 1f;

    // Реакция ~1.1 с ускоряется до ~0.44 с: справочник анимаций требует от
    // попадания 0.4 секунды — достаточно, чтобы прочитать, и мало, чтобы
    // следующее тоже прочиталось.
    private const float HitSpeed = 2.5f;

    // Удар 1.8 с против замаха в 0.4 с. 1.6 не лечит рассинхрон до конца —
    // лечит его перерезка клипа, — но убирает худшее: лапу, доезжающую через
    // секунду после того, как урон уже прошёл.
    private const float AttackSpeed = 1.6f;

    [InitializeOnLoadMethod]
    private static void AutoBuild()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(AutoBuildSessionKey, false)) return;
            SessionState.SetBool(AutoBuildSessionKey, true);
            BuildAll(silentWhenEmpty: true);
        };
    }

    [MenuItem("Разлом/Собрать контроллеры мобов")]
    public static void Build() => BuildAll(silentWhenEmpty: false);

    /// <summary>
    /// Пересобирает контроллер, как только рядом с мобом появился новый клип.
    ///
    /// Без этого сборка шла один раз за сессию редактора, и клипы, добавленные
    /// после её запуска, в контроллер не попадали: в игре моб бегал с одним
    /// состоянием, а консоль об этом молчала. Ровно на это и напоролись.
    /// </summary>
    private sealed class ClipWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] movedTo, string[] movedFrom)
        {
            if (!Touches(imported) && !Touches(deleted) && !Touches(movedTo)) return;
            // delayCall: во время самого импорта менять ассеты нельзя.
            EditorApplication.delayCall += () => BuildAll(silentWhenEmpty: true);
        }

        private static bool Touches(string[] paths)
        {
            foreach (string path in paths)
            {
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.IndexOf("/Characters/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (Path.GetFileNameWithoutExtension(path).Contains('@')) return true;
            }
            return false;
        }
    }

    private static void BuildAll(bool silentWhenEmpty)
    {
        foreach (string mob in Mobs) BuildMob(mob, silentWhenEmpty);
    }

    private static void BuildMob(string mob, bool silentWhenEmpty)
    {
        string folder = CharactersFolder + "/" + mob;
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!silentWhenEmpty)
                Debug.LogWarning($"[Разлом] Папки моба нет: {folder}");
            return;
        }

        var clips = ResolveClips(folder, mob);
        clips.TryGetValue("Idle", out AnimationClip idle);
        clips.TryGetValue("Run", out AnimationClip run);
        clips.TryGetValue("AttackA", out AnimationClip attackA);
        clips.TryGetValue("AttackB", out AnimationClip attackB);
        clips.TryGetValue("Hit", out AnimationClip hit);
        clips.TryGetValue("Death", out AnimationClip death);
        clips.TryGetValue("Stun", out AnimationClip stun);

        if (idle == null && run == null)
        {
            if (!silentWhenEmpty)
                Debug.LogWarning($"[Разлом] {mob}: нет ни Idle, ни Run — собирать нечего.");
            return;
        }

        string output = folder + "/" + mob + "_Combat.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(output) != null)
            AssetDatabase.DeleteAsset(output);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(output);
        controller.AddParameter(MoveSpeed, AnimatorControllerParameterType.Float);
        controller.AddParameter(Stunned, AnimatorControllerParameterType.Bool);
        // Все восемь триггеров заводятся всегда, даже когда клипа под них нет:
        // код зовёт ResetTrigger по всему списку, а ResetTrigger по
        // несуществующему параметру сыплет предупреждениями каждый кадр.
        foreach (string trigger in new[]
                 {
                     AttackATrigger, AttackBTrigger, HighBlock, GuardBreak,
                     HitLeft, HitRight, Knockback, Death
                 })
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        // ---- передвижение ----
        AnimatorState locomotion;
        if (idle != null && run != null)
        {
            var tree = new BlendTree
            {
                name = mob + " Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = MoveSpeed,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(idle, 0f);
            tree.AddChild(run, 1f);

            // Стойка замедлена ВНУТРИ дерева, а не скоростью состояния: то же
            // состояние играет и бег, и замедлять его целиком значило бы
            // получить бег в рапиде. children — это копия массива, поэтому
            // правится и кладётся обратно, иначе изменение не пристаёт.
            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
                if (children[i].motion == idle) children[i].timeScale = IdleTimeScale;
            tree.children = children;

            locomotion = machine.AddState("Locomotion");
            locomotion.motion = tree;
        }
        else
        {
            // Одного клипа тоже достаточно, чтобы увидеть моба в игре.
            locomotion = machine.AddState("Locomotion");
            locomotion.motion = idle != null ? idle : run;
            // Один клип — замедлять можно прямо состоянием, бегу это не мешает,
            // потому что бега здесь и нет.
            if (idle != null) locomotion.speed = IdleTimeScale;
        }
        machine.defaultState = locomotion;

        // ---- удары ----
        // Клипы Mixamo под замах в 12 тиков (0.4 с) длинны: у Стража удар идёт
        // 1.8 с, то есть урон приходит задолго до того, как лапа доедет. Пока
        // клипы не перерезаны, ускоряем — контакт съезжает к тику урона.
        AnimatorState attackAState = AddOneShot(machine, locomotion, "AttackA", attackA,
            AttackATrigger, 0.085f, 0.85f, 0.16f);
        AnimatorState attackBState = AddOneShot(machine, locomotion, "AttackB", attackB,
            AttackBTrigger, 0.085f, 0.85f, 0.16f);
        // У Корнеполза темп задаёт представление под его замах в 9 тиков;
        // множитель Хранителя иначе ускорил бы клип второй раз.
        float attackSpeed = mob == "Forest_RootSwarm" ? 1f : AttackSpeed;
        if (attackAState != null) attackAState.speed = attackSpeed;
        if (attackBState != null) attackBState.speed = attackSpeed;

        // ---- реакции ----
        // Левая и правая разведены, если клипы есть: в изометрии бьют со всех
        // сторон, и одна реакция на любое направление читается как безразличие.
        // Нет разведённых — обе стороны играют общий клип, это честнее, чем
        // зеркалить наугад.
        clips.TryGetValue("HitLeft", out AnimationClip hitLeft);
        clips.TryGetValue("HitRight", out AnimationClip hitRight);
        AnimatorState leftState = AddOneShot(machine, locomotion, "HitLeft",
            hitLeft ?? hit, HitLeft, 0.075f, 0.80f, 0.14f);
        AnimatorState rightState = AddOneShot(machine, locomotion, "HitRight",
            hitRight ?? hit, HitRight, 0.075f, 0.80f, 0.14f);

        // РЕАКЦИЯ ОБЯЗАНА БЫТЬ КОРОТКОЙ. Клипы Mixamo идут около секунды, а бьют
        // моба чаще: пока играет реакция на первый удар, приходят второй и
        // третий, и ни один не виден. Справочник анимаций требует 0.4 секунды
        // ровно поэтому. Ускорение вместо перерезки — приближение, но оно
        // возвращает попаданию отзывчивость сегодня.
        if (leftState != null) leftState.speed = HitSpeed;
        if (rightState != null) rightState.speed = HitSpeed;

        AnimatorState knockbackTarget = leftState ?? rightState;
        if (knockbackTarget != null)
            AnyStateTrigger(machine, knockbackTarget, Knockback, 0.04f);

        // ---- оглушение ----
        if (stun != null)
        {
            AnimatorState stunState = machine.AddState("StunLoop");
            stunState.motion = stun;
            AnimatorStateTransition enter = machine.AddAnyStateTransition(stunState);
            enter.AddCondition(AnimatorConditionMode.If, 0f, Stunned);
            enter.hasExitTime = false;
            enter.duration = 0.05f;
            enter.canTransitionToSelf = false;

            AnimatorStateTransition exit = stunState.AddTransition(locomotion);
            exit.AddCondition(AnimatorConditionMode.IfNot, 0f, Stunned);
            exit.hasExitTime = false;
            exit.duration = 0.08f;
        }

        // ---- смерть ----
        if (death != null)
        {
            AnimatorState deathState = machine.AddState(DeathState);
            deathState.motion = death;
            deathState.speed = mob == "Forest_RootSwarm" ? 2f : DeathSpeed;
            AnimatorStateTransition enter = machine.AddAnyStateTransition(deathState);
            enter.AddCondition(AnimatorConditionMode.If, 0f, Death);
            enter.hasExitTime = false;
            enter.duration = 0.03f;
            enter.canTransitionToSelf = false;
            // Выхода нет намеренно: последняя поза остаётся на экране, а тело
            // убирает представление по своему таймеру.
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        string missing = string.Join(", ", new[]
        {
            idle == null ? "Idle" : null,
            run == null ? "Run" : null,
            attackA == null ? "AttackA" : null,
            attackB == null ? "AttackB" : null,
            death == null ? "Death" : null,
        }.Where(name => name != null));

        if (missing.Length > 0)
            Debug.LogWarning($"[Разлом] {mob}: контроллер собран, но не хватает клипов — {missing}. "
                             + $"Положи их рядом как {mob}@<Роль>.fbx и пересобери.");
        else
            Debug.Log($"[Разлом] {mob}: контроллер собран целиком — {output}");
    }

    /// <summary>
    /// Одноразовое состояние по триггеру: вошли из любого места, доиграли,
    /// вернулись в передвижение.
    /// </summary>
    private static AnimatorState AddOneShot(AnimatorStateMachine machine, AnimatorState back,
        string stateName, AnimationClip clip, string trigger,
        float enterBlend, float exitTime, float exitBlend)
    {
        if (clip == null) return null;

        AnimatorState state = machine.AddState(stateName);
        state.motion = clip;
        AnyStateTrigger(machine, state, trigger, enterBlend);

        AnimatorStateTransition exit = state.AddTransition(back);
        exit.hasExitTime = true;
        exit.exitTime = exitTime;
        exit.duration = exitBlend;
        return state;
    }

    private static void AnyStateTrigger(AnimatorStateMachine machine, AnimatorState state,
        string trigger, float blend)
    {
        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.hasExitTime = false;
        enter.duration = blend;
        enter.canTransitionToSelf = false;
    }

    // Роль опознаётся по СЛОВАМ в имени файла, а не по точному совпадению.
    // Mixamo выгружает «Forest_Guardian@Mutant Dying.fbx», и заставлять
    // переименовывать каждую выгрузку — это ровно та бюрократия, на которой
    // конвейер и спотыкается. Порядок в массиве = порядок разбора: смерть
    // разбирается раньше атак, иначе «Dying» может утащить слово «die» из
    // чужого имени.
    private static readonly (string Role, string[] Keys)[] RoleKeys =
    {
        ("Death", new[] { "death", "dying", "die", "fall" }),
        ("Stun", new[] { "stun", "daze" }),
        // Стороны разбираются РАНЬШЕ общей реакции: иначе слово «hit» из
        // «ReactionHitLeft» утащит его в общий слот, и разведение пропадёт.
        ("HitLeft", new[] { "hitleft", "reactionleft", "hurtleft" }),
        ("HitRight", new[] { "hitright", "reactionright", "hurtright" }),
        ("Hit", new[] { "hit", "impact", "hurt", "reaction", "flinch" }),
        ("Idle", new[] { "idle", "breathing" }),
        ("Run", new[] { "run", "sprint", "walk" }),
    };

    private static readonly string[] AttackKeys =
    {
        "attack", "swip", "slash", "punch", "kick", "strike", "claw", "bite",
        "smash", "combo", "stab", "chop",
    };

    // Признаки «это второй удар»: зеркальная выгрузка Mixamo или явная пометка.
    private static readonly string[] SecondAttackKeys = { "mirror", "alt", "second", "left" };

    /// <summary>
    /// Совпадает ли роль файла с заданной. Разбор тот же, что в ResolveClips:
    /// точное имя после «@» сильнее ключевых слов, а порядок RoleKeys решает
    /// спорные случаи — «ReactionHitLeft» уходит в HitLeft, а не в Hit.
    /// </summary>
    private static bool MatchesRole(string fileName, string role)
    {
        int at = fileName.IndexOf('@');
        if (at < 0) return false;

        string key = Normalize(fileName.Substring(at + 1));
        if (key.Length == 0) return false;
        if (key == Normalize(role)) return true;

        foreach ((string candidate, string[] keys) in RoleKeys)
        {
            if (!keys.Any(word => key.Contains(word, StringComparison.Ordinal))) continue;
            return candidate == role;
        }

        return false;
    }

    /// <summary>
    /// Зациклен ли клип этого файла: стойка и бег — да, остальное играется раз.
    ///
    /// Спрашивает импортёр. Loop Time живёт в .meta, а роль файла знает только
    /// этот разбор, и вторая копия таблицы разъехалась бы с первой в тот же
    /// день, когда кто-нибудь заведёт моба с клипом «@Walk».
    /// </summary>
    public static bool IsLoopingClipFile(string fileName) =>
        MatchesRole(fileName, "Idle") || MatchesRole(fileName, "Run");

    /// <summary>
    /// Клипы, которым НЕЛЬЗЯ везти тело: цикл передвижения и смерть.
    ///
    /// У бега проезд копится вечно, у смерти — разово, но оба уводят тело от
    /// точки, которую назначил тик. Остальным клипам проезд оставлен: там это
    /// вес удара и реакции, а не перемещение. Разбор — в RazlomCharacterImport.
    /// </summary>
    public static bool IsRootTravelClipFile(string fileName) =>
        MatchesRole(fileName, "Run") || MatchesRole(fileName, "Death");

    /// <summary>
    /// Раскладывает файлы «&lt;Моб&gt;@*.fbx» по ролям и пишет разбор в консоль.
    ///
    /// Внутри файла берётся первый настоящий клип: как Mixamo назвал тейк —
    /// его дело, и завязываться на это имя значит ломаться от перевыгрузки.
    /// </summary>
    private static Dictionary<string, AnimationClip> ResolveClips(string folder, string mob)
    {
        var result = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        string prefix = mob + "@";

        var files = AssetDatabase.FindAssets("t:Model", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => Path.GetFileNameWithoutExtension(path)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var free = new List<(string Path, string Key)>();
        foreach (string path in files)
        {
            string suffix = Path.GetFileNameWithoutExtension(path).Substring(prefix.Length);
            free.Add((path, Normalize(suffix)));
        }

        // Точное имя роли всегда сильнее ключевых слов: «@AttackB.fbx» должен
        // лечь в AttackB, что бы ни думал разбор по словам.
        foreach (string role in new[]
                 {
                     "Idle", "Run", "AttackA", "AttackB",
                     "HitLeft", "HitRight", "Hit", "Death", "Stun",
                 })
        {
            int exact = free.FindIndex(entry => entry.Key == Normalize(role));
            if (exact < 0) continue;
            Assign(result, role, free[exact].Path, "точное имя");
            free.RemoveAt(exact);
        }

        foreach ((string role, string[] keys) in RoleKeys)
        {
            if (result.ContainsKey(role)) continue;
            int index = free.FindIndex(entry => keys.Any(key =>
                entry.Key.Contains(key, StringComparison.Ordinal)));
            if (index < 0) continue;
            Assign(result, role, free[index].Path, "по слову");
            free.RemoveAt(index);
        }

        // Атаки — последними и парой: сначала выбираем всех кандидатов, потом
        // решаем, кто из них второй. Зеркальная выгрузка узнаётся по имени.
        var attacks = free
            .Where(entry => AttackKeys.Any(key => entry.Key.Contains(key, StringComparison.Ordinal)))
            .ToList();
        if (attacks.Count > 0)
        {
            var second = attacks.FirstOrDefault(entry =>
                SecondAttackKeys.Any(key => entry.Key.Contains(key, StringComparison.Ordinal)));
            var first = attacks.FirstOrDefault(entry => entry.Path != second.Path);
            if (first.Path == null) { first = attacks[0]; second = default; }
            if (!result.ContainsKey("AttackA")) Assign(result, "AttackA", first.Path, "удар");
            if (second.Path != null && !result.ContainsKey("AttackB"))
                Assign(result, "AttackB", second.Path, "удар, зеркальный");
            else if (second.Path == null && attacks.Count > 1 && !result.ContainsKey("AttackB"))
                Assign(result, "AttackB", attacks[1].Path, "удар, второй по алфавиту");
        }

        return result;
    }

    private static void Assign(Dictionary<string, AnimationClip> result, string role,
        string path, string why)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview",
                StringComparison.Ordinal));

        if (clip == null)
        {
            Debug.LogWarning($"[Разлом] В {Path.GetFileName(path)} нет клипа. "
                             + "Проверь галку Import Animation в инспекторе модели.");
            return;
        }

        result[role] = clip;
        Debug.Log($"[Разлом]   {role,-8} ← {Path.GetFileName(path)}  ({why})");
    }

    private static string Normalize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char symbol in value)
            if (char.IsLetterOrDigit(symbol)) builder.Append(char.ToLowerInvariant(symbol));
        return builder.ToString();
    }
}
