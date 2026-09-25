"""Собирает просмотр из проверяемых кадров Unity; не меняет исходные клипы."""
from pathlib import Path
import csv,json,subprocess
import imageio_ffmpeg

ROOT=Path(__file__).resolve().parents[7]
REVIEW=Path(__file__).resolve().parents[2]/'review'
OUT=REVIEW/'animation_unity_package'; OUT.mkdir(exist_ok=True)
ff=imageio_ffmpeg.get_ffmpeg_exe()
report={}
for name in ('kill-60','dodge-30','hit-120'):
 folder=ROOT/'artifacts/wendigo-review'/('editor-'+name)
 if not (folder/'timing.csv').exists(): continue
 rows=list(csv.reader((folder/'timing.csv').open(encoding='utf-8')))
 frames=[r for r in rows if r and r[0]=='frame']
 poses=[r for r in rows if r and r[0]=='pose']
 if not poses:continue
 checks=[]
 for serial in sorted({r[3] for r in poses},key=int):
  group=[r for r in poses if r[3]==serial]; contact=60 if group[0][4]=='Leap' else 52
  arrived=next((r for r in group if float(r[5])>=contact-.0001),None)
  if arrived:
   error=max(0,float(arrived[2])-int(arrived[6]))/30
   checks.append({'serial':int(serial),'kind':arrived[4],'contact_delay_seconds':round(error,6),'within_display_frame':error<=1/int(name.split('-')[-1])+.00001})
 events=[{'tick':int(r[2]),'type':r[4],'variant':r[6]} for r in rows if r and r[0]=='event']
 report[name]={'capture_step_fps':int(name.split('-')[-1]),'performance_measurement':False,'frames':len(frames),'contacts':checks,'events':events}
 video=OUT/(name+'.mp4')
 if len(list(folder.glob('frame_*.png')))==len(frames):
  subprocess.run([ff,'-v','error','-framerate','24','-i',str(folder/'frame_%05d.png'),'-frames:v',str(len(frames)),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart','-y',str(video)],check=True)
 elif not video.exists(): raise RuntimeError('Для сборки видео нужны все кадры: '+name)
 print(name,len(frames),'contacts',checks,flush=True)
subprocess.run([ff,'-v','error','-framerate','24','-i',str(OUT/'hit_frames/hit_%03d.png'),'-frames:v','13','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart','-y',str(OUT/'hit.mp4')],check=True)
(OUT/'unity_checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
