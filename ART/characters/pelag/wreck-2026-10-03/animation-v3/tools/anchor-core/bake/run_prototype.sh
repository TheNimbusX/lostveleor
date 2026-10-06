#!/usr/bin/env bash
# Anchor bake prototype, end to end (no Unity, nothing written under razlom/):
#   A  Blender: grip tracks in Unity root space (grip_socket.json v2, metres - the same file the rig reads):
#      Pelag_AN_WreckA (swing), Pelag_AN_WreckFinish (slam), Pelag_AN_Abordage2_Throw right hand (throw windup)
#   A' checks of step A: against the clip Unity built (.anim FK, 5 mm / 1 deg) and against the game capture of 03.10
#   B  dotnet: self-test, then bakes with the game's own AnchorRigCore.StepLive (L = 1.6 m) per kind
#   C  contact sheets at game scale
# Usage: bash run_prototype.sh            (OBJ=<artifacts-path> to build elsewhere; default artifacts/anchor-core/obj-bake)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
RUN="${RUN:-$REPO/artifacts/wreck/anchor-tech/capture/run-wreck-seed2}"
BLENDER="${BLENDER:-/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
OBJ="${OBJ:-$REPO/artifacts/anchor-core/obj-bake}"
OUT="$HERE/out"
ANIMS="$REPO/razlom/Assets/Resources/Characters/Pelag_v5"
mkdir -p "$OUT"

export_grip() {   # clip bind hip-limit [extra args...]
  local clip="$1" bind="$2" hips="$3"; shift 3
  "$BLENDER" -b --factory-startup -P "$HERE/anchor_grip_export.py" -- --clip "$clip" --bind "$bind" --hip-limit "$hips" \
    --socket "$HERE/grip_socket.json" --dump-rig --out "$OUT" "$@" 2>&1 | grep -E "GRIP_EXPORT|Error|Traceback"
}

echo "== A: grip tracks"
export_grip Pelag_AN_WreckA Pelag_AN_MobilityBind .95
export_grip Pelag_AN_WreckFinish Pelag_AN_MobilityBind .95
export_grip Pelag_AN_Abordage2_Throw Pelag_AN_Abordage2Bind .5 --hand Right --socket-key abordage \
  --ground-from-anim "$ANIMS/Pelag_AN_Abordage2_Throw.anim"

echo "== A': checks of the grip tracks"
CHECKS=()
python "$HERE/check_vs_anim.py" "$OUT/Pelag_AN_WreckA.grip.json" "$ANIMS/Pelag_AN_WreckA.anim" --out "$OUT/Pelag_AN_WreckA.anim-check.json" | tail -1 || true
CHECKS+=(--anim-check "$OUT/Pelag_AN_WreckA.anim-check.json")
if [ -d "$RUN" ]; then
  python "$HERE/check_vs_capture.py" "$OUT/Pelag_AN_WreckA.grip.json" "$RUN" --out "$OUT/Pelag_AN_WreckA.capture-check.json" | head -1
  CHECKS+=(--capture-check "$OUT/Pelag_AN_WreckA.capture-check.json")
fi
python "$HERE/check_vs_anim.py" "$OUT/Pelag_AN_WreckFinish.grip.json" "$ANIMS/Pelag_AN_WreckFinish.anim" --out "$OUT/Pelag_AN_WreckFinish.anim-check.json" | tail -1 || true

echo "== B: build + self-test + bakes (L 1.6 m, physics = Game.View.AnchorRigCore)"
dotnet build "$HERE/anchorbake/anchorbake.csproj" -c Release --artifacts-path "$OBJ" -nologo -v q
EXE="$OBJ/bin/anchorbake/release/anchorbake.exe"
"$EXE" --grip "$OUT/Pelag_AN_WreckA.grip.json" --selftest --socket "$HERE/grip_socket.json" | tee "$OUT/selftest.txt"
set +e
"$EXE" --grip "$OUT/Pelag_AN_WreckA.grip.json" --kind swing --contact 6 --chain 1.6 --name Pelag_AN_WreckA_w6 \
  --start taut:-120,back,rest,taut:-60,taut:-90,taut:180 --retime-search --guide --tempo \
  --ends-into "Pelag_AN_WreckB@0 | live" "${CHECKS[@]}" --out "$OUT" > "$OUT/Pelag_AN_WreckA_w6.console.txt"
S1=$?
"$EXE" --grip "$OUT/Pelag_AN_WreckFinish.grip.json" --kind slam --contact 6 --impact 0,2.2 --chain 1.6 --name Pelag_AN_WreckFinish_w6 \
  --start rest,taut:-120 --retime-search --guide --anim-check "$OUT/Pelag_AN_WreckFinish.anim-check.json" --out "$OUT" > "$OUT/Pelag_AN_WreckFinish_w6.console.txt"
S2=$?
"$EXE" --grip "$OUT/Pelag_AN_Abordage2_Throw_Right.grip.json" --kind windup --release 2 --contact 0 --chain 1.6 \
  --name Pelag_AN_Abordage2_Throw_windup --start rest --out "$OUT" > "$OUT/Pelag_AN_Abordage2_Throw_windup.console.txt"
S3=$?
set -e
grep -hE "^==|BAKE|ретайм" "$OUT/Pelag_AN_WreckA_w6.console.txt" "$OUT/Pelag_AN_WreckFinish_w6.console.txt" "$OUT/Pelag_AN_Abordage2_Throw_windup.console.txt"

echo "== C: contact sheets"
for s in Pelag_AN_WreckA_w6 Pelag_AN_WreckA_w6.back Pelag_AN_WreckA_w6.rest Pelag_AN_WreckA_w6.guided Pelag_AN_WreckFinish_w6 Pelag_AN_WreckFinish_w6.guided; do
  [ -f "$OUT/$s.sheet.json" ] && python "$HERE/sheet.py" "$OUT/$s.sheet.json" "$OUT/$s.sheet.jpg"
done
echo "reports: $OUT/*.report.md (bake exits $S1 $S2 $S3: 0 pass, 2 fail)"
