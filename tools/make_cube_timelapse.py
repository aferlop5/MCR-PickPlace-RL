"""Assemble recorded checkpoint clips chronologically with real metric labels.

Clips must be named step_<step>.mp4 and captured by the operator during
inference. No training frames or metrics are fabricated.
"""
import argparse
import csv
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--clips-dir", type=Path, default=ROOT / "TimelapseCube/clips")
    parser.add_argument("--manifest", type=Path, default=ROOT / "TimelapseCube/manifest.csv")
    parser.add_argument("--output", type=Path, default=ROOT / "TimelapseCube/timelapse.mp4")
    args = parser.parse_args()
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise SystemExit("FFmpeg no está instalado. Instálalo o conserva los clips para montarlos manualmente.")
    with args.manifest.open(encoding="utf-8", newline="") as stream:
        records = {int(row["step"]): row for row in csv.DictReader(stream)}
    clips = []
    for clip in args.clips_dir.glob("step_*.mp4"):
        try:
            step = int(clip.stem.removeprefix("step_"))
        except ValueError:
            continue
        if step not in records:
            raise SystemExit(f"Sin métricas reales para el clip {clip.name}")
        clips.append((step, clip, records[step]))
    clips.sort()
    if not clips:
        raise SystemExit("No hay clips step_<pasos PPO>.mp4 para montar.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as temp:
        temp = Path(temp)
        list_file = temp / "segments.txt"
        lines = []
        for index, (step, clip, record) in enumerate(clips):
            segment = temp / f"segment_{index:03d}.mp4"
            label = (f"PPO step {step} | {record['lesson']} | "
                     f"Reward {float(record['reward']):.2f}" if record["reward"] else
                     f"PPO step {step} | {record['lesson']}")
            safe = label.replace("'", "").replace(":", " ").replace("%", "pct")
            rates = []
            for name, title in (("reach", "Reach"), ("grasp", "Grasp"),
                                ("lift", "Lift"), ("controlled_place", "ControlledPlace")):
                if record.get(name):
                    rates.append(f"{title} {100 * float(record[name]):.0f}pct")
            filter_text = ("scale=1280:720:force_original_aspect_ratio=decrease,"
                           "pad=1280:720:(ow-iw)/2:(oh-ih)/2,"
                           f"drawtext=text='{safe}':x=28:y=28:fontsize=27:fontcolor=white:"
                           "box=1:boxcolor=black@0.55")
            if rates:
                filter_text += (f",drawtext=text='{' | '.join(rates)}':x=28:y=70:"
                                "fontsize=20:fontcolor=white:box=1:boxcolor=black@0.55")
            subprocess.run([ffmpeg, "-y", "-i", str(clip), "-vf", filter_text,
                            "-an", "-r", "30", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                            str(segment)], check=True)
            lines.append(f"file '{segment.as_posix()}'")
        list_file.write_text("\n".join(lines) + "\n", encoding="utf-8")
        subprocess.run([ffmpeg, "-y", "-f", "concat", "-safe", "0", "-i",
                        str(list_file), "-c", "copy", str(args.output)], check=True)
    print(f"Vídeo creado con {len(clips)} clips reales: {args.output}")


if __name__ == "__main__":
    main()
