#!/usr/bin/env bash
# Крушение v4, слой «цепь-хлыст» (06.10, реф — цепи Клинков Хаоса): прогон → кадры Blender → видео в media/ (префикс wreck4w-).
# ДО | ПОСЛЕ одной серии (игровая камера и крупно сбоку, 1× и 0,5×), пауза (якорь висит), крупно снятие/уборка — ПОСЛЕ.
set -e
V4=/c/Users/d.grab/Desktop/the-game/ART/characters/pelag/wreck-2026-10-03/animation-v4
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
cd $V4/sim
for v in full before pause; do ./runw.sh $v > runw_$v.log 2>&1; python ../scripts/v4_seq.py wreck simw_$v.json seqw_$v.json; done
python - <<'PY'
import json
fr = json.load(open("seqw_full.json"))["frames"]
json.dump(dict(frames=fr[0:23]), open("seqw_draw.json", "w")); json.dump(dict(frames=fr[80:106]), open("seqw_stow.json", "w"))
json.dump(dict(frames=fr[0:23] + fr[80:106]), open("seqw_drawstow.json", "w"))
p = json.load(open("seqw_pause.json"))["frames"][34:]                 # с маха 2: мах → добивка клипа → пауза 1,6 с
json.dump(dict(frames=p), open("seqw_pause34.json", "w"))
lab = [dict(f, clip=("Swing2 + пауза" if f["frame"] > 15.01 and f["clip"].endswith("Swing2") else f["clip"]),
            frame=(f["frame"] - 15 if f["frame"] > 15.01 else f["frame"])) for f in p]
json.dump(dict(frames=lab), open("seqw_pause34_lab.json", "w"))
PY
F=$V4/sim/rw; mkdir -p $F; A=$(cygpath -m $V4/clips)
r() { # seq view tag width tgt res
  rm -rf $F/frames_$3_$2
  V4_WIDTH=$4 V4_TGT=$5 V4_RES=$6 "$BL" -b --factory-startup -P $(cygpath -m $V4/scripts/v4_render.py) -- $A $(cygpath -m $V4/sim/$1) $(cygpath -m $F/frames_$3_$2) $2 $3 > $F/log_$3_$2.txt 2>&1
  echo "done $3 $2 $(grep -c Saved $F/log_$3_$2.txt)"
}
r seqw_before.json game before 5.0 0,-1.3,1.5 960x540 &
r seqw_full.json game after 5.0 0,-1.3,1.5 960x540 &
r seqw_before.json side before 4.0 0,-1.3,1.38 1040x780 &
r seqw_full.json side after 4.0 0,-1.3,1.38 1040x780 &
wait
r seqw_pause34.json game pause 4.0 0,0.3,1.0 960x540 &
r seqw_pause34.json side pause 3.2 0,0.0,1.1 1040x780 &
r seqw_draw.json side draw 3.9 0,-0.5,1.08 960x540 &
r seqw_stow.json side stow 3.9 0,-1.2,1.08 960x540 &
wait
python - <<'PY'
import glob, shutil, os
d = "rw/frames_drawstow_side"; shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
for i, f in enumerate(sorted(glob.glob("rw/frames_draw_side/*.png")) + sorted(glob.glob("rw/frames_stow_side/*.png"))): shutil.copy(f, "%s/f_%03d.png" % (d, i))
PY
M=../scripts/v4_media.py; O=../media
TB="ДО (сейчас): цепь рисуется прямой, голова без вторичного"; TA="ПОСЛЕ: цепь-хлыст (симуляция) + вторичный поворот головы"
python $M pair "rw/frames_before_game/*.png" seqw_before.json "$TB" "rw/frames_after_game/*.png" seqw_full.json "$TA" $O/wreck4w-series-game &
python $M pair "rw/frames_before_side/*.png" seqw_before.json "$TB" "rw/frames_after_side/*.png" seqw_full.json "$TA" $O/wreck4w-series-side &
T="Крушение v4, цепь-хлыст"
python $M video "rw/frames_pause_game/*.png" seqw_pause34_lab.json $O/wreck4w-pause-game "$T: мах 2 → окно нажатия прошло → якорь висит и качается (игровая камера)" &
python $M video "rw/frames_pause_side/*.png" seqw_pause34_lab.json $O/wreck4w-pause-side "$T: мах 2 → пауза, якорь висит и качается, бьётся о ноги (крупно сбоку)" &
python $M video "rw/frames_drawstow_side/*.png" seqw_drawstow.json $O/wreck4w-drawstow-closeup-side "$T: крупно — снятие со спины и уборка на спину (сбоку)" &
wait
echo MEDIA_DONE
