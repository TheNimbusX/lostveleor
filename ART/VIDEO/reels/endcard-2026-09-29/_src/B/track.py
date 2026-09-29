import sys, subprocess, numpy as np, json
sys.path.insert(0, r'..\pytools')
import imageio_ffmpeg
FF=imageio_ffmpeg.get_ffmpeg_exe()
src=r"C:\Users\d.grab\Desktop\the-game\artifacts\mobsv2-check\wendigo-turn\forest_wendigo_turn_1080p60.mp4"
t0,t1=2.05,4.10
W,H=960,540
p=subprocess.run([FF,"-v","error","-ss",str(t0),"-to",str(t1),"-i",src,"-vf",f"scale={W}:{H},format=gray","-f","rawvideo","-"],capture_output=True)
fr=np.frombuffer(p.stdout,np.uint8).reshape(-1,H,W).astype(np.float32)
# use top band (rocks/trees, less VFX) for camera motion
win=np.outer(np.hanning(H),np.hanning(W))
def shift(a,b):
    A=np.fft.fft2((a-a.mean())*win); B=np.fft.fft2((b-b.mean())*win)
    R=A*np.conj(B); R/=np.abs(R)+1e-6
    r=np.real(np.fft.ifft2(R)); y,x=np.unravel_index(np.argmax(r),r.shape)
    # subpixel parabola
    def sp(c,m,p_): d=(m-p_)/(2*(m-2*c+p_)+1e-9); return d
    dy=sp(r[y,x],r[(y-1)%H,x],r[(y+1)%H,x]); dx=sp(r[y,x],r[y,(x-1)%W],r[y,(x+1)%W])
    if y>H//2: y-=H
    if x>W//2: x-=W
    return (x+dx)*2,(y+dy)*2
cum=[(0.0,0.0)]
for i in range(1,len(fr)):
    dx,dy=shift(fr[i],fr[i-1])
    cum.append((cum[-1][0]+dx,cum[-1][1]+dy))
cum=np.array(cum)
ts=t0+np.arange(len(fr))/60
i38=int(round((3.80-t0)*60))
out={f"{t:.4f}":[float(c[0]-cum[i38][0]),float(c[1]-cum[i38][1])] for t,c in zip(ts,cum)}
json.dump(out,open("camtrack.json","w"),indent=0)
for k in range(0,len(fr),6): print(f"{ts[k]:.3f} {cum[k][0]-cum[i38][0]:+7.1f} {cum[k][1]-cum[i38][1]:+7.1f}")
