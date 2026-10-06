# Крушение v2: timing.json (exec внутри wk_author.py после wk_finish.py: rows, summary, viol, seams, SIM, CLIPS).
U = lambda r: [round(-r[1], 4), round(r[2], 4), round(r[0], 4)]          # оси корня (f, l, u) → Unity (x вправо, y вверх, z вперёд)


def feet_track(clip):
    out = []
    for r in rows[clip]:
        e = dict(frame=r["frame"])
        for s in SIDES:
            low = r.get("feet_low", {}).get(s, r[s]["toe_z"])
            a = r[s]["ankle"]
            kind = "air" if low > 0.012 else ("flat" if a[2] < 0.15 else "toe")
            e[s] = dict(f=a[0], l=a[1], u=a[2], contact=kind)
        out.append(e)
    return out


def planted_frames(clip):
    res = []
    for s in SIDES:
        res.append([r["frame"] for r in rows[clip] if r.get("feet_low", {}).get(s, 1.0) <= 0.012])
    return res


def grip_track(clip):
    out = []
    for r in rows[clip]:
        out.append(dict(frame=r["frame"], on_grip=bool(r["on_grip"]), fistL=U(r["fistL"]), fistR=U(r["fistR"]), ring=U(r["ring"]),
                        handle_axis=U(r["grip_axis"]), chain_dir=U(r["chain_dir"]), hand_gap=r["hand_gap"],
                        gL=r["gL"], gR=r["gR"]))
    return out


CLIPDOC = {
    P + "Swing1": dict(frames=13, ticks="замах 7 (0–2 снятие со спины, 3 — поза 1, 3–7 мах) + 5 проводки", contact_frame=7,
                       segments={"draw": [0, 2], "ready_pose1": 3, "swing": [3, 7], "contact": 7, "follow": [8, 12], "pose3": 10},
                       marks={"draw": 2, "draw_note": "левый кулак берёт рукоять у правого плеча спереди (кадр 2); крепление спины "
                              "рукоять отпускает здесь (anchor-core §4.2: правило «ближе 4 см» к креплению — заменить на кадр 2)",
                              "sheathFrame": None, "seam_next": "кадр 8 = Swing2 кадр 0", "seam_wait": "кадр 12 = Wait1 кадр 0",
                              "right_foot": "носок правой: подушечки 4–7 (пятка вверх, поворот 20°), шаг вперёд 7→11 (встаёт в 11)"},
                       retime={"7": [0, 1, 2, 3, 4, 5, 6, 7], "8": [0, 0.67, 1.33, 2, 3, 4, 5, 6, 7]},
                       retime_rule="разворот > 90° (+1 тик): растягивается снятие со спины 0→2, мах 3→7 идёт с той же скоростью (физика головы)"),
    P + "Swing2": dict(frames=12, ticks="замах 6 (0–2 проводка маха 1 = Swing1 8–10, 3 — кисти у левого плеча, 3–6 вниз-вправо) + 5", contact_frame=6,
                       segments={"follow1": [0, 2], "top_left_shoulder": 3, "swing": [3, 6], "contact": 6, "follow": [7, 11]},
                       marks={"seam_prev": "кадр 0 = Swing1 кадр 8", "seam_next": "кадр 7 = Slam кадр 0", "seam_wait": "кадр 11 = Wait2 кадр 0",
                              "feet": "правая встаёт 3 (шаг из Swing1), левая шагает назад 3→7 (в воздухе в контакте 6 — вес на передней правой)"},
                       retime={"6": [0, 1, 2, 3, 4, 5, 6], "7": [0, 1, 2, 2.5, 3, 4, 5, 6]},
                       retime_rule="+1 тик — у верха восьмёрки (кисти у левого плеча, голова медленнее всего)"),
    P + "Slam": dict(frames=21, ticks="5 вверх + 4 вниз + 3 удержание + 8 выход", contact_frame=9,
                     segments={"up": [0, 5], "overhead": 5, "down": [5, 9], "contact": 9, "hold": [10, 12], "exit": [13, 20]},
                     marks={"overhead": 5, "seam_prev": "кадр 0 = Swing2 кадр 7", "walk_from": 15, "stow_from": 20,
                            "feet": "правая шагает назад 0→4, левая вперёд 4→8 (в позе 5 в воздухе), выпад 8–12 на носке правой, правая к стойке 13→16"},
                     retime={"9": [0, 1, 2, 3, 4, 5, 6, 7, 8, 9], "10": [0, 0.833, 1.667, 2.5, 3.333, 4.167, 5, 6, 7, 8, 9]},
                     retime_rule="+1 тик — растягивается подъём 0→5 (кадр 5 = OverheadTick), удар 5→9 всегда 4 тика"),
    P + "Wait1": dict(frames=13, ticks="цикл 12 (кадр 12 = кадр 0)", loop=True, mask="руки, грудь, голова",
                      marks={"from": "Swing1 кадр 12 (поза 3)"}),
    P + "Wait2": dict(frames=13, ticks="цикл 12 (кадр 12 = кадр 0)", loop=True, mask="руки, грудь, голова",
                      marks={"from": "Swing2 кадр 11"}),
    P + "Stow": dict(frames=8, ticks="8 после WindowExpired / конца Slam", contact_frame=None,
                     segments={"swing_up": [0, 3], "on_back": 3, "release": [3, 7]},
                     marks={"stowHandoff": 3, "seam_prev": "кадр 0 = Slam кадр 20", "end": "кадр 7 = стойка серии сабли (0°)"}),
}
SHEET = {"1": [P + "Swing1", 3, "готовность: кисти у правого бедра, корпус скручен вправо (снятие со спины — кадры 0–2)"],
         "2": [P + "Swing1", 7, "мах 1 КОНТАКТ: кисти поперёк тела влево, таз и грудь влево"],
         "3": [P + "Swing1", 10, "связка: кисти у левого бедра, корпус влево, правая шагнула вперёд (Swing2 2 — кисти уже к левому плечу)"],
         "4": [P + "Swing2", 6, "мах 2 КОНТАКТ (восьмёрка): кисти с левого плеча вниз-вправо, вес на передней правой"],
         "5": [P + "Slam", 5, "через голову: кисти над головой, грудь открыта, левая шагнула вперёд"],
         "6": [P + "Slam", 9, "удар оземь КОНТАКТ: выпад, наклон ~36–40°, спина прямая"],
         "7": [P + "Slam", 18, "восстановление: кисти к правому бедру, якорь волочится, широкая стойка"]}

T = dict(skill="Крушение v2 (Pelag_AN_Wreck2_*): база (три нажатия, кистень на цепи 1,60 м) и формы листа B2 "
                "(Девятый вал — Charge/ChargeRelease, Водяной панцирь — Swing1/2_Braced, Волнорез — Slam_Drag); 03.10",
         fps=30, frame_is_tick="кадр = тик Sim при самом быстром темпе (удары 7 / 14 / 24 от первого нажатия, вал до 31)",
         root_motion="нет: таз по XY постоянен (присед по Z); корень и его поворот ведёт вид по взгляду Sim",
         rig="Pelag_v6_MixamoRig, привязка Pelag_AN_Wreck2Bind — покой v6 стоя, объект +90° по X, масштаб .01 (как Abordage2Bind/Squall2Bind)",
         build="RazlomPelagAuthoredClips.Build(\"Pelag_AN_Wreck2_*\", false, \"Pelag_AN_Wreck2Bind\", .5f)",
         base_stance="стойка серии сабли (кадр 2 Pelag_MX_SaberCombo на v6): Swing1 кадр 0 и Stow кадр 7 — она же; ноги Slam 20 — она же",
         hands="левый кулак у кисточки рукояти, правый ниже на цепи; рукоять = ось левого кулака, правый кулак на её конце и смотрит по цепи; "
               "сабля за кушаком у левого бедра весь навык (перенос сабли — по anchor-core §4.3, sheathFrame нет: клип не проводит правую мимо ножен)",
         head_not_animated="голова якоря в клипах НЕ анимирована: путь головы запекает anchor-core (artifacts/anchor-core/DESIGN.md §3) от трека хвата ниже",
         chain=["stance → Swing1 (0..12)", "Swing1@8 = Swing2@0", "Swing1@12 = Wait1@0 (цикл)", "Swing2@7 = Slam@0", "Swing2@11 = Wait2@0 (цикл)",
                "Slam@20 = Stow@0", "Stow@7 = стойка", "из Wait1/2 следующий клип — бленд 2 кадра в его кадр 0"],
         seams=seams, sheet_pose_map=SHEET, clips={}, limits={}, flail_check=SIM,
         spec_deviations=["Swing1: снятие со спины 0–2 и поза 1 в кадре 3 (спека: 0–1 и 2) — рукой за 2 тика не дотянуться до "
                          "плеча и обратно к бедру при ≤ 70°/тик; мах 3→7 (4 тика), контакт 7 как в спеке",
                          "Swing2: левая встаёт в кадре 7 (после контакта 6), Slam: левая встаёт в 8 (поза 5 — в шаге): шаги по 4 тика, "
                          "иначе нога > 35°/тик или стопа скользит у земли",
                          "сабля: клип не проводит правую мимо ножен (sheathFrame нет) — перенос по anchor-core §4.3 (скольжение пропа)"],
         physics_note=("проверочный кистень (точечная голова 12 кг, трос 1,98 м от кольца, g 9,81; wk_flail.py) по треку хвата: "
                       "со спины за 7 тиков голова не разгоняется (цепь не натягивается — 1,6 м слабины); если голова в кадре 7 "
                       "уже на дуге 24 м/с, через 7 тиков она за спиной (≈160°, 30 м/с), а не впереди — восьмёрка до удара 2 "
                       "требует ≈ 10–13 тиков (оптимизация хвата, ≤ 8 м/с); при 7 тиках — только хват ≥ 14 м/с. Вывод для запечки: "
                       "интервалы 7/7/10 тиков спеки кистень физически не держит — нужны длиннее замахи или наведение сильнее §3.3"))
exec(open(os.path.join(HERE, "wk_timing_forms.py"), encoding="utf-8").read())     # формы (лист B2)
for c in rows:
    d = dict(CLIPDOC.get(c, {}))
    d["planted_frames"] = planted_frames(c)
    d["planted_ankles"] = feet_track(c)
    d["grip_track"] = grip_track(c)
    T["clips"][c] = d
T["grip_track_note"] = ("по кадрам, оси корня Unity (x вправо, y вверх, z вперёд), метры при росте 1,80: fistL — центр обхвата левого кулака "
                        "(рукоять), fistR — правого (цепь), ring — кольцо рукояти = fistL + 0,10·handle_axis (оценка; точную точку даёт "
                        "grip_socket.json шага A), chain_dir — плановая ось цепи от правого кулака к голове")
T["limits"] = dict(summary={c[len(P):]: {k: v for k, v in s.items()} for c, s in summary.items()},
                   violations=[[v[0], v[1], str(v[2])] for v in viol],
                   rule=dict(per_tick="тело ≤ 35°, руки ≤ 70° в тики удара (Swing1/_Braced 0→10, Swing2/_Braced 0→7, Slam/Slam_Drag 0→10, ChargeRelease 0→5, Stow 0→5), Charge и остальное ≤ 35°", seams="≤ 5° крупные кости",
                             twist="грудь к тазу ≤ 45°", forearm="≤ 70° от покоя", lean="назад никогда; удар оземь ~40°",
                             head="лицо = стойка ± 5°", hip="смещение таза ≤ 0,5 корпуса (Build .5f)", hands="0,10–0,25 м на рукояти",
                             feet="не ниже −4 мм; опора не едет (кадры и ¼ кадра)"))
T["rows"] = {c[len(P):]: [{k: v for k, v in r.items() if k not in ("contacts",)} for r in rows[c]] for c in rows}
path = os.path.join(OUT, "timing.json")
open(path, "a").close()
fh = open(path, "r+", encoding="utf-8", newline=""); fh.seek(0); fh.write(json.dumps(T, indent=1, ensure_ascii=False)); fh.truncate(); fh.close()
print("timing written", path)
