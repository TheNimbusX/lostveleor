"""Measure planted toe drift from Unity capture CSVs; world distances are metres."""
import csv
import json
import sys
from pathlib import Path
import numpy as np


def column(rows, key):
    return np.array([float(row[key]) for row in rows])


def positions(rows, prefix):
    return np.array([[float(row[prefix + axis]) for axis in "XYZ"] for row in rows])


def describe(values):
    if not len(values):
        return {"samples": 0}
    return {"samples": len(values), "mean": float(np.mean(values)),
            "median": float(np.median(values)), "p95": float(np.percentile(values, 95))}


def report(directory):
    directory = Path(directory)
    with (directory / "clip-feet.csv").open() as stream:
        clips = list(csv.DictReader(stream))
    result = {"clips": {}, "runtime": {}}
    for name in dict.fromkeys(row["clip"] for row in clips):
        rows = [row for row in clips if row["clip"] == name]
        time = column(rows, "time")
        feet = []
        for side in ("left", "right"):
            foot = positions(rows, side)
            velocity = np.diff(foot, axis=0) / np.diff(time)[:, None]
            stance = (foot[:-1, 1] < foot[:, 1].min() + .035)
            stance &= (velocity[:, 2] < -.1) if name == "Run" else np.ones(len(stance), dtype=bool)
            feet.extend(velocity[stance].tolist())
        speeds = np.array(feet)
        result["clips"][name] = {
            "length": float(rows[0]["length"]),
            "stance_backward_mps": describe(-speeds[:, 2]),
            "stance_horizontal_mps": describe(np.linalg.norm(speeds[:, [0, 2]], axis=1))}

    with (directory / "locomotion.csv").open() as stream:
        rows = list(csv.DictReader(stream))
    time = column(rows, "time")
    dt = np.diff(time)
    root = positions(rows, "root")
    root_v = np.diff(root, axis=0) / dt[:, None]
    speed = np.linalg.norm(root_v[:, [0, 2]], axis=1)
    yaw = np.unwrap(np.radians(column(rows, "yaw")))
    turning = np.abs(np.diff(yaw) / dt) > np.radians(15)
    stable = np.array([row["state"] == "Run_v5" and row["transition"] == "0" for row in rows])
    stable = stable[:-1] & stable[1:] & ~turning & (speed > 1)
    result["runtime"]["root_speed_mps"] = describe(speed[stable])
    drifts = []
    for side in ("left", "right"):
        foot = positions(rows, side)
        velocity = np.diff(foot, axis=0) / dt[:, None]
        local_y = foot[:, 1] - root[:, 1]
        mask = stable & (local_y[:-1] < .055) & (local_y[1:] < .055)
        drifts.extend(np.linalg.norm(velocity[mask][:, [0, 2]], axis=1))
    result["runtime"]["planted_toe_drift_mps"] = describe(np.array(drifts))
    (directory / "locomotion-report.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    report(sys.argv[1])
