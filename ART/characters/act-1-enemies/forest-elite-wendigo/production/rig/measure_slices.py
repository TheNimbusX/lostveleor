"""Print limb spatial slices from the mesh for asymmetric joint placement."""

import sys
from pathlib import Path

import bpy

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(source))
obj = next(o for o in bpy.data.objects if o.type == "MESH")
vertices = [v.co.copy() for v in obj.data.vertices]

def quantile(values, percent):
    return sorted(values)[round((len(values) - 1) * percent)]

for zlo, zhi in ((0, .2), (.2, .4), (.4, .6), (.6, .8), (.8, 1),
                 (1, 1.2), (1.2, 1.4), (1.4, 1.6), (1.6, 1.8), (1.8, 2)):
    for label, predicate in (("x<-0.35", lambda x: x < -.35),
                             ("x>0.35", lambda x: x > .35),
                             ("-0.35<x<0", lambda x: -.35 < x < 0),
                             ("0<x<0.35", lambda x: 0 < x < .35)):
        part = [v for v in vertices if zlo <= v.z < zhi and predicate(v.x)]
        if not part:
            continue
        ys = [v.y for v in part]
        xs = [v.x for v in part]
        print(f"z={zlo:.1f}..{zhi:.1f} {label}: n={len(part)} "
              f"x50={quantile(xs,.5):.3f} "
              f"y10={quantile(ys,.1):.3f} y50={quantile(ys,.5):.3f} "
              f"y90={quantile(ys,.9):.3f}")
