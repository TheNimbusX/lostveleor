#!/usr/bin/env bash
# Слой «цепь-хлыст»: сборка и прогон wreck4anchor: runw.sh <full|turn|pause|before> [yaw2] [key=value ...] → simw_<var>.json
V4=/c/Users/d.grab/Desktop/the-game/ART/characters/pelag/wreck-2026-10-03/animation-v4
VAR=$1; shift
(cd $V4/tools/wreck4anchor && dotnet build wreck4anchor.csproj --artifacts-path ../obj-w4a 2>&1 | grep -E " error |ошибка" | sort -u | head -20)
python $V4/sim/mkplan_w.py "$(cygpath -m $V4/clips)" $V4/sim/planw_$VAR.json $VAR "$@"
$V4/tools/obj-w4a/bin/wreck4anchor/debug/wreck4anchor.exe $V4/sim/planw_$VAR.json $V4/sim/simw_$VAR.json | tail -16
python $V4/scripts/v5_metrics.py $V4/sim/simw_$VAR.json
