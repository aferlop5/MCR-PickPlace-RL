"""Summarize physical grip and lift evidence from the Lift36 episode log."""

import argparse
import csv
from collections import Counter, defaultdict
from pathlib import Path
from statistics import mean


def describe(name: str, rows: list[dict[str, str]]) -> None:
    if not rows:
        return
    n = len(rows)
    rate = lambda field: sum(row[field] == "1" for row in rows) / n
    avg = lambda field: mean(float(row[field]) for row in rows)
    reasons = Counter(row["terminal_reason"] for row in rows)
    print(f"{name}: {n} episodios; razones={dict(reasons)}")
    print(f"  Reach {rate('reached_cube'):.1%}; agarre sostenido {rate('was_grasped'):.1%}; "
          f"Lift {rate('was_lifted'):.1%}; Place {rate('placed_successfully'):.1%}")
    print(f"  Contacto bilateral {avg('bilateral_contact_actions'):.1f} acciones/episodio; "
          f"sujeción {avg('holding_actions'):.1f}; "
          f"racha máxima {avg('max_consecutive_holding_actions'):.1f}")
    print(f"  Máx. altura sujetando {avg('maximum_held_cube_height_m'):.4f} m; "
          f"máx. altura total {avg('maximum_cube_height_m'):.4f} m")
    print(f"  Cierres {avg('gripper_close_attempts'):.1f}; "
          f"aperturas {avg('gripper_open_attempts'):.1f}; "
          f"acciones cerca de límites {avg('near_joint_limit_actions'):.1f}; "
          f"margen mínimo {avg('minimum_joint_limit_margin_deg'):.1f}°")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-id", default="franka_pickplace_v3_safe50")
    parser.add_argument("--last", type=int, default=100)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    if args.last < 1:
        parser.error("--last debe ser al menos 1")
    path = args.root / "TrainingLogs" / args.run_id / "episodes.csv"
    with path.open(newline="", encoding="utf-8-sig") as stream:
        rows = [row for row in csv.DictReader(stream)
                if row["terminal_reason"] != "manual_stop"]
    if not rows:
        raise SystemExit("No hay episodios terminados para analizar.")
    required = {"lift_goal_m", "bilateral_contact_actions", "holding_actions",
                "max_consecutive_holding_actions", "maximum_held_cube_height_m",
                "near_joint_limit_actions", "minimum_joint_limit_margin_deg"}
    missing = required - rows[0].keys()
    if missing:
        raise SystemExit(f"El CSV no pertenece a Lift36; faltan {sorted(missing)}")
    describe(f"Últimos {min(args.last, len(rows))}", rows[-args.last:])
    by_goal: dict[float, list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        by_goal[round(float(row["lift_goal_m"]), 3)].append(row)
    for goal, lesson_rows in sorted(by_goal.items()):
        describe(f"Lección {goal:.2f} m", lesson_rows)


if __name__ == "__main__":
    main()
