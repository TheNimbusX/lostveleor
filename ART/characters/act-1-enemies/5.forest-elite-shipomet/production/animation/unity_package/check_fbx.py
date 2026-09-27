"""Round-trip check of ForestThorncaster.fbx -> fbx_check.json.

blender -b -P check_fbx.py
"""
import json
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
FBX = HERE / "ForestThorncaster.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(FBX), anim_offset=0.0)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.data.objects if o.type == "MESH")
muz = bpy.data.objects.get("Muzzle_RightSpike")
scene = bpy.context.scene
out = {"objects": [(o.name, o.type) for o in bpy.data.objects], "bones": len(arm.data.bones),
       "triangles": sum(len(p.vertices) - 2 for p in mesh.data.polygons),
       "armature_world_scale": [round(x, 4) for x in arm.matrix_world.to_scale()],
       "actions": {}, "scene_fps": scene.render.fps}


def use(act):
    arm.animation_data.action = act
    if len(act.slots):
        arm.animation_data.action_slot = act.slots[0]


for act in bpy.data.actions:
    paths = {fc.data_path for lay in act.layers for st in lay.strips for bag in st.channelbags for fc in bag.fcurves}
    spans = {}
    for lay in act.layers:
        for st in lay.strips:
            for bag in st.channelbags:
                for fc in bag.fcurves:
                    if not fc.data_path.startswith("pose."):
                        v = [k.co[1] for k in fc.keyframe_points]
                        spans[f"{fc.data_path}[{fc.array_index}]"] = round(max(v) - min(v), 6)
    out["actions"][act.name] = {"range": list(act.frame_range), "object_level_curve_max_span": max(spans.values()) if spans else 0.0}
acts = {a.name.split("|")[-1]: a for a in bpy.data.actions if a.name.startswith("ARM_")}
if "ForestThorncaster_Shot" in acts and muz:
    use(acts["ForestThorncaster_Shot"])
    scene.frame_set(21)
    out["shot_f21_muzzle_empty_world"] = [round(x, 4) for x in muz.matrix_world.translation]
    out["note"] = "imported bone tails are re-guessed by the FBX importer; the Muzzle empty and the skinned mesh are the ground truth"
if "ForestThorncaster_LineCast" in acts:
    use(acts["ForestThorncaster_LineCast"])
    hand = {mesh.vertex_groups["LeftHand"].index, mesh.vertex_groups["RightHand"].index}
    spike = [v.index for v in mesh.data.vertices if any(g.group in hand and g.weight > 0.999 for g in v.groups)]
    z = {}
    for f in (23, 24, 25):
        scene.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        me = mesh.evaluated_get(dg).to_mesh()
        z[f] = round(min((mesh.matrix_world @ me.vertices[i].co).z for i in spike), 4)
        mesh.evaluated_get(dg).to_mesh_clear()
    out["linecast_spike_min_z"] = z
print("FBX_CHECK", json.dumps(out))
(HERE / "fbx_check.json").write_text(json.dumps(out, indent=1))
