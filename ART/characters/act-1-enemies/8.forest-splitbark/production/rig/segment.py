"""Shell / body segmentation of the fused Splitter mesh (numpy + heapq only, runs in Blender).

The Tripo mesh is one closed island: the bark shell halves are fused to the body along
concave creases (the V gap on the back and the belly pockets). Multi-source Dijkstra from
seed regions with an edge cost that grows on concave creases puts the label boundary on
those creases.  Labels: 0 = body, 1 = shell_L (+X), 2 = shell_R (-X).
"""
import heapq

import numpy as np

BODY, SHELL_L, SHELL_R = 0, 1, 2


def build_edges(co, tris):
    e = np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]])
    face = np.concatenate([np.arange(len(tris))] * 3)
    key = np.sort(e, axis=1)
    order = np.lexsort((key[:, 1], key[:, 0]))
    key, face = key[order], face[order]
    uniq, start, counts = np.unique(key, axis=0, return_index=True, return_counts=True)
    fn = np.cross(co[tris[:, 1]] - co[tris[:, 0]], co[tris[:, 2]] - co[tris[:, 0]])
    fn /= np.maximum(np.linalg.norm(fn, axis=1, keepdims=True), 1e-12)
    fc = co[tris].mean(axis=1)
    concave = np.zeros(len(uniq))
    two = counts == 2
    f1 = face[start[two]]
    f2 = face[start[two] + 1]
    ang = np.arccos(np.clip((fn[f1] * fn[f2]).sum(1), -1, 1))
    sign = ((fc[f2] - fc[f1]) * fn[f1]).sum(1) > 0  # other face in front -> concave
    concave[two] = np.where(sign, ang, 0.0)
    length = np.linalg.norm(co[uniq[:, 0]] - co[uniq[:, 1]], axis=1)
    return uniq, length, concave


def adjacency(n, edges, cost):
    adj = [[] for _ in range(n)]
    for (a, b), c in zip(edges.tolist(), cost.tolist()):
        adj[a].append((b, c))
        adj[b].append((a, c))
    return adj


def dijkstra(adj, sources, n):
    dist = np.full(n, np.inf)
    heap = [(0.0, int(s)) for s in sources]
    for _, s in heap:
        dist[s] = 0.0
    heapq.heapify(heap)
    while heap:
        d, v = heapq.heappop(heap)
        if d > dist[v]:
            continue
        for w, c in adj[v]:
            nd = d + c
            if nd < dist[w]:
                dist[w] = nd
                heapq.heappush(heap, (nd, w))
    return dist


def smooth_concavity(n, edges, concave, iters=1):
    """Vertex concavity = max concave dihedral of incident edges, then spread to edges."""
    vc = np.zeros(n)
    np.maximum.at(vc, edges[:, 0], concave)
    np.maximum.at(vc, edges[:, 1], concave)
    for _ in range(iters):
        nb = np.zeros(n)
        np.maximum.at(nb, edges[:, 0], vc[edges[:, 1]])
        np.maximum.at(nb, edges[:, 1], vc[edges[:, 0]])
        vc = np.maximum(vc, 0.6 * nb)
    return np.maximum(vc[edges[:, 0]], vc[edges[:, 1]])


def vertex_normals(co, tris):
    fn = np.cross(co[tris[:, 1]] - co[tris[:, 0]], co[tris[:, 2]] - co[tris[:, 0]])
    vn = np.zeros_like(co)
    for k in range(3):
        np.add.at(vn, tris[:, k], fn)
    return vn / np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)


def seeds(co, vn):
    x, y, z = co[:, 0], co[:, 1], co[:, 2]
    ax = np.abs(x)
    sx = np.sign(x)
    underside = (np.abs(y) < 0.18) & (ax > 0.38) & (z > 0.22) & (z < 0.6) & (vn[:, 2] < -0.3) & (vn[:, 0] * sx < -0.2)
    inner_wall = (ax > 0.08) & (z > 0.5) & (y > -0.52) & (y < 0.22) & (vn[:, 0] * sx < -0.6) & (np.abs(vn[:, 2]) < 0.6)
    shell = underside | inner_wall | (
        ((ax > 0.5) & (z > 0.55))  # outer flanks above the front legs
        | (z > 1.1)  # lips above the back
        | ((y < -0.36) & (ax > 0.24) & (z > 0.5))  # front tips flanking the head
        | ((y > 0.3) & (ax > 0.4) & (z > 0.5))  # rear flanks
    )
    feet = np.zeros(len(co), bool)
    for fx, fy in ((0.45, -0.32), (-0.46, -0.32), (0.42, 0.49), (-0.39, 0.49)):
        feet |= (np.hypot(x - fx, y - fy) < 0.2) & (z < 0.22)
    body = (
        ((ax < 0.045) & (z > 0.8) & (z < 1.03) & (y > -0.25) & (y < 0.35))  # back strip (V floor)
        | (y > 0.54)  # rump
        | ((y < -0.48) & (ax < 0.13))  # head / beak
        | feet
        | ((ax < 0.15) & (z < 0.42))  # belly
    )
    shell &= ~body
    return np.nonzero(shell & (x > 0))[0], np.nonzero(shell & (x < 0))[0], np.nonzero(body)[0]


def segment(co, tris, crease_gain=6.0):
    n = len(co)
    edges, length, concave = build_edges(co, tris)
    cc = smooth_concavity(n, edges, concave)
    cost = length * (1.0 + crease_gain * cc)
    adj = adjacency(n, edges, cost)
    sl, sr, sb = seeds(co, vertex_normals(co, tris))
    d = np.stack([dijkstra(adj, sb, n), dijkstra(adj, sl, n), dijkstra(adj, sr, n)], axis=1)
    labels = np.argmin(d, axis=1)
    return labels, d, edges, length


def geodesic_from_boundary(n, edges, length, labels, target):
    """Plain geodesic distance (metres along surface) from the boundary of label==target,
    measured into the other labels (0 inside target)."""
    adj = adjacency(n, edges, length)
    a, b = edges[:, 0], edges[:, 1]
    cross = (labels[a] == target) != (labels[b] == target)
    src = np.unique(np.where(labels[a[cross]] == target, a[cross], b[cross]))
    dist = dijkstra(adj, src, n)
    dist[labels == target] = 0.0
    return dist


def clean_labels(labels, edges, min_size=60):
    """Relabel connected label patches smaller than min_size verts to their majority neighbour label."""
    n = len(labels)
    labels = labels.copy()
    nbr = [[] for _ in range(n)]
    for a, b in edges.tolist():
        nbr[a].append(b)
        nbr[b].append(a)
    changed = True
    while changed:
        changed = False
        seen = np.zeros(n, bool)
        for v in range(n):
            if seen[v]:
                continue
            lab = labels[v]
            comp, stack = [v], [v]
            seen[v] = True
            border = []
            while stack:
                c = stack.pop()
                for w in nbr[c]:
                    if labels[w] == lab:
                        if not seen[w]:
                            seen[w] = True
                            stack.append(w)
                            comp.append(w)
                    else:
                        border.append(labels[w])
            if len(comp) < min_size and border:
                labels[comp] = np.bincount(border, minlength=3).argmax()
                changed = True
    return labels
