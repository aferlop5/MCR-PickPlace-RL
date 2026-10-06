"""Check the shape and basic invariants of a V3 training CSV session."""

import argparse
import csv
import math
from collections import Counter
from pathlib import Path


REQUIRED_EPISODE = (
    "session_id", "run_name", "arena_id", "arena_episode", "global_episode",
    "episode_steps", "cumulative_reward", "success", "terminal_reason",
    "number_of_arenas", "time_scale", "robot_base_offset_x_m",
    "robot_base_offset_z_m", "robot_base_local_x_m", "robot_base_local_y_m",
    "robot_base_local_z_m", "cube_spawn_local_x_m", "cube_spawn_local_y_m",
    "cube_spawn_local_z_m", "target_local_x_m", "target_local_y_m",
    "target_local_z_m", "initial_tcp_cube_distance_m",
    "minimum_tcp_cube_distance_m", "final_tcp_cube_distance_m",
    "cube_initial_height_m", "maximum_cube_height_m", "final_cube_height_m",
    "target_offset_x_m", "target_offset_z_m",
    "initial_cube_target_distance_m", "minimum_cube_target_distance_m",
    "final_cube_target_distance_m", "reached_cube", "grasp_attempted",
    "was_grasped", "was_lifted", "entered_target", "released_in_target",
    "placed_successfully", "step_reached_cube", "step_first_grasp",
    "step_first_lift", "step_entered_target", "step_success",
    "stable_target_decisions", "final_cube_linear_speed",
    "final_cube_angular_speed",
)
LIFT36_EPISODE = (
    "lift_goal_m", "bilateral_contact_actions", "holding_actions",
    "max_consecutive_holding_actions", "maximum_held_cube_height_m",
    "near_joint_limit_actions", "minimum_joint_limit_margin_deg",
)
SAFE50_EPISODE = (
    "minimum_manipulability", "mean_manipulability", "final_manipulability",
    "minimum_joint_limit_margin", "maximum_joint_velocity",
    "singularity_warning_count", "joint_limit_warning_count",
)
REQUIRED_SUMMARY = (
    "global_episode", "episodes_in_window", "mean_reward", "reach_rate",
    "grasp_rate", "lift_rate", "target_entry_rate", "place_success_rate",
    "mean_episode_length", "mean_min_tcp_cube_distance", "mean_max_cube_height",
    "mean_min_cube_target_distance", "mean_final_cube_target_distance",
    "mean_steps_to_reach", "mean_steps_to_lift", "mean_steps_to_place",
    "number_of_arenas", "time_scale",
)
LIFT36_SUMMARY = (
    "mean_lift_goal_m", "mean_bilateral_contact_actions", "mean_holding_actions",
    "mean_max_consecutive_holding_actions", "mean_maximum_held_cube_height_m",
    "mean_near_joint_limit_actions", "mean_minimum_joint_limit_margin_deg",
)
SAFE50_SUMMARY = (
    "mean_minimum_manipulability", "mean_minimum_joint_limit_margin",
    "mean_maximum_joint_velocity",
)


def read_csv(path: Path, required: tuple[str, ...]) -> list[dict[str, str]]:
    if not path.is_file():
        raise ValueError(f"No existe {path}")
    with path.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        missing = set(required) - set(reader.fieldnames or ())
        if missing:
            raise ValueError(f"{path.name}: faltan columnas {sorted(missing)}")
        rows = list(reader)
    if not rows:
        raise ValueError(f"{path.name}: no contiene filas")
    if any(None in row for row in rows):
        raise ValueError(f"{path.name}: hay filas con más celdas que la cabecera")
    return rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--expected-arenas", type=int, default=50)
    parser.add_argument("--base-limit-m", type=float, default=0.0)
    parser.add_argument("--target-offset-m", type=float, default=0.18)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    directory = args.root / "TrainingLogs" / args.run_id
    lift36 = (args.run_id.startswith("franka_pickplace_v3_lift36") or
              args.run_id.startswith("v3_lift36_") or
              args.run_id.startswith("franka_pickplace_v3_safe50") or
              args.run_id.startswith("v3_safe50_"))
    safe50 = args.run_id.startswith("franka_pickplace_v3_safe50") or args.run_id.startswith("v3_safe50_")
    episodes = read_csv(directory / "episodes.csv", REQUIRED_EPISODE +
                        (LIFT36_EPISODE if lift36 else ()) +
                        (SAFE50_EPISODE if safe50 else ()))
    summary = read_csv(directory / "summary.csv", REQUIRED_SUMMARY +
                       (LIFT36_SUMMARY if lift36 else ()) +
                       (SAFE50_SUMMARY if safe50 else ()))
    ids = set()
    reasons = Counter()
    offsets = set()
    session_episodes = {}
    fixed_local_positions = None
    for row in episodes:
        if row["run_name"] != args.run_id:
            raise ValueError("Se mezclaron Run IDs en episodes.csv")
        arena = int(row["arena_id"])
        count = int(row["number_of_arenas"])
        if count != args.expected_arenas or not 0 <= arena < count:
            raise ValueError(f"arena_id/número de arenas incorrecto: {arena}/{count}")
        ids.add(arena)
        reasons[row["terminal_reason"]] += 1
        key = (row["session_id"], arena)
        current = int(row["arena_episode"])
        if current <= session_episodes.get(key, 0):
            raise ValueError(f"arena_episode no aumenta para {key}")
        session_episodes[key] = current
        x = float(row["robot_base_offset_x_m"])
        z = float(row["robot_base_offset_z_m"])
        if abs(x) > args.base_limit_m + 1e-5 or abs(z) > args.base_limit_m + 1e-5:
            raise ValueError(f"Offset de base fuera de rango: {x}, {z}")
        offsets.add((round(x, 5), round(z, 5)))
        fixed = (
            float(row["robot_base_local_x_m"]) - x,
            float(row["robot_base_local_y_m"]),
            float(row["robot_base_local_z_m"]) - z,
            float(row["cube_spawn_local_x_m"]),
            float(row["cube_spawn_local_y_m"]),
            float(row["cube_spawn_local_z_m"]),
            float(row["target_local_x_m"]),
            float(row["target_local_y_m"]),
            float(row["target_local_z_m"]),
        )
        if fixed_local_positions is None:
            fixed_local_positions = fixed
        elif any(abs(a - b) > 1e-4 for a, b in zip(fixed, fixed_local_positions)):
            raise ValueError("La base nominal, el spawn del cubo o el target cambiaron entre episodios")
        tx = float(row["target_offset_x_m"])
        tz = float(row["target_offset_z_m"])
        if abs(tx) > 1e-4 or abs(tz - args.target_offset_m) > 1e-4:
            raise ValueError(f"Target local inesperado: {tx}, {tz}")
        if abs(fixed[6] - fixed[3] - tx) > 1e-4 or abs(fixed[8] - fixed[5] - tz) > 1e-4:
            raise ValueError("Los offsets de target no coinciden con sus coordenadas locales")
        for field in REQUIRED_EPISODE[11:]:
            if field in ("reached_cube", "grasp_attempted", "was_grasped", "was_lifted",
                         "entered_target", "released_in_target", "placed_successfully"):
                if row[field] not in ("0", "1"):
                    raise ValueError(f"Flag inválido: {field}={row[field]}")
            else:
                if not math.isfinite(float(row[field])):
                    raise ValueError(f"Dato no finito: {field}")
        if row["success"] == "1" and not (
            row["was_lifted"] == row["released_in_target"] ==
            row["placed_successfully"] == "1" and row["terminal_reason"] == "place_success"
        ):
            raise ValueError("Se registró éxito sin lift, release o terminal reason correcta")
        if lift36:
            goal = float(row["lift_goal_m"])
            if not 0.049 <= goal <= 0.301:
                raise ValueError(f"Objetivo de altura inválido: {goal}")
            for field in LIFT36_EPISODE:
                if not math.isfinite(float(row[field])):
                    raise ValueError(f"Dato no finito: {field}")
            if int(row["max_consecutive_holding_actions"]) > int(row["holding_actions"]):
                raise ValueError("La racha de agarre supera las acciones sujetando")
            if row["terminal_reason"] == "curriculum_lift_success" and (
                row["was_lifted"] != "1" or goal >= 0.299
            ):
                raise ValueError("Éxito de currículo sin elevación de la lección")
        if safe50:
            for field in SAFE50_EPISODE:
                value = row[field]
                if value == "" and field in (
                    "minimum_manipulability", "mean_manipulability",
                    "final_manipulability", "minimum_joint_limit_margin"
                ):
                    continue  # Episodio abortado antes de poder medir.
                if not math.isfinite(float(value)):
                    raise ValueError(f"Dato no finito: {field}")
            if int(row["singularity_warning_count"]) < 0 or int(row["joint_limit_warning_count"]) < 0:
                raise ValueError("Contador de avisos negativo")
    if ids != set(range(args.expected_arenas)):
        raise ValueError(f"IDs observados {sorted(ids)}; se esperaban 0..{args.expected_arenas-1}")
    for row in summary:
        if int(row["number_of_arenas"]) != args.expected_arenas:
            raise ValueError("summary.csv mezcla números de arenas")
        for field in REQUIRED_SUMMARY[2:] + (SAFE50_SUMMARY if safe50 else ()):
            if safe50 and row[field] == "" and field in SAFE50_SUMMARY[:2]:
                continue
            if not math.isfinite(float(row[field])):
                raise ValueError(f"Resumen no finito: {field}")
    print(f"V3 CSV OK: {len(episodes)} episodios, {len(summary)} ventanas, "
          f"{len(ids)} arenas, {len(offsets)} valores de offset, reasons={dict(reasons)}")


if __name__ == "__main__":
    main()
