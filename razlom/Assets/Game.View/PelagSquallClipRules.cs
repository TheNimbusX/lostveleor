using System;
using Game.Sim;

namespace Game.View
{
    /// <summary>Клипы Шквала v2 (02.10). Значения — только дописывать: идут в имена состояний и параметров.</summary>
    public enum PelagSquallClip : byte
    {
        None = 0,
        Load = 1,
        Forehand = 2,
        Backhand = 3,
        FinishFore = 4,
        FinishBack = 5,
        ReturnFore = 6,
        ReturnBack = 7,
    }

    /// <summary>
    /// Чистые правила показа Шквала v2 — без Unity, проверяются тестами представления
    /// (tools/Combat.Presentation.Tests/SquallClipRulesTests.cs). Источник чисел —
    /// ART/characters/pelag/squall-forms-2026-10-02/animation/timing.json: кадр = тик,
    /// 30 к/с, корень клипа стоит (таз по XY постоянен), везёт Sim, поворачивает вид.
    ///
    /// * Load — 2 тика замаха (Simulation.SquallWindupTicks), кадр 2 = кадр 0 Forehand.
    /// * Forehand/Backhand — полёт [0..6] растянут на F = 2…6 тиков полёта
    ///   (flight_retime), контакт — кадр 6 в тик удара, опора [6..8] — 2 тика как есть;
    ///   кадр 8 одного = кадр 0 другого.
    /// * FinishFore/Back — с кадра 6 последнего удара, 9 тиков (удержание 3 + выход 6).
    /// * ReturnFore/Back — Неуловимый и «Возврат»: полёт [0..6] на тики возврата, 4 тика
    ///   посадки; ReturnFore идёт после Forehand (кадр 0 = кадр 8 Forehand).
    ///
    /// Поворот корня к следующей цели — в опоре (кадры 7–8) и в первом тике полёта,
    /// S-кривой; пока стоит левая стопа (кадры 6–8, весь замах), корень крутится вокруг
    /// её лодыжки, а не вокруг центра. Время — тик показа (ShownTick), как у рывка.
    /// </summary>
    public static class PelagSquallClipRules
    {
        public const string BindReference = "Pelag_AN_Squall2Bind";

        /// <summary>Предел таза сборки: клипы дают 0,48 длины корпуса, по умолчанию 0,4 (timing.json view_requirements).</summary>
        public const float HipLimit = .5f;

        public const float ContactFrame = 6f;
        public const float SupportEndFrame = 8f;
        public const int SupportTicks = 2;
        public const int LoadTicks = 2;
        public const int FinishTicks = 9;
        public const int ReturnRecoveryTicks = 4;

        /// <summary>Поздний финиш (следующий прыжок сорвался после опоры): Finish с этого кадра, смешиванием.</summary>
        public const float LateFinishFrame = 2f;

        public static readonly PelagSquallClip[] Clips =
        {
            PelagSquallClip.Load, PelagSquallClip.Forehand, PelagSquallClip.Backhand,
            PelagSquallClip.FinishFore, PelagSquallClip.FinishBack,
            PelagSquallClip.ReturnFore, PelagSquallClip.ReturnBack,
        };

        public static string Suffix(PelagSquallClip clip) => clip switch
        {
            PelagSquallClip.Load => "Load",
            PelagSquallClip.Forehand => "Forehand",
            PelagSquallClip.Backhand => "Backhand",
            PelagSquallClip.FinishFore => "FinishFore",
            PelagSquallClip.FinishBack => "FinishBack",
            PelagSquallClip.ReturnFore => "ReturnFore",
            PelagSquallClip.ReturnBack => "ReturnBack",
            _ => "",
        };

        /// <summary>Имя FBX и клипа (.anim) — Assets/Resources/Characters/Pelag_v5/Mixamo/{имя}.fbx.</summary>
        public static string ClipName(PelagSquallClip clip) => "Pelag_AN_Squall2_" + Suffix(clip);

        public static string StateName(PelagSquallClip clip) => "Squall2_" + Suffix(clip) + "_v5";

        public static string StatePath(PelagSquallClip clip) => "Base Layer." + StateName(clip);

        /// <summary>Своё время у каждого клипа: при смешивании уходящий не двигается чужим параметром.</summary>
        public static string PhaseParameter(PelagSquallClip clip) => "Squall2Phase" + Suffix(clip);

        /// <summary>Последний кадр клипа (кадров на один больше).</summary>
        public static int LastFrame(PelagSquallClip clip) => clip switch
        {
            PelagSquallClip.Load => 2,
            PelagSquallClip.Forehand => 8,
            PelagSquallClip.Backhand => 8,
            PelagSquallClip.FinishFore => 9,
            PelagSquallClip.FinishBack => 9,
            PelagSquallClip.ReturnFore => 10,
            PelagSquallClip.ReturnBack => 10,
            _ => 1,
        };

        public static PelagSquallClip StrikeClip(bool backhand) => backhand ? PelagSquallClip.Backhand : PelagSquallClip.Forehand;
        public static PelagSquallClip FinishClip(bool backhand) => backhand ? PelagSquallClip.FinishBack : PelagSquallClip.FinishFore;
        public static PelagSquallClip ReturnClip(bool lastBackhand) => lastBackhand ? PelagSquallClip.ReturnBack : PelagSquallClip.ReturnFore;

        /// <summary>
        /// Стык без смешивания: конец одного клипа — та же поза, что начало другого
        /// (timing.json limits.seam_max_bone_deg = 0). Остальное — смешиванием.
        /// </summary>
        public static bool IsSeam(PelagSquallClip from, PelagSquallClip to)
        {
            switch (from)
            {
                case PelagSquallClip.Load: return to == PelagSquallClip.Forehand;
                case PelagSquallClip.Forehand:
                    return to == PelagSquallClip.Backhand || to == PelagSquallClip.FinishFore || to == PelagSquallClip.ReturnFore;
                case PelagSquallClip.Backhand:
                    return to == PelagSquallClip.Forehand || to == PelagSquallClip.FinishBack || to == PelagSquallClip.ReturnBack;
                default: return false;
            }
        }

        // ---- время ----

        /// <summary>
        /// Тик показа: тело героя нарисовано между двумя последними шагами Sim
        /// (TickDriver.GetRenderPosition), то есть на тик позже события — sim.Tick − 2 + Alpha,
        /// как у рывка (CharacterAnimatorView.Dash). По тику события (− 1) нога вставала бы
        /// за тик до остановки тела и ехала по земле до 0,66 м.
        /// </summary>
        public static float ShownTick(int simTick, float alpha) => simTick - 2 + alpha;

        /// <summary>Тик события из контекста кадра (FrameEventContext.SimulationTick снят после Tick++).</summary>
        public static int EventTick(int simulationTick) => simulationTick - 1;

        /// <summary>
        /// Кадр полёта на k-м тике (0…F) полёта длиной F тиков (timing.json flight_retime):
        /// тик 1 — толчок (кадр 1), дальше поровну до контакта (кадр 6); при F = 2 —
        /// кадры 0, 3, 6. Между тиками — по прямой. F больше 6 (возврат) — то же правило.
        /// </summary>
        public static float FlightFrame(int flightTicks, float k)
        {
            if (flightTicks < 2) flightTicks = 2;
            if (k <= 0f) return 0f;
            if (k >= flightTicks) return ContactFrame;
            if (flightTicks == 2) return k * (ContactFrame / 2f);
            return k <= 1f ? k : 1f + (ContactFrame - 1f) * (k - 1f) / (flightTicks - 1);
        }

        /// <summary>Кадр опоры: от удара (кадр 6) два тика до кадра 8.</summary>
        public static float SupportFrame(float sinceContact) => ContactFrame + Clamp(sinceContact, 0f, SupportTicks);

        public static float FinishFrame(float sinceContact) => Clamp(sinceContact, 0f, FinishTicks);

        /// <summary>Кадр возврата: полёт по FlightFrame, потом посадка кадр на тик до 10.</summary>
        public static float ReturnFrame(int returnTicks, float k)
            => k <= returnTicks ? FlightFrame(returnTicks, k)
                : ContactFrame + Clamp(k - returnTicks, 0f, ReturnRecoveryTicks);

        public static float LoadFrame(float sinceCast) => Clamp(sinceCast, 0f, LoadTicks);

        // ---- поворот корня ----

        /// <summary>Поворот к следующей цели начинается с кадра 7 опоры (тик удара + 1).</summary>
        public const float TurnDelayTicks = 1f;

        /// <summary>Кадры 7–8 опоры и первый тик полёта.</summary>
        public const float TurnTicksBase = 2f;

        /// <summary>Замах при касте (2 тика) и первый тик полёта.</summary>
        public const float LoadTurnTicks = 3f;

        /// <summary>Потолок скорости корня ≈1200°/с: S-кривая на 1,5·Δ/T в пике.</summary>
        public const float MaxTurnDegPerTick = 40f;

        /// <summary>Разворот почти кругом — в сторону проводки удара, а не кратчайшим путём.</summary>
        public const float HalfTurnPreferenceDeg = 170f;

        public static float TurnTicks(float deltaDeg, float minTicks, float maxTicks)
        {
            float need = 1.5f * Math.Abs(deltaDeg) / MaxTurnDegPerTick;
            return Clamp(Math.Max(minTicks, need), minTicks, Math.Max(minTicks, maxTicks));
        }

        /// <summary>S-кривая 0…1 (smoothstep): скорость 0 на концах.</summary>
        public static float Smooth(float u)
        {
            u = Clamp(u, 0f, 1f);
            return u * u * (3f - 2f * u);
        }

        public static float WrapDeg(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            else if (deg <= -180f) deg += 360f;
            return deg;
        }

        /// <summary>
        /// Знаковый поворот from → to, градусы. preferSign: +1 — влево (против часовой,
        /// после прямого удара: проводка уводит корпус влево), −1 — вправо, 0 — кратчайший.
        /// </summary>
        public static float SignedTurn(float fromDeg, float toDeg, int preferSign)
        {
            float d = WrapDeg(toDeg - fromDeg);
            if (preferSign != 0 && Math.Abs(d) > HalfTurnPreferenceDeg && Math.Sign(d) != preferSign)
                d -= 360f * Math.Sign(d);
            return d;
        }

        /// <summary>Сторона проводки удара: прямой (справа налево) уводит влево (+1), обратный — вправо (−1).</summary>
        public static int FollowThroughSign(bool backhand) => backhand ? -1 : 1;

        /// <summary>Рысканье в плоскости Sim (X, Y = мировая Z): atan2, против часовой (влево) — плюс.</summary>
        public static float YawOf(float x, float y) => (float)(Math.Atan2(y, x) * (180.0 / Math.PI));

        public static void YawVector(float yawDeg, out float x, out float y)
        {
            double r = yawDeg * (Math.PI / 180.0);
            x = (float)Math.Cos(r);
            y = (float)Math.Sin(r);
        }

        // ---- опорная стопа ----

        /// <summary>
        /// Левая лодыжка от корня, когда стопа стоит (кадры 6–8 удара, весь замах и
        /// финиш), — вокруг неё крутится корень. Метры рига timing.json, рост 1,8.
        /// Не авторская точка (AuthoredAnkle*): после переноса на Pelag_v6 лодыжка в Unity
        /// стоит ближе к корню. Проба U6 02.10 (SampleAnimation контроллера на игровом
        /// теле ×1,82): на всех кадрах опоры 0,131…0,155 м влево и 0,436…0,449 м вперёд;
        /// здесь середина облака (0,143; 0,443 м в игре). С авторской точкой опорная стопа
        /// на развороте уезжала бы на 4–6 см.
        /// </summary>
        public const float LeftAnkleLeft = .141f, LeftAnkleForward = .438f;

        /// <summary>
        /// Авторская левая лодыжка в опоре: timing.json rows[*][6..8].Left.ankle
        /// (x — влево, y — минус вперёд), 0,163 м влево и 0,476 м вперёд.
        /// </summary>
        public const float AuthoredAnkleLeft = .163f, AuthoredAnkleForward = .476f;

        /// <summary>Рост рига timing.json, м (3 % роста = 5,4 см). Игровой герой — масштаб 1,82 рига ростом ≈1.</summary>
        public const float TimingRigHeight = 1.8f;

        /// <summary>Метры timing.json → метры мира при масштабе героя heroScaleY (обычно 1,82).</summary>
        public static float TimingToWorld(float heroScaleY) => heroScaleY / TimingRigHeight;

        public static void LeftAnkle(float yawDeg, float scale, out float x, out float y)
        {
            YawVector(yawDeg, out float fx, out float fy);
            // влево = вперёд, повёрнутое на 90° против часовой
            x = (fx * LeftAnkleForward - fy * LeftAnkleLeft) * scale;
            y = (fy * LeftAnkleForward + fx * LeftAnkleLeft) * scale;
        }

        /// <summary>
        /// Сдвиг корня от позиции Sim, чтобы при повороте yaw0 → yaw левая лодыжка
        /// осталась на месте: A(yaw0) − A(yaw).
        /// </summary>
        public static void PivotShift(float yaw0Deg, float yawDeg, float scale, out float x, out float y)
        {
            LeftAnkle(yaw0Deg, scale, out float x0, out float y0);
            LeftAnkle(yawDeg, scale, out float x1, out float y1);
            x = x0 - x1;
            y = y0 - y1;
        }

        /// <summary>Сдвиг опоры гаснет в полёте S-кривой: к удару (кадр 6) тело ровно в точке Sim.</summary>
        public static float ShiftDecay(float sinceFlightStart, float flightTicks)
            => flightTicks <= 0f ? 0f : 1f - Smooth(sinceFlightStart / flightTicks);

        // ---- возврат ----

        /// <summary>Тики отрезка возврата — ровно как Simulation.SquallLegTicks: та же скорость, не меньше 2.</summary>
        public static int ReturnLegTicks(FixVec2 a, FixVec2 b)
        {
            int ticks = (FixVec2.Distance(a, b) / Simulation.SquallFlightStep + Fix64.Half).ToInt();
            return ticks < ForcedMotion.MinTicks ? ForcedMotion.MinTicks : ticks;
        }

        /// <summary>
        /// Куда повернёт первый отрезок возврата — до события SquallReturn, по правилу
        /// Simulation.PlanSquallReturn без проверки стен: изгиб 30% пути (1–1,5 м) × 8/9
        /// в сторону удара (после прямого — влево). Стены поменяют сторону — событие поправит.
        /// </summary>
        public static float PredictReturnYaw(float fromX, float fromY, float originX, float originY, bool lastBackhand)
        {
            float dx = originX - fromX, dy = originY - fromY;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-4f) return YawOf(dx, dy);
            float bend = Clamp(length * .3f, 1f, 1.5f) * (8f / 9f);
            float side = lastBackhand ? -1f : 1f;
            float vx = dx / 3f - dy / length * bend * side;
            float vy = dy / 3f + dx / length * bend * side;
            return YawOf(vx, vy);
        }

        // ---- стойка покоя на входе и выходе ----
        //
        // Load начинается, а Finish/Return кончаются в стойке серии сабли (как рывок): левая
        // лодыжка 0,44 м вперёд от корня. В стойке покоя она 0,21 м вперёд и на 8 см шире —
        // смешивание вход/выход везло её по земле на 24 см (проверка 02.10, 60 к/с: 2 кадра на
        // входе, 5 на выходе). Вход: корень держит стопу на месте весь замах, первый полёт
        // сдвиг гасит (PelagSquallTimeline.EntryWeight). Выход: полёта больше нет — стопы
        // переступают дугой по очереди, левая, потом правая (ExitSteps, PelagFootPlantView).
        //
        // Вход, раунд 2 (проба 02.10, кадры 90–92): стопа держалась точно, но таз прыгал
        // +15 см и назад −17 см, правая стопа — на 20 см. Причина не в порядке кадров: на
        // касте веса слоёв стойки сабли (Saber Stance, Saber Footwork — таз и ноги) падают в 0
        // сразу, а смешивание аниматора (.035 с) идёт от ГОЛОГО CombatIdle базового слоя,
        // которого на экране не было. Первый кадр — полпозы этого CombatIdle, второй — Load.
        // Теперь поза кадра каста снимается (PelagFootPlantView.BeginSquallEntry, как у
        // Рассечения) и переходит в позу аниматора; стопу корень держит уже по итоговой позе
        // того же кадра.
        //
        // Вход, раунд 3 (проверка 02.10, кадры 91–92): таз ещё ходил туда-обратно на ~3 см,
        // правая стопа — на ~5 см. Первый кадр после каста брал 19 % позы аниматора, а тот
        // ещё смешивал голый CombatIdle (EntryCrossFadeSeconds). Теперь снимок стойки держится
        // целиком, пока смешивание аниматора не кончится, и только потом S-кривой переходит в
        // клип — к первому полёту (EntryFlightSeconds) поза уже целиком клип.
        //
        // Вход, раунд 4 (проверка 03.10): при 30 к/с между концом смешивания и полётом нет ни
        // одного кадра — поза менялась целиком за кадр (шаг 1,0), при 40 к/с — 0,71. Теперь
        // переход знает длину кадра: начинается за кадр (не меньше EntryLeadSeconds) до конца
        // смешивания — доля CombatIdle в кадре не больше ~2,5 % — и кончается через кадр (не меньше
        // EntryTailSeconds) после старта полёта. Шаг за кадр: 30 к/с — 0,49, 40 — 0,41,
        // 60 — 0,35, 120 — 0,20, 144 — 0,17 (не больше раунда 2). Левую стопу корень держит
        // в замахе по итоговой позе кадра, как раньше.

        /// <summary>
        /// Лодыжка стоящей стопы над корнем — не выше этого, м рига timing.json: стоит 0,136
        /// (в игре 0,14–0,155), оторванная — от 0,21. Выше — стопа в воздухе, её не держим.
        /// </summary>
        public const float PlantedAnkleHeight = .17f;

        /// <summary>Потолок входного сдвига, м рига: стойки расходятся на 0,24–0,25 м.</summary>
        public const float EntryShiftMax = .4f;

        /// <summary>
        /// Смешивание аниматора на касте, с (CharacterAnimatorView.SquallEnterBlend): от голого
        /// CombatIdle базового слоя к Load. Идёт с кадра после каста, кончается ровно через это
        /// время (проба 02.10, 60 к/с: кадры 91 и 92 — смешивание, 93 — Load). Почти всё это
        /// время на экране снимок стойки (EntryPoseWeight: последний кадр смешивания — с малой
        /// долей позы аниматора).
        /// </summary>
        public const float EntryCrossFadeSeconds = .035f;

        /// <summary>
        /// Самое раннее начало первого полёта от кадра каста, с: замах — LoadTicks тиков Sim,
        /// каст виден на тике показа CastTick − 1 + Alpha, то есть полёт через 2–3 тика. Окно
        /// перехода от стойки к клипу (EntryPoseWeight) не короче EntryPoseSeconds −
        /// EntryCrossFadeSeconds + EntryLeadSeconds + EntryTailSeconds ≈ 0,062 с.
        /// </summary>
        public const float EntryPoseSeconds = LoadTicks / (float)Simulation.TicksPerSecond;

        /// <summary>Секунд до первого полёта (конец замаха, CastTick + LoadTicks) от тика показа <paramref name="shownTick"/>.</summary>
        public static float EntryFlightSeconds(int castTick, float shownTick)
            => (castTick + LoadTicks - shownTick) / Simulation.TicksPerSecond;

        /// <summary>
        /// Переход к клипу начинается не позже чем за столько до конца смешивания аниматора, с
        /// (и не позже чем за кадр): при 120–144 к/с иначе он не укладывается в шаг раунда 2.
        /// Доля CombatIdle в позе кадра при этом — не больше ~2,5 % (раунд 3: ~10 % двигали таз на 3 см).
        /// </summary>
        public const float EntryLeadSeconds = .018f;

        /// <summary>Переход кончается не раньше чем через столько после старта первого полёта, с (и не раньше чем через кадр).</summary>
        public const float EntryTailSeconds = .012f;

        /// <summary>
        /// Вес позы аниматора против снимка стойки. <paramref name="sinceCast"/> — секунд от кадра
        /// каста, <paramref name="untilFlight"/> — секунд до первого полёта (EntryFlightSeconds,
        /// по тику показа этого кадра), <paramref name="frameSeconds"/> — длина кадра. S-кривая
        /// от start = EntryCrossFadeSeconds − max(кадр, EntryLeadSeconds) до полёта плюс
        /// max(кадр, EntryTailSeconds): при 30 к/с и каст, и полёт — по кадру на переход, а не
        /// вся поза за кадр; при 60+ к/с почти весь переход — между концом смешивания и полётом.
        /// 0 до start; 1 после конца. Полёт начался раньше start — 1 сразу: стойка в полёте
        /// хуже остатка смешивания.
        /// </summary>
        public static float EntryPoseWeight(float sinceCast, float untilFlight, float frameSeconds)
        {
            float frame = frameSeconds > 0f ? frameSeconds : 0f;
            float start = Math.Max(0f, EntryCrossFadeSeconds - Math.Max(frame, EntryLeadSeconds));
            float tail = Math.Max(frame, EntryTailSeconds);
            float flight = sinceCast + untilFlight;
            if (untilFlight <= -tail || (flight <= start && sinceCast >= 0f)) return 1f;
            float blend = sinceCast - start;
            if (blend <= 0f) return 0f;
            return Smooth(blend / (flight + tail - start));
        }

        /// <summary>Подъём стопы в шаге выхода, м на теле ×1,82 — как шаги разворота на месте.</summary>
        public const float ExitStepLift = .055f;

        /// <summary>
        /// Шагов выхода два, по очереди: левая стопа — progress 0…1 (время смешивания выхода),
        /// правая — 1…2. Правую в стойке покоя доводит слой стойки сабли (KnifeIdle, 0,15 с после
        /// конца) — смешивание везло её по земле на 15–24 см; пока левая в воздухе, правая стоит.
        /// </summary>
        public const float ExitStepsSpan = 2f;

        /// <summary>Оба шага выхода (см. <see cref="ExitStepsSpan"/>): та же дуга ExitStep, правая — со сдвигом на 1.</summary>
        public static void ExitSteps(float progress, out float leftTravel, out float leftLift,
            out float rightTravel, out float rightLift)
        {
            ExitStep(progress, out leftTravel, out leftLift);
            ExitStep(progress - 1f, out rightTravel, out rightLift);
        }

        /// <summary>
        /// Шаг выхода: travel — доля пути от точки, где стояла стопа, к позе смешивания
        /// (S-кривая, у земли скорость 0), lift — доля подъёма (дуга, 0 на концах).
        /// progress 0…1 — время смешивания выхода.
        /// </summary>
        public static void ExitStep(float progress, out float travel, out float lift)
        {
            float u = Clamp(progress, 0f, 1f);
            travel = Smooth(u);
            lift = (float)Math.Sin(u * Math.PI);
        }

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
