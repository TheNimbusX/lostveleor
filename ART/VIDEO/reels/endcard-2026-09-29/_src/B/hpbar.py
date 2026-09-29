# Track the Wendigo HP bar through the source frames B uses and pick a clone offset that hides it.
# The fill for a bar pixel p is the same frame's ground at p + V (both move with the camera, so the
# patch stays locked to the world). Output: hpbar.json {frame_index: [x0, y0]} (+ chosen V).
import sys, os, json, subprocess
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'pytools'))
import imageio_ffmpeg
FF = imageio_ffmpeg.get_ffmpeg_exe()
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = r"C:\Users\d.grab\Desktop\the-game\artifacts\mobsv2-check\wendigo-turn\forest_wendigo_turn_1080p60.mp4"
S0 = 3.4333 - 0.95 - 0.45 / 2
NFR = 71
BAR = (715, 298, 930, 348)           # bar box in the freeze frame (index 70)
nb = 1920 * 1080 * 3
p = subprocess.Popen([FF, '-v', 'error', '-ss', f'{S0:.4f}', '-i', SRC, '-frames:v', str(NFR),
                      '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], stdout=subprocess.PIPE)
frames = []
for i in range(NFR):
    b = p.stdout.read(nb)
    frames.append(np.frombuffer(b, np.uint8).reshape(1080, 1920, 3).astype(np.float32))
p.wait()
gray = [f @ np.array([0.299, 0.587, 0.114], np.float32) for f in frames]

# template: the red bar body + icon (strongly saturated, unlike grass)
def sat(f):
    return f[..., 0] - 0.5 * (f[..., 1] + f[..., 2])
T = sat(frames[70])[BAR[1]:BAR[3], BAR[0]:BAR[2]]
T = T - T.mean(); T /= np.sqrt((T * T).sum())
th, tw = T.shape

def match(img, cx, cy, rad):
    x0, y0 = int(cx - rad), int(cy - rad)
    win = img[y0:y0 + 2 * rad + th, x0:x0 + 2 * rad + tw]
    best = (-2, 0, 0)
    # coarse-to-fine brute force NCC (small window, fine in numpy)
    for dy in range(0, 2 * rad + 1, 2):
        for dx in range(0, 2 * rad + 1, 2):
            w = win[dy:dy + th, dx:dx + tw]
            w = w - w.mean(); n = np.sqrt((w * w).sum()) + 1e-6
            s = (w * T).sum() / n
            if s > best[0]: best = (s, dx, dy)
    s, bx, by = best
    for dy in range(max(by - 2, 0), min(by + 3, 2 * rad + 1)):
        for dx in range(max(bx - 2, 0), min(bx + 3, 2 * rad + 1)):
            w = win[dy:dy + th, dx:dx + tw]
            w = w - w.mean(); n = np.sqrt((w * w).sum()) + 1e-6
            sc = (w * T).sum() / n
            if sc > best[0]: best = (sc, dx, dy)
    return best[0], x0 + best[1], y0 + best[2]

pos = {}
cx, cy = BAR[0], BAR[1]
for i in range(70, -1, -1):
    sc, x, y = match(sat(frames[i]), cx, cy, 40)
    pos[i] = (x, y, sc)
    cx, cy = x, y
for i in sorted(pos):
    if i % 5 == 0 or i == 70: print(i, pos[i][0], pos[i][1], round(pos[i][2], 3))

# mask (relative to bar box): padded rounded box, feathered
PADX, PADY = 22, 20
def ring_err(V):
    """seam error of cloning with offset V, averaged over a few frames, measured on a ring just outside the mask"""
    errs = []
    for i in (0, 20, 40, 55, 70):
        x, y, _ = pos[i]
        X0, Y0, X1, Y1 = x - PADX, y - PADY, x + tw + PADX, y + th + PADY
        f = frames[i]
        R = 10
        outer = f[Y0 - R:Y1 + R, X0 - R:X1 + R]
        src = f[Y0 - R + V[1]:Y1 + R + V[1], X0 - R + V[0]:X1 + R + V[0]]
        m = np.ones(outer.shape[:2], bool); m[R:-R, R:-R] = False
        errs.append(np.abs(outer - src)[m].mean())
    return float(np.mean(errs))
cands = []
for vy in range(-150, -84, 6):
    for vx in range(-260, 261, 10):
        cands.append((ring_err((vx, vy)), vx, vy))
cands.sort()
print('best V', cands[:8])
V = (cands[0][1], cands[0][2])
json.dump({'V': V, 'pad': [PADX, PADY], 'size': [tw, th], 'pos': {str(i): [int(pos[i][0]), int(pos[i][1])] for i in pos}},
          open(os.path.join(HERE, 'hpbar.json'), 'w'), indent=0)
