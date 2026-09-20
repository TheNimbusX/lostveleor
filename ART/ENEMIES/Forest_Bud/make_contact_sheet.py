from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
R=Path(r'C:/Users/d.grab/Desktop/the-game');D=R/'artifacts/forest-bud-production/review-final'
labels=[(1,'0.00 / Ready'),(10,'0.30 / Gather weight'),(19,'0.60 / Crouch'),(25,'0.80 / Fruit 1'),(31,'1.00 / Fruit 2'),(37,'1.20 / Fruit 3'),(43,'1.40 / Fruit 4'),(49,'1.60 / Fruit 5'),(56,'1.83 / Recover'),(67,'2.20 / Ready')]
canvas=Image.new('RGB',(1800,780),(24,29,24));draw=ImageDraw.Draw(canvas)
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',19)
for i,(frame,label) in enumerate(labels):
 im=Image.open(D/f'attack_{frame:02d}.png').convert('RGB').resize((360,360),Image.Resampling.LANCZOS)
 x=(i%5)*360;y=(i//5)*390;canvas.paste(im,(x,y));draw.text((x+12,y+364),label,font=font,fill=(235,227,197))
canvas.save(D/'Attack_ContactSheet.jpg',quality=95)
print(D/'Attack_ContactSheet.jpg')
