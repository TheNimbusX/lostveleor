"""Prepare a supplied one-shot without changing the source or its leading timing."""
import argparse
import array
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys
import wave


def find_ffmpeg():
    installed = shutil.which("ffmpeg")
    if installed:
        return installed
    local = Path(__file__).resolve().parents[1] / "artifacts/tools/python"
    if local.exists():
        sys.path.insert(0, str(local))
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def prepare(source, output, peak_db=-6.0, channels=1):
    source, output = Path(source).resolve(), Path(output).resolve()
    if source == output or output.exists():
        raise ValueError("Choose a new output WAV; originals and existing versions are preserved.")
    decoded = subprocess.run(
        [find_ffmpeg(), "-v", "error", "-i", str(source), "-f", "s16le", "-acodec", "pcm_s16le",
         "-ar", "48000", "-ac", str(channels), "pipe:1"],
        capture_output=True, check=True,
    ).stdout
    pcm = array.array("h")
    pcm.frombytes(decoded)
    if sys.byteorder != "little":
        pcm.byteswap()
    if not pcm:
        raise ValueError("Source contains no audio samples.")
    dc = [sum(pcm[c::channels]) / len(pcm[c::channels]) for c in range(channels)]
    peak = max(abs(value - dc[i % channels]) for i, value in enumerate(pcm))
    if peak < 1:
        raise ValueError("Source is silent; normalization would only amplify noise.")
    gain = min(10.0, 32767 * 10 ** (peak_db / 20) / peak)
    frames = len(pcm) // channels
    # Три миллисекунды на краях убирают щелчок; начало файла не обрезается.
    fade = min(144, max(1, frames // 2))
    for i, value in enumerate(pcm):
        frame = i // channels
        envelope = min(1.0, frame / fade, (frames - 1 - frame) / fade)
        pcm[i] = max(-32767, min(32767, round((value - dc[i % channels]) * gain * envelope)))
    final_peak = max(abs(v) for v in pcm)
    if sys.byteorder != "little":
        pcm.byteswap()
    output.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(output), "wb") as writer:
        writer.setparams((channels, 2, 48000, frames, "NONE", "not compressed"))
        writer.writeframes(pcm.tobytes())
    report = {
        "source": str(source), "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "output": str(output), "sample_rate": 48000, "channels": channels,
        "duration_seconds": frames / 48000, "peak_dbfs": 20 * math.log10(max(1, final_peak) / 32767),
        "gain_db": 20 * math.log10(gain), "dc_removed": dc, "trimmed": False,
    }
    output.with_suffix(".json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--peak-db", type=float, default=-6)
    parser.add_argument("--stereo", action="store_true")
    args = parser.parse_args()
    if not -24 <= args.peak_db <= -1:
        parser.error("--peak-db must be between -24 and -1 dBFS")
    print(json.dumps(prepare(args.source, args.output, args.peak_db, 2 if args.stereo else 1),
                     ensure_ascii=False, indent=2))
