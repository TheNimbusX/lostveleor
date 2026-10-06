"""Абордаж v3: лист поз новых клипов и GIF в масштабе игры — сцена и перенос ab_render.py (v2) без изменений,
другие только список клипов и режимы съёмки (ab3_render_modes.py).

blender -b --factory-startup -P ab3_render.py -- <anim_dir> <frames_dir> sheet
blender -b --factory-startup -P ab3_render.py -- <anim_dir> <frames_dir> gif
"""
import os
_here = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(_here, "ab_render.py"), encoding="utf-8").read()
_old = 'CLIPS = [P + c for c in ("Throw", "Pull", "Punch", "Recover")]'
assert _old in _src
_src = _src.replace(_old, 'CLIPS = [P + c for c in ("Throw", "PullShort", "Uppercut", "Slam", "Pull", "Punch", "Recover")]')
assert '"ab_render_modes.py"' in _src
_src = _src.replace('"ab_render_modes.py"', '"ab3_render_modes.py"')
__file__ = os.path.join(_here, "ab_render.py")
exec(compile(_src, os.path.join(_here, "ab_render.py"), "exec"))
