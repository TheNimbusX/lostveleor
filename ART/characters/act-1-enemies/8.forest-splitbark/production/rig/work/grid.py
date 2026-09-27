import sys, glob
from PIL import Image
out=sys.argv[1]; fs=sys.argv[2:]; n=len(fs); c=min(4,n); r=(n+c-1)//c; S=450
W=Image.new('RGB',(S*c,S*r))
for i,f in enumerate(fs):
    im=Image.open(f).convert('RGB'); im.thumbnail((S,S)); W.paste(im,((i%c)*S,(i//c)*S))
W.save(out); print(out, fs)
