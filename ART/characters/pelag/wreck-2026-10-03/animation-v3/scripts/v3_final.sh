#!/usr/bin/env bash
# Крушение v3, Swing1: финальная сборка из файлов animation-v3/bake (воспроизводимо, без Unity, без razlom/).
#   bake/Pelag_AN_Wreck2_Swing1.plan.json    — путь хвата (оптимизирован gripopt) + кольцо запечки по кадрам
#   bake/Pelag_AN_Wreck2_Swing1.start.anchorbake.json — старт головы (кадр 0: якорь уже сорван со спины и раскручен)
#   bake/corr.json, bake/arm_solutions.rows.json — поправка переноса Build и решения рук прошлого круга
# 1) тело (v3_author: клип, привязка, .blend, пределы) → 2) хват как в Unity (anchor_grip_export) → 3) запечка
# anchorbake (тот же AnchorRigCore.StepLive, 17 проверок) → 4) показ рига при первом нажатии (gripopt --seam) →
# 5) кадры и GIF/лист (v3_render, v3_media) → 6) timing.json (v3_timing).
# Нужны: OPT (gripopt.exe) и AB (anchorbake.exe), собранные dotnet build ... --artifacts-path <вне репозитория>.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"
REPO=/c/Users/d.grab/Desktop/the-game; TOOL=$REPO/artifacts/anchor-core/bake
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
OPT="${OPT:?gripopt exe}"; AB="${AB:?anchorbake exe}"; FR="${FRAMES_DIR:?temp dir for render frames}"
B=$V3/bake; C=Pelag_AN_Wreck2_Swing1
cd "$HERE" && V3_CORR=$B/corr.json V3_SOL=$B/arm_solutions.rows.json "$BL" -b --factory-startup -P v3_author.py -- $B/$C.plan.json $V3 \
  2>&1 | grep -E "SEAM|CHK|VIOLATIONS|  V |exported"
"$BL" -b --factory-startup -P $TOOL/anchor_grip_export.py -- --clip $V3/$C.fbx --bind $V3/Pelag_AN_Wreck2Bind.fbx --hip-limit .5 \
  --socket $TOOL/grip_socket.json --dump-rig --out $B 2>&1 | grep -E "GRIP_EXPORT" | sed 's/.*frames=/frames=/'
python "$HERE/v3_corr.py" $B/$C.grip.json $B/$C.plan.json "$FR/corr_check.json" $B/corr.json || echo "residual > 3 mm: run another round"
cd $B && "$AB" --repo "C:/Users/d.grab/Desktop/the-game" --grip $C.grip.json --kind swing --contact 7 --chain 1.6 --name ${C}_w7 \
  --start "$C.start.anchorbake.json@0" --tempo --ends-into "Pelag_AN_Wreck2_Swing2@0 | live" --out . > validation.txt || true; cat validation.txt
cd $REPO && "$OPT" --body $B/$C.grip.json --seam $B/${C}_w7.anchorbake.json --seam-out "$(cygpath -m $B)/draw_seam_ingame.json" > $B/draw_seam_ingame.txt
A=$B/${C}_w7.anchorbake.json; SH=$B/draw_seam_ingame.json
cd "$HERE"
V3_HEAD=bake V3_TAG=bake V3_RES=720x480 "$BL" -b --factory-startup -P v3_render.py -- $V3 $A $SH $FR seq game | grep "render done"
V3_HEAD=game V3_TAG=ingame V3_RES=720x480 "$BL" -b --factory-startup -P v3_render.py -- $V3 $A $SH $FR seq game | grep "render done"
V3_HEAD=bake V3_TAG=bake V3_RES=880x620 V3_WIDTH=5.4 "$BL" -b --factory-startup -P v3_render.py -- $V3 $A $SH $FR seq rside | grep "render done"
V3_HEAD=bake V3_TAG=bake V3_RES=880x620 V3_WIDTH=5.4 "$BL" -b --factory-startup -P v3_render.py -- $V3 $A $SH $FR seq fl | grep "render done"
V3_HEAD=bake V3_STILL=520 "$BL" -b --factory-startup -P v3_render.py -- $V3 $A $SH $FR still rside,fl,game 0,1,4,7,10,12 | grep "render done"
PYTHONIOENCODING=utf-8 python v3_media.py "$FR" $V3/media
PYTHONIOENCODING=utf-8 python v3_timing.py $V3
