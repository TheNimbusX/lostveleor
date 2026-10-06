"""Слой «цепь-хлыст»: покадровая сводка цепи из simw_*.json. python w_dump.py <sim.json> [every]"""
import json, math, sys
d = json.load(open(sys.argv[1])); ev = int(sys.argv[2]) if len(sys.argv) > 2 else 1
for f in d['frames'][::ev]:
    ch = f['chain']; L = f['cable']; n = len(ch) - 1
    segs = [math.dist(ch[i], ch[i + 1]) for i in range(n)]
    print("%.3f %-6s %-7s L %.3f span %.3f bow %.3f str %.3f cpen %.4f dev %4.1f seg %.2f-%.2f ringY %.2f lowY %.2f grip %s" % (
        f['t'], f['clip'][16:], f['mode'], L, f['span'], f['bow'], f['strain'], f['cpen'], f['dev'], min(segs) / (L / n), max(segs) / (L / n),
        f['ring'][1], min(c[1] for c in ch), [round(x, 2) for x in f['grip']]))
