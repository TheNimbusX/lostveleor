#!/usr/bin/env bash
# Крушение v4, правка якоря (06.10): прогон слоя якоря → кадры Blender → видео и листы в media/ (поверх; прошлые — *_old).
set -e
V4=/c/Users/d.grab/Desktop/the-game/ART/characters/pelag/wreck-2026-10-03/animation-v4
cd $V4/sim
./run5.sh full > run5_full.log 2>&1; ./run5.sh turn 150 > run5_turn.log 2>&1
python ../scripts/v4_seq.py wreck sim5_full.json seq5_full.json; python ../scripts/v4_seq.py wreck sim5_turn.json seq5_turn.json
python - <<'PY'
import json
fr = json.load(open("seq5_full.json"))["frames"]; t0 = fr[16]["t"]
out = [dict(f, t=round(f["t"] - t0, 5)) for f in fr[16:]]; out[0].update(clip="Pelag_AN_Wreck4_Swing1", frame=0.0)
json.dump(dict(frames=out), open("seq5_fromS1.json", "w"))
json.dump(dict(frames=fr[0:23]), open("seq5_draw.json", "w")); json.dump(dict(frames=fr[80:106]), open("seq5_stow.json", "w"))
json.dump(dict(frames=fr[0:23] + fr[80:106]), open("seq5_drawstow.json", "w"))
PY
./render5_all.sh
python - <<'PY'
import glob, shutil, os
R = "r5"
def fill(d, fs):
    shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
    for i, f in enumerate(fs): shutil.copy(f, "%s/f_%03d.png" % (d, i))
for v in ("game", "side"): fill("%s/frames_fromS1_%s" % (R, v), sorted(glob.glob("%s/frames_full_%s/*.png" % (R, v)))[16:])
fill(R + "/frames_drawstow_side", sorted(glob.glob(R + "/frames_draw_side/*.png")) + sorted(glob.glob(R + "/frames_stow_side/*.png")))
PY
M=../scripts/v4_media.py; O=../media; T="Крушение v4, якорь переделан"; S="Крушение v4 (якорь переделан)"; SB="сабля 1–3 (принятая)"
python $M video "r5/frames_full_game/*.png" seq5_full.json $O/wreck4-series-game "$T: снятие → мах 1 → мах 2 → выпад → уборка (игровая камера), цепь 0,45 м" &
python $M video "r5/frames_full_side/*.png" seq5_full.json $O/wreck4-series-side "$T: снятие → мах 1 → мах 2 → выпад → уборка (сбоку), цепь 0,45 м" &
python $M pair "r5/frames_fromS1_game/*.png" seq5_fromS1.json "$S" "r5/frames_sabre_game/*.png" seq_sabre.json "$SB" $O/wreck4-vs-sabre-game &
python $M pair "r5/frames_fromS1_side/*.png" seq5_fromS1.json "$S" "r5/frames_sabre_side/*.png" seq_sabre.json "$SB" $O/wreck4-vs-sabre-side &
wait
python $M video "r5/frames_turn_game/*.png" seq5_turn.json $O/wreck4-turn-game "$T: разворот на 150° перед махом 2 (игровая камера)" &
python $M video "r5/frames_turn_side/*.png" seq5_turn.json $O/wreck4-turn-side "$T: разворот на 150° перед махом 2 (сбоку)" &
python $M video "r5/frames_turn_top/*.png" seq5_turn.json $O/wreck4-turn-top "$T: разворот на 150° перед махом 2 (сверху)" &
python $M video "r5/frames_drawstow_side/*.png" seq5_drawstow.json $O/wreck4-drawstow-closeup-side "$T: крупно — снятие со спины и уборка на спину (сбоку)" &
wait
for v in game side; do
  P="r5/frames_full_$v/full_${v}_"; [ $v = game ] && W="игровая камера" || W="сбоку"
  python $M sheet $O/wreck4-keyposes-$v.jpg "$T — ключевые моменты ($W), тики Sim 30/с" \
   ${P}007.png "снятие: хват за правым плечом (тик 3,5)" ${P}011.png "снятие: якорь над правым плечом (тик 5,5)" ${P}016.png "конец снятия = мах 1, тик 0" ${P}026.png "мах 1 — контакт (тик 5)" \
   ${P}044.png "мах 2 — контакт (тик 5)" ${P}056.png "выпад — замах через верх (тик 2)" ${P}061.png "выпад — выпуск (тик 4,5), цепь выдаётся" ${P}068.png "выпад — удар в 2,2 м (тик 8), венец в воронке" \
   ${P}077.png "выбор цепи одним рывком (тик 12,5)" ${P}089.png "уборка: якорь над правым плечом (тик 2,5)" ${P}095.png "уборка: лёг на крепление v3 (тик 5,5)" ${P}118.png "уборка: на спине (конец)"
done
python ../scripts/v5_metrics.py sim5_full.json metrics5_full.json; python ../scripts/v5_metrics.py sim5_turn.json metrics5_turn.json
echo MEDIA_DONE
