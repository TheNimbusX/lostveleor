"""Re-import unity_package/ForestRootSnarer.fbx next to the package scene and compare it with the
authored takes. blender -b ForestRootSnarer_Package.blend -P verify_package.py
Writes unity_package/fbx_check.json."""
import json, re
from pathlib import Path
import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
FBX = HERE / "unity_package" / "ForestRootSnarer.fbx"
raw = FBX.read_bytes()
stacks = [m.group(1).decode() for m in re.finditer(rb"([A-Za-z0-9_|.\-]+)\x00\x01AnimStack", raw)]

sc = bpy.context.scene
src = bpy.data.objects["ARM_ForestRootSnarer"]
src_mesh = bpy.data.objects["SM_ForestRootSnarer_LOD0"]
for tr in src.animation_data.nla_tracks:
    tr.mute = True
authored = {a.name: a for a in bpy.data.actions}
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(FBX), use_anim=True, ignore_leaf_bones=False,
                         automatic_bone_orientation=False, anim_offset=0.0)
new = [o for o in bpy.data.objects if o not in before]
imp = next(o for o in new if o.type == "ARMATURE")
imp_mesh = next(o for o in new if o.type == "MESH")
imported = {a.name: a for a in bpy.data.actions if a.name not in authored}


def heads(arm):
    mw = arm.matrix_world
    return {pb.name: np.array(mw @ pb.head) for pb in arm.pose.bones}


def mesh_co(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.array([ob.matrix_world @ v.co for v in me.vertices])
    ev.to_mesh_clear()
    return co


me = imp_mesh.data
me.calc_loop_triangles()
out = {"fbx": str(FBX), "size_mb": round(FBX.stat().st_size / 1e6, 2), "anim_stacks_in_fbx": stacks,
       "imported_bones": len(imp.data.bones), "imported_bone_names": [b.name for b in imp.data.bones],
       "triangles": len(me.loop_triangles), "vertices": len(me.vertices), "materials": len(me.materials),
       "images": sorted({n.image.filepath for m in me.materials if m and m.node_tree
                         for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}),
       "imported_actions": sorted(imported), "takes": {}}
imp.animation_data_create()
for name, act in authored.items():
    ia = next((a for n, a in imported.items() if n == name or n.endswith("|" + name)), None)
    if ia is None:
        out["takes"][name] = {"missing": True}
        continue
    src.animation_data.action = act
    imp.animation_data.action = ia
    n = int(round(act.frame_range[1]))
    bone_err, vert_err, root_xy, pelvis_xy = 0.0, 0.0, [], []
    for f in range(n + 1):
        sc.frame_set(f)
        a, b = heads(src), heads(imp)
        bone_err = max(bone_err, max(float(np.linalg.norm(a[k] - b[k])) for k in b if k in a))
        root_xy.append(b["root"][:2])
        pelvis_xy.append(b["pelvis"][:2])
        if f % 6 == 0 or f == n:
            ca, cb = mesh_co(src_mesh), mesh_co(imp_mesh)
            if len(ca) == len(cb):
                vert_err = max(vert_err, float(np.abs(ca - cb).max()))
    out["takes"][name] = {"imported_action": ia.name, "frame_range": [round(v, 2) for v in ia.frame_range],
                          "max_bone_head_diff_m": round(bone_err, 5), "max_vertex_diff_m": round(vert_err, 5),
                          "root_xy_travel_m": round(float(np.ptp(np.array(root_xy), 0).max()), 5),
                          "pelvis_xy_range_m": round(float(np.ptp(np.array(pelvis_xy), 0).max()), 4)}
    print("CHECK", name, out["takes"][name], flush=True)
# facing: head bone in front of pelvis along -Y at rest
imp.animation_data.action = None
for pb in imp.pose.bones:
    pb.matrix_basis.identity()
sc.frame_set(0)
h = heads(imp)
out["rest_facing_head_minus_pelvis"] = [round(float(v), 3) for v in (h["head"] - h["pelvis"])]
out["rest_L_clavicle_x"] = round(float(h["L_clavicle"][0]), 3)
(HERE / "unity_package" / "fbx_check.json").write_text(json.dumps(out, indent=1))
print("VERIFY_DONE", json.dumps({k: v for k, v in out.items() if k != "takes"})[:900], flush=True)
