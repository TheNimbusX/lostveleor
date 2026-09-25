"""Run the shared non-destructive reviewer using each revised action's own range."""

from pathlib import Path
import runpy

shared = Path(__file__).resolve().parents[2] / "preview" / "render_clip_review.py"
namespace = runpy.run_path(str(shared), run_name="wendigo_review")
namespace["action_frames"].__globals__["contract_for"] = lambda name: {}
namespace["main"]()
