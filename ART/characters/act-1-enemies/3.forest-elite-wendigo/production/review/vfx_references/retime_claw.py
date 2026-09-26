from pathlib import Path
import numpy as np,json,imageio_ffmpeg
from PIL import Image,ImageDraw,ImageFilter
from scipy.ndimage import maximum_filter,gaussian_filter
root=Path('ART/characters/act-1-enemies/forest-elite-wendigo/production/review/vfx_references');out=Path('artifacts/wendigo-vfx-refs')
reader=imageio_ffmpeg.read_frames(str(root/'claw.mp4'),pix_fmt='rgb24');meta=next(reader);w,h=meta['size'];frames=np.array([np.frombuffer(b,np.uint8).reshape(h,w,3) for b in reader])
mat=Image.new('L',(w,h));ImageDraw.Draw(mat).polygon([(925,165),(1279,165),(1279,719),(735,719),(755,675),(835,635),(907,607),(925,510)],fill=255)
mask=np.asarray(mat.filter(ImageFilter.GaussianBlur(7)),dtype=np.float32)/255
yy,xx=np.mgrid[:h,:w]
flash=np.clip((1.3-((xx-1050)/85)**2-((yy-603)/42)**2)/.5,0,1)
writer=imageio_ffmpeg.write_frames(str(root/'claw_sync_r03.mp4'),(w,h),fps=24,codec='libx264',pix_fmt_in='rgb24',pix_fmt_out='yuv420p',quality=9,macro_block_size=1,output_params=['-movflags','+faststart']);writer.send(None)
review=[]
for i,frame in enumerate(frames):
 image=frame
 if i>=30:
  src=frames[min(i+10,len(frames)-1)].astype(np.float32);alpha=mask.copy()
  if i<=45:
   v=frame.astype(np.float32);glow=(v[:,:,0]>150)&(v[:,:,1]>120)&(v[:,:,0]>v[:,:,2]*1.08)&(v[:,:,1]>v[:,:,2]*1.06)
   keep=gaussian_filter(maximum_filter(glow.astype(np.float32),size=7),1.2)
   if 39<=i<=45:keep*=1-flash
   alpha*=1-keep
  # Не переносим вместе с грунтом прежний световой след: он остаётся в исходном времени.
  light=(src[:,:,0]>145)&(src[:,:,1]>110)&(src[:,:,0]>src[:,:,2]*1.08)&(src[:,:,1]>src[:,:,2]*1.06)
  source_light=gaussian_filter(maximum_filter(light.astype(np.float32),size=11),1.5)
  if i+10<=45:alpha*=1-source_light
  image=np.clip(frame.astype(np.float32)*(1-alpha[:,:,None])+src*alpha[:,:,None],0,255).astype(np.uint8)
 writer.send(np.ascontiguousarray(image))
 if i in [28,29,30,31,32,33,35,38,42,48,60,80]:review.append((i,Image.fromarray(image)))
writer.close()
sheet=Image.new('RGB',(1200,960),(25,30,25));d=ImageDraw.Draw(sheet)
for j,(f,im) in enumerate(review):
 im=im.resize((400,225));x=j%3*400;y=j//3*240;sheet.paste(im,(x,y));d.text((x+8,y+225),f'Frame {f} / {f/24:.3f}s',fill='white')
sheet.save(out/'claw_sync_final_sheet.jpg')
print(root/'claw_sync_r03.mp4')