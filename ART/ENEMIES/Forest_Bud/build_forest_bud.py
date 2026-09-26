"""Forest Bud: восстановленная спина, объёмный бутон и анимации для боя.

Клипы: Idle, Walk, Ranged_Attack, Death и короткий Hit (11 кадров, 0,33 с) —
реакция на попадание, начинается и кончается ровно в позе покоя.

Запуск через Blender MCP или CLI. Исходный пакет никогда не перезаписывается.
Кадры контактов и раскрытия задаются здесь, игровой урон остаётся в Sim.
"""
import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT=Path(r'C:/Users/d.grab/Desktop/the-game')
SOURCE=ROOT/'razlom/Assets/Resources/Characters/Forest_Bud'
PROD=ROOT/'ART/ENEMIES/Forest_Bud'
SOURCE_BLEND=PROD/'SourceOriginal/ForestBudRanged.blend'
OUT=ROOT/'artifacts/forest-bud-production'
PROD.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
if not globals().get('FOREST_SOURCE_LOADED',False):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
scene=bpy.context.scene;scene.render.fps=30;scene.unit_settings.system='METRIC'
rig=bpy.data.objects['ARM_ForestBudRanged'];body=bpy.data.objects['SM_Body']
if bpy.context.view_layer.objects.active and bpy.context.view_layer.objects.active.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
rig.animation_data_clear()
for b in rig.pose.bones:b.matrix_basis.identity()
for a in list(bpy.data.actions):bpy.data.actions.remove(a)
geo=bpy.data.collections.new('COL_ForestBud_Geo');scene.collection.children.link(geo)
rigcol=bpy.data.collections.new('COL_ForestBud_Rig');scene.collection.children.link(rigcol)
studio=bpy.data.collections.new('COL_ForestBud_Studio');scene.collection.children.link(studio)
def recollect(ob,col):
    for c in list(ob.users_collection):c.objects.unlink(ob)
    col.objects.link(ob)
recollect(body,geo);recollect(rig,rigcol)
for ob in list(bpy.data.objects):
    if ob.name.startswith('SM_') and ob!=body:bpy.data.objects.remove(ob,do_unlink=True)
    elif ob not in [body,rig]:recollect(ob,studio)

def solidmat(name,color,rough=.6):
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*color,1);bs.inputs['Roughness'].default_value=rough
    m.diffuse_color=(*color,1);m.use_backface_culling=True
    return m
leafmat=solidmat('MAT_ForestBud_Calyx',(0.21,.32,.055))
innermat=solidmat('MAT_ForestBud_PetalInner',(.38,.042,.043),.64)
rimmat=solidmat('MAT_ForestBud_CreamRim',(.88,.69,.36),.65)
cupmat=solidmat('MAT_ForestBud_Receptacle',(.23,.075,.037),.65)
stemmat=solidmat('MAT_ForestBud_FruitCrown',(.20,.30,.035),.6)
petalmat=bpy.data.materials['MAT_ForestBud_Petals.001'];petalmat.name='MAT_ForestBud_Petals';petalmat.use_backface_culling=True
fruitmat=bpy.data.materials['MAT_ForestBud_Fruit.001'];fruitmat.name='MAT_ForestBud_Fruit';fruitmat.use_backface_culling=True
fruitmat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.62
fruitmat.node_tree.nodes.get('Principled BSDF').inputs['Specular IOR Level'].default_value=.25
for im in bpy.data.images:
    if im.source!='FILE':continue
    filename=Path(im.filepath).name
    if (SOURCE/'Textures'/filename).exists():im.filepath=str(SOURCE/'Textures'/filename)

# Срез закрывается настоящими гранями; чашечка не служит ширмой для дырки.
body.data.materials.append(leafmat)
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
# Tripo оставила крошечные треугольные клапаны на тройных рёбрах.
bad=[f for f in bm.faces if f.calc_area()<.000001 and any(len(e.link_faces)>2 for e in f.edges)]
bmesh.ops.delete(bm,geom=bad,context='FACES')
unused={e for e in bm.edges if e.is_boundary};loops=[]
while unused:
    seed=unused.pop();edges={seed};stack=[seed]
    while stack:
        for v in stack.pop().verts:
            for e in v.link_edges:
                if e in unused:unused.remove(e);edges.add(e);stack.append(e)
    loops.append(edges)
big=max(loops,key=len);start=next(iter(big)).verts[0];ordered=[start];prev=None;cur=start
while True:
    e=next(e for e in cur.link_edges if e in big and e!=prev)
    nxt=e.other_vert(cur)
    if nxt==start:break
    ordered.append(nxt);prev=e;cur=nxt
    if len(ordered)>len(big):raise RuntimeError('Ветвящаяся граница спины')
center=sum((v.co for v in ordered),Vector())/len(ordered);center.z=.773
layer=bm.verts.layers.deform.verify();inner=[]
for v in ordered:
    co=center+(v.co-center)*.45;co.z=.772
    nv=bm.verts.new(co)
    for group,value in v[layer].items():nv[layer][group]=value
    inner.append(nv)
capcenter=bm.verts.new(center);capcenter[layer][body.vertex_groups['spine'].index]=.65;capcenter[layer][body.vertex_groups['bud'].index]=.35
filled=[]
for i,v in enumerate(ordered):
    j=(i+1)%len(ordered)
    filled.append(bm.faces.new((v,ordered[j],inner[j],inner[i])))
    filled.append(bm.faces.new((inner[i],inner[j],capcenter)))
filled+=bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0)['faces']
for f in filled:f.material_index=len(body.data.materials)-1
bmesh.ops.triangulate(bm,faces=filled,quad_method='BEAUTY',ngon_method='BEAUTY')
# На неплоском маленьком контуре beauty-triangulation может отбросить грань.
remaining={e for e in bm.edges if e.is_boundary}
while remaining:
    seed=remaining.pop();edges={seed};todo=[seed]
    while todo:
        for v in todo.pop().verts:
            for e in v.link_edges:
                if e in remaining:remaining.remove(e);edges.add(e);todo.append(e)
    points={v for e in edges for v in e.verts}
    middle=bm.verts.new(sum((v.co for v in points),Vector())/len(points))
    for v in points:
        for index,value in v[layer].items():middle[layer][index]=middle[layer].get(index,0)+value/len(points)
    for edge in edges:
        face=bm.faces.new((edge.verts[0],edge.verts[1],middle));face.material_index=len(body.data.materials)-1
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()

# Пальцы не могут наследовать движение головы/таза: старые автовеса
# назначали им до 65% spine, поэтому присед втягивал лапы в грунт.
def weight_smooth(x):x=max(0,min(1,x));return x*x*(3-2*x)
for v in body.data.vertices:
    z=v.co.z
    if z>=.34:continue
    leg=('L' if v.co.x>=0 else 'R')+('_Front' if v.co.y<.14 else '_Hind')
    amount=1-weight_smooth((z-.18)/.16)
    lower=rig.data.bones[leg+'_Lower'];knee=lower.head_local;ankle=lower.tail_local
    q=max(0,min(1,(z-ankle.z)/(knee.z-ankle.z)))
    center=ankle.lerp(knee,q);radial=math.hypot(v.co.x-center.x,v.co.y-center.y)
    mask=1-weight_smooth((radial-.13)/.095)
    amount*=1-weight_smooth((z-.11)/.07)*(1-mask)
    foot=1-weight_smooth((z-.105)/.145)
    old={g.group:g.weight*(1-amount) for g in v.groups}
    for name,value in [(leg+'_Foot',foot),(leg+'_Lower',1-foot)]:
        index=body.vertex_groups[name].index;old[index]=old.get(index,0)+value*amount
    chosen=sorted(old.items(),key=lambda item:-item[1])[:4];total=sum(value for _,value in chosen)
    for group in list(v.groups):body.vertex_groups[group.group].remove([v.index])
    for index,value in chosen:
        if value>.000001:body.vertex_groups[index].add([v.index],value/total,'REPLACE')

def skin(ob,weights):
    ob.parent=rig
    mod=ob.modifiers.new('ForestBud_Skin','ARMATURE');mod.object=rig
    groups={}
    for i,ww in enumerate(weights):
        for name,value in ww.items():
            if value<=.00001:continue
            g=groups.get(name)
            if g is None:g=ob.vertex_groups.new(name=name);groups[name]=g
            g.add([i],value,'REPLACE')

def meshob(name,verts,faces,uvs,mats,face_mats=None,weights=None):
    me=bpy.data.meshes.new(name+'_Mesh');me.from_pydata(verts,[],faces);me.update()
    ob=bpy.data.objects.new(name,me);geo.objects.link(ob)
    for m in mats:me.materials.append(m)
    if face_mats:
        for p,mi in zip(me.polygons,face_mats):p.material_index=mi
    uv=me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        p.use_smooth=True
        for li in p.loop_indices:uv.data[li].uv=uvs[me.loops[li].vertex_index]
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    if weights:skin(ob,weights)
    return ob

def smooth(x):x=max(0,min(1,x));return x*x*(3-2*x)
def ease(a,b,t):return smooth((t-a)/(b-a))
def interp(t,keys):
    for (ta,va),(tb,vb) in zip(keys,keys[1:]):
        if t<=tb:return va+(vb-va)*smooth((t-ta)/(tb-ta))
    return keys[-1][1]

CY=.055
def profile(t):
    # Округлая яйцевидная оболочка вместо плоских панелей старого пакета.
    r=.145+.205*math.sin(math.pi*t)-.135*t*t*t
    z=.747+.60*t
    return r,z
def point(t,ang):
    r,z=profile(t);return Vector((math.cos(ang)*r,CY+math.sin(ang)*r,z))

# Три последовательно изгибающиеся кости на лепесток.
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
for i in range(6):
    angle=-math.pi/2+i*math.tau/6
    names=[f'petal_{i+1:02d}',f'petal_{i+1:02d}_mid',f'petal_{i+1:02d}_tip']
    for j,(a,b) in enumerate([(0,.34),(.34,.69),(.69,1)]):
        bone=rig.data.edit_bones.get(names[j]) or rig.data.edit_bones.new(names[j]);bone.head=point(a,angle);bone.tail=point(b,angle)
        bone.parent=rig.data.edit_bones['bud' if j==0 else names[j-1]];bone.use_connect=j>0;bone.use_deform=True
for i in range(5):
    angle=-math.pi/2+i*math.tau/5;rad=.115
    bone=rig.data.edit_bones[f'Spawn_Fruit_{i+1:02d}'];bone.head=(math.cos(angle)*rad,CY+math.sin(angle)*rad,.897);bone.tail=bone.head+Vector((0,0,.095));bone.use_deform=True
bpy.ops.object.mode_set(mode='OBJECT')

def petalweights(t,i):
    names=[f'petal_{i+1:02d}',f'petal_{i+1:02d}_mid',f'petal_{i+1:02d}_tip']
    if t<.19:return {names[0]:1}
    if t<.49:
        q=smooth((t-.19)/.30);return {names[0]:1-q,names[1]:q}
    if t<.56:return {names[1]:1}
    if t<.86:
        q=smooth((t-.56)/.30);return {names[1]:1-q,names[2]:q}
    return {names[2]:1}

for i in range(6):
    theta=-math.pi/2+i*math.tau/6
    verts=[];uvs=[];faces=[];fm=[];weights=[];NT=20;NU=10
    for layer in range(2):
        for it in range(NT+1):
            t=it/NT;r,z=profile(t)
            half=math.radians(36)*(0.92+.08*math.sin(math.pi*t))
            for iu in range(NU+1):
                u=0 if iu==0 else 1 if iu==NU else .025+.95*(iu-1)/(NU-2)
                v=2*u-1;ang=theta+v*half
                thickness=.013+.017*math.sin(math.pi*t)
                rr=r+(.002 if i%2 else .008)+(.5 if layer==0 else -.5)*thickness
                # Центр лепестка выпуклый, края мягко подбираются внутрь.
                rr+=.014*(1-v*v)*math.sin(math.pi*t)
                verts.append((math.cos(ang)*rr,CY+math.sin(ang)*rr,z+.004*(i%2)))
                uvs.append((.06+.88*u,.37+.59*t));weights.append(petalweights(t,i))
    side=(NT+1)*(NU+1)
    def at(layer,it,iu):return layer*side+it*(NU+1)+iu
    for layer in range(2):
        for it in range(NT):
            for iu in range(NU):
                face=(at(layer,it,iu),at(layer,it,iu+1),at(layer,it+1,iu+1),at(layer,it+1,iu))
                faces.append(face if layer==0 else tuple(reversed(face)))
                fm.append(2 if iu in [0,NU-1] or it==NT-1 else layer)
    for it in range(NT):
        for iu in [0,NU]:faces.append((at(0,it,iu),at(1,it,iu),at(1,it+1,iu),at(0,it+1,iu)));fm.append(2)
    for iu in range(NU):
        for it in [0,NT]:faces.append((at(0,it,iu),at(0,it,iu+1),at(1,it,iu+1),at(1,it,iu)));fm.append(2)
    meshob(f'SM_Petal_{i+1:02d}',verts,faces,uvs,[petalmat,innermat,rimmat],fm,weights)

def ellipsoid(name,center,scale,mat,bone=None,nlon=32,nlat=12,lobes=0):
    vs=[(center[0],center[1],center[2]-scale[2])];uv=[(.5,0)];fs=[]
    for j in range(1,nlat):
        lat=-math.pi/2+math.pi*j/nlat
        for k in range(nlon):
            lon=math.tau*k/nlon;rr=math.cos(lat)*(1+lobes*math.cos(lon*5)*math.cos(lat)**2)
            vs.append((center[0]+scale[0]*rr*math.cos(lon),center[1]+scale[1]*rr*math.sin(lon),center[2]+scale[2]*math.sin(lat)));uv.append((k/nlon,j/nlat))
    top=len(vs);vs.append((center[0],center[1],center[2]+scale[2]));uv.append((.5,1))
    for k in range(nlon):fs.append((0,1+(k+1)%nlon,1+k))
    for j in range(nlat-2):
        for k in range(nlon):
            a=1+j*nlon+k;b=1+j*nlon+(k+1)%nlon;c=b+nlon;d=a+nlon;fs.append((a,b,c,d))
    start=1+(nlat-2)*nlon
    for k in range(nlon):fs.append((start+k,start+(k+1)%nlon,top))
    return meshob(name,vs,fs,uv,[mat],weights=[{bone:1} for _ in vs] if bone else None)

ellipsoid('SM_CalyxCup',(0,CY,.735),(.285,.31,.073),leafmat,'bud',40,10)
ellipsoid('SM_Receptacle',(0,CY,.811),(.237,.237,.052),cupmat,'bud',40,10)
# Чашелистики цельные, их утолщённые кончики поддерживают бутон снизу.
for i in range(8):
    ang=math.tau*i/8;vs=[];uv=[];fs=[];n=10;w=6
    for side in range(2):
        for j in range(n+1):
            t=j/n;rad=.17+.23*t;width=.012+.082*math.sin(math.pi*t)**.8
            for k in range(w+1):
                u=k/w;across=(u*2-1)*width;along=rad
                z=.749+.04*math.sin(math.pi*t)+.026*t+.02*(1-(u*2-1)**2)*math.sin(math.pi*t)+(.006 if side==0 else -.006)
                vs.append((math.cos(ang)*along-math.sin(ang)*across,CY+math.sin(ang)*along+math.cos(ang)*across,z));uv.append((u,t))
    count=(n+1)*(w+1)
    def at(s,j,k):return s*count+j*(w+1)+k
    for s in range(2):
        for j in range(n):
            for k in range(w):fs.append((at(s,j,k),at(s,j,k+1),at(s,j+1,k+1),at(s,j+1,k)))
    for j in range(n):
        for k in [0,w]:fs.append((at(0,j,k),at(1,j,k),at(1,j+1,k),at(0,j+1,k)))
    for k in range(w):
        for j in [0,n]:fs.append((at(0,j,k),at(0,j,k+1),at(1,j,k+1),at(1,j,k)))
    meshob(f'SM_CalyxLeaf_{i+1:02d}',vs,fs,uv,[leafmat],weights=[{'bud':1} for v in vs])

def joinparts(parts,name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts:ob.hide_set(False);ob.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();parts[0].name=name
    return parts[0]
def crownfruit(ob,center,bone):
    vs=[];uv=[];fs=[]
    for side in range(2):
        for i in range(10):
            angle=math.tau*i/10;r=.034 if i%2==0 else .013
            vs.append((center[0]+r*math.cos(angle),center[1]+r*math.sin(angle),center[2]+.083+(.006 if side==0 else -.003)+(0 if i%2==0 else .010)));uv.append((.5+.45*math.cos(angle),.5+.45*math.sin(angle)))
    vs.extend([(center[0],center[1],center[2]+.102),(center[0],center[1],center[2]+.087)]);uv.extend([(.5,.5),(.5,.5)])
    for i in range(10):
        j=(i+1)%10;fs.extend([(20,i,j),(21,10+j,10+i),(i,i+10,j+10,j)])
    cr=meshob(ob.name+'_Crown',vs,fs,uv,[stemmat],weights=[{bone:1} for v in vs] if bone else None)
    return joinparts([ob,cr],ob.name)

for i in range(5):
    bone=rig.data.bones[f'Spawn_Fruit_{i+1:02d}'];center=bone.head_local
    fruit=ellipsoid(f'SM_LoadedFruit_{i+1:02d}',center,(.073,.073,.092),fruitmat,bone.name,20,10,.085)
    crownfruit(fruit,center,bone.name)
# Один замкнутый плод для пула снарядов. Начало координат в центре массы.
projectile=ellipsoid('SM_ProjectileFruit',(0,0,0),(.073,.073,.092),fruitmat,None,24,12,.085)
crownfruit(projectile,(0,0,0),None)
projectile.hide_render=True;projectile.hide_set(True)
joinparts([bpy.data.objects[f'SM_Petal_{i+1:02d}'] for i in range(6)],'SM_BudPetals')
joinparts([bpy.data.objects['SM_CalyxCup'],bpy.data.objects['SM_Receptacle']]+[bpy.data.objects[f'SM_CalyxLeaf_{i+1:02d}'] for i in range(8)],'SM_BudBase')

# Анимации: IK удерживает лапы, вторичное движение не меняет корень.
for pb in rig.pose.bones:pb.rotation_mode='QUATERNION'
def qworld(name,axis,angle):
    bone=rig.data.bones[name];localaxis=bone.matrix_local.to_3x3().inverted()@Vector(axis)
    return Quaternion(localaxis,angle)
def locworld(name,offset):return rig.data.bones[name].matrix_local.to_3x3().inverted()@Vector(offset)
def reset():
    for pb in rig.pose.bones:pb.location=(0,0,0);pb.rotation_quaternion=(1,0,0,0);pb.scale=(1,1,1)
def rotation(name,x=0,y=0,z=0):
    rig.pose.bones[name].rotation_quaternion=qworld(name,(1,0,0),x)@qworld(name,(0,1,0),y)@qworld(name,(0,0,1),z)
def offset(name,x=0,y=0,z=0):rig.pose.bones[name].location=locworld(name,(x,y,z))
def petals(opening,t,death=0):
    for i in range(6):
        theta=-math.pi/2+i*math.tau/6;axis=(-math.sin(theta),math.cos(theta),0)
        for j,suffix in enumerate(['','_mid','_tip']):
            name=f'petal_{i+1:02d}'+suffix
            delay=i*.012+j*.022
            if isinstance(opening,float):amount=opening
            else:amount=opening(t-delay)
            angle=amount*[.90,.65,.22][j]+math.sin(t*2.1+i*.83-j*.6)*.007*(1-.7*amount)+death*[.07,.16,.25][j]
            rig.pose.bones[name].rotation_quaternion=qworld(name,axis,angle)
def put_keys(frame,scale=False):
    for pb in rig.pose.bones:
        pb.keyframe_insert(data_path='location',frame=frame,group=pb.name)
        pb.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=pb.name)
        if scale:pb.keyframe_insert(data_path='scale',frame=frame,group=pb.name)

rig.animation_data_create()
clip_frames={'Idle':91,'Walk':33,'Ranged_Attack':67,'Death':37,'Hit':11}
for clip,endframe in clip_frames.items():
    action=bpy.data.actions.new(clip);rig.animation_data.action=action
    for frame in range(1,endframe+1):
        t=(frame-1)/30;reset()
        if clip=='Idle':
            p=math.tau*t/3
            offset('pelvis',z=.0055*math.sin(p));rotation('spine',x=.008*math.sin(p-.3),y=.007*math.sin(p))
            rotation('head',x=-.006*math.sin(p-.3),z=.016*math.sin(p));rotation('bud',x=.008*math.sin(p-.7),y=.009*math.sin(p-.4))
            petals(0.0,t)
        elif clip=='Walk':
            p=t/(32/30);wave=math.tau*p
            offset('pelvis',y=.013*math.sin(wave*2),z=-.07+.01*math.cos(wave*2));rotation('pelvis',y=.025*math.sin(wave))
            rotation('spine',x=.018*math.sin(wave*2+.2),y=-.018*math.sin(wave));rotation('head',x=-.02*math.sin(wave*2+.2));rotation('bud',x=.025*math.sin(wave*2-.4),y=.017*math.sin(wave-.5))
            for leg,phase in [('L_Front',0),('R_Hind',.25),('R_Front',.5),('L_Hind',.75)]:
                q=(p+phase)%1;stance=.56
                hip=rig.data.bones[leg+'_Upper'].head_local
                foot=rig.data.bones['CTRL_'+leg].head_local
                if q<stance:
                    y=hip.y-.27+.54*q/stance;z=0;roll=0
                else:
                    u=(q-stance)/(1-stance);y=hip.y+.27-.54*smooth(u);z=.065*math.sin(math.pi*u)**1.3;roll=.10*math.sin(math.pi*u)
                offset('CTRL_'+leg,y=y-foot.y,z=z);rotation('CTRL_'+leg,x=roll)
            petals(0.0,t)
        elif clip=='Ranged_Attack':
            crouch=interp(t,[(0,0),(.16,.025),(.52,.13),(.66,.135),(.79,.063),(1.60,.055),(1.79,.085),(2.2,0)])
            recoil=0
            for shot in [.8,1,1.2,1.4,1.6]:
                dt=t-shot
                if 0<=dt<.18:recoil+=.019*math.sin(math.pi*dt/.18)*math.exp(-dt*7)
            offset('pelvis',y=.024*ease(0,.55,t)*(1-ease(1.65,2.2,t)),z=-crouch-recoil)
            lean=.075*ease(.1,.52,t)*(1-ease(1.7,2.2,t))
            rotation('pelvis',x=lean*.3);rotation('spine',x=lean);rotation('head',x=-lean*.7)
            rotation('bud',x=-.07*ease(.12,.62,t)*(1-ease(1.65,2.15,t))+recoil*1.8,y=.014*math.sin(t*5)*ease(.6,.8,t)*(1-ease(1.7,2.2,t)))
            opening=lambda time:ease(.40,.775,time)*(1-ease(1.68,2.16,time))
            petals(opening,t)
        elif clip=='Hit':
            # Удар спереди: корпус отбрасывает назад, бутон по инерции клюёт вперёд
            # и сплющивается, лепестки на миг распахиваются и прихлопываются.
            # Кадры 1 и 11 — ровно поза покоя: View накладывает клип на любую фазу.
            k=frame-1
            r=[0,.65,1,.85,.5,.18,-.04,-.08,-.04,-.01,0][k]
            s=[0,.3,.75,1,.8,.4,.05,-.18,-.12,-.03,0][k]
            h=[0,.2,.65,1,.75,.25,-.12,-.15,-.06,0,0][k]
            w=[0,.5,1,.6,-.2,-.6,-.45,-.1,.12,.08,0][k]
            offset('pelvis',y=.035*r,z=-.065*s);rotation('pelvis',x=-.05*r)
            rotation('spine',x=-.10*r+.06*s);rotation('head',x=-.16*h+.05*s)
            rotation('bud',x=.12*w,y=.03*w)
            # Масштаб по оси кости бутона (мировая Z): сплющивание без потери объёма.
            rig.pose.bones['bud'].scale=(1+.06*s,1-.11*s,1+.06*s)
            flinch=[(n/30,.26*v) for n,v in enumerate([0,.55,1,.8,.22,0,0,.03,.03,.01,0])]
            for i in range(6):
                theta=-math.pi/2+i*math.tau/6;axis=(-math.sin(theta),math.cos(theta),0)
                for j,suffix in enumerate(['','_mid','_tip']):
                    name=f'petal_{i+1:02d}'+suffix
                    # Короткая волна от основания к кончику; окно гасит запаздывание к кадру 11.
                    # Внутрь лепестки не заходят: разнобой соседей открывал бы тёмную щель на макушке.
                    amount=interp(t-i*.003-j*.012,flinch)*(1-ease(8/30,10/30,t))
                    rig.pose.bones[name].rotation_quaternion=qworld(name,axis,amount*[.90,.65,.22][j])
        else:
            fall=ease(.08,.95,t);front=ease(.06,.65,t)
            offset('pelvis',y=-.035*fall,z=-.175*fall);rotation('pelvis',x=-.045*fall,y=.045*fall)
            rotation('spine',x=.17*front,y=-.023*fall);rotation('head',x=.04*front)
            rotation('bud',x=.11*ease(.24,1.08,t),y=.09*ease(.35,1.12,t))
            for leg in ['L_Front','R_Front','L_Hind','R_Hind']:
                offset('CTRL_'+leg,x=(.032 if leg.startswith('L') else -.032)*fall,y=(-.035 if 'Front' in leg else .025)*fall)
            petals(0.03,t,fall)
        put_keys(frame,scale=clip=='Hit')
    # Точные замыкания исключают фазовый шов вторичного движения.
    if clip in ['Idle','Walk','Ranged_Attack']:
        scene.frame_set(1)
        for pb in rig.pose.bones:
            pb.keyframe_insert(data_path='location',frame=endframe,group=pb.name)
            pb.keyframe_insert(data_path='rotation_quaternion',frame=endframe,group=pb.name)
    if clip=='Death':
        scene.frame_set(33)
        for frame in range(34,38):put_keys(frame)
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for key in fc.keyframe_points:key.interpolation='BEZIER';key.handle_left_type='AUTO_CLAMPED';key.handle_right_type='AUTO_CLAMPED'
    action.use_fake_user=True

rig.animation_data.action=bpy.data.actions['Idle'];rig.animation_data.action_slot=bpy.data.actions['Idle'].slots[0]
scene.frame_start=1;scene.frame_end=91;scene.frame_set(1)
for act in bpy.data.actions:
    if act.name in clip_frames:
        tr=rig.animation_data.nla_tracks.new();tr.name=act.name;tr.strips.new(act.name,1,act);tr.mute=True
rig['walk_root_equivalent_speed']=.54/(.56*(32/30))
rig['attack_shots_seconds']=[.8,1,1.2,1.4,1.6]
rig['hit_seconds']=10/30
rig['authoring']='Root stays at origin. Generic rig. Petals have three blended bend joints. Sim owns impacts.'

# Источник сохраняется вне Assets, чтобы Unity не импортировала старую .blend-сцену.
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(PROD/'ForestBudRanged_Production.blend'))
print('FOREST_BUD_PRODUCTION_READY')
