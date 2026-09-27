"""Inspect the Meshy auto-rig GLB: objects, scales, bones, weights."""
import sys
import bpy
from mathutils import Vector

src = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

for o in bpy.data.objects:
    print("OBJ", o.name, o.type, "parent=", o.parent.name if o.parent else None,
          "loc", tuple(round(v, 4) for v in o.location),
          "rot", tuple(round(v, 4) for v in o.rotation_euler),
          "scale", tuple(round(v, 4) for v in o.scale))
    if o.type == "MESH":
        me = o.data
        me.calc_loop_triangles()
        mw = o.matrix_world
        ws = [mw @ v.co for v in me.vertices]
        mn = Vector((min(p.x for p in ws), min(p.y for p in ws), min(p.z for p in ws)))
        mx = Vector((max(p.x for p in ws), max(p.y for p in ws), max(p.z for p in ws)))
        print("  verts", len(me.vertices), "tris", len(me.loop_triangles),
              "bbox_world", tuple(round(v, 4) for v in mn), tuple(round(v, 4) for v in mx))
        print("  vgroups", len(o.vertex_groups), [g.name for g in o.vertex_groups])
        print("  modifiers", [(m.type, getattr(m, 'object', None) and m.object.name) for m in o.modifiers])
        print("  materials", [m.name if m else None for m in me.materials])
        unweighted = 0
        maxinf = 0
        hist = {}
        for v in me.vertices:
            gs = [g for g in v.groups if g.weight > 1e-5]
            n = len(gs)
            hist[n] = hist.get(n, 0) + 1
            if n == 0:
                unweighted += 1
        print("  influence_hist", dict(sorted(hist.items())), "unweighted", unweighted)
    if o.type == "ARMATURE":
        mw = o.matrix_world
        for b in o.data.bones:
            h = mw @ b.head_local
            t = mw @ b.tail_local
            print("  BONE", b.name, "parent=", b.parent.name if b.parent else None,
                  "head", tuple(round(v, 3) for v in h), "tail", tuple(round(v, 3) for v in t),
                  "deform", b.use_deform)
