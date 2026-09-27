"""Minimal binary FBX reader (pure Python, no Blender): the independent view of what Unity sees.
parse(path) -> root Node; Node(name, props, children)."""
import struct, zlib

KTIME = 46186158000  # FBX ticks per second


class Node:
    __slots__ = ("name", "props", "children")

    def __init__(self, name, props, children):
        self.name, self.props, self.children = name, props, children

    def find(self, name):
        return next((c for c in self.children if c.name == name), None)

    def all(self, name):
        return [c for c in self.children if c.name == name]

    def __repr__(self):
        return f"Node({self.name!r}, {self.props[:3]!r}, {len(self.children)} ch)"


_ARR = {"f": ("f", 4), "d": ("d", 8), "l": ("q", 8), "i": ("i", 4), "b": ("?", 1)}


def _prop(buf, p):
    t = chr(buf[p]); p += 1
    if t == "Y": return struct.unpack_from("<h", buf, p)[0], p + 2
    if t == "C": return bool(buf[p]), p + 1
    if t == "I": return struct.unpack_from("<i", buf, p)[0], p + 4
    if t == "F": return struct.unpack_from("<f", buf, p)[0], p + 4
    if t == "D": return struct.unpack_from("<d", buf, p)[0], p + 8
    if t == "L": return struct.unpack_from("<q", buf, p)[0], p + 8
    if t in _ARR:
        n, enc, clen = struct.unpack_from("<III", buf, p); p += 12
        raw = buf[p:p + clen]; p += clen
        if enc == 1:
            raw = zlib.decompress(raw)
        fmt, _ = _ARR[t]
        return list(struct.unpack("<%d%s" % (n, fmt), raw)), p
    if t in "SR":
        n = struct.unpack_from("<I", buf, p)[0]; p += 4
        v = bytes(buf[p:p + n]); p += n
        return (v.decode("utf-8", "replace") if t == "S" else v), p
    raise ValueError("bad prop type %r at %d" % (t, p - 1))


def _node(buf, p, wide):
    if wide:
        end, nprops, plen = struct.unpack_from("<QQQ", buf, p); p += 24
    else:
        end, nprops, plen = struct.unpack_from("<III", buf, p); p += 12
    nlen = buf[p]; p += 1
    if end == 0:
        return None, p
    name = bytes(buf[p:p + nlen]).decode(); p += nlen
    props = []
    for _ in range(nprops):
        v, p = _prop(buf, p)
        props.append(v)
    kids = []
    null = 25 if wide else 13
    while p < end:
        if end - p == null and not any(buf[p:end]):
            p = end
            break
        c, p = _node(buf, p, wide)
        if c is None:
            break
        kids.append(c)
    return Node(name, props, kids), end


def parse(path):
    buf = memoryview(open(path, "rb").read())
    assert bytes(buf[:20]) == b"Kaydara FBX Binary  ", "not a binary FBX"
    ver = struct.unpack_from("<I", buf, 23)[0]
    wide = ver >= 7500
    p, kids = 27, []
    while p < len(buf) - 200:
        c, p2 = _node(buf, p, wide)
        if c is None:
            break
        kids.append(c)
        p = p2
    root = Node("<root>", [ver], kids)
    return root


def props70(node):
    """Properties70 block -> {name: [values...]}"""
    out = {}
    pb = node.find("Properties70") if node else None
    for P in (pb.children if pb else []):
        out[P.props[0]] = P.props[4:]
    return out
