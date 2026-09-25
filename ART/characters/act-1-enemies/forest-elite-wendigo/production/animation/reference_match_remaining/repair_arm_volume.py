"""Сохраняем объём древесных предплечий при LBS в Unity, локализуя изгиб возле суставов."""
import bpy,json
from mathutils import Vector
from pathlib import Path
r=bpy.data.objects['ARM_ForestWendigo'];m=bpy.data.objects['SM_ForestWendigo_LOD0'];changed={}
def smooth(v):v=max(0,min(1,v));return v*v*(3-2*v)
for side in ['L','R']:
 names=[side+n for n in ['_clavicle','_arm_upper','_arm_lower','_hand']]
 groups=[m.vertex_groups[n] for n in names];gis={g.index for g in groups};bones=[r.data.bones[n] for n in names]
 joints=[bones[i].head_local for i in range(1,4)]
 dirs=[]
 for i in range(1,4):dirs.append(((bones[i-1].tail_local-bones[i-1].head_local).normalized()+(bones[i].tail_local-bones[i].head_local).normalized()).normalized())
 count=0
 for vertex in m.data.vertices:
  old={g.group:g.weight for g in vertex.groups};total=sum(w for gi,w in old.items() if gi in gis)
  if total<.85:continue
  s=[smooth(.5+(vertex.co-j).dot(d)/.15) for j,d in zip(joints,dirs)]
  weights=[1-s[0],s[0]*(1-s[1]),s[0]*s[1]*(1-s[2]),s[0]*s[1]*s[2]]
  for g in groups:g.remove([vertex.index])
  for g,w in zip(groups,weights):
   if w>1e-6:g.add([vertex.index],w*total,'REPLACE')
  count+=1
 changed[side]=count
result={'vertices_reweighted':changed,'method':'rigid shaft; 15 cm joint blends; Unity linear skinning'}
