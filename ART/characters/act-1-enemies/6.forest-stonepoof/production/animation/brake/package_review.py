from pathlib import Path
import json,subprocess,hashlib,ast
from PIL import Image,ImageDraw
import imageio_ffmpeg
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1];OUT=PROD/'review'/'animation_brake';RUN=PROD/'review'/'animation_charge_loop';ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
tree=ast.parse((HERE.parent/'charge_loop'/'package_review.py').read_text(encoding='utf8'))
for node in tree.body:
 if isinstance(node,ast.FunctionDef) and node.name=='encode':exec(compile(ast.Module(body=[node],type_ignores=[]),'encode','exec'))
for view in ('side','game'):
 brake=[OUT/(view+'_frames')/f'{i:04d}.png' for i in range(19)]
 transition=[RUN/('transition_'+view+'_frames')/f'{i:04d}.png' for i in range(36)]+[RUN/(view+'_frames')/f'{i%12:04d}.png' for i in range(24)]+brake
 for name,paths in [(view,brake),('transition_'+view,transition)]:
  for codec in ('webm','mp4'):encode(paths,OUT/(name+'.'+codec),codec)
 sheet=Image.new('RGB',(1440,810),'#202823');draw=ImageDraw.Draw(sheet)
 for i,f in enumerate([0,2,3,4,5,6,8,10,12,14,16,18]):
  im=Image.open(brake[f]).convert('RGB');im.thumbnail((360,240));x=(i%4)*360;y=(i//4)*270;sheet.paste(im,(x,y));draw.text((x+8,y+247),f'Frame {f} / 18   |   {f/30:.2f} s',fill='white')
 sheet.save(OUT/(view+'_keys.jpg'),quality=94)
mapping=[(0,3.125),(3,3.375),(7,3.625),(14,4),(18,4.375)]
ref=OUT/'reference_frames';ref.mkdir(exist_ok=True)
for f in range(19):
 for (a,ta),(b,tb) in zip(mapping,mapping[1:]):
  if a<=f<=b:source_time=ta+(tb-ta)*(f-a)/(b-a);break
 subprocess.run([ffmpeg,'-v','error','-y','-ss',str(source_time),'-i',str(PROD/'references'/'charge.mp4'),'-frames:v','1','-vf','crop=700:466:250:160,scale=960:640','-q:v','3',str(ref/f'{f:04d}.jpg')],check=True)
validation=json.loads((HERE/'validation.json').read_text());validation.pop('detail',None)
manifest={'stage':'brake_owner_review','owner_clip_approved':False,'previous_clip_approved':True,'previous_approval_quote':'ок идем дальше','fps':30,'frames':[0,18],'duration_seconds':.6,'preview_start_speed_m_s':12,'preview_distance_m':3.4,'preview_stop_frame':14,'transition_preview_frames':79,'reference_time_map':mapping,'reference_note':'Phase-aligned extract of accepted Higgsfield charge/braking reference, compressed to agreed gameplay duration. No synthetic inbetween reference frames.','editable_master':'../../animation/brake/Stonehoof_Brake_r01.blend','derived_clip':'../../animation/brake/Stonehoof_Brake_Baked_r01.blend','accepted_source_sha256':json.loads((HERE/'build.json').read_text())['source_sha256'],'master_sha256':hashlib.sha256((HERE/'Stonehoof_Brake_r01.blend').read_bytes()).hexdigest(),'model_triangles':20042,'validation':validation,'bake_validation':json.loads((HERE/'bake_validation.json').read_text()),'interpolation_validation':json.loads((HERE/'interpolation_validation.json').read_text()),'runtime_followup':'During integration: reserve braking distance inside arena; blend arbitrary incoming loop phase without delaying the stop or snapping. Only canonical phase-0 join is shown here.','browser_check':'file:// CUA policy denies automated browsing; local assets, decoding and JS syntax checked separately. Owner playback approval pending.'}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
print('Packaged 8 videos, 19 reference frames, 2 contact sheets, manifest.')
