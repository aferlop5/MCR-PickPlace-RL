"""List retained from-scratch checkpoints with real TensorBoard metrics.

Run after training has produced checkpoints. This reads only the new replay run.
"""
import csv
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RUN = "franka_cube_replay_from_scratch_v2"
MODELS = ROOT / "results" / RUN / "FrankaCubeReplay"
OUTPUT = ROOT / "TimelapseCube" / "manifest.csv"
TAGS = {
    "reward": "Environment/Cumulative Reward",
    "reach": "V3/ReachRate",
    "grasp": "V3/GraspConfirmedRate",
    "lift": "V3/LiftRate",
    "controlled_place": "V3/ControlledPlaceSuccessRate",
    "lesson": "Environment/Lesson Number/lift_goal_m",
}
LESSONS = {0: "Lift05cm", 1: "Lift10cm", 2: "Lift20cm", 3: "PickPlace30cm"}


def main():
    try:
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    except ImportError:
        sys.path.append(str(ROOT / ".venv/Lib/site-packages"))
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    checkpoints = []
    for path in MODELS.glob("FrankaCubeReplay-*.pt"):
        match = re.fullmatch(r"FrankaCubeReplay-(\d+)\.pt", path.name)
        if match:
            checkpoints.append((int(match.group(1)), path))
    if not checkpoints:
        raise SystemExit("Todavía no hay checkpoints numerados del nuevo entrenamiento.")
    acc = EventAccumulator(str(MODELS), size_guidance={"scalars": 0})
    acc.Reload()
    available = set(acc.Tags().get("scalars", []))
    data = {name: [(v.step, v.value) for v in acc.Scalars(tag)] if tag in available else []
            for name, tag in TAGS.items()}
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=["step", "checkpoint", "lesson", "reward", "reach",
                                                  "grasp", "lift", "controlled_place"])
        writer.writeheader()
        for step, path in sorted(checkpoints):
            record = {"step": step, "checkpoint": str(path.relative_to(ROOT))}
            for name in TAGS:
                observed = [value for measured_step, value in data[name] if measured_step <= step]
                record[name] = observed[-1] if observed else ""
            if record["lesson"] != "":
                record["lesson"] = LESSONS.get(round(float(record["lesson"])),
                                                f"Lesson {record['lesson']}")
            writer.writerow(record)
    print(f"{len(checkpoints)} checkpoints reales -> {OUTPUT}")


if __name__ == "__main__":
    main()
