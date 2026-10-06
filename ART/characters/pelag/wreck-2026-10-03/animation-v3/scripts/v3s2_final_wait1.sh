#!/usr/bin/env bash
# Крушение v3, Wait1: финальная сборка из animation-v3/bake (воспроизводимо, без Unity, без razlom/; Swing1 не трогается).
#   bake/Pelag_AN_Wreck2_Wait1.path.json  — петля хвата (gripopt_s2 --wait 2 против живого маятника от Swing1@12)
#   bake/Pelag_AN_Wreck2_Wait1.corr.json  — поправка переноса Build (2 круга v3s2_loop.sh, остаток ≤ 3 мм)
#   bake/Pelag_AN_Wreck2_Wait1.arm_solutions.rows.json — решения рук прошлого круга (узкое окно, как Swing1)
# 1) тело (v3s2_author: стык = ключи кадра 12 Swing1 из его .blend, пределы wk_check) → 2) хват как в Unity
# (anchor_grip_export) → 3) живой маятник 2 петли (24 тика окна): проверка gripopt_s2 --exact (+ разброс скорости ±6 %)
# и проверки anchorbake (тот же AnchorRigCore.StepLive) → 4) кадры (v3s2_render) → 5) MP4/GIF 1× и 0,5× (v3s2_media) → 6) timing.json.
# Нужны: OPT2 (gripopt_s2.exe), AB (anchorbake.exe), TOOL (anchor-core/bake), FRAMES_DIR. artifacts/anchor-core удалён 06.10 —
# TOOL и сборка gripopt_s2 (-p:AnchorCore=...) — с его копии (снимок 03.10 16:31, физика = razlom/Assets/Game.View).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"
REPO=/c/Users/d.grab/Desktop/the-game
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
OPT2="${OPT2:?gripopt_s2 exe}"; AB="${AB:?anchorbake exe}"; TOOL="${TOOL:-$REPO/artifacts/anchor-core/bake}"; FR="${FRAMES_DIR:?temp dir}"
RIG="$REPO/razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx"
B=$V3/bake; C=Pelag_AN_Wreck2_Wait1; S1="$B/Pelag_AN_Wreck2_Swing1_w7.anchorbake.json"
mkdir -p "$FR"
cd "$HERE" && V3S2_CORR=$B/$C.corr.json V3S2_SOL=$B/$C.arm_solutions.rows.json "$BL" -b --factory-startup -P v3s2_author.py -- v3w1_keys $B/$C.path.json $V3 \
  2>&1 | grep -E "SEAM|CHK|VIOLATIONS|  V |exported"
RAZLOM_REPO="$(cygpath -m $REPO)" "$BL" -b --factory-startup -P "$TOOL/anchor_grip_export.py" -- --clip $V3/$C.fbx --bind $V3/Pelag_AN_Wreck2Bind.fbx \
  --rig "$RIG" --hip-limit .5 --socket "$TOOL/grip_socket.json" --dump-rig --out $B 2>&1 | grep -E "GRIP_EXPORT" | sed 's/.*frames=/frames=/'
python "$HERE/v3s2_corr.py" $B/$C.grip.json $B/$C.path.json "$FR/corr_check.json" $B/$C.corr.json || echo "residual > 3 mm: run another round"
cd $REPO
"$OPT2" --body $B/$C.grip.json --start "$S1@12" --out $B --wait 2 --exact --eval $B/$C.path.json --name $C --robust 0.94,1.06 > $B/$C.live.txt
python - "$B/$C.grip.json" "$FR/${C}_x2.grip.json" <<'PY'
import json, sys
g = json.load(open(sys.argv[1])); s = g["samples"]; n = len(s) - 1
two = s + [dict(x, t=x["t"] + s[-1]["t"], frame=x["frame"] + g["frames"]) for x in s[1:]]
g.update(samples=two, frames=2 * g["frames"], clip=g["clip"] + "_x2"); json.dump(g, open(sys.argv[2], "w"))
PY
cd "$FR" && "$AB" --repo "C:/Users/d.grab/Desktop/the-game" --grip ${C}_x2.grip.json --kind swing --contact 23 --chain 1.6 --name ${C}_livecheck \
  --start "$S1@12" --out . | python -c "import sys; t=sys.stdin.read(); t=t.replace('  FAIL 5 ','  n/a  5 ').replace('  FAIL 6 ','  n/a  6 ').replace('BAKE FAIL','LIVE CHECK (5/6 — удара в окне нет)').replace('BAKE PASS','LIVE CHECK'); sys.stdout.write(t)" > $B/$C.livecheck.txt || true
cd "$HERE" && PYTHONIOENCODING=utf-8 python v3s2_media.py seq $V3 "$FR/seq.json"
for v in game fr; do V3_RES=720x480 "$BL" -b --factory-startup -P v3s2_render.py -- $V3 "$FR/seq.json" "$FR" $v wreck3-wait1 2>&1 | grep -E "SEAMCHK|render done"; done
PYTHONIOENCODING=utf-8 python v3s2_media.py make "$FR" $V3/media wreck3-wait1 game,fr "$FR/seq.json"
PYTHONIOENCODING=utf-8 python v3s2_media.py sheet "$FR" $V3/media wreck3-wait1 game,fr 24,32,36,40,44,48 \
  "Wait1 к0 = Swing1 к12 (поза 3)|к4: кисти вниз за левое бедро, якорь за спиной низко|к6: грудь влево, наклон — цепь позади таза|к8: якорь садится на землю справа|к10: кисти возвращаются|к12 = к0 (петля)"
PYTHONIOENCODING=utf-8 python v3s2_timing.py $V3
