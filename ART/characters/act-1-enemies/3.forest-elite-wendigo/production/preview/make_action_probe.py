"""Create a throwaway Blender 5.2 layered-action fixture for review-script QA."""

import sys
from pathlib import Path

import bpy

output = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
obj = max((obj for obj in bpy.context.scene.objects if obj.type == "MESH"),
          key=lambda item: len(item.data.vertices))
obj.location = (0, 0, 0)
obj.keyframe_insert(data_path="location", frame=0)
obj.location = (0.12, 0, 0)
obj.keyframe_insert(data_path="location", frame=10)
obj.location = (0, 0, 0)
obj.keyframe_insert(data_path="location", frame=20)
obj.animation_data.action.name = "AN_ForestWendigo_Probe"
bpy.ops.wm.save_as_mainfile(filepath=str(output))
print("PROBE_ACTION_SLOTS", [(slot.identifier, slot.target_id_type)
                             for slot in obj.animation_data.action.slots], flush=True)
