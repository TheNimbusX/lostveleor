#!/usr/bin/env bash
# Крушение v3, Swing2: можно ли от состояния запечки Swing1@8 (голова влево 25,6 м/с) получить контакт впереди в кадре C.
# gripopt_s2 (физика игры AnchorRigCore.StepLive, проверки anchorbake), CMA по пути хвата; тело — Swing1 кадры 8–12 (держится 12).
# v3s2_swing2_probe.sh <work dir>   (OPT2 — gripopt_s2.exe) → <work>/feasibility.txt
set -uo pipefail
W="$1"; HERE="$(cd "$(dirname "$0")" && pwd)"; V3="$(cd "$HERE/.." && pwd)"; REPO=/c/Users/d.grab/Desktop/the-game
OPT2="${OPT2:?gripopt_s2 exe}"; S1="$V3/bake/Pelag_AN_Wreck2_Swing1_w7.anchorbake.json"
mkdir -p "$W"; OUTF="$W/feasibility.txt"; : > "$OUTF"
standin() { python - "$V3/bake/Pelag_AN_Wreck2_Swing1.grip.json" "$W/standin$1.grip.json" "$1" <<'PY'
import json, copy, sys
g = json.load(open(sys.argv[1])); sub = g["sub"]; S = g["samples"]; N = int(sys.argv[3])
out = []
for i in range(N * sub + 1):
    s = copy.deepcopy(S[min(8 * sub + i, 12 * sub)]); s["t"] = i / sub / 30; s["frame"] = i / sub; out.append(s)
g.update(samples=out, frames=N, clip="Pelag_AN_Wreck2_Swing2_standin"); g.pop("unityRig", None); json.dump(g, open(sys.argv[2], "w"))
PY
}
probe() {   # name contact extra-args...
  local name="$1" c="$2"; shift 2; local n=$((c + 5)); standin $n
  ( cd $REPO && "$OPT2" --body "$W/standin$n.grip.json" --start "$S1@8" --out "$W/$name" --contact $c --gens 250 --restarts 3 "$@" ) > "$W/$name.txt" 2>&1
  local cost=$(grep -E "^cost" "$W/$name.txt" | head -1)
  echo "$name (контакт кадр $c = тик $((8 + c)); $*): $(echo "$cost" | sed -E 's/.*\| (c [^|]*).*/\1/')" | tee -a "$OUTF"
  grep -E "  (ok  |FAIL) (5|6|10) " "$W/$name.txt" | sed 's/^/    /' | tee -a "$OUTF"
}
probe c6_backhand 6 --sigma .06
probe c6_any_fast_hands 6 --nodir --vmax 25 --amax 600 --sigma .12
probe c11_any 11 --nodir --sigma .1
probe c15_any 15 --nodir --sigma .1
probe c16_backhand 16 --dirw 80 --sigma .08
probe c20_backhand 20 --dirw 80 --sigma .08
