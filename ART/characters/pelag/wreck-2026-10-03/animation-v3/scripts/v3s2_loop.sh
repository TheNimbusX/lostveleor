#!/usr/bin/env bash
# Крушение v3, клипы после Swing1: круг «путь хвата → тело → выгрузка → хват как в Unity → поправка цели» (до 3 кругов, ≤ 3 мм).
# v3s2_loop.sh <keys module> <clip> <work dir> <path.json> [corr0.json]
#   нужны: OPT2 (gripopt_s2.exe), TOOL (каталог anchor-core/bake: anchor_grip_export.py, grip_socket.json)
set -uo pipefail
KM="$1"; C="$2"; W="$3"; PATHJ="$4"; CORR="${5:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"
REPO=/c/Users/d.grab/Desktop/the-game
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
TOOL="${TOOL:-$REPO/artifacts/anchor-core/bake}"
RIG="$REPO/razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx"
mkdir -p "$W/author" "$W/bake"
SOL=""
for round in 1 2 3; do
  ( cd "$HERE" && V3S2_SOL="$SOL" V3S2_CORR="$CORR" V3S2_QUICK="${QUICK:-}" "$BL" -b --factory-startup -P v3s2_author.py -- "$KM" "$PATHJ" "$W/author" 2>&1 \
     | grep -E "SMOOTH|SEAM|CHK|VIOLATIONS|  V |Error|Traceback|exported" | cut -c1-230 )
  RAZLOM_REPO="$(cygpath -m $REPO)" "$BL" -b --factory-startup -P "$TOOL/anchor_grip_export.py" -- --clip "$W/author/$C.fbx" \
     --bind "$V3/Pelag_AN_Wreck2Bind.fbx" --rig "$RIG" --hip-limit .5 --socket "$TOOL/grip_socket.json" --dump-rig --out "$W/bake" 2>&1 \
     | grep -E "GRIP_EXPORT|Error|Traceback" | cut -c1-200
  NEW="$W/corr_r$round.json"
  python "$HERE/v3s2_corr.py" "$W/bake/$C.grip.json" "$PATHJ" "$NEW" "$CORR"; ok=$?
  cp "${CORR:-$NEW}" "$W/corr_used.json" 2>/dev/null || true
  SOL="$W/author/$C.rows.json"; CORR="$NEW"
  [ $ok -eq 0 ] && break
done
