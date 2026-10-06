#!/usr/bin/env bash
# Крушение v3 Swing1: один круг «путь хвата → тело → выгрузка → хват Unity → поправка → запечка».
# v3_loop.sh <work_dir> <path.json> [opt args...]   (plan берётся из path.json через gripopt --eval на теле work/../body)
set -uo pipefail
W="$1"; PATH_JSON="$2"; BODY="$3"; shift 3
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO=/c/Users/d.grab/Desktop/the-game
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
TOOL=$REPO/artifacts/anchor-core/bake
OPT="${OPT:?gripopt exe}"; AB="${AB:?anchorbake exe}"
mkdir -p "$W/author" "$W/bake"
"$OPT" --body "$BODY" --eval "$PATH_JSON" --whirl --free0 --out "$W" --name plan | grep -E "^cost"
CORR="${CORR:-}"
for round in 1 2 3; do
  ( cd "$HERE" && V3_SOL="${SOL:-}" V3_CORR="$CORR" V3_QUICK="${QUICK:-}" "$BL" -b --factory-startup -P v3_author.py -- "$W/plan.plan.json" "$W/author" 2>&1 \
     | grep -E "CHK|VIOLATIONS|  V |Error|Traceback" | cut -c1-220 )
  "$BL" -b --factory-startup -P $TOOL/anchor_grip_export.py -- --clip "$W/author/Pelag_AN_Wreck2_Swing1.fbx" --bind "$W/author/Pelag_AN_Wreck2Bind.fbx" \
     --hip-limit .5 --socket $TOOL/grip_socket.json --dump-rig --out "$W/bake" 2>&1 | grep -E "GRIP_EXPORT|Error" | cut -c1-200
  NEW="$W/corr_r$round.json"; SOL="$W/author/Pelag_AN_Wreck2_Swing1.rows.json"
  python "$HERE/v3_corr.py" "$W/bake/Pelag_AN_Wreck2_Swing1.grip.json" "$W/plan.plan.json" "$NEW" "$CORR"; ok=$?
  cp "${CORR:-$NEW}" "$W/corr_used.json" 2>/dev/null || true; CORR="$NEW"
  [ $ok -eq 0 ] && break
done
cd "$W/bake" && "$AB" --repo "C:/Users/d.grab/Desktop/the-game" --grip Pelag_AN_Wreck2_Swing1.grip.json --kind swing --contact 7 --chain 1.6 \
  --name Pelag_AN_Wreck2_Swing1_w7 --start "$W/plan.start.anchorbake.json@0" --out . | grep -E "FAIL|BAKE|ok   5|ok   6"
