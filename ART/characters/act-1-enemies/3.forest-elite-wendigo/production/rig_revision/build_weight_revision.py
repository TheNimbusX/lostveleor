"""Make an isolated Wendigo rig variant with cleaned wrist/neck weights.

Uses the approved mesh and armature, leaves source untouched, adds no geometry.
Run Blender 5.2 in background with ForestWendigo_Rig.blend open.
"""

import json
from pathlib import Path

import bpy


OUT = Path(__file__).resolve().parent
mesh = bpy.data.objects["SM_ForestWendigo_LOD0"]
rig = bpy.data.objects["ARM_ForestWendigo"]
mesh.data.calc_loop_triangles()
triangles = len(mesh.data.loop_triangles)
if triangles > 25000:
    raise RuntimeError(f"Source exceeds budget: {triangles}")

names = {group.index: group.name for group in mesh.vertex_groups}
def read_weights(vertex):
    return {names[g.group]: g.weight for g in vertex.groups if g.weight > 1e-6}

weights = [read_weights(v) for v in mesh.data.vertices]
verts = mesh.data.vertices
neighbors = [set() for _ in verts]
for edge in mesh.data.edges:
    a, b = edge.vertices
    if (verts[a].co - verts[b].co).length < .09:
        neighbors[a].add(b)
        neighbors[b].add(a)

regions = {"R_wrist": set(), "L_wrist": set(), "head_neck": set()}
for v in verts:
    co = v.co
    w = weights[v.index]
    if co.x > .05 and co.y < -.48 and .56 < co.z < 1.20 and w.get("R_hand",0) > .08:
        regions["R_wrist"].add(v.index)
    if co.x < -.05 and co.y > .48 and .56 < co.z < 1.20 and w.get("L_hand",0) > .08:
        regions["L_wrist"].add(v.index)
    if abs(co.x) < .48 and -.62 < co.y < .44 and 2.20 < co.z < 2.66 and (w.get("head",0)+w.get("neck",0)) > .08:
        regions["head_neck"].add(v.index)

allowed = {
    "R_wrist":{"R_hand","R_arm_lower","R_arm_upper"},
    "L_wrist":{"L_hand","L_arm_lower","L_arm_upper"},
    "head_neck":{"head","neck","spine_02"},
}

for region, ids in regions.items():
    for i in ids:
        weights[i] = {name: weight for name, weight in weights[i].items() if name in allowed[region]}
        if not weights[i]:
            raise RuntimeError(f"Unweighted vertex after cleanup: {i} {region}")
        total = sum(weights[i].values())
        weights[i] = {name: weight / total for name, weight in weights[i].items()}

# Local Laplacian in the deform transition. The more distant rigid claws and
# antlers remain untouched so their sculpted silhouettes do not rubber-bend.
for region, ids in regions.items():
    for _ in range(4):
        previous = [{**w} for w in weights]
        for i in ids:
            local_neighbors = [j for j in neighbors[i] if j in ids]
            if len(local_neighbors) < 2:
                continue
            current = previous[i]
            avg = {name: sum(previous[j].get(name,0) for j in local_neighbors)/len(local_neighbors)
                   for name in allowed[region]}
            blend = .48
            mixed = {name: (1-blend)*current.get(name,0)+blend*avg.get(name,0)
                     for name in allowed[region]}
            mixed = {name:value for name,value in mixed.items() if value > 1e-4}
            total = sum(mixed.values())
            weights[i] = {name:value/total for name,value in mixed.items()}

touched = set().union(*regions.values())
for i in touched:
    for group in mesh.vertex_groups:
        group.remove([i])
    ordered = sorted(weights[i].items(),key=lambda kv:kv[1],reverse=True)[:4]
    total = sum(value for _,value in ordered)
    for name,value in ordered:
        mesh.vertex_groups[name].add([i],value/total,"REPLACE")

# Every source vertex already passed QA; independently verify the revision.
deform = {bone.name for bone in rig.data.bones if bone.use_deform}
unweighted = over_four = unnormalized = 0
for v in mesh.data.vertices:
    active = [g.weight for g in v.groups if names[g.group] in deform and g.weight > 1e-5]
    unweighted += not active
    over_four += len(active) > 4
    unnormalized += bool(active) and abs(sum(active)-1) > .005
if any((unweighted,over_four,unnormalized)):
    raise RuntimeError((unweighted,over_four,unnormalized))

output = OUT / "ForestWendigo_Rig_WeightRevision_v2.blend"
bpy.ops.wm.save_as_mainfile(filepath=str(output))
report = {"source":"ForestWendigo_Rig.blend","output":str(output),"triangles":triangles,
          "bones":len(rig.data.bones),"touched_vertices":len(touched),
          "region_vertices":{key:len(value) for key,value in regions.items()},
          "unweighted":unweighted,"over_four":over_four,"unnormalized":unnormalized}
(OUT / "weight_revision_v2_report.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print("WEIGHT_REVISION",json.dumps(report),flush=True)
