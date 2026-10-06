"""v3s5nw: центры решений рук петли — скользящее среднее по кругу (кадр N = кадр 0), чтобы узкий перебор следующего круга шёл
без перескока на шве петли. python v3s5nw_rows_wrap.py <rows.json> <out rows.json> [проходов 3] [кадров N 12]"""
import json, sys
src, dst = sys.argv[1], sys.argv[2]
passes = int(sys.argv[3]) if len(sys.argv) > 3 else 3
N = int(sys.argv[4]) if len(sys.argv) > 4 else 12
R = json.load(open(src, encoding="utf-8"))
rows = R["rows"]
sol = [{s: list(rows[f]["sol"][s]) for s in ("Left", "Right")} for f in range(N)]
for _ in range(passes):
    sol = [{s: [(sol[(f - 1) % N][s][i] + 2 * sol[f][s][i] + sol[(f + 1) % N][s][i]) / 4 for i in range(4)] for s in ("Left", "Right")} for f in range(N)]
for f in range(N + 1):
    rows[f]["sol"] = {s: [round(x, 2) for x in sol[f % N][s]] for s in ("Left", "Right")}
json.dump(R, open(dst, "w", encoding="utf-8"), ensure_ascii=False)
print("wrapped", N, "frames,", passes, "passes")
