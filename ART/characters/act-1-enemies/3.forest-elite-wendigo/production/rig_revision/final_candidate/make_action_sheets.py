"""Pair identical review cameras for a visual skin comparison."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parent/'action_stress'
a=root/'ForestWendigo_Rig'
b=root/'ForestWendigo_Rig_SkinCandidate'
for action,frame in [('Claw',15),('Claw',18),('Leap',23),('Leap',33),('Death',50)]:
    key=f'{action}_{frame:02d}'
    sheet=Image.new('RGB',(1300,2040),'#252a27')
    draw=ImageDraw.Draw(sheet)
    for col,folder in enumerate((a,b)):
        for row,angle in enumerate(('side','front','game')):
            pic=Image.open(folder/f'{key}_{angle}.png').convert('RGB')
            sheet.paste(pic,(col*650,row*680+30))
            label=('Original' if col==0 else 'Skin candidate')+' / '+angle
            draw.text((col*650+15,row*680+6),label,fill='white')
    sheet.save(root/(key+'_compare.jpg'),quality=94)
print('Wrote',len(list(root.glob('*_compare.jpg'))),'comparison sheets')
