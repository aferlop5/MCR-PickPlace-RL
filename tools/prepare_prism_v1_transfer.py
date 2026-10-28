"""Convert the PlaceFix4 33-observation checkpoint to Prism V1's 35 inputs.

This only prepares and checks weights. It never starts Unity or PPO training.
Run with the project's Python 3.10 virtual environment.
"""

import hashlib
import json
from pathlib import Path

import torch
import yaml
from mlagents.plugins.trainer_type import get_default_trainer_types
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
from mlagents.trainers.settings import TrainerSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.base_env import (
    ActionSpec,
    BehaviorSpec,
    DimensionProperty,
    ObservationSpec,
    ObservationType,
)


ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "results/franka_pickplace_v3_placefix4/FrankaPickPlaceV3/checkpoint.pt"
TRANSFER_RUN = "franka_pickplace_v3_prism_v1_transfer"
DESTINATION = ROOT / "results" / TRANSFER_RUN / "FrankaPickPlaceV3" / "checkpoint.pt"
MANIFEST = DESTINATION.parent / "transfer_manifest.json"
BEHAVIOR = "FrankaPickPlaceV3"
INPUT_WEIGHT = "network_body._body_endoder.seq_layers.0.weight"
NORMALIZER_SUFFIXES = ("running_mean", "running_variance")


def make_modules(observation_size, settings):
    spec = BehaviorSpec(
        [ObservationSpec((observation_size,), (DimensionProperty.NONE,),
                         ObservationType.DEFAULT, "VectorSensor")],
        ActionSpec(8, ()),
    )
    policy = TorchPolicy(
        0, spec, settings.network_settings, SimpleActor,
        {"conditional_sigma": False, "tanh_squash": False},
    )
    optimizer = TorchPPOOptimizer(policy, settings)
    return policy, optimizer, {**policy.get_modules(), **optimizer.get_modules()}


def converted_network(source_state, target_state, name):
    if set(source_state) != set(target_state):
        raise RuntimeError(f"{name}: network state keys differ")
    converted = {}
    resized = []
    for key, old in source_state.items():
        expected = target_state[key]
        if old.shape == expected.shape:
            converted[key] = old.clone()
            continue
        if key == INPUT_WEIGHT and tuple(old.shape) == (256, 33) and tuple(expected.shape) == (256, 35):
            new = torch.zeros_like(expected)
            new[:, :33] = old
        elif key.endswith(".normalizer.running_mean") and tuple(old.shape) == (33,) and tuple(expected.shape) == (35,):
            new = torch.zeros_like(expected)
            new[:33] = old
        elif key.endswith(".normalizer.running_variance") and tuple(old.shape) == (33,) and tuple(expected.shape) == (35,):
            step_key = key.removesuffix("running_variance") + "normalization_steps"
            steps = source_state[step_key]
            if steps.item() < 1:
                raise RuntimeError(f"{name}: invalid normalization steps")
            new = torch.empty_like(expected)
            new[:33] = old
            new[33:] = steps.to(new.dtype)
        else:
            raise RuntimeError(f"{name}: unexpected shape change {key}: {tuple(old.shape)} -> {tuple(expected.shape)}")
        converted[key] = new
        resized.append(key)
    if len(resized) != 5 or sum(key == INPUT_WEIGHT for key in resized) != 1:
        raise RuntimeError(f"{name}: expected one input weight and four normalizer tensors, found {resized}")
    return converted, resized


def validate(source, converted, settings):
    old_policy, old_optimizer, old_modules = make_modules(33, settings)
    new_policy, new_optimizer, new_modules = make_modules(35, settings)
    if set(source) != set(old_modules) or set(converted) != set(new_modules):
        raise RuntimeError("Checkpoint module names differ from the installed ML-Agents version")
    for name in ("Policy", "Optimizer:critic"):
        old_modules[name].load_state_dict(source[name], strict=True)
        new_modules[name].load_state_dict(converted[name], strict=True)
        for key, old in source[name].items():
            new = converted[name][key]
            if key == INPUT_WEIGHT:
                assert torch.equal(new[:, :33], old) and torch.count_nonzero(new[:, 33:]) == 0
            elif key.endswith(".normalizer.running_mean"):
                assert torch.equal(new[:33], old) and torch.count_nonzero(new[33:]) == 0
            elif key.endswith(".normalizer.running_variance"):
                step_key = key.removesuffix("running_variance") + "normalization_steps"
                assert torch.equal(new[:33], old)
                assert torch.all(new[33:] == source[name][step_key].to(new.dtype))
            else:
                assert torch.equal(new, old), (name, key)
    new_modules["global_step"].load_state_dict(converted["global_step"])
    new_modules["Optimizer:value_optimizer"].load_state_dict(converted["Optimizer:value_optimizer"])
    assert new_policy.get_current_step() == 0
    assert not new_modules["Optimizer:value_optimizer"].state_dict()["state"]
    with torch.no_grad():
        old_input = torch.zeros(2, 33)
        new_input = torch.cat((old_input, torch.tensor([[0., 1.], [1., 0.]])), dim=1)
        for old_net, new_net in (
            (old_policy.actor.network_body, new_policy.actor.network_body),
            (old_optimizer._critic.network_body, new_optimizer._critic.network_body),
        ):
            old_output, _ = old_net([old_input])
            new_output, _ = new_net([new_input])
            assert tuple(new_output.shape) == (2, 256)
            assert torch.isfinite(new_output).all()
            assert torch.allclose(old_output, new_output, atol=1e-6, rtol=1e-6)
        actor_output = new_policy.actor([new_input])
        assert tuple(actor_output[2].shape) == (2, 8)
        assert torch.isfinite(actor_output[2]).all()
        values, _ = new_optimizer._critic([new_input])
        assert values and all(torch.isfinite(value).all() for value in values.values())
    print("STRICT LOAD AND FORWARD OK: actor, critic, 35 observations, 8 actions; original outputs preserved")


def main():
    if not SOURCE.is_file():
        raise FileNotFoundError(SOURCE)
    get_default_trainer_types()
    with (ROOT / "trainer_config_v3_prism_v1.yaml").open(encoding="utf-8") as stream:
        settings = TrainerSettings.structure(yaml.safe_load(stream)["behaviors"][BEHAVIOR], TrainerSettings)
    source = torch.load(SOURCE, map_location="cpu")
    _, _, target_modules = make_modules(35, settings)
    if set(source) != set(target_modules):
        raise RuntimeError("Unexpected checkpoint modules; conversion aborted")
    converted = {}
    resized = {}
    for name, module in target_modules.items():
        if name in ("Policy", "Optimizer:critic"):
            converted[name], resized[name] = converted_network(source[name], module.state_dict(), name)
        elif name == "global_step":
            converted[name] = module.state_dict()  # independent run starts at zero
        elif name == "Optimizer:value_optimizer":
            converted[name] = module.state_dict()  # fresh Adam state and initial learning rate
        else:
            raise RuntimeError(f"Unexpected module: {name}")
    validate(source, converted, settings)
    if MANIFEST.exists():
        raise FileExistsError("Transfer manifest already exists; refusing to overwrite it")
    if not DESTINATION.exists():
        DESTINATION.parent.mkdir(parents=True, exist_ok=True)
        torch.save(converted, DESTINATION)
    reloaded = torch.load(DESTINATION, map_location="cpu")
    validate(source, reloaded, settings)
    for name in ("Policy", "Optimizer:critic"):
        for key, value in converted[name].items():
            if not torch.equal(value, reloaded[name][key]):
                raise RuntimeError(f"Existing transfer differs: {name}/{key}")
    digest = hashlib.sha256()
    with SOURCE.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    manifest = {
        "source": str(SOURCE.relative_to(ROOT)),
        "source_sha256": digest.hexdigest(),
        "source_step": int(next(iter(source["global_step"].values())).item()),
        "transfer_run": TRANSFER_RUN,
        "target_step": 0,
        "old_observations": 33,
        "new_observations": 35,
        "resized_keys": resized,
        "optimizer": "fresh Adam state at initial learning rate",
        "torch_version": torch.__version__,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"SAVED: {DESTINATION}")
    print(f"SOURCE STEP: {manifest['source_step']}; SHA256: {manifest['source_sha256']}")


if __name__ == "__main__":
    main()
