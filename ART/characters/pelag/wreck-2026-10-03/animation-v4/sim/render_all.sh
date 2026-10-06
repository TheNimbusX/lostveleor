#!/usr/bin/env bash
# Кадры для медиа Крушения v4 (60/с симуляции): полная серия, сабля, разворот — игровая камера, сбок, сверху.
V4=/c/Users/d.grab/Desktop/the-game/ART/characters/pelag/wreck-2026-10-03/animation-v4
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
A=$(cygpath -m $V4/clips); F=$V4/sim
r() { # seq view tag width tgt
  V4_WIDTH=$4 V4_TGT=$5 V4_RES=960x540 "$BL" -b --factory-startup -P $(cygpath -m $V4/scripts/v4_render.py) -- $A $(cygpath -m $F/$1) $(cygpath -m $F/frames_$3_$2) $2 $3 > $F/log_$3_$2.txt 2>&1
  echo "done $3 $2 $(grep -c Saved $F/log_$3_$2.txt)"
}
r seq_full.json game full 5.0 0,-1.3,1.5 &
r seq_full.json side full 4.8 0,-1.25,1.0 &
r seq_sabre.json game sabre 5.0 0,-1.3,1.5 &
wait
r seq_sabre.json side sabre 4.8 0,-1.25,1.0 &
r seq_turn.json game turn 6.0 -1.1,0.3,1.5 &
r seq_turn.json top turn 5.6 -1.1,0.0,0.0 &
wait
echo ALLDONE
