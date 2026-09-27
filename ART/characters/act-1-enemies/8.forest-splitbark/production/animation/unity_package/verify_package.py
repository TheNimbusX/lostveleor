"""Round-trip check of ForestSplitter.fbx against the baked takes (nothing is saved).

blender -b ../ForestSplitter_Baked_r02.blend -P verify_package.py  -> verify.json (all 10 takes)
Per take and frame: world bone heads and skinned vertices of the re-imported FBX vs the baked blend;
FBX AnimationStack names; root bone travel; FacingGuide position.
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from takes import ORDER, TAKES  # noqa: E402

FBX = HERE / "ForestSplitter.fbx"
scene = bpy.context.scene
STEP = 3  # every 3rd vertex is enough


def use(arm, act):
    arm.animation_data_create()
    arm.animation_data.action = act
    if len(act.slots):
        arm.animation_data.action_slot = act.slots[0]


def sample(arm, mesh, n):
    out = []
    for f in [x * 0.5 for x in range(2 * n + 1)]:
        scene.frame_set(int(f), subframe=f - int(f))
        bones = {b.name: (arm.matrix_world @ b.head, arm.matrix_world @ b.tail) for b in arm.pose.bones}
        dg = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(dg)
        me = ev.to_mesh()
        co = np.empty(len(me.vertices) * 3, dtype=np.float64)
        me.vertices.foreach_get("co", co)
        ev.to_mesh_clear()
        co = co.reshape(-1, 3)[::STEP]
        mw = np.array(mesh.matrix_world)
        co = co @ mw[:3, :3].T + mw[:3, 3]
        out.append((f, bones, co))
    return out


src_arm, src_mesh = bpy.data.objects["ARM_ForestSplitter"], bpy.data.objects["SM_ForestSplitter_LOD0"]
src = {}
for take in ORDER:
    use(src_arm, bpy.data.actions[take])
    src[take] = sample(src_arm, src_mesh, TAKES[take]["frames"])

# FBX stack names straight from the file
from io_scene_fbx import parse_fbx  # noqa: E402

root, _ver = parse_fbx.parse(str(FBX))
stacks = []
for el in root.elems:
    if el.id == b"Objects":
        for ob in el.elems:
            if ob.id == b"AnimationStack":
                stacks.append(ob.props[1].split(b"\x00")[0].decode())

# clear by hand (NOT read_factory_settings: it wipes shared extension wheels)
for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials,
             bpy.data.images):
    for block in list(coll):
        coll.remove(block)
bpy.ops.import_scene.fbx(filepath=str(FBX), anim_offset=0.0)
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")
guide = next((o for o in scene.objects if o.name.startswith("FacingGuide")), None)
report = {"fbx": FBX.name, "fbx_bytes": FBX.stat().st_size, "animation_stacks": stacks,
          "stack_names_exact": stacks == ORDER, "imported_actions": [a.name for a in bpy.data.actions],
          "imported_bones": len(arm.data.bones),
          "triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
          "facing_guide_world": [round(v, 4) for v in guide.matrix_world.translation] if guide else None,
          "takes": {}}
ok = report["stack_names_exact"] and report["triangles"] <= 25000
for take in ORDER:
    act = next((a for a in bpy.data.actions if a.name == take or a.name.endswith("|" + take)), None)
    if act is None:
        report["takes"][take] = {"missing": True}
        ok = False
        continue
    use(arm, act)
    dst = sample(arm, mesh, TAKES[take]["frames"])
    bone_err, vert_err, root_travel = 0.0, 0.0, 0.0
    root0 = dst[0][1]["root"][0]
    for (f, sb, sc), (_, db, dc) in zip(src[take], dst):
        for n, (h, t) in sb.items():
            if n in db:
                bone_err = max(bone_err, (db[n][0] - h).length)  # heads: FBX has no bone tails
        if sc.shape == dc.shape:
            vert_err = max(vert_err, float(np.linalg.norm(sc - dc, axis=1).max()))
        else:
            vert_err = float("nan")
        root_travel = max(root_travel, (db["root"][0] - root0).length)
    t = {"action": act.name, "action_range": [round(x, 3) for x in act.frame_range],
         "max_bone_err_m": round(bone_err, 5), "max_vertex_err_m": round(vert_err, 5),
         "root_travel_m": round(root_travel, 6)}
    t["pass"] = (bone_err < 0.002 and vert_err < 0.004 and root_travel < 1e-4
                 and abs(act.frame_range[1] - TAKES[take]["frames"]) < 0.01 and act.frame_range[0] == 0)
    ok = ok and t["pass"]
    report["takes"][take] = t
report["all_pass"] = ok
(HERE / "verify.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("VERIFY", json.dumps(report), flush=True)
