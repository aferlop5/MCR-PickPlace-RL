"""Short, read-only compatibility check for the fixed-cube final replay."""
from pathlib import Path
import hashlib
import re

import torch
import yaml
from mlagents.plugins.trainer_type import get_default_trainer_types
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
from mlagents.trainers.settings import TrainerSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.base_env import (
    ActionSpec, BehaviorSpec, DimensionProperty, ObservationSpec, ObservationType,
)

ROOT = Path(__file__).resolve().parents[1]
BEHAVIOR = "FrankaCubeReplay"
SOURCE = ROOT / "results/franka_pickplace_v3_placefix4/FrankaPickPlaceV3/checkpoint.pt"
COPY = ROOT / "results/franka_cube_final_evaluation_v2/FrankaCubeReplay/checkpoint.pt"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    get_default_trainer_types()
    with (ROOT / "trainer_config_cube_final_evaluation.yaml").open(encoding="utf-8") as f:
        settings = TrainerSettings.structure(yaml.safe_load(f)["behaviors"][BEHAVIOR], TrainerSettings)
    spec = BehaviorSpec(
        [ObservationSpec((33,), (DimensionProperty.NONE,), ObservationType.DEFAULT, "VectorSensor")],
        ActionSpec(8, ()),
    )
    policy = TorchPolicy(0, spec, settings.network_settings, SimpleActor,
                         {"conditional_sigma": False, "tanh_squash": False})
    optimizer = TorchPPOOptimizer(policy, settings)
    modules = {**policy.get_modules(), **optimizer.get_modules()}
    data = torch.load(SOURCE, map_location="cpu")
    assert set(data) == set(modules), "Checkpoint incompatible: módulos distintos"
    for name, module in modules.items():
        if isinstance(module, torch.optim.Optimizer):
            module.load_state_dict(data[name])
        else:
            module.load_state_dict(data[name], strict=True)
    weight = data["Policy"]["network_body._body_endoder.seq_layers.0.weight"]
    assert tuple(weight.shape) == (256, 33), "Número de observaciones incompatible"
    assert policy.get_current_step() == 4_000_182, "Step final inesperado"
    with torch.no_grad():
        action = policy.actor([torch.zeros(2, 33)])[2]
    assert tuple(action.shape) == (2, 8) and torch.isfinite(action).all(), "Salida de acciones inválida"
    if COPY.exists():
        assert digest(SOURCE) == digest(COPY), "Copia de evaluación diferente del original"
    training_scene = (ROOT / "Assets/Scenes/PickPlaceCubeReplay.unity").read_text(encoding="utf-8")
    eval_scene = (ROOT / "Assets/Scenes/PickPlaceCubeFinalReplay.unity").read_text(encoding="utf-8")
    prefab = (ROOT / "Assets/Prefabs/TrainingArenaCubeReplay.prefab").read_text(encoding="utf-8")
    agent = (ROOT / "Assets/Scripts/FrankaPickPlaceAgentCubeReplay.cs").read_text(encoding="utf-8")
    assert re.search(r"(?m)^  numberOfArenas: 50$", training_scene)
    assert re.search(r"(?m)^  numberOfArenas: 1$", eval_scene)
    assert "trainingRunName: franka_cube_replay_from_scratch_v2" in training_scene
    assert "trainingRunName: franka_cube_final_evaluation_v2" in eval_scene
    assert "guid: 4b2235e096a24759aae47694d7acd034" in training_scene
    assert "guid: 4b2235e096a24759aae47694d7acd034" in eval_scene
    for required in ("VectorObservationSize: 33", "ContinuousActions: 8", "m_BehaviorName: FrankaCubeReplay", "DecisionPeriod: 5",
                     "MaxStep: 1000", "m_LocalScale: {x: 0.05, y: 0.05, z: 0.05}", "m_Mass: 0.1"):
        assert required in prefab, required
    assert "m_LocalRotation: {x: -0, y: -0, z: -0, w: 1}" in prefab
    for forbidden in ("Random.Range", "Random.value", "Mathf.Sin(2", "Mathf.Cos(2"):
        assert forbidden not in agent, forbidden
    old_prefab = (ROOT / "Assets/Prefabs/TrainingArenaV3.prefab").read_text(encoding="utf-8")
    assert "VectorObservationSize: 35" in old_prefab
    assert "m_BehaviorName: FrankaPickPlaceV3" in old_prefab
    print("OK: checkpoint 4,000,182; 33 observaciones; 8 acciones; 50/1 arenas; cubo 5 cm, 0.1 kg")
    print("SHA256 PlaceFix4:", digest(SOURCE))


if __name__ == "__main__":
    main()
