import json
from pathlib import Path
ROOT=Path(r'C:/Users/d.grab/Desktop/the-game');P=ROOT/'ART/ENEMIES/Forest_Bud';A=ROOT/'artifacts/forest-bud-production';L=ROOT/'razlom/Assets/Resources/Characters/Forest_Bud'
validation=json.loads((A/'validation.json').read_text(encoding='utf8'))
roundtrip=json.loads((A/'fbx_roundtrip.json').read_text(encoding='utf8'))
manifest={
 'revision':'2026-09-19-combat-production',
 'source_scene':'ART/ENEMIES/Forest_Bud/ForestBudRanged_Production.blend',
 'original_scene':'ART/ENEMIES/Forest_Bud/SourceOriginal/ForestBudRanged.blend',
 'build_recipe':'ART/ENEMIES/Forest_Bud/build_forest_bud.py',
 'rig':'Generic quadruped, in-place root, IK authoring baked to deform bones',
 'triangles':validation['triangles_character'],
 'mesh_objects':8,'bones_in_blend':48,'deform_bones':40,'fps':30,
 'spawn_sockets':[f'Spawn_Fruit_{i:02d}' for i in range(1,6)],
 'loaded_fruit_meshes':[f'SM_LoadedFruit_{i:02d}' for i in range(1,6)],
 'walk_root_equivalent_speed_mps':validation['walk_root_equivalent_speed'],
 'clips':{c['name']:{'frames':c['frames'],'duration_seconds':c['duration'],'loop':c['name'] in ['Idle','Walk'],'root_motion':False} for c in validation['clips']},
 'attack_shot_times_seconds':[.8,1,1.2,1.4,1.6],
 'projectile':'ProjectileFruit.fbx, sealed skin and green crown, pivot at mass center',
 'projectile_triangles':next(m['triangles'] for m in validation['meshes'] if m['name']=='SM_ProjectileFruit'),
 'export':{'format':'FBX 7400','axis_forward':'-Z','axis_up':'Y','scale':1,'deform_only':True,'sampling_step_frames':1,'simplify':0},
 'textures':'Existing 2K source textures preserved and packed into production Blender scene. Petal UVs exclude painted green patches.',
 'model_job_original':'3abba23e-4077-4830-b7de-55e34a343130',
 'notes':['Back opening rebuilt with skinned manifold surface; generated micro-flaps removed.','Petals rebuilt as thick watertight curved surfaces, three bend bones and smooth weights per petal.','Foot weights repaired; all clips sampled frame by frame against ground.','Fruit visual visibility is controlled by View from Sim shot events, not by bone scale.','Game.Sim owns aim, 0.2 second firing intervals, 2 second flight and impacts.'],
 'validation':{'fbx_roundtrip_pass':roundtrip['pass'],'max_roundtrip_bone_position_error_m':max(c['max_bone_position_error_m'] for c in roundtrip['clips']),'max_ground_penetration_m':max(0,-min(c['body_min_z'] for c in validation['clips']))}
}
for path in [P/'asset_manifest.json',L/'asset_manifest.json']:path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
validation['fbx_roundtrip']=roundtrip
for path in [P/'validation.json',L/'validation.json']:path.write_text(json.dumps(validation,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
print('Manifest and validation updated')
