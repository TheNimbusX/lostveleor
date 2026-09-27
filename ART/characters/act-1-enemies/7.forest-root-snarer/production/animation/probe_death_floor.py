"""Probe the death floor snap (nothing keyed or saved).
blender -b ForestRootSnarer_Anim_r01.blend -P probe_death_floor.py"""
import sys
from pathlib import Path
import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, clip_death  # noqa: E402

rig = anim_core.Rig(bpy.data.objects[anim_core.ARM_NAME], bpy.data.objects[anim_core.MESH_NAME])
clip_death.prepare(rig)
print("INFO", clip_death.INFO, flush=True)
chest, face = clip_death._masks(rig)
for f in (25, 28, 32, 45):
    rig.apply(clip_death.death_pose(f))
    co = rig.mesh_co()
    print(f"f={f} chest_min_z={co[chest][:, 2].min()*1000:.1f}mm face_min_z={co[face][:, 2].min()*1000:.1f}mm", flush=True)
