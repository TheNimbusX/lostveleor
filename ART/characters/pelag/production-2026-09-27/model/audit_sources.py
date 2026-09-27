import bpy, json, os, hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path(r'C:/Users/d.grab/Desktop/the-game')
OUT=ROOT/'ART/characters/pelag/production-2026-09-27/model'
SOURCES={
 'body':'razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx',
 'saber':'razlom/Assets/Resources/Weapons/Pelag/FantasySaber/Pelag_FantasySaber.fbx',
 'anchor':'razlom/Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_AnchorHead.fbx',
 'grip':'razlom/Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_AnchorGrip.fbx',
 'link':'razlom/Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_ChainLink_Painted.fbx',
}
report={}
for key,p in SOURCES.items():
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.fbx(filepath=str(ROOT/p),use_anim=False)
 meshes=[]
 for o in bpy.data.objects:
  if o.type!='MESH':continue
  m=o.data;m.calc_loop_triangles();coords=[o.matrix_world@v.co for v in m.vertices]
  meshes.append({'name':o.name,'vertices':len(m.vertices),'tris':len(m.loop_triangles),'uvs':list(m.uv_layers.keys()),'materials':[x.name for x in m.materials],'dimensions':list(o.dimensions),'matrix': [list(r) for r in o.matrix_world], 'bounds':[[min(v[i] for v in coords) for i in range(3)],[max(v[i] for v in coords) for i in range(3)]], 'weights_max':max((len(v.groups) for v in m.vertices),default=0),'vertices_unweighted':sum(len(v.groups)==0 for v in m.vertices),'modifiers':[(x.name,x.type) for x in o.modifiers]})
 bones={}
 for o in bpy.data.objects:
  if o.type=='ARMATURE':
   bones[o.name]={'count':len(o.data.bones),'matrix':[list(r) for r in o.matrix_world], 'points':{b.name:[list(o.matrix_world@b.head_local),list(o.matrix_world@b.tail_local)] for b in o.data.bones}}
 report[key]={'path':p,'sha256':hashlib.sha256((ROOT/p).read_bytes()).hexdigest(),'meshes':meshes,'armatures':bones,'images':[{'name':x.name,'path':x.filepath,'size':list(x.size)} for x in bpy.data.images]}
(OUT/'source_audit.json').write_text(json.dumps(report,indent=2))
print('SOURCE_AUDIT',json.dumps({k:{'tris':sum(m['tris'] for m in v['meshes']),'meshes':[(m['name'],m['dimensions'],m['bounds']) for m in v['meshes']]} for k,v in report.items()}))
