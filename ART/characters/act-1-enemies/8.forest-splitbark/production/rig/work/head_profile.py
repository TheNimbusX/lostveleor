import numpy as np
from PIL import Image, ImageDraw
d=np.load('mesh_dump.npz'); co=d['co']; tris=d['tris']; col=d['tri_col']
S=900; y0,y1,z0,z1=-0.66,-0.26,0.25,0.85
img=Image.new('RGB',(S*2+20,S),(40,44,48)); dr=ImageDraw.Draw(img)
def px(y,z,ox): return ox+(y-y0)/(y1-y0)*S, S-(z-z0)/(z1-z0)*S
for k,xv in enumerate((0.0,0.06)):
    ox=k*(S+20)
    for g in np.arange(-0.65,-0.25,0.05):
        a,_=px(g,0,ox); dr.line([(a,0),(a,S)],fill=(70,74,78)); dr.text((a+2,2),'%.2f'%g,fill=(200,200,0))
    for g in np.arange(0.25,0.86,0.05):
        _,b=px(0,g,ox); dr.line([(ox,b),(ox+S,b)],fill=(70,74,78)); dr.text((ox+2,b+2),'%.2f'%g,fill=(200,200,0))
    P=co[tris]; s=P[:,:,0]-xv
    for t in np.nonzero((s.min(1)<0)&(s.max(1)>0))[0]:
        pts=[]
        for i in range(3):
            j=(i+1)%3; a,b=s[t,i],s[t,j]
            if (a<0)!=(b<0):
                f=a/(a-b); p=P[t,i]+f*(P[t,j]-P[t,i]); pts.append(px(p[1],p[2],ox))
        if len(pts)==2:
            c=tuple(int(255*min(1,max(0,v))**(1/2.2)) for v in col[t]); dr.line(pts,fill=c,width=3)
    dr.text((ox+10,S-20),'x=%.2f'%xv,fill=(255,255,0))
img.save('head_profile.png')
