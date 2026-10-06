"""Exporta todos los scalars reales de results/<run-id> a un CSV sin tocar los eventos."""

from __future__ import annotations

import argparse
import csv
from pathlib import Path
import re

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-id", required=True, help="Run ID de ML-Agents")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]*", args.run_id):
        parser.error("--run-id solo admite letras, números, guion y guion bajo")

    root = Path(__file__).resolve().parents[1]
    source = root / "results" / args.run_id
    if not source.is_dir():
        parser.error(f"No existe {source}")
    files = sorted(source.rglob("events.out.tfevents.*"))
    if not files:
        parser.error(f"No hay archivos de eventos en {source}")

    rows: list[tuple[str, str, int, float, float]] = []
    for event_file in files:
        events = EventAccumulator(str(event_file), size_guidance={"scalars": 0}).Reload()
        for metric in events.Tags().get("scalars", []):
            for sample in events.Scalars(metric):
                rows.append((args.run_id, metric, sample.step, sample.wall_time, sample.value))
    rows.sort(key=lambda row: (row[1], row[2], row[3]))

    destination = root / "TrainingLogs" / args.run_id / "tensorboard_metrics.csv"
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("w", newline="", encoding="utf-8") as output:
        writer = csv.writer(output)
        writer.writerow(("run_id", "metric", "step", "wall_time", "value"))
        writer.writerows(rows)
    print(f"{len(rows)} scalars de {len(files)} archivos exportados a {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
