"""Encode reviewed PNG sequences to MP4 at the gameplay rate of 30 fps."""
from pathlib import Path
import subprocess
import shutil

HERE = Path(__file__).resolve().parent
ROOT = HERE / 'review_delivery_final' / 'AN_ForestWendigo_Claw'
FFMPEG = shutil.which('ffmpeg')
if not FFMPEG:
    try:
        import imageio_ffmpeg
        FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError as exc:
        raise RuntimeError('Install FFmpeg or imageio-ffmpeg to re-encode the PNG reviews') from exc

for view in ('game', 'front', 'side'):
    frames = ROOT / view / 'frames'
    if len(list(frames.glob('*.png'))) != 31:
        raise RuntimeError(f'{view}: expected 31 rendered PNGs')
    output = HERE / f'ForestWendigo_Claw_{view}.mp4'
    command = [str(FFMPEG), '-y', '-hide_banner', '-loglevel', 'error',
               '-framerate', '30', '-start_number', '0', '-i', str(frames / '%04d.png'),
               '-frames:v', '31', '-c:v', 'libx264', '-pix_fmt', 'yuv420p',
               '-crf', '18', str(output)]
    subprocess.run(command, check=True)
    print('ENCODED', output, output.stat().st_size)
