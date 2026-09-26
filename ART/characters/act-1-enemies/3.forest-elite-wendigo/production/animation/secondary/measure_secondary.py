"""Read-only action/ground/loop audit for the secondary Wendigo animation source."""

import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


out = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
scene = bpy.context.scene
rig = next(obj for obj in scene.objects if obj.type == "ARMATURE")
mesh = next(obj for obj in scene.objects if obj.type == "MESH")
specs = {"Idle": 60, "Walk": 12, "Hit": 14, "Death": 45}
report = {}
foot_indices = {}
for side in ("L", "R"):
    groups = {mesh.vertex_groups[f"{side}_foot"].index, mesh.vertex_groups[f"{side}_toe"].index}
    foot_indices[side] = [vertex.index for vertex in mesh.data.vertices
                          if sum(g.weight for g in vertex.groups if g.group in groups) >= 0.60]

for name, final_frame in specs.items():
    rig.animation_data.action = bpy.data.actions[f"AN_ForestWendigo_{name}"]
    samples = []
    for frame in range(final_frame + 1):
        scene.frame_set(frame)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = mesh.evaluated_get(depsgraph)
        evaluated_mesh = evaluated.to_mesh()
        zs = [(evaluated.matrix_world @ vertex.co).z for vertex in evaluated_mesh.vertices]
        foot_mins = {side: round(min(zs[index] for index in indices), 4)
                     for side, indices in foot_indices.items()}
        evaluated.to_mesh_clear()
        coords = {}
        for side in ("L", "R"):
            for suffix in ("foot", "toe", "hand"):
                bone = rig.pose.bones[f"{side}_{suffix}"]
                coords[f"{side}_{suffix}"] = [round(n, 4) for n in rig.matrix_world @ bone.head]
        samples.append({"frame": frame, "mesh_min_z": round(min(zs), 4),
                        "mesh_verts_below_minus_3cm": sum(z < -0.03 for z in zs),
                        "foot_mesh_min_z": foot_mins,
                        "bone_heads": coords,
                        "root": [round(n, 5) for n in rig.pose.bones["root"].location]})
    seam = None
    if name in ("Idle", "Walk"):
        seam = {joint: round(max(abs(samples[0]["bone_heads"][joint][i] - samples[-1]["bone_heads"][joint][i]) for i in range(3)), 6)
                for joint in samples[0]["bone_heads"]}
    report[name] = {"min_mesh_z": min(s["mesh_min_z"] for s in samples),
                    "max_penetrating_verts": max(s["mesh_verts_below_minus_3cm"] for s in samples),
                    "seam_joint_error_m": seam,
                    "notable_frames": [samples[i] for i in sorted({0, final_frame // 2, final_frame})],
                    "samples": samples}
    if name == "Walk":
        contacts = []
        for sample in samples:
            t = sample["frame"] / final_frame
            for side, phase in (("L", t % 1), ("R", (t + 0.5) % 1)):
                if phase <= 0.60:
                    contacts.append(sample["foot_mesh_min_z"][side])
        report[name]["planted_foot_contact_z_range"] = [round(min(contacts), 4), round(max(contacts), 4)]

out.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps({name: {key: value for key, value in data.items() if key != "samples"} for name, data in report.items()}, indent=2))
