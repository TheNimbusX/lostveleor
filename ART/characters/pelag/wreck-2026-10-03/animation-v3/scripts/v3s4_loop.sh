#!/usr/bin/env bash
# Крушение v3 (Swing2 / Slam / Wait2 / Stow после решения 06.10): круг «тело по плану хвата → выгрузка → хват как в Unity → поправка цели»
# (до 3 кругов, ≤ 3 мм), потом запечка/проверка.
# v3s4_loop.sh <keys module> <clip> <work dir> <plan.json|path.json> [corr0.json]
#   окружение: TOOL (anchor-core/bake), V3S4_PATH / V3S4_LAIR (путь gripopt_s4 и узлы левой стопы — для Swing2),
#              SOL0=<rows.json> — решения рук первого круга; ROUNDS (3)
set -uo pipefail
KM="$1"; C="$2"; W="$3"; PLAN="$4"; CORR="${5:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"
REPO=/c/Users/d.grab/Desktop/the-game
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
TOOL="${TOOL:-$REPO/artifacts/anchor-core/bake}"
RIG="$REPO/razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx"
mkdir -p "$W/author" "$W/bake"
SOL="${SOL0:-}"
for round in $(seq 1 "${ROUNDS:-3}"); do
  ( cd "$HERE" && V3S4_SOL="$SOL" V3S4_CORR="$CORR" V3S4_QUICK="${QUICK:-}" "$BL" -b --factory-startup -P v3s4_author.py -- "$KM" "$PLAN" "$W/author" 2>&1 \
     | grep -E "CHK|VIOLATIONS|  V |Error|Traceback|exported" | cut -c1-230 )
  RAZLOM_REPO="$(cygpath -m $REPO)" "$BL" -b --factory-startup -P "$TOOL/anchor_grip_export.py" -- --clip "$W/author/$C.fbx" \
     --bind "$V3/Pelag_AN_Wreck2Bind.fbx" --rig "$RIG" --hip-limit .5 --socket "$TOOL/grip_socket.json" --dump-rig --out "$W/bake" 2>&1 \
     | grep -E "GRIP_EXPORT|Error|Traceback" | sed 's/.*frames=/frames=/' | cut -c1-200
  NEW="$W/corr_r$round.json"
  if [ -n "${HALFCORR:-}" ]; then python "$HERE/v3s4_corr.py" "$W/bake/$C.grip.json" "$PLAN" "$NEW" "$CORR" "${SKIP:-}" "${HALFS:-}"; ok=$?
  else python "$HERE/v3s3_corr.py" "$W/bake/$C.grip.json" "$PLAN" "$NEW" "$CORR"; ok=$?; fi
  cp "${CORR:-$NEW}" "$W/corr_used.json" 2>/dev/null || true
  SOL="$W/author/$C.rows.json"; CORR="$NEW"
  [ $ok -eq 0 ] && break
done
echo "LOOP done: corr $W/corr_used.json, rows $SOL"
