"""Create independent fixed-cube Unity assets from saved PlaceFix4 references.

This is a one-time, non-overwriting reconstruction. The historical scene and
prefab are read from docs/reference_placefix4_cube. The PlaceFix4 logic is read
from the archived pre-curriculum-change agent; only yaw-specific code is removed.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GUID = {
    "agent": "bfc06f9b08c14a35aa9f6762b1979e10",
    "logger": "cb512ba451e44f4898756c9288376789",
    "refs": "18d388b2fe914c05acbd9e8114f13f49",
    "spawner": "d31a332d7e374db0b763c8fb0da4ffbe",
    "prefab": "4b2235e096a24759aae47694d7acd034",
    "scene": "ea61f025dd7248139f0c77c4777877a4",
    "eval_scene": "97cd7c7d8b8142329b78b23e49b93a9f",
}


def once(path, content):
    if path.exists():
        raise FileExistsError(f"Refusing to overwrite {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def replace(text, old, new, expected=1):
    count = text.count(old)
    if count != expected:
        raise ValueError(f"Expected {expected} occurrences, found {count}: {old[:65]!r}")
    return text.replace(old, new)


def meta(path, key, kind="script"):
    if kind == "script":
        body = f"fileFormatVersion: 2\nguid: {GUID[key]}\n"
    else:
        importer = "PrefabImporter" if kind == "prefab" else "DefaultImporter"
        body = (f"fileFormatVersion: 2\nguid: {GUID[key]}\n{importer}:\n"
                "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    once(path.with_name(path.name + ".meta"), body)


def main():
    agent = (ROOT / "docs/reference_prism_v1/FrankaPickPlaceAgentV3.cs").read_text(encoding="utf-8")
    agent = replace(agent, "FrankaPickPlaceAgentV3", "FrankaPickPlaceAgentCubeReplay", agent.count("FrankaPickPlaceAgentV3"))
    agent = replace(agent, "TrainingCsvLoggerV3", "TrainingCsvLoggerCubeReplay", agent.count("TrainingCsvLoggerV3"))
    agent = replace(agent, "    private float initialObjectYawDeg;\n", "")
    agent = replace(agent,
        "        float cubeYaw = UnityEngine.Random.Range(0f, 360f);\n"
        "        initialObjectYawDeg = cubeYaw;\n"
        "        cubeRigidbody.rotation = arenaRoot.rotation *\n"
        "            Quaternion.Euler(0f, cubeYaw, 0f) * cubeInitialLocalRotation;",
        "        cubeRigidbody.rotation = arenaRoot.rotation * cubeInitialLocalRotation;")
    agent = replace(agent,
        "        // 33..34: orientación REAL del eje largo (+Z del prisma) en el marco de la arena.\n"
        "        Vector3 prismForward = arenaRoot.InverseTransformDirection(cubeTransform.forward);\n"
        "        float horizontalSquared = prismForward.x * prismForward.x + prismForward.z * prismForward.z;\n"
        "        float yawRadians = !float.IsNaN(horizontalSquared) && !float.IsInfinity(horizontalSquared) && horizontalSquared > 1e-8f\n"
        "            ? Mathf.Atan2(prismForward.x, prismForward.z) : 0f;\n"
        "        sensor.AddObservation(Mathf.Sin(2f * yawRadians));\n"
        "        sensor.AddObservation(Mathf.Cos(2f * yawRadians));\n", "")
    agent = replace(agent, "            initialObjectYawDeg = initialObjectYawDeg,\n", "")
    if any(word in agent for word in ("cubeYaw", "initialObjectYawDeg", "prismForward", "MaximumAlignmentReward")):
        raise ValueError("Prism-specific code survived in cube agent")
    path = ROOT / "Assets/Scripts/FrankaPickPlaceAgentCubeReplay.cs"
    once(path, agent)
    meta(path, "agent")

    logger = (ROOT / "docs/reference_prism_v1/TrainingCsvLoggerV3.cs").read_text(encoding="utf-8")
    logger = replace(logger, "TrainingCsvLoggerV3", "TrainingCsvLoggerCubeReplay", logger.count("TrainingCsvLoggerV3"))
    logger = replace(logger, "FrankaPickPlaceAgentV3", "FrankaPickPlaceAgentCubeReplay", logger.count("FrankaPickPlaceAgentV3"))
    logger = replace(logger, "        public float initialObjectYawDeg;\n", "")
    logger = replace(logger, '"vertical_velocity_at_preplace_eligible_mps,initial_object_yaw_deg";',
                     '"vertical_velocity_at_preplace_eligible_mps";')
    logger = replace(logger, "        Append(line, e.initialObjectYawDeg);\n", "")
    path = ROOT / "Assets/Scripts/TrainingCsvLoggerCubeReplay.cs"
    once(path, logger)
    meta(path, "logger")

    for old, new, key in (("TrainingArenaReferencesV3", "TrainingArenaReferencesCubeReplay", "refs"),
                          ("TrainingArenaSpawnerV3", "TrainingArenaSpawnerCubeReplay", "spawner")):
        code = (ROOT / f"Assets/Scripts/{old}.cs").read_text(encoding="utf-8")
        code = code.replace("TrainingArenaReferencesV3", "TrainingArenaReferencesCubeReplay")
        code = code.replace("TrainingArenaSpawnerV3", "TrainingArenaSpawnerCubeReplay")
        code = code.replace("FrankaPickPlaceAgentV3", "FrankaPickPlaceAgentCubeReplay")
        code = code.replace("TrainingCsvLoggerV3", "TrainingCsvLoggerCubeReplay")
        path = ROOT / f"Assets/Scripts/{new}.cs"
        once(path, code)
        meta(path, key)

    prefab = (ROOT / "docs/reference_placefix4_cube/TrainingArenaV3.prefab.txt").read_text(encoding="utf-8")
    prefab = prefab.replace("guid: f22fd222805a5d64d87154cf7aeceece",
                            f"guid: {GUID['agent']}")
    prefab = prefab.replace("guid: de3a8feadc0ed8549a7c0af30030fcde",
                            f"guid: {GUID['refs']}")
    prefab = replace(prefab, "m_Name: TrainingArenaV3", "m_Name: TrainingArenaCubeReplay")
    prefab = replace(prefab, "m_BehaviorName: FrankaPickPlaceV3", "m_BehaviorName: FrankaCubeReplay")
    prefab = replace(prefab, "Assembly-CSharp::TrainingArenaReferencesV3",
                     "Assembly-CSharp::TrainingArenaReferencesCubeReplay")
    prefab = replace(prefab, "Assembly-CSharp::FrankaPickPlaceAgentV3",
                     "Assembly-CSharp::FrankaPickPlaceAgentCubeReplay")
    if "VectorObservationSize: 33" not in prefab or "m_NumContinuousActions: 8" not in prefab:
        raise ValueError("Historical prefab lacks 33/8 interface")
    path = ROOT / "Assets/Prefabs/TrainingArenaCubeReplay.prefab"
    once(path, prefab)
    meta(path, "prefab", "prefab")

    scene = (ROOT / "docs/reference_placefix4_cube/PickPlaceV3.unity.txt").read_text(encoding="utf-8")
    scene = scene.replace("guid: 8015088dd42c89b43a6204cd7fc84f9e", f"guid: {GUID['prefab']}")
    scene = scene.replace("guid: de3a8feadc0ed8549a7c0af30030fcde", f"guid: {GUID['refs']}")
    scene = scene.replace("guid: 34bf29d6f6ece1e42bc32173bafc485a", f"guid: {GUID['spawner']}")
    scene = replace(scene, "Assembly-CSharp::TrainingArenaReferencesV3",
                    "Assembly-CSharp::TrainingArenaReferencesCubeReplay")
    scene = replace(scene, "Assembly-CSharp::TrainingArenaSpawnerV3",
                    "Assembly-CSharp::TrainingArenaSpawnerCubeReplay")
    scene = replace(scene, "m_Name: TrainingArenaSpawnerV3", "m_Name: TrainingArenaSpawnerCubeReplay")
    scene = replace(scene, "trainingRunName: franka_pickplace_v3_placefix4",
                    "trainingRunName: franka_cube_replay_from_scratch_v2")
    if "numberOfArenas: 50" not in scene or "timeScale: 1" not in scene:
        raise ValueError("Historical scene lacks 50 arenas / timeScale 1")
    path = ROOT / "Assets/Scenes/PickPlaceCubeReplay.unity"
    once(path, scene)
    meta(path, "scene", "scene")

    eval_scene = replace(scene, "numberOfArenas: 50", "numberOfArenas: 1")
    eval_scene = replace(eval_scene, "trainingRunName: franka_cube_replay_from_scratch_v2",
                         "trainingRunName: franka_cube_final_evaluation_v2")
    path = ROOT / "Assets/Scenes/PickPlaceCubeFinalReplay.unity"
    once(path, eval_scene)
    meta(path, "eval_scene", "scene")
    print("Created isolated cube agent, logger, arena, 50-arena training scene and 1-arena evaluation scene")


if __name__ == "__main__":
    main()
