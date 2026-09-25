"""Read-only action-space pole offset sweep on approved SkinCandidate.

The script changes pose control offsets in memory only and writes measurements
under pole_candidate.  This is intentionally not an action/rig patch.
"""

import json
from pathlib import Path

import bpy


HERE = Path(__file__).resolve().parent
PROD = HERE.parent.parent
rig = bpy.data.objects["ARM_ForestWendigo"]
mesh = bpy.data.objects["SM_ForestWendigo_LOD0"]
for path, names in (
    (PROD / "animation" / "revision_primary" / "Wendigo_Primary_v3_Skin.blend",
     ["AN_ForestWendigo_Leap", "AN_ForestWendigo_Claw"]),
    (PROD / "animation" / "secondary" / "output_v9" / "Wendigo_Secondary_Actions.blend",
     ["AN_ForestWendigo_Walk", "AN_ForestWendigo_Death"]),
):
    with bpy.data.libraries.load(str(path), link=False) as (src, dst):
        dst.actions = names
rig.animation_data_create()
scene = bpy.context.scene
results = {}

groups = {}
for side in ("L", "R"):
    for kind in ("hand", "foot"):
        names = {f"{side}_{kind}"} if kind == "hand" else {f"{side}_foot", f"{side}_toe"}
        indices = {mesh.vertex_groups[name].index for name in names}
        groups[f"{side}_{kind}"] = [v.index for v in mesh.data.vertices
                                    if sum(g.weight for g in v.groups if g.group in indices) > .7]

for action_name, frames in {"Leap": (23, 33), "Claw": (15, 18),
                            "Walk": (3, 6), "Death": (34, 45)}.items():
    rig.animation_data.action = bpy.data.actions[f"AN_ForestWendigo_{action_name}"]
    for frame in frames:
        scene.frame_set(frame)
        original = {name: rig.pose.bones[name].location.copy()
                    for name in ("CTRL_R_knee", "CTRL_R_elbow")}
        result = []
        for control in ("CTRL_R_knee", "CTRL_R_elbow"):
            for offset in (-0.4, -0.2, 0, 0.2, 0.4, 0.6, 0.8):
                rig.pose.bones[control].location = original[control]
                rig.pose.bones[control].location.x += offset
                bpy.context.view_layer.update()
                pb = rig.pose.bones
                xyz = lambda bone: tuple(round(v, 4) for v in (rig.matrix_world @ pb[bone].head))
                evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
                deformed = evaluated.to_mesh()
                paw_floor = {
                    name: round(min((evaluated.matrix_world @ deformed.vertices[i].co).z
                                    for i in ids), 4)
                    for name, ids in groups.items()
                }
                evaluated.to_mesh_clear()
                result.append({
                    "control": control,
                    "offset_local_x_m": offset,
                    "R_elbow": xyz("R_arm_lower"),
                    "R_knee": xyz("R_leg_lower"),
                    "R_wrist": xyz("R_hand"),
                    "R_ankle": xyz("R_foot"),
                    "paw_floor_m": paw_floor,
                })
                rig.pose.bones[control].location = original[control]
        results[f"{action_name}_{frame}"] = result

(HERE / "action_pole_sweep.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print("ACTION_POLE_SWEEP", HERE / "action_pole_sweep.json")
