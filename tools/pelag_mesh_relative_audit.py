"""Compare animated mesh/foot heights relative to Pelag hips for source and exports."""
from __future__ import annotations
import json
from pathlib import Path
import bpy

PROJECT = Path(r"C:\Users\d.grab\Desktop\the-game")
FILES = [
    PROJECT / "razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_MX_AnchorAttack.fbx",
    PROJECT / "razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_MX_Idle.fbx",
    PROJECT / "razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_MX_Run.fbx",
    *sorted((PROJECT / "artifacts/pelag-animation-build/exports").glob("Pelag_MX_*.fbx")),
]
OUT = PROJECT / "artifacts/pelag-animation-build/mesh-relative-audit.json"

def clear():
    for obj in list(bpy.data.objects): bpy.data.objects.remove(obj, do_unlink=True)

def run(path):
    clear()
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    act = arm.animation_data.action
    start,end=map(round,act.frame_range)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    deps=bpy.context.evaluated_depsgraph_get()
    rows=[]
    for f in range(start,end+1):
        bpy.context.scene.frame_set(f); bpy.context.view_layer.update()
        hips=arm.matrix_world @ arm.pose.bones['mixamorig:Hips'].head
        foot=[]
        for n in ('mixamorig:LeftFoot','mixamorig:LeftToeBase','mixamorig:RightFoot','mixamorig:RightToeBase'):
            b=arm.pose.bones.get(n)
            if b: foot.append(float((arm.matrix_world @ b.head).z))
        mn=1e9; mx=-1e9; count=0
        for obj in meshes:
            ev=obj.evaluated_get(deps); me=ev.to_mesh()
            try:
                for v in me.vertices:
                    z=float((ev.matrix_world @ v.co).z); mn=min(mn,z); mx=max(mx,z); count+=1
            finally: ev.to_mesh_clear()
        rows.append({'frame':f,'hips_z':float(hips.z),'feet_min_z':min(foot),'mesh_min_z':mn,'mesh_max_z':mx,'mesh_minus_hips':mn-float(hips.z),'feet_minus_hips':min(foot)-float(hips.z),'vertices':count})
    return {'file':str(path),'frames':[start,end],'fps':bpy.context.scene.render.fps,'min_mesh_z':min(r['mesh_min_z'] for r in rows),'min_mesh_minus_hips':min(r['mesh_minus_hips'] for r in rows),'min_feet_minus_hips':min(r['feet_minus_hips'] for r in rows),'rows':rows}

payload=[run(p) for p in FILES if p.is_file()]
OUT.write_text(json.dumps(payload,indent=2),encoding='utf-8')
for item in payload:
    print(Path(item['file']).name, 'minmesh', item['min_mesh_z'], 'rel', item['min_mesh_minus_hips'], 'feetrel', item['min_feet_minus_hips'])
