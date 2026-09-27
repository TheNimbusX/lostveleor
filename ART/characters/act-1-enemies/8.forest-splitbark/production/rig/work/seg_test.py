import sys, time
import numpy as np
sys.path.insert(0, '.')
import segment as S
d = np.load('work/mesh_dump.npz'); co, tris = d['co'].astype(float), d['tris']
t = time.time()
gain = float(sys.argv[1]) if len(sys.argv) > 1 else 6.0
labels, dist, edges, length = S.segment(co, tris, gain)
print('time', round(time.time() - t, 2), 'counts', np.bincount(labels))
tl = np.array([np.bincount(labels[t], minlength=3).argmax() for t in tris])
np.save('work/tri_labels.npy', tl); np.save('work/vert_labels.npy', labels)
pal = np.array([(1.0, 0.15, 0.1), (0.1, 0.35, 1.0), (0.1, 0.9, 0.2)])
tex = d['vcol'] ** (1 / 2.2)
mix = 0.55 * tex + 0.45 * pal[labels]
np.save('work/vert_mix.npy', mix.astype(np.float32))
