#!/bin/sh
# Rebuild the animation master + Unity package, then run the independent FBX verification.
# sh rebuild_and_verify.sh   (from anywhere; Git Bash)
set -e
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
V="$(cd "$(dirname "$0")" && pwd)"
A="$V/../.."
run() { "$B" -b "$@" 2>&1 | grep -E "BUILD_OK|VALIDATION_OK|PACKAGE_OK|FBX_CHECK|WROTE|TOPO_OK|^TAKE|Error:" | cut -c1-200 || true; }
run -P "$A/build_anim.py"
run "$A/ForestThorncaster_Anim_r01.blend" -P "$A/validate_anim.py"
run -P "$A/unity_package/build_package.py"
run -P "$A/unity_package/check_fbx.py"
for s in v_raw.py v_takes.py v_dump_topo.py v_contacts.py v_fold.py; do run -P "$V/$s"; done
cd "$V" && python a_stretch.py > out/stretch.txt && python a_walk.py > /dev/null && python a_verification.py && echo "VERIFY_DONE"
