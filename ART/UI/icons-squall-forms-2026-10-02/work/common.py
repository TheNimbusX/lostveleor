from PIL import Image
import numpy as np
SRC=r'C:/Users/d.grab/Desktop/the-game/ART/UI/icons-squall-forms-2026-10-02/cdb0170f-fe43-462e-ba86-fb349ee352e9.png'
S=614
TILES={'base':(2,2),'hunt':(634,2),'foam':(2,634),'elusive':(634,634)}
def load():
    im=Image.open(SRC).convert('RGB')
    return {k:np.asarray(im.crop((x,y,x+S,y+S))).astype(float) for k,(x,y) in TILES.items()}
def hsv(a):
    a=a/255.0; mx=a.max(2); mn=a.min(2); d=mx-mn
    r,g,b=a[...,0],a[...,1],a[...,2]
    m=d>1e-6; dd=np.where(m,d,1)
    hr=((g-b)/dd)%6; hg=(b-r)/dd+2; hb=(r-g)/dd+4
    h=np.where(mx==r,hr,np.where(mx==g,hg,hb))*60; h=np.where(m,h,0)
    s=np.where(mx>0,d/np.where(mx>0,mx,1),0)
    return h,s,mx
def rgb(h,s,v):
    h=(h%360)/60.0; i=np.floor(h).astype(int)%6; f=h-np.floor(h)
    p=v*(1-s); q=v*(1-s*f); t=v*(1-s*(1-f))
    r=np.choose(i,[v,q,p,p,t,v]); g=np.choose(i,[t,v,v,q,p,p]); b=np.choose(i,[p,p,t,v,v,q])
    return np.stack([r,g,b],-1)*255
def band(h,lo,hi):
    return ((h>=lo)|(h<hi)) if lo>hi else ((h>=lo)&(h<hi))
