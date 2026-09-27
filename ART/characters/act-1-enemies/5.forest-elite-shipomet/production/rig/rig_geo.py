"""Geometry helpers: welded islands, texture sampling, spike detection (numpy)."""
import numpy as np


def world_coords(mesh_obj):
    me = mesh_obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    m = np.array(mesh_obj.matrix_world)
    return co @ m[:3, :3].T + m[:3, 3]


def weld_ids(co, tol=1e-5):
    """Map each vertex to a representative id shared by coincident vertices."""
    keys = np.round(co / tol).astype(np.int64)
    _, inv = np.unique(keys, axis=0, return_inverse=True)
    return inv.reshape(-1)


def welded_components(mesh_obj, weld, mask=None):
    """Connected components over edges after welding; optional vertex mask."""
    me = mesh_obj.data
    ev = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)
    n = weld.max() + 1
    parent = np.arange(n)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for a, b in ev:
        if mask is not None and not (mask[a] and mask[b]):
            continue
        ra, rb = find(weld[a]), find(weld[b])
        if ra != rb:
            parent[ra] = rb
    roots = np.array([find(weld[i]) for i in range(len(weld))])
    return roots


def vertex_colors_from_image(mesh_obj, image):
    """Average texture colour of each vertex from its loop UVs."""
    me = mesh_obj.data
    w, h = image.size
    px = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    uv = np.empty(len(me.loops) * 2, dtype=np.float64)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    xs = np.clip((uv[:, 0] % 1.0) * w, 0, w - 1).astype(np.int64)
    ys = np.clip((uv[:, 1] % 1.0) * h, 0, h - 1).astype(np.int64)
    col = px[ys, xs, :3]
    acc = np.zeros((len(me.vertices), 3))
    cnt = np.zeros(len(me.vertices))
    np.add.at(acc, lv, col)
    np.add.at(cnt, lv, 1)
    return acc / np.maximum(cnt, 1)[:, None]


def is_red(col):
    r, g, b = col[:, 0], col[:, 1], col[:, 2]
    return (r > 0.35) & (r > 1.8 * g) & (r > 1.8 * b)
