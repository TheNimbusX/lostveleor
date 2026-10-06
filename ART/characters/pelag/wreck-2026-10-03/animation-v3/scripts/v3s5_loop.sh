#!/usr/bin/env bash
# Крушение v3, формы «Якорная броня»: круг «поза 10 поверх базы → выгрузка → хват как в Unity → поправка к хвату базы» (≤ ROUNDS).
# v3s5_loop.sh <braced clip> <work dir> [corr0.json]   окружение: ROUNDS (3), SEAMDIR (откуда копии стыков)
set -uo pipefail
C="$1"; W="$2"; CORR="${3:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"
REPO=/c/Users/d.grab/Desktop/the-game
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
TOOL="${TOOL:-$REPO/artifacts/anchor-core/bake}"
RIG="$REPO/razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx"
BASE=$(python -c "import sys; sys.path.insert(0, r'$(cygpath -w $HERE)'); import v3s5_brace_keys as K; print(K.CFG['$C']['base'])")
SKIP=$(python -c "import sys; sys.path.insert(0, r'$(cygpath -w $HERE)'); import v3s5_brace_keys as K; c=K.CFG['$C']; print(','.join(str(f) for f in sorted(set(c.get('SEAM',{}))|set(c.get('IDENT',())))))")
mkdir -p "$W/author" "$W/bake"
for round in $(seq 1 "${ROUNDS:-3}"); do
  ( cd "$HERE" && V3S5_CORR="$CORR" V3S5_SEAMDIR="${SEAMDIR:-}" V3S5_QUICK="${QUICK:-}" "$BL" -b --factory-startup -P v3s5_brace.py -- "$C" "$(cygpath -m $W/author)" 2>&1 \
     | grep -E "CHK|VIOLATIONS|  V |Error|Traceback|exported" | cut -c1-230 )
  RAZLOM_REPO="$(cygpath -m $REPO)" "$BL" -b --factory-startup -P "$TOOL/anchor_grip_export.py" -- --clip "$(cygpath -m $W/author/$C.fbx)" \
     --bind "$(cygpath -m $V3/Pelag_AN_Wreck2Bind.fbx)" --rig "$(cygpath -m $RIG)" --hip-limit .5 --socket "$(cygpath -m $TOOL/grip_socket.json)" --dump-rig --out "$(cygpath -m $W/bake)" 2>&1 \
     | grep -E "GRIP_EXPORT|Error|Traceback|exceeds" | sed 's/.*frames=/frames=/' | cut -c1-200
  NEW="$W/corr_r$round.json"
  python "$HERE/v3s5_corr.py" "$W/bake/$C.grip.json" "$V3/bake/$BASE.grip.json" "$NEW" "$CORR" "$SKIP"; ok=$?
  cp "${CORR:-$NEW}" "$W/corr_used.json" 2>/dev/null || true
  [ $ok -eq 0 ] && break
  CORR="$NEW"
done
echo "LOOP done $C: corr $W/corr_used.json"
