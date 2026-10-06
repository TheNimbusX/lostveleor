#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Экономика лагеря в F8 (план «Лагерь 06–10.10», S.8): вкладка «Забег», секция «Лагерь», под уровнем героя.
    /// Засчитать босса акта по порядку — проверить ранги лагеря («босс r и уровень»), сердце и сталь без боя;
    /// добавить пепел и сталь — проверить клятвы и Эни. Всё пишет сохранение лагеря, поэтому со вторым нажатием.
    ///
    /// Своим файлом через статическую регистрацию, как формы Пелага: оболочка F8 и её список встроенных
    /// пунктов (BuiltInIds, его сверяет DeveloperMenuInventoryTests) не трогаются. «+1 сердце Чащи» — сердце
    /// без победы для проверки вплавления у Эни; «Клятвы» — доска без окна палатки (окно — в UI-проходе).
    /// </summary>
    internal static class CampEconomyDevEntries
    {
        private const string CampSection = "Лагерь";
        private const DevFlags Writes = DevFlags.Confirm | DevFlags.WritesSave;

        // Без цифр в id: реестр и проверки меню принимают только [a-z.-].
        private static readonly string[] BossIds = { "camp.boss-first", "camp.boss-second", "camp.boss-third" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            for (int i = 0; i < RunBossKeys.Count && i < BossIds.Length; i++)
            {
                int index = i;
                DevMenu.Button(BossIds[i], DevTab.Run, CampSection, "Засчитать босса " + (index + 1),
                    c => c.Session.Camp.DeveloperCreditBoss(index), Writes,
                    blocked: NoSession,
                    dynamicLabel: c => "Засчитать босса " + (index + 1) + BossNote(c, index),
                    hint: index == 0
                        ? "Победа над боссом акта по порядку: сердце, сталь и ступень ранга лагеря (ранг r — босс r и уровень). Без боя, без золота."
                        : null,
                    order: 20 + index);
            }

            DevMenu.Button("camp.ash", DevTab.Run, CampSection, "+50 пепла",
                c => c.Session.Camp.Earn(CurrencyType.Ash, 50), Writes,
                blocked: NoSession,
                dynamicLabel: c => "+50 пепла" + Wallet(c, CurrencyType.Ash),
                order: 30);

            DevMenu.Button("camp.steel", DevTab.Run, CampSection, "+1 сталь",
                c => c.Session.Camp.Earn(CurrencyType.Steel, 1), Writes,
                blocked: NoSession,
                dynamicLabel: c => "+1 сталь" + Wallet(c, CurrencyType.Steel),
                order: 31);

            DevMenu.Button("camp.heart", DevTab.Run, CampSection, "+1 сердце Чащи",
                c => c.Session.Camp.DeveloperAddHeart(0), Writes,
                blocked: NoSession,
                dynamicLabel: c => "+1 сердце Чащи" + (c.Session == null ? "" : " · сейчас " + c.Session.Camp.HeartCount(RunBossKeys.ThicketMaster)),
                hint: "Сердце Хозяина Чащи без победы: проверить вплавление у Эни. Ранги и сталь не трогает.",
                order: 32);

            // Стол сборов открывается после первого настоящего забега; «Вернуться в лагерь» из F8
            // забег не засчитывает, поэтому без этой кнопки стол не проверить (06.10).
            DevMenu.Button("camp.attempt", DevTab.Run, CampSection, "Засчитать забег",
                c => c.Session.Camp.RecordRealAttemptEnded(1, 0), Writes,
                blocked: NoSession,
                dynamicLabel: c => "Засчитать забег" + (c.Session == null ? "" : " · сейчас " + c.Session.Camp.AttemptCount),
                hint: "Как будто забег завершился: открывает стол «Перед походом». Золото и вещи не трогает.",
                order: 33);

            DevMenu.Custom(OathsId, DevTab.Run, CampSection, DrawOaths, order: 40);
        }

        // ---- клятвы ----

        private const string OathsId = "camp.oaths";

        // Номер в сетке + 1 = OathId: порядок доски из DESIGN 06.10 (герой, выживание, удача, добыча).
        private static readonly string[] OathShort =
        {
            "Шкура", "Рука", "Шаг", "Глаз", "Запас", "Кувырок",
            "Вдох", "Стойкость", "Родник", "Кровь врага",
            "Взгляд", "Чутьё", "Благоскл.",
            "Руки", "Монета", "Пепел", "Знаток рун",
        };

        private static readonly string[] OathNames =
        {
            "Крепкая шкура", "Тяжёлая рука", "Лёгкий шаг", "Острый глаз", "Глубокий запас", "Быстрый кувырок",
            "Последний вдох", "Стойкость", "Щедрый родник", "Кровь врага",
            "Второй взгляд", "Чутьё", "Благосклонность",
            "Цепкие руки", "Звонкая монета", "Пепельный след", "Знаток рун",
        };

        // Выбранная в сетке клятва — состояние меню, не игры: в сохранение не идёт.
        private static int _oath;

        /// <summary>
        /// «Клятвы»: сетка 17 клятв, под ней ступень выбранной и две кнопки — купить ступень за пепел и
        /// включить/выключить. Через GameSession: герой лагеря сразу получает статовые клятвы (манекены).
        /// Только в лагере: в забег клятвы уже ушли снимком, правка доски там не видна до следующего.
        /// </summary>
        private static void DrawOaths(DevContext context, DevUi ui)
        {
            var camp = context.Session != null ? context.Session.Camp : null;
            bool usable = camp != null && context.InCamp;
            ui.Text(camp == null ? "Клятвы"
                : "Клятвы · включено " + camp.ActiveOathCount + " из " + camp.OathSlots
                  + " · пепел " + camp.Money(CurrencyType.Ash) + " · цена " + camp.NextOathPrice);
            int chosen = ui.Grid(_oath, OathShort, perRow: 3, enabled: camp != null);
            if (chosen >= 0 && chosen < OathShort.Length) _oath = chosen;

            var id = (OathId)(_oath + 1);
            if (camp != null)
            {
                int rank = camp.OathRank(id);
                ui.Value(OathNames[_oath], rank == 0 ? "не куплена"
                    : "ступень " + rank + " из " + RunBoons.MaxRank(id) + (camp.OathActive(id) ? " · включена" : " · выключена"));
            }

            ui.BeginRow();
            bool canBuy = usable && camp.OathRank(id) < RunBoons.MaxRank(id);
            if (ui.Button(OathsId + ".buy", "Купить ступень" + (camp != null ? " · " + camp.NextOathPrice + " пепла" : ""),
                    Writes, context.RealRun, canBuy))
                ui.Defer(c => Apply(c, id, buy: true), OathsId);
            bool owned = usable && camp.OathRank(id) > 0;
            bool active = owned && camp.OathActive(id);
            if (ui.Button(OathsId + ".toggle", active ? "Выключить" : "Включить", Writes, context.RealRun, owned))
                ui.Defer(c => Apply(c, id, buy: false), OathsId);
            ui.EndRow();

            ui.Consequences(Writes, context);
            ui.Hint(camp == null ? "игра ещё не началась"
                : !context.InCamp ? "Только в лагере: в этот забег клятвы уже ушли снимком."
                : "Цена — 60 + 20 × уже куплено, ступени тоже. Новая клятва сама включается, если есть слот. "
                  + "Пепел — «+50 пепла» выше. Чутьё и Благосклонность пока только покупаются: ждут блока лута.");
            ui.Error(OathsId);
        }

        private static void Apply(DevContext context, OathId id, bool buy)
        {
            var session = context.Session;
            if (session == null || session.Mode != GameMode.Camp)
                throw new System.ArgumentException("Клятвы меняются только в лагере.");
            OathResult result = buy ? session.BuyOath(id) : session.SetOathActive(id, !session.Camp.OathActive(id));
            if (result != OathResult.Success) throw new System.ArgumentException(Explain(result));
        }

        private static string Explain(OathResult result)
        {
            switch (result)
            {
                case OathResult.MaxRank: return "клятва уже на последней ступени";
                case OathResult.NotEnoughAsh: return "не хватает пепла";
                case OathResult.NotOwned: return "клятва не куплена";
                case OathResult.NoFreeSlot: return "все слоты заняты — сначала выключи другую";
                default: return "нет такой клятвы";
            }
        }

        private static string NoSession(DevContext c) => c.Session == null ? "игра ещё не началась" : null;

        private static string BossNote(DevContext c, int index)
        {
            if (c.Session == null) return "";
            int key = RunBossKeys.At(index);
            return c.Session.Camp.BossDefeated(key) ? " · сердец " + c.Session.Camp.HeartCount(key) : "";
        }

        private static string Wallet(DevContext c, CurrencyType currency)
            => c.Session == null ? "" : " · сейчас " + c.Session.Camp.Money(currency);
    }
}
#endif
