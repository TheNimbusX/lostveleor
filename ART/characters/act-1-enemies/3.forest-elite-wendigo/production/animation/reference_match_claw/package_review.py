from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import subprocess,json,argparse
import imageio_ffmpeg

HERE=Path(__file__).resolve().parent
parser=argparse.ArgumentParser();parser.add_argument('--revision',default='');args=parser.parse_args()
suffix='_'+args.revision if args.revision else ''
OUT=HERE.parents[1]/'review'/'animation_reference_claw'
OUT.mkdir(exist_ok=True,parents=True)
FRAMES=HERE/'comparison_frames';FRAMES.mkdir(exist_ok=True)
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',24)
small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',17)
selected=[30,36,40,44,48,50,54,57,61,65,69,75] if args.revision else [0,19,24,30,36,48,50,51,52,53,57,78]
sheet=Image.new('RGB',(1440,4*268),(28,31,31))
for f in range(97):
 ref=Image.open(HERE/'reference'/f'ref_{f+1:03d}.png').convert('RGB').resize((720,720),Image.Resampling.LANCZOS)
 mesh=Image.open(HERE/'frames'/f'pose_{f:04d}.png').convert('RGB')
 pair=Image.new('RGB',(1440,760),(22,27,27));pair.paste(ref,(0,40));pair.paste(mesh,(720,40));dr=ImageDraw.Draw(pair)
 dr.text((16,6),'Референс Higgsfield',font=font,fill='white');dr.text((736,6),'3D · рабочая версия',font=font,fill='white')
 dr.text((1220,9),f'{f:02d} / 96   {f/24:.2f} с',font=small,fill=(206,211,208))
 pair.save(FRAMES/f'pair_{f:04d}.png')
 if f in selected:
  i=selected.index(f);tile=pair.resize((480,253),Image.Resampling.LANCZOS)
  sheet.paste(tile,((i%3)*480,(i//3)*268))
sheet.save(OUT/f'comparison_keyframes{suffix}.jpg',quality=94)
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
for directory,pattern,target in [(FRAMES,'pair_%04d.png',f'claw_comparison{suffix}.mp4'),(HERE/'frames','pose_%04d.png',f'claw_3d{suffix}.mp4')]:
 subprocess.run([ffmpeg,'-v','error','-framerate','24','-start_number','0','-i',str(directory/pattern),'-frames:v','97','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart','-y',str(OUT/target)],check=True)
(OUT/'review_status.json').write_text(json.dumps({'status':'working_reference_study_not_approved','revision':args.revision or 'r01','unity_import_allowed':False,'frames':97,'fps':24,'motion':'Claw only','source':'../../../review/higgsfield-refs/claw.mp4','scene':'../../animation/reference_match_claw/Claw_Reference_Spline.blend','remaining_review':'Owner comparison of motion; no claim of exact match or finished animation pack.'},indent=2))
print(OUT)
