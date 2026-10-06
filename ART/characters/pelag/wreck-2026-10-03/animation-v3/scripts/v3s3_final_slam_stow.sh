#!/usr/bin/env bash
# Крушение v3, Slam и Stow: финальная сборка из animation-v3/bake (воспроизводимо; без Unity, без razlom/; Swing1 и Wait1 не трогаются).
#   Slam: bake/Pelag_AN_Wreck2_Slam.plan.json (путь хвата gripopt_s3 + голова запечки), .corr.json (поправка переноса), .arm_solutions.rows.json
#         старт головы — КОНТРАКТ bake/Pelag_AN_Wreck2_Swing2.contract.json@7 (Swing2 не собран); разбор срока — bake/…Slam.feasibility.txt
#   Stow: bake/Pelag_AN_Wreck2_Stow.path.json (кисть к переду правого плеча), старт — bake/Pelag_AN_Wreck2_Wait1.windowend.json@24
# 1) тело (v3s3_author) → 2) хват как в Unity (anchor_grip_export) → 3) Slam: anchorbake --kind slam; Stow: gripopt_s3 --stow (уборка рига)
# → 4) кадры (v3s3_render) → 5) MP4/GIF 1× и 0,5× (v3s3_media) → 6) timing.json (v3s3_timing).
# Нужны: OPT3 (gripopt_s3.exe: dotnet build gripopt_s3/gripopt_s3.csproj -p:AnchorCore=<копия anchor-core> --artifacts-path <вне репо>),
#        AB (anchorbake.exe), TOOL (anchor-core/bake; artifacts/anchor-core удалён 06.10 — снимок 03.10), FRAMES_DIR.
# Подбор пути (не нужен для пересборки): gripopt_s3 --body <тело> --prev <contract>@7 --contact 16 --overhead 9 --above .5 --vmax 10
#   --fwdz .45 --thighclr .2 --handkeys <ключи кистей> --keyw 300 --headclr .26 --clrmin .01 --contactw 1500 --reachl .53 --reachr .53;
#   срок контакта: --contact 9 / 13 / 15 / 16 (+ --designed — своя стартовая точка со сшивкой рига) — таблица в Slam.feasibility.txt.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"
REPO=/c/Users/d.grab/Desktop/the-game
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
OPT3="${OPT3:?gripopt_s3 exe}"; AB="${AB:?anchorbake exe}"; TOOL="${TOOL:-$REPO/artifacts/anchor-core/bake}"; FR="${FRAMES_DIR:?temp dir}"
RIG="$REPO/razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx"
B=$V3/bake; W=$(cygpath -m "$V3"); BW=$W/bake
mkdir -p "$FR"
export_grip() { RAZLOM_REPO="$(cygpath -m $REPO)" "$BL" -b --factory-startup -P "$TOOL/anchor_grip_export.py" -- --clip "$W/$1.fbx" --bind "$W/Pelag_AN_Wreck2Bind.fbx" \
  --rig "$RIG" --hip-limit .5 --socket "$TOOL/grip_socket.json" --dump-rig --out "$BW" 2>&1 | grep -E "GRIP_EXPORT" | sed 's/.*frames=/frames=/'; }

C=Pelag_AN_Wreck2_Slam
cd "$HERE" && V3S3_SOL=$BW/$C.arm_solutions.rows.json V3S3_CORR=$BW/$C.corr.json "$BL" -b --factory-startup -P v3s3_author.py -- v3sl_keys $BW/$C.plan.json "$W" \
  2>&1 | grep -E "CHK|VIOLATIONS|  V |exported"
export_grip $C
python v3s3_corr.py $BW/$C.grip.json $BW/$C.plan.json "$FR/slam_corr_check.json" $BW/$C.corr.json || echo "residual > 3 mm"
cd $B && "$AB" --repo "$(cygpath -m $REPO)" --grip $C.grip.json --kind slam --contact 16 --overhead 9 --impact 0,2.2 --chain 1.6 --name ${C}_w16 \
  --start "Pelag_AN_Wreck2_Swing2.contract.json@7" --tempo --out . > $C.validation.txt || true; grep -E "BAKE" $C.validation.txt

C=Pelag_AN_Wreck2_Stow
cd "$HERE" && "$BL" -b --factory-startup -P v3s3_author.py -- v3st_keys $BW/$C.path.json "$W" 2>&1 | grep -E "CHK|VIOLATIONS|  V |exported"
export_grip $C
python - "$BW/$C.grip.json" "$FR/stow_exported.path.json" <<'PY'
import json, sys
g = json.load(open(sys.argv[1])); json.dump({"frames": [g["samples"][f * g["sub"]]["grip"] for f in range(g["frames"] + 1)]}, open(sys.argv[2], "w"))
PY
cd $REPO && "$OPT3" --stow --body $BW/$C.grip.json --prev $BW/Pelag_AN_Wreck2_Wait1.windowend.json@24 --eval "$(cygpath -m $FR)/stow_exported.path.json" \
  --out $BW --name $C | head -6

cd "$HERE"; FW=$(cygpath -m "$FR")
PYTHONIOENCODING=utf-8 python v3s3_media.py slam "$W" "$FW/slam_seq.json"; PYTHONIOENCODING=utf-8 python v3s3_media.py pause "$W" "$FW/pause_seq.json"
for v in game frw; do
  V3_RES=720x480 "$BL" -b --factory-startup -P v3s3_render.py -- "$W" "$FW/slam_seq.json" "$FW" $v wreck3-slam 2>&1 | grep -E "render done"
  V3_RES=720x480 "$BL" -b --factory-startup -P v3s3_render.py -- "$W" "$FW/pause_seq.json" "$FW" $v wreck3-pause 2>&1 | grep -E "SEAMCHK|render done"
done
PYTHONIOENCODING=utf-8 python v3s3_media.py make "$FW" "$W/media" wreck3-slam game,frw "$FW/slam_seq.json"
PYTHONIOENCODING=utf-8 python v3s3_media.py make "$FW" "$W/media" wreck3-pause game,frw "$FW/pause_seq.json"
PYTHONIOENCODING=utf-8 python v3s3_media.py sheet "$FW" "$W/media" wreck3-slam game,frw 0,10,18,24,32,38,48,54 \
  "к0 = контракт Swing2@7 (поза 4 + 1)|к5: кисти над правым плечом, якорь справа-сзади|к9: поза 5 — кисти над головой, якорь над головой сзади|к12: рывок вниз, якорь слева|к16: поза 6 — КОНТАКТ, выпад, 2,2 м впереди|к19: удержание, якорь отскочил и скользит вправо|к24: поза 7 — широкая стойка, цепь подтянута|к27: стойка серии сабли" \
  "Крушение v3 · Slam, контакт 16: ключевые позы"
PYTHONIOENCODING=utf-8 python v3s3_timing.py "$W"
