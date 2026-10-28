"""Freeze Prism V2 policy/critic weights for a new Prism V3 run, without PPO.

ML-Agents --initialize-from loads Adam as well as the networks. This checkpoint
keeps both trained networks and their normalizers exactly, while resetting Adam
and the run step before the native loader uses it.
"""

import hashlib
import json
from pathlib import Path

import torch
import yaml
from mlagents.plugins.trainer_type import get_default_trainer_types
from mlagents.trainers.model_saver.torch_model_saver import TorchModelSaver
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
from mlagents.trainers.settings import TrainerSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.base_env import (
    ActionSpec, BehaviorSpec, DimensionProperty, ObservationSpec, ObservationType,
)

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "results/franka_pickplace_v3_prism_v2/FrankaPickPlaceV3/checkpoint.pt"
TRANSFER_RUN = "franka_pickplace_v3_prism_v3_transfer"
DESTINATION = ROOT / "results" / TRANSFER_RUN / "FrankaPickPlaceV3" / "checkpoint.pt"
MANIFEST = DESTINATION.parent / "transfer_manifest.json"
BEHAVIOR = "FrankaPickPlaceV3"
INPUT_WEIGHT = "network_body._body_endoder.seq_layers.0.weight"


def build(settings):
    spec = BehaviorSpec(
        [ObservationSpec((35,), (DimensionProperty.NONE,),
                         ObservationType.DEFAULT, "VectorSensor")],
        ActionSpec(8, ()),
    )
    policy = TorchPolicy(0, spec, settings.network_settings, SimpleActor,
                         {"conditional_sigma": False, "tanh_squash": False})
    optimizer = TorchPPOOptimizer(policy, settings)
    modules = {**policy.get_modules(), **optimizer.get_modules()}
    return policy, optimizer, modules


def validate(source, transfer, settings):
    old_policy, old_optimizer, old = build(settings)
    new_policy, new_optimizer, new = build(settings)
    if set(source) != set(old) or set(transfer) != set(new):
        raise RuntimeError("Checkpoint module names differ from installed ML-Agents")
    for name in ("Policy", "Optimizer:critic"):
        if set(source[name]) != set(old[name].state_dict()):
            raise RuntimeError(f"Unexpected network keys in {name}")
        old[name].load_state_dict(source[name], strict=True)
        new[name].load_state_dict(transfer[name], strict=True)
        for key, tensor in source[name].items():
            if not torch.equal(tensor, transfer[name][key]):
                raise RuntimeError(f"Weight or normalizer changed: {name}/{key}")
        if tuple(transfer[name][INPUT_WEIGHT].shape) != (256, 35):
            raise RuntimeError(f"Wrong observation size in {name}")
    new["global_step"].load_state_dict(transfer["global_step"])
    new["Optimizer:value_optimizer"].load_state_dict(transfer["Optimizer:value_optimizer"])
    if new_policy.get_current_step() != 0:
        raise RuntimeError("Transfer step is not zero")
    state = new["Optimizer:value_optimizer"].state_dict()
    if state["state"] or state["param_groups"][0]["lr"] != 0.0003:
        raise RuntimeError("Adam state is not fresh")
    with torch.no_grad():
        observation = torch.zeros(2, 35)
        for previous, current in (
            (old_policy.actor.network_body, new_policy.actor.network_body),
            (old_optimizer._critic.network_body, new_optimizer._critic.network_body),
        ):
            before, _ = previous([observation])
            after, _ = current([observation])
            if not torch.equal(before, after) or not torch.isfinite(after).all():
                raise RuntimeError("Actor or critic forward pass changed")
        actor_output = new_policy.actor([observation])
        if tuple(actor_output[2].shape) != (2, 8) or not torch.isfinite(actor_output[2]).all():
            raise RuntimeError("Actor action output is invalid")
        values, _ = new_optimizer._critic([observation])
        if not values or not all(torch.isfinite(v).all() for v in values.values()):
            raise RuntimeError("Critic value output is invalid")
    print("STRICT NETWORK LOAD / 35 OBSERVATIONS / 8 ACTIONS / FRESH ADAM: OK")


def main():
    if not SOURCE.is_file():
        raise FileNotFoundError(SOURCE)
    if DESTINATION.exists() or MANIFEST.exists():
        raise FileExistsError("Prism V3 transfer already exists; refusing to overwrite")
    get_default_trainer_types()
    with (ROOT / "trainer_config_v3_prism_v3.yaml").open(encoding="utf-8") as stream:
        settings = TrainerSettings.structure(yaml.safe_load(stream)["behaviors"][BEHAVIOR], TrainerSettings)
    source = torch.load(SOURCE, map_location="cpu")
    _, _, fresh = build(settings)
    if set(source) != set(fresh):
        raise RuntimeError("Checkpoint modules differ from installed ML-Agents")
    transfer = {
        "Policy": {k: v.clone() for k, v in source["Policy"].items()},
        "global_step": fresh["global_step"].state_dict(),
        "Optimizer:value_optimizer": fresh["Optimizer:value_optimizer"].state_dict(),
        "Optimizer:critic": {k: v.clone() for k, v in source["Optimizer:critic"].items()},
    }
    validate(source, transfer, settings)
    sha = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    source_step = int(next(iter(source["global_step"].values())).item())
    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    torch.save(transfer, DESTINATION)
    validate(source, torch.load(DESTINATION, map_location="cpu"), settings)
    # Exercise the same native loading path used by --initialize-from, without training.
    settings.init_path = str(DESTINATION)
    policy, optimizer, _ = build(settings)
    saver = TorchModelSaver(settings, str(ROOT / "results/franka_pickplace_v3_prism_v3/FrankaPickPlaceV3"), load=False)
    saver.register(policy)
    saver.register(optimizer)
    saver.initialize_or_load()
    if policy.get_current_step() != 0:
        raise RuntimeError("Native initialize-from did not reset the step")
    manifest = {
        "source": str(SOURCE.relative_to(ROOT)),
        "source_sha256": sha,
        "source_step": source_step,
        "transfer_run": TRANSFER_RUN,
        "target_step": 0,
        "observations": 35,
        "continuous_actions": 8,
        "actor_and_critic": "exact tensor copies, including normalization",
        "optimizer": "fresh Adam state at learning rate 0.0003",
        "torch_version": torch.__version__,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"NATIVE INITIALIZE-FROM LOAD OK; source step {source_step}; SHA256 {sha}")
    print(f"SAVED: {DESTINATION}")


if __name__ == "__main__":
    main()
