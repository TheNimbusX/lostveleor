"""Крушение v3, Slam от Swing2: путь хвата-догадка (для первого тела и как старт gripopt_s3). blender -b -P v3s4sl_guess.py -- <g0 x,y,z> <out.json>"""
import sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import v3s4sl_keys as K
argv = sys.argv[sys.argv.index("--") + 1:]
g0 = tuple(float(x) for x in argv[0].split(","))
json.dump(dict(frames=[[round(c, 5) for c in p] for p in K.guess(g0)], contact=K.CONTACT, overhead=K.OVERHEAD), open(argv[1], "w"), indent=1)
print("GUESS", K.CONTACT, K.OVERHEAD, K.N)
