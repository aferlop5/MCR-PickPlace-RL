"""Comprueba las versiones y el handshake de ML-Agents antes de entrenar."""

from __future__ import annotations

import importlib.metadata as metadata
import sys
import warnings


EXPECTED = {
    "mlagents": "1.2.0.dev0",
    "mlagents-envs": "1.2.0.dev0",
    "torch": "2.2.2",
    "setuptools": "80.9.0",
    "protobuf": "3.20.3",
    "numpy": "1.23.5",
    "tensorboard": "2.20.0",
}


def main() -> int:
    errors: list[str] = []
    print(f"Python: {sys.version.split()[0]} ({sys.executable})")
    if not (sys.version_info[:2] == (3, 10) and 1 <= sys.version_info.micro <= 12):
        errors.append("ML-Agents 4.1.0 requiere Python 3.10.1–3.10.12")

    for package, wanted in EXPECTED.items():
        try:
            actual = metadata.version(package)
            print(f"{package}: {actual}")
            if actual != wanted:
                errors.append(f"{package}: se esperaba {wanted}, se encontró {actual}")
        except metadata.PackageNotFoundError:
            errors.append(f"falta el paquete {package}")

    with warnings.catch_warnings():
        warnings.simplefilter("ignore", DeprecationWarning)
        warnings.simplefilter("ignore", UserWarning)
        try:
            from distutils.version import StrictVersion

            parsed = StrictVersion("1.5.0")
            print(f"StrictVersion('1.5.0').version: {getattr(parsed, 'version', None)}")
            if parsed.version != (1, 5, 0):
                errors.append("StrictVersion no devuelve la tupla (1, 5, 0)")
        except (ImportError, AttributeError) as exc:
            errors.append(f"StrictVersion no funciona: {exc}")

        try:
            import pkg_resources  # noqa: F401

            print("pkg_resources: OK")
        except ImportError as exc:
            errors.append(f"pkg_resources no se puede importar: {exc}")

        try:
            from mlagents_envs.environment import UnityEnvironment

            compatible = UnityEnvironment._check_communication_compatibility(
                "1.5.0", UnityEnvironment.API_VERSION, "4.1.0"
            )
            print(f"ML-Agents handshake 1.5.0: {compatible}")
            if not compatible:
                errors.append("el handshake de versiones 1.5.0 no es compatible")
        except (ImportError, AttributeError, TypeError) as exc:
            errors.append(f"el chequeo de comunicación falla: {exc}")

    if errors:
        print("TRAINING ENVIRONMENT FAILED")
        for error in errors:
            print(f"- {error}")
        return 1
    print("TRAINING ENVIRONMENT OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
