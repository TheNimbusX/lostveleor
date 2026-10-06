#!/bin/sh
# Бросок якоря: сборка клипов по рефам + выгрузка, треки хвата (шаг A anchor-core), проверка режима Thrown/Yank,
# независимая проверка (метод проверяющего Шквала/Абордажа), листы, GIF, timing.json. Unity и razlom/ не трогаются.
# sh run_all.sh [fast]   — fast: без листов и GIF
set -e
B="C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"
G="C:/Users/d.grab/Desktop/the-game"
ANIM="$G/ART/characters/pelag/anchor-throw-2026-10-03/animation"
REF="$G/ART/characters/pelag/anchor-throw-2026-10-03/motion-ref-pack/B-key-poses-higgsfield.png"
CHK="$G/artifacts/anchor-throw/clips-check"
LOG="$G/artifacts/anchor-throw/clips"
mkdir -p "$LOG" "$CHK/indep" "$ANIM/grip"
cd "$ANIM/scripts"
"$B" -b --factory-startup -P at_author.py -- "$ANIM" > "$LOG/author.log" 2>&1
grep -E "^CHK speed|^VIOL|exported" "$LOG/author.log"
[ -f "$ANIM/Pelag_AnchorThrow_Work.blend1" ] && rm -f "$ANIM/Pelag_AnchorThrow_Work.blend1"
for c in Throw Fly Yank Haul Catch; do
  "$B" -b --factory-startup -P "$G/artifacts/anchor-core/bake/anchor_grip_export.py" -- --clip "$ANIM/Pelag_AN_AnchorThrow_$c.fbx" \
    --bind "$ANIM/Pelag_AN_AnchorThrowBind.fbx" --hip-limit .5 --dump-rig --out "$ANIM/grip" > "$LOG/grip_$c.log" 2>&1
  grep -E "GRIP_EXPORT" "$LOG/grip_$c.log" | cut -c1-60
done
python at_thrown_check.py "$ANIM" | tee "$LOG/thrown.txt"
(cd "$CHK" && "$B" -b --factory-startup -P measure.py -- "$CHK/indep" > "$CHK/indep/measure.log" 2>&1 && python summary.py indep/measure.json > indep/summary.txt)
head -3 "$CHK/indep/summary.txt"
[ "$1" = "fast" ] && exit 0
"$B" -b --factory-startup -P at_render.py -- "$ANIM" "$LOG/frames" sheet > "$LOG/frames.log" 2>&1 &
"$B" -b --factory-startup -P at_render.py -- "$ANIM" "$LOG/gif" gif > "$LOG/gif.log" 2>&1 &
wait
python at_timing.py "$ANIM" "$LOG/frames/v6_check.json" "$CHK/indep/measure.json" "$ANIM/grip/thrown-check.json"
python at_sheet.py "$LOG/frames" "$ANIM" "$REF"
python at_gif.py "$LOG/gif" "$ANIM/anchorthrow-3casts.gif"
echo done
