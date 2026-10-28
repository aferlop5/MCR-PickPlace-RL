"""Regenerate evidence tables and vector PDF figures for the fixed-cube report.

Usage: bundled-python tools/generate_cube_report.py --output <latex-project>
Only historical cube runs are read. No checkpoint or training result is changed.
"""
import argparse
import csv
import json
import math
import statistics
import sys
from pathlib import Path

import pandas as pd
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
RUNS = ["franka_reach_v2", "franka_pickplace_v3_safe50",
        "franka_pickplace_v3_placefix1", "franka_pickplace_v3_placefix2",
        "franka_pickplace_v3_placefix3", "franka_pickplace_v3_placefix4"]
SHORT = {run: run.replace("franka_pickplace_v3_", "").replace("franka_", "") for run in RUNS}
COLORS = [(0.08, .33, .55), (.87, .40, .12), (.15, .56, .40),
          (.59, .35, .65), (.77, .60, .15), (.18, .65, .72)]
FIELDS = ["reached_cube", "grasp_confirmed", "was_lifted", "target_aligned",
          "release_ready_reached", "released_correctly", "released_too_high",
          "place_success", "placed_successfully", "controlled_place_success"]
NUMBERS = ["cumulative_reward", "episode_steps", "release_command_height_above_target_m",
           "grasp_loss_height_above_target_m", "release_confirmed_height_above_target_m",
           "reward_preplace_total", "reward_high_release_penalty_total"]


def load_episodes(run):
    path = ROOT / "TrainingLogs" / run / "episodes.csv"
    with path.open(encoding="utf-8-sig", newline="") as stream:
        columns = set(next(csv.reader(stream)))
    selected = list(columns.intersection(FIELDS + NUMBERS + ["terminal_reason", "curriculum_lesson", "global_episode", "success"]))
    data = pd.read_csv(path, usecols=selected, low_memory=False)
    valid = data[data["terminal_reason"] != "manual_stop"].copy()
    return data, valid, columns


def fraction(frame, field):
    if field not in frame or frame.empty:
        return None
    values = pd.to_numeric(frame[field], errors="coerce")
    return float(values.mean()) if values.notna().any() else None


def mean(frame, field):
    if field not in frame or frame.empty:
        return None
    values = pd.to_numeric(frame[field], errors="coerce").dropna()
    return float(values.mean()) if len(values) else None


def summarize(run, raw, frame, columns):
    recent = frame.tail(min(1000, len(frame)))
    result = {"run": run, "csv": str(ROOT / "TrainingLogs" / run / "episodes.csv"),
              "rows_total": len(raw), "manual_stop": len(raw) - len(frame),
              "episodes_valid": len(frame), "window": len(recent),
              "columns_available": {field: field in columns for field in FIELDS}}
    for field in FIELDS:
        result[field] = fraction(recent, field)
    result["physical_place_rate"] = result["place_success"] if result["place_success"] is not None \
        else result["placed_successfully"]
    for field in NUMBERS:
        result[field] = mean(recent, field)
    if "curriculum_lesson" in frame:
        result["recent_lessons"] = recent["curriculum_lesson"].fillna("").value_counts().to_dict()
    return result


def tb_scalars(run):
    try:
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    except ImportError:
        site = ROOT / ".venv" / "Lib" / "site-packages"
        sys.path.append(str(site))
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    path = ROOT / "results" / run / ("FrankaLift" if run == "franka_reach_v2" else "FrankaPickPlaceV3")
    if not path.is_dir() or not list(path.glob("events.out.tfevents.*")):
        return {}
    acc = EventAccumulator(str(path), size_guidance={"scalars": 0})
    acc.Reload()
    return {tag: [(event.step, event.value) for event in acc.Scalars(tag)]
            for tag in acc.Tags().get("scalars", [])}


def roll(points, n=5):
    return [(points[i][0], statistics.mean(v for _, v in points[max(0, i-n+1):i+1]))
            for i in range(len(points))]


def pdf_plot(path, title, xlabel, ylabel, series, note="", percent=False):
    series = [(name, [(float(x), float(y)) for x, y in points
                       if x is not None and y is not None and math.isfinite(float(y))], color)
              for name, points, color in series]
    series = [(name, points, color) for name, points, color in series if points]
    if not series:
        return False
    width, height = 680, 420
    c = canvas.Canvas(str(path), pagesize=(width, height))
    c.setTitle(title)
    c.setFont("Helvetica-Bold", 15)
    c.drawString(52, 385, title[:82])
    left, bottom, right, top = 66, 74, 635, 320 if len(series) > 6 else 340
    xs = [x for _, points, _ in series for x, _ in points]
    ys = [y for _, points, _ in series for _, y in points]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    if percent:
        ymin, ymax = 0, 1
    else:
        margin = max(.01, (ymax - ymin) * .12)
        ymin, ymax = ymin - margin, ymax + margin
    if xmin == xmax:
        xmax = xmin + 1
    if ymin == ymax:
        ymax = ymin + 1
    px = lambda x: left + (x - xmin) / (xmax - xmin) * (right - left)
    py = lambda y: bottom + (y - ymin) / (ymax - ymin) * (top - bottom)
    c.setLineWidth(.5)
    c.setStrokeColorRGB(.80, .84, .87)
    c.setFont("Helvetica", 8)
    for i in range(6):
        yval = ymin + (ymax - ymin) * i / 5
        ypos = py(yval)
        c.line(left, ypos, right, ypos)
        label = f"{100*yval:.0f}%" if percent else f"{yval:.2f}"
        c.drawRightString(left - 7, ypos - 2, label)
        xval = xmin + (xmax - xmin) * i / 5
        c.drawCentredString(px(xval), bottom - 15, f"{xval/1e6:.1f}M" if xmax > 1e6 else f"{xval:.0f}")
    c.setStrokeColorRGB(.1, .1, .1)
    c.line(left, bottom, left, top)
    c.line(left, bottom, right, bottom)
    c.setFont("Helvetica", 10)
    c.drawCentredString((left + right) / 2, 43, xlabel)
    c.saveState()
    c.translate(19, (bottom + top) / 2)
    c.rotate(90)
    c.drawCentredString(0, 0, ylabel)
    c.restoreState()
    for name, points, color in series:
        c.setStrokeColorRGB(*color)
        c.setLineWidth(1.7)
        path_obj = c.beginPath()
        for index, (x, y) in enumerate(points):
            (path_obj.moveTo if index == 0 else path_obj.lineTo)(px(x), py(y))
        c.drawPath(path_obj)
        if len(points) == 1:
            c.setFillColorRGB(*color)
            c.circle(px(points[0][0]), py(points[0][1]), 4, fill=1, stroke=0)
    c.setFont("Helvetica", 8)
    for i, (name, _, color) in enumerate(series):
        row, col = divmod(i, 3)
        x, y = 67 + col * 188, 364 - row * 12
        c.setStrokeColorRGB(*color)
        c.setLineWidth(2)
        c.line(x, y, x + 17, y)
        c.setFillColorRGB(.1, .1, .1)
        c.drawString(x + 21, y - 3, name[:26])
    if note:
        c.setFont("Helvetica", 7)
        c.drawString(67, 20, note[:115])
    c.save()
    return True


def pdf_components(path, values):
    values = [(name, float(value)) for name, value in values if value is not None]
    if not values:
        return False
    c = canvas.Canvas(str(path), pagesize=(680, 420))
    c.setTitle("Componentes de recompensa: PlaceFix4")
    c.setFont("Helvetica-Bold", 15)
    c.drawString(48, 386, "Componentes de recompensa: ultimos 1000 episodios")
    zero = 300
    maxabs = max(abs(value) for _, value in values) or 1
    c.setStrokeColorRGB(.25, .25, .25)
    c.line(zero, 57, zero, 355)
    c.setFont("Helvetica", 9)
    for i, (name, value) in enumerate(values):
        y = 345 - i * 30
        c.setFillColorRGB(.1, .1, .1)
        c.drawRightString(270, y, name)
        length = min(290, 290 * abs(value) / maxabs)
        c.setFillColorRGB(*(COLORS[i % len(COLORS)] if value >= 0 else (.72, .25, .22)))
        c.rect(zero if value >= 0 else zero - length, y - 4, length, 11, fill=1, stroke=0)
        c.setFillColorRGB(.1, .1, .1)
        if value >= 0:
            c.drawString(zero + length + 5, y - 2, f"{value:.3f}")
        else:
            c.drawRightString(zero - length - 5, y - 2, f"{value:.3f}")
    c.setFont("Helvetica", 8)
    c.drawString(48, 23, "Media por episodio valido; valores leidos de episodes.csv")
    c.save()
    return True


def pdf_optimizer_panels(path, scalars):
    pairs = [(name, scalars.get(tag, [])) for name, tag in (
        ("Policy loss", "Losses/Policy Loss"), ("Value loss", "Losses/Value Loss"),
        ("Entropy", "Policy/Entropy"), ("Learning rate", "Policy/Learning Rate"))]
    if not any(points for _, points in pairs):
        return False
    c = canvas.Canvas(str(path), pagesize=(680, 420))
    c.setTitle("Metricas de optimizacion PPO")
    c.setFont("Helvetica-Bold", 15)
    c.drawString(45, 386, "Metricas de optimizacion PPO: PlaceFix4")
    for index, (name, points) in enumerate(pairs):
        if not points:
            continue
        col, row = index % 2, index // 2
        left = 48 + col * 320
        bottom = 220 - row * 175
        right, top = left + 275, bottom + 125
        xs = [x for x, _ in points]
        ys = [y for _, y in points]
        xmin, xmax = min(xs), max(xs)
        ymin, ymax = min(ys), max(ys)
        if xmin == xmax: xmax += 1
        if ymin == ymax: ymax += 1
        c.setStrokeColorRGB(.4, .4, .4)
        c.setLineWidth(.5)
        c.rect(left, bottom, right-left, top-bottom, stroke=1, fill=0)
        c.setFont("Helvetica-Bold", 9)
        c.setFillColorRGB(.1, .1, .1)
        c.drawString(left, top + 9, name)
        c.setFont("Helvetica", 7)
        c.drawString(left, bottom - 12, f"{xmin/1e6:.1f}M pasos")
        c.drawRightString(right, bottom - 12, f"{xmax/1e6:.1f}M pasos")
        c.drawRightString(right, top + 9, f"{ymin:.4g} a {ymax:.4g}")
        p = c.beginPath()
        for i, (x, y) in enumerate(points):
            px = left + (x-xmin)/(xmax-xmin)*(right-left)
            py = bottom + (y-ymin)/(ymax-ymin)*(top-bottom)
            (p.moveTo if i == 0 else p.lineTo)(px, py)
        c.setStrokeColorRGB(*COLORS[index])
        c.setLineWidth(1.3)
        c.drawPath(p)
    c.save()
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    out = args.output.resolve()
    fig = out / "figuras"
    fig.mkdir(parents=True, exist_ok=True)
    loaded = {run: load_episodes(run) for run in RUNS}
    results = {run: summarize(run, *loaded[run]) for run in RUNS}
    tb = {run: tb_scalars(run) for run in RUNS}
    plots = []

    def create(stem, title, xlabel, ylabel, series, note="", percent=False):
        if pdf_plot(fig / (stem + ".pdf"), title, xlabel, ylabel, series, note, percent):
            plots.append(stem)

    last = RUNS[-1]
    def tag_series(tag, runs=(last,), smooth=0):
        answer = []
        for i, run in enumerate(runs):
            points = tb[run].get(tag, [])
            if points:
                answer.append((SHORT[run], roll(points, smooth) if smooth else points, COLORS[i]))
        return answer

    reward = tb[last].get("Environment/Cumulative Reward", [])
    create("01_reward", "Recompensa acumulada: PlaceFix4", "Pasos PPO", "Reward medio",
           [("Original", reward, COLORS[0]), ("Media movil 5", roll(reward), COLORS[1])],
           "TensorBoard; media movil de cinco puntos, sin concatenar runs")
    for number, name, tag in [
        (2, "reach", "V3/ReachRate"), (3, "grasp", "V3/GraspConfirmedRate"),
        (4, "lift", "V3/LiftRate"),
        (6, "physical_place", "V3/PlaceSuccessRate"),
        (7, "controlled_place", "V3/ControlledPlaceSuccessRate"),
        (8, "high_release", "V3/HighReleaseRate")]:
        create(f"{number:02d}_{name}", name.replace("_", " ").title() + ": PlaceFix4",
               "Pasos PPO", "Tasa", tag_series(tag), "TensorBoard; tasa registrada por el agente", True)
    summary_path = ROOT / "TrainingLogs" / last / "summary.csv"
    summary = pd.read_csv(summary_path, low_memory=False)
    summary = summary[pd.to_numeric(summary["episodes_in_window"], errors="coerce") >= 50]
    x = pd.to_numeric(summary["global_episode"], errors="coerce")
    create("05_target_aligned", "TargetAligned: PlaceFix4", "Episodios completados", "Tasa",
           [("TargetAligned", list(zip(x, pd.to_numeric(summary["target_aligned_rate"], errors="coerce"))), COLORS[0])],
           "CSV summary; ventanas de al menos 50 episodios, sin eje de pasos PPO", True)
    create("09_release_height", "Altura real de orden OPEN: PlaceFix4", "Episodios completados", "Altura media (m)",
           [("Orden OPEN", list(zip(x, pd.to_numeric(summary["mean_release_command_height_above_target_m"], errors="coerce"))), COLORS[0])],
           "CSV summary; altura relativa al apoyo; solo episodios con orden OPEN")
    milestones = []
    for i, cm in enumerate((30, 25, 20, 15, 10, 5)):
        col = f"preplace_{cm}cm_rate"
        if col in summary:
            milestones.append((f"{cm} cm", list(zip(x, pd.to_numeric(summary[col], errors="coerce"))), COLORS[i]))
    create("10_preplace", "Hitos PrePlace: PlaceFix4", "Episodios completados", "Tasa",
           milestones, "CSV summary; ventanas de 100 episodios, sin eje de pasos PPO", True)
    if "reward_preplace_total" in loaded[last][1]:
        data = loaded[last][1]
        y = pd.to_numeric(data["reward_preplace_total"], errors="coerce").rolling(500, min_periods=100).mean()
        create("11_reward_preplace", "Recompensa PrePlace: PlaceFix4", "Episodios completados", "Reward medio",
               [("Media movil 500", list(zip(range(1, len(data)+1), y)), COLORS[0])],
               "CSV episodes; media movil de 500 episodios")
    reward_cols = [("Reach", "reward_reach_total"), ("Grasp", "reward_grasp_total"),
                   ("Lift", "reward_lift_total"), ("Transport", "reward_transport_total"),
                   ("PrePlace", "reward_preplace_total"),
                   ("Milestones", "reward_preplace_milestones_total"),
                   ("ReleaseReady", "reward_release_ready_total"),
                   ("ControlledRelease", "reward_controlled_release_total"),
                   ("Penalizaciones", "reward_penalty_total")]
    with (ROOT / "TrainingLogs" / last / "episodes.csv").open(encoding="utf-8-sig") as stream:
        episode_columns = set(next(csv.reader(stream)))
    actual_cols = [col for _, col in reward_cols if col in episode_columns]
    extra = pd.read_csv(ROOT / "TrainingLogs" / last / "episodes.csv",
                        usecols=actual_cols + ["terminal_reason"], low_memory=False)
    extra = extra[extra.terminal_reason != "manual_stop"].tail(1000)
    bars = [(name, mean(extra, col)) for name, col in reward_cols if col in extra]
    if pdf_components(fig / "12_reward_components.pdf", bars):
        plots.append("12_reward_components")
    if pdf_optimizer_panels(fig / "13_ppo_losses.pdf", tb[last]):
        plots.append("13_ppo_losses")
    candidates = RUNS[1:]
    create("14_versiones", "Comparacion de runs: ultimos 1000", "Run (indice cronologico)", "Tasa",
           [(field, [(i, results[run][field]) for i, run in enumerate(candidates)
                     if results[run][field] is not None], COLORS[j])
            for j, field in enumerate(("was_lifted", "physical_place_rate", "controlled_place_success"))],
           "Indice 0=Safe50; 1-4=PlaceFix1-4; definiciones cambian entre versiones", True)
    create("15_exito_final", "Exito final y calidad de liberacion", "Pasos PPO", "Tasa",
           [(name, tb[last].get(tag, []), COLORS[i]) for i, (name, tag) in enumerate([
               ("Reach", "V3/ReachRate"), ("Grasp", "V3/GraspConfirmedRate"),
               ("Lift", "V3/LiftRate"), ("Place fisico", "V3/PlaceSuccessRate"),
               ("Place controlado", "V3/ControlledPlaceSuccessRate")])],
           "TensorBoard; PlaceFix4 unicamente", True)
    with (out / "cube_metrics.json").open("w", encoding="utf-8") as stream:
        json.dump({"runs": results, "figures": plots,
                   "tensorboard_tags": {run: list(tb[run]) for run in RUNS}}, stream, indent=2)
    print(json.dumps({"figures": len(plots), "runs": {run: {k: results[run][k] for k in
        ("episodes_valid", "window", "was_lifted", "place_success", "controlled_place_success")}
        for run in RUNS}}, indent=2))


if __name__ == "__main__":
    main()
