"""Create a loopable two-view review clip with short holds at either end."""

import subprocess
from pathlib import Path

import imageio_ffmpeg
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
source = root / 'review' / 'AN_ForestWendigo_Hit'
out = root / 'review_composite'
out.mkdir(parents=True, exist_ok=True)
sequence = ([0] * 8 + list(range(15)) + [14] * 6) * 3
for index, frame in enumerate(sequence):
    side = Image.open(source / 'side' / 'frames' / f'{frame:04d}.png').convert('RGB')
    game = Image.open(source / 'game' / 'frames' / f'{frame:04d}.png').convert('RGB')
    panel = Image.new('RGB', (side.width + game.width, side.height), (0, 0, 0))
    panel.paste(side, (0, 0))
    panel.paste(game, (side.width, 0))
    draw = ImageDraw.Draw(panel)
    draw.text((16, 12), f'SIDE  |  frame {frame:02d}/14', fill='white')
    draw.text((side.width + 16, 12), 'GAME CAMERA', fill='white')
    panel.save(out / f'{index:04d}.png')

video = root / 'ForestWendigo_Hit_v2_review.mp4'
cmd = [imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-hide_banner', '-loglevel', 'error',
       '-framerate', '30', '-i', str(out / '%04d.png'), '-c:v', 'libx264',
       '-pix_fmt', 'yuv420p', '-crf', '18', '-preset', 'medium',
       '-movflags', '+faststart', str(video)]
subprocess.run(cmd, check=True)
print(video, video.stat().st_size)
