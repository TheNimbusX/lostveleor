"""Read-only validation of revised locomotion/collapse from a loaded .blend."""

import json
import sys
from pathlib import Path

import bpy

out = Path(sys.argv[sys.argv.index("--") + 1])
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH" and o.find_armature() == rig)
triangles = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
report = {"fps": scene.render.fps, "triangles": triangles, "max_triangles": 25000,
          "root_xy_max_m": 0.0, "scale_curves": [], "nla_tracks": [], "actions": {}}

for track in rig.animation_data.nla_tracks:
    report["nla_tracks"].append({"name": track.name, "muted": track.mute,
                                 "strips": [strip.name for strip in track.strips]})

for clip in ("Walk", "Death"):
    action = bpy.data.actions[f"AN_ForestWendigo_{clip}"]
    rig.animation_data.action = action
    end = int(action.frame_range[1])
    samples = []
    for f in range(end + 1):
        scene.frame_set(f)
        bpy.context.view_layer.update()
        root = rig.pose.bones["root"]
        report["root_xy_max_m"] = max(report["root_xy_max_m"], abs(root.location.x), abs(root.location.y))
        record = {"frame": f}
        for side in ("L", "R"):
            bone = rig.pose.bones[f"{side}_foot"]
            record[f"{side}_foot_y"] = round((rig.matrix_world @ bone.head).y, 4)
        samples.append(record)
    report["actions"][clip] = {"frames": [0, end], "samples": samples}
    for slot in action.slots:
        for layer in action.layers:
            for strip in layer.strips:
                bag = strip.channelbag(slot)
                if bag is None:
                    continue
                for curve in bag.fcurves:
                    if "scale" in curve.data_path:
                        report["scale_curves"].append(f"{action.name}:{curve.data_path}")

# Ground checks were independently derived from evaluated vertices in
# measure_contacts.py, not inferred from controls.
contacts_path = out.parent / "contacts.json"
if contacts_path.exists():
    contacts = json.loads(contacts_path.read_text())
    report["walk_stance_foot_contact_z_m"] = {}
    report["walk_stance_compensated_y_slip_m"] = {}
    for side, first in (("L", 0), ("R", 9)):
        points = contacts["Walk"][first:first+8]
        report["walk_stance_foot_contact_z_m"][side] = [round(min(p[f"{side}_foot_min"] for p in points),4),
                                                        round(max(p[f"{side}_foot_min"] for p in points),4)]
        deltas = [(points[i+1][f"{side}_foot_head_y"] - points[i][f"{side}_foot_head_y"]) + .12
                  for i in range(len(points)-1)]
        report["walk_stance_compensated_y_slip_m"][side] = round(max(abs(v) for v in deltas),4)
    report["death_final_contact_z_m"] = {key: contacts["Death"][-1][key]
                                           for key in ("L_hand_min","R_hand_min","mesh_min")}
    report["death_deepest_penetration_m"] = min(p["mesh_min"] for p in contacts["Death"])
    report["death_max_vertices_below_3cm"] = max(p["below_3cm"] for p in contacts["Death"])

report["pass"] = (
    report["fps"] == 30 and triangles <= 25000 and not report["scale_curves"]
    and report["root_xy_max_m"] < 1e-6
    and report.get("death_max_vertices_below_3cm", 999) == 0
    and all(v < 0.03 for v in report.get("walk_stance_compensated_y_slip_m", {}).values())
)
out.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps({k:v for k,v in report.items() if k != "actions"}, indent=2))
