from pathlib import Path
import json,subprocess,hashlib,ast,shutil
from PIL import Image,ImageDraw
import imageio_ffmpeg
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1];OUT=PROD/'review'/'animation_collision'
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
tree=ast.parse((HERE.parent/'charge_loop'/'package_review.py').read_text(encoding='utf8'))
for node in tree.body:
    if isinstance(node,ast.FunctionDef) and node.name=='encode':
        exec(compile(ast.Module(body=[node],type_ignores=[]),'encode','exec'))
for view in ('side','game'):
    frames=[OUT/(view+'_frames')/f'{i:04d}.png' for i in range(49)]
    for name,paths in [(view,frames),('impact_'+view,frames[12:])]:
        for codec in ('webm','mp4'):encode(paths,OUT/(name+'.'+codec),codec)
    sheet=Image.new('RGB',(1440,810),'#202823');draw=ImageDraw.Draw(sheet)
    for i,f in enumerate([8,10,12,13,14,16,19,22,26,34,42,48]):
        im=Image.open(frames[f]).convert('RGB');im.thumbnail((360,240))
        x=(i%4)*360;y=(i//4)*270;sheet.paste(im,(x,y))
        draw.text((x+8,y+247),f'Frame {f} / 48 | impact {(f-12)/30:+.2f}s',fill='white')
    sheet.save(OUT/(view+'_keys.jpg'),quality=94)
mapping=[(0,0),(8,1/3),(12,.5),(16,.75),(22,1.4),(32,2.3),(40,3.2),(48,3.95)]
ref=OUT/'reference_frames';ref.mkdir(exist_ok=True)
for f in range(49):
    for (a,ta),(b,tb) in zip(mapping,mapping[1:]):
        if a<=f<=b:
            source_time=ta+(tb-ta)*(f-a)/(b-a);break
    subprocess.run([ffmpeg,'-v','error','-y','-ss',str(source_time),'-i',str(PROD/'references'/'collision.mp4'),'-frames:v','1','-vf','scale=960:540,pad=960:640:0:50:color=0x87928b','-q:v','3',str(ref/f'{f:04d}.jpg')],check=True)
validation=json.loads((HERE/'validation.json').read_text());validation.pop('detail',None)
manifest={
    'stage':'collision_owner_review','owner_clip_approved':False,
    'previous_clip_approved':True,'previous_approval_quote':'ок идем дальше',
    'fps':30,'review_frames':[0,48],'impact_frame':12,'stun_frames':36,'stun_seconds':1.2,
    'runtime_clips':{'AN_Stonehoof_WallBrace':{'source_frames':[8,12],'duration':4/30},'AN_Stonehoof_WallImpact':{'source_frames':[12,48],'duration':1.2}},
    'reference_time_map':mapping,'reference_note':'Phase-aligned accepted Higgsfield reference. Recoil reduced and hoof support made earlier; anatomy and gameplay 1.2 s preserved. Original timing available in full reference.',
    'upper_camera_note':'Elevated rear three-quarter view, rotated so the review obstacle does not occlude the head. This is a Blender review, not in-game evidence.',
    'editable_master':'../../animation/collision/Stonehoof_Collision_r01.blend',
    'derived_clip':'../../animation/collision/Stonehoof_Collision_Baked_r01.blend',
    'accepted_source_sha256':json.loads((HERE/'build.json').read_text())['source_sha256'],
    'master_sha256':hashlib.sha256((HERE/'Stonehoof_Collision_r01.blend').read_bytes()).hexdigest(),
    'model_triangles':20042,'validation':validation,
    'bake_validation':json.loads((HERE/'bake_validation.json').read_text()),
    'interpolation_validation':json.loads((HERE/'interpolation_validation.json').read_text()),
    'runtime_validation':json.loads((HERE/'runtime_validation.json').read_text()),
    'runtime_followup':'Blend arbitrary incoming gait phase at the actual Sim collision without delaying impact. WallBrace only within approach time. Preview carrier and obstacle are not gameplay assets.',
    'browser_check':'Automated file:// browsing unavailable by CUA policy. Local assets, decoding, player logic and syntax checked separately; owner playback and art approval pending.'
}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
shutil.copyfile(PROD/'review'/'animation_brake'/'review.css',OUT/'review.css')
print('Packaged 8 videos, 49 reference frames, 2 pose sheets and manifest.')
