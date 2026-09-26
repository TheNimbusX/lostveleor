"""Сборка контрольных видео из кадров Blender, без изменения скорости движений."""
import json,subprocess,hashlib
from pathlib import Path
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'production';REVIEW=OUT/'review'/'model_stage'
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
for name,fps,count in [('turntable',24,96),('idle',30,120),('run',30,14)]:
    frames=list((REVIEW/(name+'_frames')).glob('*.png'))
    if len(frames)!=count:raise RuntimeError(f'{name}: expected {count} frames, got {len(frames)}')
    common=[ffmpeg,'-hide_banner','-loglevel','error','-y','-framerate',str(fps),'-i',str(REVIEW/(name+'_frames')/'%04d.png')]
    color=['-pix_fmt','yuv420p','-colorspace','bt709','-color_primaries','bt709','-color_trc','bt709','-color_range','tv']
    subprocess.run(common+['-c:v','libx264','-profile:v','baseline','-level:v','3.1','-crf','18']+color+['-movflags','+faststart',str(REVIEW/(name+'.mp4'))],check=True)
    subprocess.run(common+['-c:v','libvpx-vp9','-b:v','0','-crf','25','-row-mt','1','-cpu-used','3']+color+[str(REVIEW/(name+'.webm'))],check=True)
manifest={'stage':'model_review','owner_approved':False,'model':'../../Stonehoof_ModelCandidate_r03.blend','model_sha256':hashlib.sha256((OUT/'Stonehoof_ModelCandidate_r03.blend').read_bytes()).hexdigest(),'model_stats':json.loads((OUT/'model_audit.json').read_text()),'comparison':json.loads((OUT/'comparison_audit.json').read_text()),'motion_note':'Idle и Run — исходные движения на исправленной копии; новая боевая анимация ещё не сделана.','next_gate':'owner model and scale review before Higgsfield video references'}
if (REVIEW/'manifest.json').exists():
    previous=json.loads((REVIEW/'manifest.json').read_text(encoding='utf8'))
    for key in ('owner_approved','owner_approved_at','approval_scope','next_gate'):
        if key in previous:manifest[key]=previous[key]
if (OUT/'model_validation.json').exists():manifest['validation']=json.loads((OUT/'model_validation.json').read_text())
(REVIEW/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
print('Review videos and manifest ready')
