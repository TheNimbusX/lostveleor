using System;

namespace Game.Sim
{
    /// <summary>
    /// Когда выпадает экран «выбор формы» — ЗАГЛУШКИ плана форм 02.10, все в одном
    /// месте; меняются одной константой, когда владелец ответит на вопросы плана.
    /// Решения: AGENTS/DESIGN.md «Пелаг — новая структура набора» и «Лут леса —
    /// решения владельца, 2 октября 2026» (форма одна гарантированно к А6, если
    /// навыков ≥ 2; вторая ~25% на А7–А8; на экране — формы РАЗНЫХ взятых навыков).
    ///
    /// Когда лут построит двери, дверь «Форма» зовёт тот же RollFormOffers(), а
    /// гарантия и шанс переезжают в бросок дверей: меняется место вызова, не правила.
    /// </summary>
    public static class FormRewardRules
    {
        /// <summary>
        /// Включатель владельца: false, пока он не принял формы Вихря в игре.
        /// Выключено — обычный забег бит в бит прежний (те же сиды — те же награды).
        /// </summary>
        public const bool UseSkillForms = false;

        /// <summary>Раньше этой арены экрана формы нет («вау с первой локации, но не перебрать»).</summary>
        public const int FormEarliestArena = 3;

        /// <summary>Шанс экрана формы на награде арен FormEarliestArena…FormGuaranteeArena−1, пока экрана не было.</summary>
        public const int FormChancePercent = 20;

        /// <summary>Гарантия «к А6»: экран после зачистки арены FormGuaranteeArena−1, если навыков ≥ FormGuaranteeMinSkills.</summary>
        public const int FormGuaranteeArena = 6;
        public const int FormGuaranteeMinSkills = 2;

        /// <summary>Вторая форма ~25% на каждой из арен SecondFormFromArena…SecondFormToArena.</summary>
        public const int SecondFormChancePercent = 25;
        public const int SecondFormFromArena = 7;
        public const int SecondFormToArena = 8;

        /// <summary>«Одна форма гарантированно, вторая ~25%» — больше двух экранов за забег нет.</summary>
        public const int MaxFormScreensPerRun = 2;

        /// <summary>Сабля — кандидат на экране формы (вопрос 5 плана); 0 — не предлагается.</summary>
        public const int SabreFormWeight = 1;

        /// <summary>Вопрос 3 плана: навык выкинули и взяли снова — форму выбрать нельзя. Пока — можно.</summary>
        public static readonly bool FormLockedAfterRemoval = false;
    }

    /// <summary>
    /// Награда «выбор формы» (план форм 02.10). Своя логика — здесь; в RiftRun.cs
    /// вставки: сброс в StartRun, бросок в RollOffers, взятие в StepChoosing,
    /// хеш в Hash, сабля последней в бросках талантов.
    ///
    /// СЛУЧАЙНОСТЬ — ИЗ ЛОКАЛЬНОГО ПОТОКА (сид арены и глубина), а экран формы
    /// перезаписывает уже брошенные карточки, как родник: Loot, Affix, Layout и
    /// Spawns расходуются ровно как без форм, и остальные награды сида не
    /// зависят от того, выпала ли форма.
    /// </summary>
    public sealed partial class RiftRun
    {
        private const ulong FormChanceStream = 0x464F524D53UL;   // "FORMS"
        private const ulong FormOffersStream = 0x464F524D4FUL;   // "FORMO"

        /// <summary>Включатель этого забега (по умолчанию — константа владельца). Тесты и F8 включают у себя.</summary>
        public bool SkillFormsEnabled { get; set; } = FormRewardRules.UseSkillForms;

        /// <summary>
        /// Меню разработчика и тесты: зарезервированные, ещё не написанные формы
        /// считаются готовыми — экран можно увидеть до механики. Конфигурация, не состояние.
        /// </summary>
        public bool DeveloperFormsUnlocked { get; set; }

        /// <summary>Сколько экранов формы уже было в этом забеге (предпросмотр F8 не считается).</summary>
        public int FormScreensShown { get; private set; }

        /// <summary>Фаза, куда вернуться после предпросмотра F8; Idle — предпросмотра нет.</summary>
        private RunPhase _formPreviewResume;

        /// <summary>Открыт предпросмотр экрана формы из меню разработчика.</summary>
        public bool FormPreviewOpen => _formPreviewResume != RunPhase.Idle;

        /// <summary>Экран награды сейчас — выбор формы. Отказа и переброса на нём нет.</summary>
        public bool ChoosingForm => Phase == RunPhase.ChoosingReward && _offers[0].Kind == RewardKind.Form;

        private void ResetForms()
        {
            FormScreensShown = 0;
            _formPreviewResume = RunPhase.Idle;
        }

        // ---- когда ----

        /// <summary>
        /// Пора ли экрану формы на этой награде. Никогда: включатель выключен, босс
        /// (там артефакт), финальная арена, нет ни одной линии с формой, лимит за забег.
        /// Гарантия: после зачистки FormGuaranteeArena−1, если формы ещё не было и
        /// навыков хватает, — даже раненому. Иначе шанс по глубине; раненому (родник
        /// хочется) экран формы ждёт следующую арену.
        /// </summary>
        private bool FormScreenDue()
        {
            if (!SkillFormsEnabled || BossId >= 0 || LevelSettings.Boss || IsFinalLevel) return false;
            if (FormScreensShown >= FormRewardRules.MaxFormScreensPerRun) return false;
            if (CountFormLines(DeveloperFormsUnlocked) == 0) return false;
            if (FormScreensShown == 0 && Depth == FormRewardRules.FormGuaranteeArena - 1
                && Loadout.SkillCount >= FormRewardRules.FormGuaranteeMinSkills) return true;
            int chance = FormChanceAt(Depth);
            if (chance <= 0 || SpringWanted()) return false;
            Pcg32 rng = FormRng(FormChanceStream);
            return rng.NextInt(0, 100) < chance;
        }

        /// <summary>Шанс экрана формы на награде этой арены, без гарантии.</summary>
        private int FormChanceAt(int depth)
        {
            if (FormScreensShown == 0 && depth >= FormRewardRules.FormEarliestArena
                && depth < FormRewardRules.FormGuaranteeArena) return FormRewardRules.FormChancePercent;
            if (depth >= FormRewardRules.SecondFormFromArena && depth <= FormRewardRules.SecondFormToArena)
                return FormRewardRules.SecondFormChancePercent;
            return 0;
        }

        private Pcg32 FormRng(ulong stream)
            => new Pcg32(LayoutSeed ^ unchecked((ulong)Depth * 0x9E3779B97F4A7C15UL), stream);

        // ---- что ----

        /// <summary>
        /// Экран формы по правилам забега: перезаписывает три брошенные карточки и
        /// считается в лимит. False — ни одной подходящей линии, экран прежний.
        /// Сюда же позовёт дверь «Форма» из сессии лута.
        /// </summary>
        private bool RollFormOffers()
        {
            if (!RollFormScreen(DeveloperFormsUnlocked)) return false;
            FormScreensShown++;
            return true;
        }

        /// <summary>Состав экрана из таблицы форм игры (готовых или, в F8 и тестах, всех).</summary>
        private bool RollFormScreen(bool unreadyAllowed)
        {
            Span<int> lines = stackalloc int[PelagKit.LineCount];
            int count = CollectFormLines(lines, unreadyAllowed);
            if (count == 0) return false;
            Pcg32 rng = FormRng(FormOffersStream);
            ComposeFormScreen(ref rng, lines, count, new TableForms(!unreadyAllowed), _offers);
            return true;
        }

        /// <summary>Сколько линий могут получить форму прямо сейчас.</summary>
        private int CountFormLines(bool unreadyAllowed)
        {
            Span<int> lines = stackalloc int[PelagKit.LineCount];
            return CollectFormLines(lines, unreadyAllowed);
        }

        /// <summary>Подходящие линии в порядке пула, затем сабля: во владении, без формы, с формами.</summary>
        private int CollectFormLines(Span<int> lines, bool unreadyAllowed)
        {
            int count = 0;
            for (int i = 0; i < PelagKit.LineCount; i++)
            {
                int line = PelagKit.LineAt(i);
                if (line == PelagKit.SabreLine && FormRewardRules.SabreFormWeight <= 0) continue;
                if (IsFormLine(line, unreadyAllowed)) lines[count++] = line;
            }
            return count;
        }

        private bool IsFormLine(int line, bool unreadyAllowed)
            => Loadout.Owns(line) && Loadout.FormOf(line) == PelagForm.None && !Loadout.IsFormLocked(line)
               && PelagForms.FormCount(line, !unreadyAllowed) > 0;

        /// <summary>
        /// Состав экрана (вопросы 1–2 плана — одна функция): перемешать линии; карточка
        /// i — линия L[i % L], форма — случайная из ещё не показанных на экране; у линии
        /// не нашлось — следующая линия; нигде — пустое место (None). Три навыка с
        /// формами — три разных навыка; один навык — три его формы. Чистая функция:
        /// тесты подставляют свою таблицу (в игре пока формы только у Вихря).
        /// </summary>
        internal static void ComposeFormScreen<T>(ref Pcg32 rng, Span<int> lines, int count, in T forms,
            RewardOffer[] offers) where T : struct, IFormSource
        {
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                int swap = lines[i];
                lines[i] = lines[j];
                lines[j] = swap;
            }

            for (int card = 0; card < RewardChoices; card++)
            {
                offers[card] = RewardOffer.OfForm(RunLoadout.EmptySlot, PelagForm.None);
                for (int step = 0; step < count; step++)
                {
                    int line = lines[(card + step) % count];
                    PelagForm form = RollFreshForm(ref rng, line, in forms, offers, card);
                    if (form == PelagForm.None) continue;
                    offers[card] = RewardOffer.OfForm(line, form);
                    break;
                }
            }
        }

        /// <summary>Случайная форма линии, которой ещё нет на карточках 0…filled−1; None — все уже там.</summary>
        private static PelagForm RollFreshForm<T>(ref Pcg32 rng, int line, in T forms, RewardOffer[] offers, int filled)
            where T : struct, IFormSource
        {
            int total = forms.Count(line), fresh = 0;
            for (int i = 0; i < total; i++)
                if (!FormOnScreen(forms.At(line, i), offers, filled)) fresh++;
            if (fresh == 0) return PelagForm.None;
            int pick = rng.NextInt(0, fresh);
            for (int i = 0; i < total; i++)
            {
                PelagForm form = forms.At(line, i);
                if (!FormOnScreen(form, offers, filled) && pick-- == 0) return form;
            }
            return PelagForm.None;
        }

        private static bool FormOnScreen(PelagForm form, RewardOffer[] offers, int filled)
        {
            for (int i = 0; i < filled; i++)
                if (offers[i].Kind == RewardKind.Form && offers[i].Form == form) return true;
            return false;
        }

        /// <summary>Формы из таблицы игры: только готовые или все занятые номера.</summary>
        private readonly struct TableForms : IFormSource
        {
            private readonly bool _readyOnly;
            public TableForms(bool readyOnly) => _readyOnly = readyOnly;
            public int Count(int line) => PelagForms.FormCount(line, _readyOnly);
            public PelagForm At(int line, int i) => PelagForms.FormAt(line, i, _readyOnly);
        }

        // ---- взятие ----

        /// <summary>
        /// Карточка формы нажата. Пустое место или устаревшая карточка — нажатие
        /// ничего не делает (как недостающий артефакт). Форма встаёт в набор; в
        /// симуляцию — со следующей арены, как талант. Из предпросмотра F8 — бой
        /// продолжается сразу, уже с формой в сборке.
        /// </summary>
        private void TakeFormOffer(in RewardOffer offer)
        {
            bool unreadyAllowed = DeveloperFormsUnlocked || FormPreviewOpen;
            if (!Loadout.ChooseForm(offer.PoolIndex, offer.Form, unreadyAllowed)) return;
            if (_takenCount < MaxTakenRewards) _taken[_takenCount++] = offer;
            if (FormPreviewOpen)
            {
                Phase = _formPreviewResume;
                _formPreviewResume = RunPhase.Idle;
                ApplyLoadout();
                return;
            }
            FinishChoice();
        }

        // ---- меню разработчика ----

        /// <summary>
        /// F8: открыть экран формы прямо сейчас (из боя или по пути к выходу). Лимиты,
        /// шанс и готовность форм не действуют; включатель SkillFormsEnabled не нужен.
        /// Выбор ставит форму и возвращает в бой. False — нет линии с формами (нужен
        /// Вихрь в слоте) или фаза не та. Забег стоит помечать тестовым.
        /// </summary>
        public bool DebugOpenFormScreen()
        {
            if (Phase != RunPhase.Clearing && Phase != RunPhase.SeekingExit) return false;
            if (!RollFormScreen(unreadyAllowed: true)) return false;
            _formPreviewResume = Phase;
            Phase = RunPhase.ChoosingReward;
            return true;
        }

        /// <summary>F8: закрыть предпросмотр без выбора — бой продолжается.</summary>
        public bool DebugCloseFormScreen()
        {
            if (!FormPreviewOpen || Phase != RunPhase.ChoosingReward) return false;
            Phase = _formPreviewResume;
            _formPreviewResume = RunPhase.Idle;
            return true;
        }

        /// <summary>Хеш забега: только когда формы уже что-то значили — иначе как до форм.</summary>
        private void HashForms(ref ulong hash)
        {
            if (FormScreensShown == 0 && _formPreviewResume == RunPhase.Idle) return;
            Hashing.Mix(ref hash, 0x464F524D);   // "FORM"
            Hashing.Mix(ref hash, FormScreensShown);
            Hashing.Mix(ref hash, (int)_formPreviewResume);
        }
    }

    /// <summary>Откуда состав экрана формы берёт формы линии: таблица игры или подставная в тестах.</summary>
    internal interface IFormSource
    {
        int Count(int line);
        PelagForm At(int line, int i);
    }
}
