#!/bin/sh
# r02 (27.09): roll takes appended to the approved r01 ForestSplitter package. Run from this folder (Git Bash).
# The r01 blends are inputs only; their actions are copied into the r02 blends, never re-baked.
set -e
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$B" -b ../rig/ForestSplitter_Rig.blend -P bake_roll.py > bake_r02.log 2>&1              # new takes -> roll_actions_r02.blend
"$B" -b ForestSplitter_Baked_r01.blend -P merge_r02.py -- baked > merge_r02.log 2>&1      # -> ForestSplitter_Baked_r02.blend
"$B" -b ForestSplitter_Anim_r01.blend -P merge_r02.py -- editable >> merge_r02.log 2>&1   # -> ForestSplitter_Anim_r02.blend
"$B" -b ForestSplitter_Baked_r02.blend -P validate_roll.py > validate_r02.log 2>&1        # -> validation_r02.json
(cd unity_package && "$B" -b ../ForestSplitter_Baked_r02.blend -P build_package.py > build_r02.log 2>&1)
(cd unity_package && "$B" -b ../ForestSplitter_Baked_r02.blend -P verify_package.py > verify_r02.log 2>&1)
(cd unity_package/check && "$B" -b --factory-startup -P check_r02.py > r02.log 2>&1)     # old FBX vs new FBX
(cd unity_package/check && python write_verification_r02.py)                               # -> verification.json r02
"$B" -b ForestSplitter_Baked_r02.blend -P render_review.py -- Roll --roll-seq > render_r02.log 2>&1
python review_media.py Roll --roll-seq
python review_page.py
grep -h -E "VALIDATION_R02|PACKAGE_EXPORTED|CHECK_R02" validate_r02.log unity_package/build_r02.log unity_package/check/r02.log | cut -c1-120
# 27.09 independent check (re-import, raw FBX curves vs r01, contact/support per take) -> verification.json block
(cd unity_package/check && "$B" -b --factory-startup -P check_r02_indep.py > r02_indep.log 2>&1)
(cd unity_package/check && python write_verification_r02_indep.py)
