"""Collect rig_report.json from the master blend + build/probe/export/roundtrip JSONs.

usage: blender -b ForestSplitter_Rig.blend -P report_rig.py -- <rig_dir>
"""
import json
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import rig_spec as spec  # noqa: E402

rig_dir = Path(sys.argv[sys.argv.index("--") + 1]).resolve()


def load(name):
    p = rig_dir / name
    return json.loads(p.read_text(encoding="utf-8")) if p.exists() else None


arm = bpy.data.objects[spec.ARM_NAME]
mesh = bpy.data.objects[spec.MESH_NAME]
me = mesh.data
me.calc_loop_triangles()
bm = bmesh.new()
bm.from_mesh(me)
geo = {"triangles": len(me.loop_triangles), "vertices": len(me.vertices),
       "boundary_edges": sum(e.is_boundary for e in bm.edges), "islands_note": "one welded island (shells fused to body)"}
bm.free()
names = [g.name for g in mesh.vertex_groups]
cnt = np.array([sum(1 for g in v.groups if g.weight > 1e-6) for v in me.vertices])
wsum = np.array([sum(g.weight for g in v.groups) for v in me.vertices])
bones = []
for b in arm.data.bones:
    bones.append({"name": b.name, "parent": b.parent.name if b.parent else None, "deform": b.use_deform,
                  "connected": b.use_connect, "head": [round(x, 3) for x in b.head_local],
                  "tail": [round(x, 3) for x in b.tail_local]})
constraints = {pb.name: [{"type": c.type, "name": c.name, "influence": c.influence} for c in pb.constraints]
               for pb in arm.pose.bones if pb.constraints}
images = [{"name": i.name, "packed": bool(i.packed_file), "size": list(i.size)} for i in bpy.data.images
          if i.source == "FILE" and i.size[0] > 0]

report = {
    "mob": "Расщепень / ForestSplitter",
    "step": "1 - game rig",
    "rig_version": arm.get("rig_version"),
    "files": {
        "master_blend": str(rig_dir / "ForestSplitter_Rig.blend"),
        "unity_fbx": str(rig_dir / "ForestSplitter_Rig.fbx"),
        "fbx_textures": (load("export_report.json") or {}).get("textures"),
        "scripts": ["rig_spec.py", "segment.py", "proxy_heat.py", "weights.py", "ik_controls.py", "build_rig.py",
                    "pose_lib.py", "render_util.py", "deform_test.py", "sheet.py", "export_rig.py",
                    "verify_export.py", "report_rig.py"],
        "rebuild": "blender -b -P build_rig.py -- ../model/ForestSplitter_24k_candidate.blend <rig_dir>; "
                   "then export_rig.py, verify_export.py, deform_test.py, report_rig.py on the rig blend",
    },
    "model": {"source": str(rig_dir.parent / "model" / "ForestSplitter_24k_candidate.blend"), "height_m": 1.3,
              "front_axis_blender": "-Y", "up": "+Z", "origin": "entity origin on the ground (0,0,0)",
              "triangle_budget": 25000, **geo, "materials": [m.name for m in me.materials], "images": images},
    "objects": [o.name for o in bpy.data.objects],
    "scene_fps": bpy.context.scene.render.fps,
    "bones": bones,
    "deform_bone_count": sum(b.use_deform for b in arm.data.bones),
    "control_bones": [b.name for b in arm.data.bones if not b.use_deform],
    "constraints_default_influence_0": constraints,
    "conventions": {
        "side": "_L = +X = character left, _R = -X",
        "shell_open": "shell_L/shell_R: positive rotation about the bone's local X opens the half (top outward); "
                      "pivot = bone head on the belly-pocket crease (|x|=0.40, y=-0.05, z=0.47); parented to spine",
        "shell_open_armature_space": "shell_L about +Y world, shell_R about -Y world",
        "neck": "not connected: can be translated forward for a head thrust out of the shell gap",
        "legs": "leg_<front|hind>_<L|R>_<upper|lower|foot>; FK default; IK_leg_* + FootRot_leg_* (influence 0) "
                "with CTRL_foot_* (ankle, foot frame) and CTRL_pole_* for planted feet",
        "fbx": "deform bones only, no leaf bones, axis_forward -Z / up Y (Blender -Y front -> Unity +Z), no animation",
        "children": "same rig at uniform scale 0.6 (scale the Unity root); weights are scale-independent",
    },
    "weights": {
        "method": ["bone heat on a watertight voxel proxy (12 mm, largest component) transferred back "
                   "(raw mesh and plain remesh both fail the heat solve)",
                   "shell/body segmentation: multi-source Dijkstra with concave-crease cost (segment.py)",
                   "shell verts 100% rigid to shell_L/shell_R",
                   "body verts within 0.25 m of a crease: harmonic shell influence (spreads the opening stretch over "
                   "the V floor and belly pockets); neck/head excluded",
                   "max 4 influences, normalized"],
        "max_influences": int(cnt.max()), "unweighted_vertices": int((cnt == 0).sum()),
        "max_weight_sum_error": round(float(np.abs(wsum - 1).max()), 6),
        "build": load("rig_build.json"),
    },
    "deformation_test": {
        "requested_pose": "combined = shells 25 deg open, neck thrust 0.12 m + head pitch up 12 deg, "
                          "front-left paw lifted 0.16 m / forward 0.10 m via IK+FootRot",
        "renders": str(rig_dir / "deform_test"),
        "sheet": str(rig_dir / "deform_test" / "sheet_requested.png"),
        "child_renders": str(rig_dir / "deform_test_child"),
        "metrics_adult": (load("deform_test/deform_metrics.json") or {}).get("poses"),
        "metrics_child_0.6": (load("deform_test_child/deform_metrics.json") or {}).get("poses"),
    },
    "roundtrip_fbx": load("roundtrip_report.json"),
    "checked_in_renders": [
        "shell halves stay rigid in every pose (shell edge length ratio 0.9995-1.0009)",
        "shells 15 deg: gap reads as the creature's green back between the halves (best range for idle/bite)",
        "shells 25 deg (requested): tops move ~0.36 m out each; V floor stretches ~2.5-3x (p99 edge 3.1x), "
        "dark wedges between shell front rims and head sides; still reads as an open pod from the game camera",
        "head thrust: neck slides forward out of the gap, beak and eyes undistorted",
        "front-left paw lift (IK+FootRot): paw stays level, knee folds under the shell rim, no shell pull",
        "hind leg IK step, spine/body twist, shell clap -6 deg: clean",
        "child at 0.6 uniform scale: identical deformation",
        "FBX round trip: 19 bones, 24000 tris, weights and posed shell identical to master",
    ],
    "open_problems": [
        "Shells are fused to the body (one welded island, no body surface under them): any opening stretches "
        "the back strip; >25 deg is unusable and full detachment (death burst / shells flying off) needs the "
        "halves split off + a core cap under them (model change, owner OK) or full VFX cover.",
        "No jaw bone: the beak is a solid wedge without mouth line or cavity; a probe jaw only lengthened the beak. "
        "A readable bite gape needs a mouth cut + interior in the model; until then bite = head lunge + shells.",
        "Shell pivot sits on the belly-pocket crease: at 25 deg the lower rim swings ~0.1 m inward/down toward "
        "the front leg tops (hidden from the game camera, visible from below).",
        "FK spine/body pitch drives the front legs into the ground; clips should key IK_leg_*/FootRot_leg_* "
        "influence 1 for planted feet.",
        "Segmentation seeds are hand-placed coordinates for this candidate; a new model needs re-tuning (segment.py).",
    ],
}
(rig_dir / "rig_report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
print("REPORT_OK", report["objects"], report["deform_bone_count"], geo, flush=True)
