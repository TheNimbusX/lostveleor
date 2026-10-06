#!/usr/bin/env bash
# Крушение v3, формы и правка пауз (06.10): финальная сборка из animation-v3/bake (без Unity, без razlom/; база не трогается).
#  1) «Якорная броня»: Swing1/Swing2/Slam_Braced — v3s5_brace.py поверх рабочих .blend базы (bake/<клип>.corr.json — поправка
#     переноса, ≈ 0), выгрузка хвата → сверка с хватом базы (v3s5_corr) → запечка теми же ключами, что база → сверка головы.
#  2) «Волнорез»: Slam_Drag — v3s4_author + v3s5dr_keys (0–16 — копия Slam), путь bake/…Slam_Drag.plan.json (v3s5dr_plan.py),
#     поправка/решения рук bake/…Slam_Drag.corr.json / .arm_solutions.rows.json → запечка slam → протяжка (v3s5dr_check).
#  3) Wait1/Wait2: левая стопа на землю (v3s5_waitfoot.py; повторный прогон ничего не меняет) → хват = принятый.
#  4) кадры/видео/листы (v3s5_media + v3s3_render), 5) timing.json (v3s5_timing.py).
# Нужны: AB (anchorbake.exe), FRAMES_DIR (временные кадры вне репозитория); TOOL — anchor-core/bake (artifacts/ или tools/ копия).
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"; B=$V3/bake
REPO=/c/Users/d.grab/Desktop/the-game
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
AB="${AB:?anchorbake exe}"; FR="${FRAMES_DIR:?temp dir}"; TOOL="${TOOL:-$REPO/artifacts/anchor-core/bake}"
[ -d "$TOOL" ] || TOOL=$V3/tools/anchor-core/bake
RIG="$REPO/razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx"
W=$(cygpath -m "$V3"); BW=$W/bake; export PYTHONIOENCODING=utf-8
mkdir -p "$FR"
export_grip() { RAZLOM_REPO="$(cygpath -m $REPO)" "$BL" -b --factory-startup -P "$TOOL/anchor_grip_export.py" -- --clip "$W/$1.fbx" --bind "$W/Pelag_AN_Wreck2Bind.fbx" \
  --rig "$(cygpath -m $RIG)" --hip-limit .5 --socket "$(cygpath -m $TOOL/grip_socket.json)" --dump-rig --out "$BW" 2>&1 | grep -E "GRIP_EXPORT" | sed 's/.*frames=/frames=/' | cut -c1-160; }
bk() { (cd $B && "$AB" --repo "$(cygpath -m $REPO)" "$@" --out . ) ; }

# ---------- 1) Якорная броня
for C in Swing1_Braced Swing2_Braced Slam_Braced; do
  C=Pelag_AN_Wreck2_$C; BASE=${C%_Braced}
  (cd "$HERE" && V3S5_CORR=$BW/$C.corr.json "$BL" -b --factory-startup -P v3s5_brace.py -- $C "$W" 2>&1 | grep -E "CHK|VIOLATIONS|  V |exported" | cut -c1-200)
  export_grip $C
  python "$HERE/v3s5_corr.py" $B/$C.grip.json $B/$BASE.grip.json "$FR/${C}_gripdiff.json" "" "$(python -c "import sys; sys.path.insert(0, r'$(cygpath -w $HERE)'); import v3s5_brace_keys as K; c=K.CFG['$C']; print(','.join(str(f) for f in sorted(set(c.get('SEAM',{}))|set(c.get('IDENT',())))))")" | tee $B/$C.gripdiff.txt
done
cp $B/Pelag_AN_Wreck2_Swing1.start.anchorbake.json "$FR/" 2>/dev/null
bk --grip Pelag_AN_Wreck2_Swing1_Braced.grip.json --kind swing --contact 7 --chain 1.6 --name Pelag_AN_Wreck2_Swing1_Braced_w7 \
   --start "Pelag_AN_Wreck2_Swing1.start.anchorbake.json@0" --tempo --ends-into "Pelag_AN_Wreck2_Swing2_Braced@0 | live" > $B/Pelag_AN_Wreck2_Swing1_Braced.validation.txt
bk --grip Pelag_AN_Wreck2_Swing2_Braced.grip.json --kind swing --contact 14 --chain 1.6 --name Pelag_AN_Wreck2_Swing2_Braced_w14 \
   --start "Pelag_AN_Wreck2_Swing1_Braced_w7.anchorbake.json@8" --tempo > $B/Pelag_AN_Wreck2_Swing2_Braced.validation.txt
bk --grip Pelag_AN_Wreck2_Slam_Braced.grip.json --kind slam --contact 13 --overhead 7 --impact 0,2.2 --chain 1.6 --name Pelag_AN_Wreck2_Slam_Braced_w13 \
   --start "Pelag_AN_Wreck2_Swing2_Braced_w14.anchorbake.json@15" --tempo > $B/Pelag_AN_Wreck2_Slam_Braced.validation.txt
for c in Swing1:w7 Swing2:w14 Slam:w13; do n=${c%%:*}; w=${c##*:}; grep BAKE $B/Pelag_AN_Wreck2_${n}_Braced.validation.txt
  python "$HERE/v3s5_bakediff.py" $B/Pelag_AN_Wreck2_${n}_Braced_${w}.anchorbake.json $B/Pelag_AN_Wreck2_${n}_${w}.anchorbake.json | tee $B/Pelag_AN_Wreck2_${n}_Braced.bakediff.txt
  python "$HERE/v3s5_bakediff.py" $B/Pelag_AN_Wreck2_${n}_Braced_${w}.anchorbake.json $B/Pelag_AN_Wreck2_${n}_${w}.anchorbake.json 13 | tee -a $B/Pelag_AN_Wreck2_${n}_Braced.bakediff.txt
done

# ---------- 2) Волнорез
C=Pelag_AN_Wreck2_Slam_Drag
(cd "$HERE" && V3S4_SOL=$BW/$C.arm_solutions.rows.json V3S4_CORR=$BW/$C.corr.json "$BL" -b --factory-startup -P v3s4_author.py -- v3s5dr_keys $BW/$C.plan.json "$W" \
   2>&1 | grep -E "CHK|VIOLATIONS|  V |exported" | cut -c1-200)
export_grip $C
python "$HERE/v3s4_corr.py" $B/$C.grip.json $B/$C.plan.json "$FR/drag_corr_check.json" $B/$C.corr.json 22,23,24 || echo "residual > 3 mm"
python - "$B/$C.grip.json" "$B/Pelag_AN_Wreck2_Slam.grip.json" <<'PY'
import json, math, sys
a, b = (json.load(open(p)) for p in sys.argv[1:3]); s = a["sub"]
print("DRAG vs SLAM 0–16: хват макс %.4f м" % max(math.dist(x["grip"], y["grip"]) for x, y in zip(a["samples"][:16 * s + 1], b["samples"][:16 * s + 1])))
PY
cp $B/Pelag_AN_Wreck2_Swing2_w14.anchorbake.json "$FR/" 2>/dev/null
bk --grip $C.grip.json --kind slam --contact 13 --overhead 7 --impact 0,2.2 --chain 1.6 --name ${C}_w13 --start "Pelag_AN_Wreck2_Swing2_w14.anchorbake.json@15" --tempo > $B/$C.validation.txt
grep -E "BAKE|16b" $B/$C.validation.txt
python "$HERE/v3s5dr_check.py" $B/${C}_w13.anchorbake.json 16 22 --json $B/$C.drag.json | head -1 | tee $B/$C.drag.txt
python "$HERE/v3s5dr_check.py" $B/${C}_w13.anchorbake.json 13.5 25 | head -1 | tee -a $B/$C.drag.txt

# ---------- 3) Wait1 / Wait2: левая стопа на землю
for C in Wait1 Wait2; do C=Pelag_AN_Wreck2_$C
  (cd "$HERE" && "$BL" -b --factory-startup -P v3s5_waitfoot.py -- $C "$W" 2>&1 | grep -E "LEGS|FOOT|SOCKET|CHK|VIOLATIONS|  V |exported" | cut -c1-200)
  cp $B/$C.grip.json "$FR/$C.accepted.grip.json"; export_grip $C
  python "$HERE/v3s5_corr.py" $B/$C.grip.json "$FR/$C.accepted.grip.json" "$FR/${C}_gripdiff.json" | tee $B/$C.footfix.txt
done

# ---------- 4) кадры, видео, листы
cd "$HERE"; FW=$(cygpath -m "$FR")
python v3s5_media.py braced "$W" "$FW/braced_seq.json"; python v3s5_media.py drag "$W" "$FW/drag_seq.json"; python v3s5_media.py cmp "$W" "$FW/cmp_seq.json"
python v3s4_media.py pause1 "$W" "$FW/pause1_seq.json"; python v3s4_media.py pause2 "$W" "$BW/Pelag_AN_Wreck2_Stow_from_Wait2.live.json" "$FW/pause2_seq.json"
render() { V3_RES=720x480 "$BL" -b --factory-startup -P v3s3_render.py -- "$W" "$FW/$1_seq.json" "$FW" $2 wreck3-$1 2>&1 | grep -E "SEAMCHK|render done|Error"; }
for s in braced:game braced:frw drag:game drag:rside cmp:frw; do render ${s%%:*} ${s##*:} & done; wait
for s in pause1:game pause1:frw pause2:game pause2:frw; do render ${s%%:*} ${s##*:} & done; wait
python v3s5_media.py make "$FW" "$W/media" wreck3-braced game,frw "$FW/braced_seq.json"
python v3s5_media.py make "$FW" "$W/media" wreck3-drag game,rside "$FW/drag_seq.json"
python v3s5_media.py make "$FW" "$W/media" wreck3-pause1 game,frw "$FW/pause1_seq.json"
python v3s5_media.py make "$FW" "$W/media" wreck3-pause2 game,frw "$FW/pause2_seq.json"
python v3s5_media.py sheet "$FW" "$W/media" wreck3-cmp frw "$FW/cmp_seq.json" 6 "Крушение v3 · база | «Якорная броня» (поза 10): шире и ниже до предела таза 0,5, колени наружу, подбородок вниз; кисти и якорь — база"
python - "$FW" <<'PY'
import json, sys
d = sys.argv[1]; s = json.load(open(d + "/drag_seq.json", encoding="utf-8"))
keep = [26, 32, 34, 36, 38, 40, 42, 44, 46, 50]
json.dump(dict(frames=[s["frames"][i] for i in keep]), open(d + "/drag_keys_seq.json", "w", encoding="utf-8"), ensure_ascii=False)
import os
for v in ("game", "rside"):
    for j, i in enumerate(keep):
        src = "%s/wreck3-drag_%s_%03d.png" % (d, v, i)
        if os.path.exists(src): __import__("shutil").copy(src, "%s/wreck3-dragkeys_%s_%03d.png" % (d, v, j))
PY
python v3s5_media.py sheet "$FW" "$W/media" wreck3-dragkeys game,rside "$FW/drag_keys_seq.json" 4 "Крушение v3 · «Волнорез» Slam_Drag: удар (к13) → протяжка, поза 11 (к17–21) → стойка к отпуску Sim (к23)"

# ---------- 5) timing.json
python v3s5_timing.py "$W"
