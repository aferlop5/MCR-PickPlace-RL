"""Valida integridad básica de episodes.csv y summary.csv de un run real."""

from __future__ import annotations

import argparse
import csv
import math
from pathlib import Path


NUMERIC = {
    "initial_distance_m", "minimum_distance_m", "final_distance_m",
    "maximum_cube_height_m", "final_cube_height_m", "cumulative_reward",
    "cube_random_x_m", "cube_random_z_m", "time_scale",
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--expected-arenas", type=int)
    args = parser.parse_args()
    directory = args.project_root / "TrainingLogs" / args.run_id
    episodes_path = directory / "episodes.csv"
    summary_path = directory / "summary.csv"
    if not episodes_path.is_file() or not summary_path.is_file():
        parser.error(f"Falta episodes.csv o summary.csv en {directory}")

    with episodes_path.open(newline="", encoding="utf-8") as source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or len(reader.fieldnames) != len(set(reader.fieldnames)):
            raise ValueError("Cabecera ausente o duplicada en episodes.csv")
        rows = list(reader)
    if not rows:
        raise ValueError("episodes.csv no tiene episodios")

    arena_ids = set()
    per_session_arena: dict[tuple[str, int], int] = {}
    last_global = 0
    for row in rows:
        if None in row or any(value is None for value in row.values()):
            raise ValueError("Número de columnas incorrecto en episodes.csv")
        arena = int(row["arena_id"])
        arena_ids.add(arena)
        key = (row["session_id"], arena)
        episode = int(row["arena_episode"])
        if episode != per_session_arena.get(key, 0) + 1:
            raise ValueError(f"Secuencia de episodios incorrecta en {key}: {episode}")
        per_session_arena[key] = episode
        global_episode = int(row["global_episode"])
        if global_episode != last_global + 1:
            raise ValueError(f"global_episode no es consecutivo: {global_episode}")
        last_global = global_episode
        for column in NUMERIC:
            value = float(row[column])
            if not math.isfinite(value):
                raise ValueError(f"{column} no es finito en episodio {global_episode}")
        if int(row["episode_steps"]) < 0:
            raise ValueError("episode_steps negativo")
    if args.expected_arenas is not None and arena_ids != set(range(args.expected_arenas)):
        raise ValueError(f"IDs encontrados {sorted(arena_ids)}, esperados 0..{args.expected_arenas - 1}")

    with summary_path.open(newline="", encoding="utf-8") as source:
        reader = csv.DictReader(source)
        if reader.fieldnames is None or len(reader.fieldnames) != len(set(reader.fieldnames)):
            raise ValueError("Cabecera ausente o duplicada en summary.csv")
        summaries = list(reader)
    if not summaries:
        raise ValueError("summary.csv no tiene ventanas")
    if any(None in row or any(value is None for value in row.values()) for row in summaries):
        raise ValueError("Número de columnas incorrecto en summary.csv")

    print(f"TRAINING LOGS OK: {len(rows)} episodios, {len(summaries)} ventanas, "
          f"arenas {sorted(arena_ids)}, global_episode {last_global}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
