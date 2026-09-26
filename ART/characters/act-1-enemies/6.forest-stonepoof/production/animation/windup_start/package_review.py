from pathlib import Path
import json,subprocess,hashlib
from PIL import Image,ImageDraw
import imageio_ffmpeg
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1];OUT=PROD/'review'/'animation_windup_start'
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
for view in ('side','game'):
    folder=OUT/(view+'_frames');frames=sorted(folder.glob('*.png'));assert len(frames)==37
    common=[ffmpeg,'-v','error','-y','-framerate','30','-start_number','0','-i',str(folder/'%04d.png'),'-an']
    subprocess.run(common+['-c:v','libvpx-vp9','-crf','24','-b:v','0','-row-mt','1','-cpu-used','4','-pix_fmt','yuv420p',str(OUT/(view+'.webm'))],check=True)
    subprocess.run(common+['-c:v','libx264','-profile:v','baseline','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/(view+'.mp4'))],check=True)
    chosen=[0,6,9,12,17,18,21,27,30,32,34,36]
    sheet=Image.new('RGB',(1440,810),'#202823');draw=ImageDraw.Draw(sheet)
    for i,f in enumerate(chosen):
        im=Image.open(folder/f'{f:04d}.png').convert('RGB');im.thumbnail((360,240));x=(i%4)*360;y=(i//4)*270;sheet.paste(im,(x,y));draw.text((x+8,y+247),f'Frame {f} / 36   |   {f/30:.2f} s',fill='white')
    sheet.save(OUT/(view+'_keys.jpg'),quality=94)

# Extract directly from the untouched video at mapped source times. Same 37
# review frames, no invented intermediate motion. Crop merely enlarges the boar.
ref=OUT/'reference_frames';ref.mkdir(exist_ok=True)
for f in range(37):
    source_time=f/30*1.5 if f<=30 else 1.5+(f-30)/30*(.333333/.2)
    subprocess.run([ffmpeg,'-v','error','-y','-ss',str(source_time),'-i',str(PROD/'references'/'charge.mp4'),'-frames:v','1','-vf','scale=960:540,crop=520:360:410:105,scale=960:640','-q:v','3',str(ref/f'{f:04d}.jpg')],check=True)
manifest={'stage':'windup_start_owner_review','references_approved':True,'owner_clip_approved':False,'fps':30,'frames':37,'duration_seconds':1.2,'windup_seconds':1,'launch_seconds':.2,'reference_time_map':[[0,0],[1,1.5],[1.2,1.833333]],'reference_note':'Reference preparation lacks the two scrapes; they are authored according to the agreed attack design. Phase-aligned comparison, not equal absolute timings.','editable_master':'../../animation/windup_start/Stonehoof_WindupStart_r01.blend','derived_clip':'../../animation/windup_start/Stonehoof_WindupStart_Baked_r01.blend','model_triangles':20042,'validation':json.loads((HERE/'validation.json').read_text()),'bake_validation':json.loads((HERE/'bake_validation.json').read_text())}
manifest['validation'].pop('samples_detail',None)
manifest['master_sha256']=hashlib.sha256((HERE/'Stonehoof_WindupStart_r01.blend').read_bytes()).hexdigest()
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
print('Encoded 4 videos, 37 reference frames; review manifest saved.')
