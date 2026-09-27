"""Probe set: selected frames of one take. Set env PROBE_TAKE=<take name>, PROBE_FRAMES=0,3,6."""
import os
import clip_idle_hit, clip_walk, clip_slam, clip_death

TAKES = {}
for m in (clip_idle_hit, clip_walk, clip_slam, clip_death):
    TAKES.update(m.TAKES)


def poses(rig):
    take = TAKES[os.environ["PROBE_TAKE"]]
    if "prepare" in take:
        take["prepare"](rig)
    frames = [float(x) for x in os.environ["PROBE_FRAMES"].split(",")]
    return {f"f{int(f):02d}": take["pose"](f) for f in frames}
