"""Report completed Prism episodes by yaw, sampling mode and recent window.

Uniform estimates weight each 15-degree bin equally and are omitted when
any bin in the requested range has no observations.
"""

import argparse
import csv
import math
import statistics
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
DEFAULT = ROOT / "TrainingLogs/franka_pickplace_v3_prism_v3/episodes.csv"
BOUNDS = {1: (30, 150), 2: (15, 165), 3: (0, 180)}
ALIGNMENT = "finger_short_axis_alignment_at_first_grasp"


def numeric(value):
    try:
        number = float(value)
        return number if math.isfinite(number) else None
    except (TypeError, ValueError):
        return None


def percent(rows, field):
    return 100 * sum(row[field] == "1" for row in rows) / len(rows) if rows else None


def alignment(rows):
    values = [numeric(row.get(ALIGNMENT)) for row in rows]
    values = [value for value in values if value is not None]
    return statistics.mean(values) if values else None


def fmt(value, suffix="%"):
    return f"{value:.1f}{suffix}" if value is not None else "n/a"


def fmt_alignment(value):
    return f"{value:.3f}" if value is not None else "n/a"


def select(rows, low, high):
    return [row for row in rows if low <= row["yaw"] < high]


def describe(label, rows):
    print(f"  {label}: n={len(rows)}, Lift={fmt(percent(rows, 'was_lifted'))}, "
          f"ControlledPlace={fmt(percent(rows, 'controlled_place_success'))}, "
          f"first_grasp_alignment={fmt_alignment(alignment(rows))}")


def sectors(rows, low, high, width):
    print(f"  Sectors {width} degrees [lower, upper):")
    for start in range(low, high, width):
        describe(f"[{start}, {min(start + width, high)})",
                 select(rows, start, min(start + width, high)))


def uniform_estimate(rows, low, high):
    """Equal 15-degree sector weighting, with no imputation for missing bins."""
    bins = [select(rows, start, start + 15) for start in range(low, high, 15)]
    if any(not bucket for bucket in bins):
        missing = [f"{start}-{start + 15}" for start, bucket in
                   zip(range(low, high, 15), bins) if not bucket]
        print("  Uniform 15-degree estimate: n/a; unsampled sectors: " + ", ".join(missing))
        return
    lift = statistics.mean(percent(bucket, "was_lifted") for bucket in bins)
    place = statistics.mean(percent(bucket, "controlled_place_success") for bucket in bins)
    print(f"  Uniform 15-degree estimate: Lift={fmt(lift)}, ControlledPlace={fmt(place)} "
          "(equal bin weights; descriptive, not a held-out evaluation)")


def report_window(name, rows, show_five):
    print(f"\n{name}: {len(rows)} completed episodes")
    for level in sorted({row["level"] for row in rows}):
        low, high = BOUNDS[level]
        level_rows = [row for row in rows if row["level"] == level]
        print(f"Level {level}, yaw mod 180 [{low}, {high}), n={len(level_rows)}")
        describe("all sampling modes", level_rows)
        for mode in ("focused", "uniform", "legacy"):
            mode_rows = [row for row in level_rows if row["mode"] == mode]
            if mode_rows:
                describe(mode, mode_rows)
                if level == 1 and mode != "legacy":
                    sectors(mode_rows, low, high, 15)
        if level == 1:
            for label, a, b in (("lower edge", 30, 45),
                                ("middle", 45, 135), ("upper edge", 135, 150)):
                describe(label, select(level_rows, a, b))
            uniform_estimate(level_rows, low, high)
        sectors(level_rows, low, high, 15)
        if show_five:
            sectors(level_rows, low, high, 5)


def load(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        required = {"initial_object_yaw_deg", "was_lifted", "controlled_place_success",
                    "terminal_reason", ALIGNMENT}
        if not required.issubset(reader.fieldnames or []):
            raise ValueError(f"Missing episode columns: {sorted(required - set(reader.fieldnames or []))}")
        rows = []
        for row in reader:
            if row["terminal_reason"] == "manual_stop":
                continue
            yaw = numeric(row["initial_object_yaw_deg"])
            level_number = numeric(row.get("orientation_curriculum_level"))
            level = int(level_number) if level_number is not None else 3
            if yaw is None or level not in BOUNDS:
                continue
            mod = yaw % 180.0
            low, high = BOUNDS[level]
            # UnityEngine.Random.Range(float, float) can return the upper endpoint.
            if mod == high:
                mod = math.nextafter(float(high), -math.inf)
            if not low <= mod < high:
                continue
            row["yaw"] = mod
            row["level"] = level
            mode = row.get("orientation_sampling_mode", "").strip()
            row["mode"] = mode if mode in ("focused", "uniform") else "legacy"
            rows.append(row)
        return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("episodes_csv", nargs="?", type=Path, default=DEFAULT)
    parser.add_argument("--window", type=int, default=1000,
                        help="Number of completed episodes per recent window (default: 1000)")
    parser.add_argument("--all-sectors-5deg", action="store_true",
                        help="Print 5-degree sectors for every level and window")
    args = parser.parse_args()
    if args.window < 1:
        parser.error("--window must be positive")
    rows = load(args.episodes_csv)
    print(f"Source: {args.episodes_csv}; valid completed episodes: {len(rows)}")
    report_window("All", rows, args.all_sectors_5deg)
    previous = rows[-2 * args.window:-args.window]
    if previous:
        report_window(f"Previous {args.window}", previous, args.all_sectors_5deg)
    recent = rows[-args.window:]
    if recent:
        report_window(f"Latest {args.window}", recent, True)


if __name__ == "__main__":
    main()
