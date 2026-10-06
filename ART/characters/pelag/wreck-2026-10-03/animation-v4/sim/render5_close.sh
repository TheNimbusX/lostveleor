#!/usr/bin/env bash
# Только крупный план снятия/уборки (сбоку) и его видео.
V4=/c/Users/d.grab/Desktop/the-game/ART/characters/pelag/wreck-2026-10-03/animation-v4
source <(sed -n '/^BL=/,/^}/p' $V4/sim/render5_all.sh)
cd $V4/sim
eval "$(grep 'r seq5_draw.json' render5_all.sh)"; eval "$(grep 'r seq5_stow.json' render5_all.sh)"; wait
python - <<'PY'
import glob, shutil, os
d = "r5/frames_drawstow_side"; shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
for i, f in enumerate(sorted(glob.glob("r5/frames_draw_side/*.png")) + sorted(glob.glob("r5/frames_stow_side/*.png"))): shutil.copy(f, "%s/f_%03d.png" % (d, i))
PY
python ../scripts/v4_media.py video "r5/frames_drawstow_side/*.png" seq5_drawstow.json ../media/wreck4-drawstow-closeup-side "Крушение v4, якорь переделан: крупно — снятие со спины и уборка на спину (сбоку)"
