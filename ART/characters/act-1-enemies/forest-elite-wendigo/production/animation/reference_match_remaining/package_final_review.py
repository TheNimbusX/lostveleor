from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import subprocess,json
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parent;ANIM=ROOT.parent;REVIEW=ANIM.parent/'review';ff=imageio_ffmpeg.get_ffmpeg_exe()
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',23)
template=(REVIEW/'animation_reference_leap/index.html').read_text(encoding='utf-8')
for kind,title,count in [('Leap','Охотничий прыжок',97),('Death','Смерть',97),('Walk','Ходьба',49)]:
 name=kind.lower();work=ANIM/'reference_match_leap' if kind=='Leap' else ROOT/(name+'_work')
 frames=work/'final_frames';out=REVIEW/('animation_reference_'+name);out.mkdir(exist_ok=True)
 pairs=work/'final_pairs';pairs.mkdir(exist_ok=True)
 for f in range(count):
  reference=ANIM/'reference_match_leap/reference'/f'ref_{f+1:03d}.png' if kind=='Leap' else ROOT/('death' if kind=='Death' else 'idle_walk')/f'ref_{f+1+(36 if kind=="Walk" else 0):03d}.png'
  image=Image.new('RGB',(1440,760),(22,27,27));image.paste(Image.open(reference).convert('RGB').resize((720,720)),(0,40));image.paste(Image.open(frames/f'pose_{f:04d}.png').convert('RGB'),(720,40))
  dr=ImageDraw.Draw(image);dr.text((16,6),'Видеореференс',font=font,fill='white');dr.text((736,6),'3D · '+title+' · '+str(f),font=font,fill='white');image.save(pairs/f'pair_{f:04d}.png')
 for folder,pattern,suffix in [(pairs,'pair_%04d.png','comparison'),(frames,'pose_%04d.png','3d')]:
  subprocess.run([ff,'-v','error','-framerate','24','-i',str(folder/pattern),'-frames:v',str(count),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart','-y',str(out/f'{name}_{suffix}_r03.mp4')],check=True)
 text=template.replace('Охотничий прыжок',title).replace('охотничий прыжок',title.lower()).replace('версия 1','версия 3').replace('r01','r03').replace('leap_comparison',name+'_comparison').replace('leap_3d',name+'_3d')
 start=text.index('<div class="controls" id="beats">');end=text.index('</div>',start)+6;text=text[:start]+text[end:]
 start=text.index('<p class="note">');end=text.index('</p>',start)+4
 note='Исправлен разворот предплечья в полёте и при посадке. Дуга прыжка и положение ладоней сохранены.' if kind=='Leap' else 'Падение на лапы: опора кистей, проседание плеч, затем головы.' if kind=='Death' else 'Слева движение вперёд, справа игровой цикл на месте. Движение по арене задаёт симуляция; длина игрового шага — 1,8 м.'
 text=text[:start]+f'<p class="note">{note}</p>'+text[end:]
 start=text.index('<p class="links">');end=text.index('</p>',start)+4
 scene='../../animation/reference_match_leap/Leap_Final.blend' if kind=='Leap' else f'../../animation/reference_match_remaining/{name}_work/{kind}_Final.blend'
 text=text[:start]+f'<p class="links"><a href="{name}_3d_r03.mp4">Только 3D</a><a href="{scene}">Редактируемая сцена Blender</a><a href="../animation_unity_package/index.html">Весь комплект и бой в Unity</a></p>'+text[end:]
 start=text.index('<details>');end=text.index('</details>',start)+10;text=text[:start]+text[end:]
 if count==49:text=text.replace('96','48')
 (out/'index.html').write_text(text,encoding='utf-8')
 (out/'review_status.json').write_text(json.dumps({'version':'r03','unity_import_allowed':True,'owner_art_review_pending':True,'fps':24,'frames':count,'note':note},ensure_ascii=False,indent=2),encoding='utf-8')
 print(name,'ready',flush=True)
