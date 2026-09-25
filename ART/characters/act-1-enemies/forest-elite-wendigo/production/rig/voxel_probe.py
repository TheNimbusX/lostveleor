"""Explore voxel closure + quad retopology; never edits approved source files."""

import json
import sys
import time
from pathlib import Path

import bmesh
import bpy

source = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
target = Path(sys.argv[sys.argv.index("--") + 2]).resolve()
target.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
obj = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
obj.data.remesh_voxel_size = 0.008
started = time.monotonic()
bpy.ops.object.voxel_remesh()
obj.data.calc_loop_triangles()
report = {"voxel_vertices": len(obj.data.vertices),
          "voxel_triangles": len(obj.data.loop_triangles),
          "voxel_seconds": round(time.monotonic() - started, 2)}
print("VOXEL_REPORT", report, flush=True)
bm = bmesh.new()
bm.from_mesh(obj.data)
report["voxel_boundary"] = sum(not e.is_manifold for e in bm.edges)
report["voxel_noncontiguous"] = sum(not e.is_contiguous for e in bm.edges)
bm.free()
obj.name = "SM_ForestWendigo_VoxelProbe"
bpy.ops.wm.save_as_mainfile(filepath=str(target / "voxel_probe.blend"))
started = time.monotonic()
report["quad_op_status"] = list(bpy.ops.object.quadriflow_remesh(mode="FACES", target_faces=11000,
                                use_preserve_sharp=False,
                                use_preserve_boundary=False,
                                preserve_attributes=False,
                                use_mesh_symmetry=False, seed=1))
obj.data.calc_loop_triangles()
report.update({"quad_vertices": len(obj.data.vertices),
               "quad_faces": len(obj.data.polygons),
               "quad_triangles": len(obj.data.loop_triangles),
               "quad_seconds": round(time.monotonic() - started, 2)})
bpy.ops.wm.save_as_mainfile(filepath=str(target / "voxel_quad_probe.blend"))
(target / "voxel_probe.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("QUAD_REPORT", report, flush=True)
