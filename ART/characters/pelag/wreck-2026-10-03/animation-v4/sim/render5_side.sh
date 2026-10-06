#!/usr/bin/env bash
# Только виды сбоку (цель выше: голова над героем в кадре).
V4=/c/Users/d.grab/Desktop/the-game/ART/characters/pelag/wreck-2026-10-03/animation-v4
source <(sed -n '/^BL=/,/^}/p' $V4/sim/render5_all.sh)
A=$(cygpath -m $V4/clips); F=$V4/sim/r5
r seq5_full.json side full 4.8 0,-1.25,1.35 &
r seq_sabre.json side sabre 4.8 0,-1.25,1.35 &
r seq5_turn.json side turn 5.8 0,0.6,1.35 &
wait
