param(
    [switch]$Resume,
    [ValidateRange(10, 600)]
    [int]$TimeoutWait = 600
)

$ErrorActionPreference = 'Stop'
$runId = 'franka_pickplace_v3_placefix3'
$sourceRunId = 'franka_pickplace_v3_placefix2'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $root '.venv\Scripts\python.exe'
$learner = Join-Path $root '.venv\Scripts\mlagents-learn.exe'
$config = Join-Path $root 'trainer_config_v3_placefix3.yaml'
$scene = Join-Path $root 'Assets\Scenes\PickPlaceV3.unity'
$prefab = Join-Path $root 'Assets\Prefabs\TrainingArenaV3.prefab'
$sourceCheckpoint = Join-Path $root 'results\franka_pickplace_v3_placefix2\FrankaPickPlaceV3\checkpoint.pt'
$runDirectory = Join-Path $root 'results\franka_pickplace_v3_placefix3'
$logsDirectory = Join-Path $root 'TrainingLogs\franka_pickplace_v3_placefix3'

foreach ($path in @($python, $learner, $config, $scene, $prefab)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Falta: $path" }
}
$sceneText = Get-Content -LiteralPath $scene -Raw
$prefabText = Get-Content -LiteralPath $prefab -Raw
$configText = Get-Content -LiteralPath $config -Raw
if ($sceneText -notmatch '(?m)^\s*trainingRunName:\s*franka_pickplace_v3_placefix3\s*$' -or
    $sceneText -notmatch '(?m)^\s*numberOfArenas:\s*50\s*$' -or
    $sceneText -notmatch '(?m)^\s*timeScale:\s*1\s*$') {
    throw 'La escena debe usar placefix3, 50 arenas y timeScale 1.'
}
if ($prefabText -notmatch '(?m)^\s*m_BehaviorName:\s*FrankaPickPlaceV3\s*$' -or
    $prefabText -notmatch '(?m)^\s*VectorObservationSize:\s*33\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_NumContinuousActions:\s*8\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_Model:\s*\{fileID: 0\}\s*$' -or
    $prefabText -notmatch '(?m)^\s*MaxStep:\s*1000\s*$') {
    throw 'El prefab debe conservar Behavior, 33 observaciones, 8 acciones, Model None y MaxStep 1000.'
}
if ($configText -notmatch '(?m)^\s*max_steps:\s*3000000\s*$' -or
    $configText -notmatch '(?m)^\s*- name:\s*PickPlace30cm\s*$' -or
    $configText -notmatch '(?m)^\s*value:\s*0\.30\s*$' -or
    $configText -match '(?m)^\s*init_path\s*:') {
    throw 'La configuración debe usar 3M steps y la única lección PickPlace30cm = 0.30.'
}
if ($Resume) {
    $checkpoint = Join-Path $runDirectory 'FrankaPickPlaceV3\checkpoint.pt'
    if (!(Test-Path -LiteralPath $checkpoint)) { throw "Falta checkpoint del nuevo run: $checkpoint" }
} else {
    if (!(Test-Path -LiteralPath $sourceCheckpoint)) { throw "Falta checkpoint PlaceFix2: $sourceCheckpoint" }
    if ((Test-Path -LiteralPath $runDirectory) -or (Test-Path -LiteralPath $logsDirectory)) {
        throw 'PlaceFix3 ya tiene resultados o CSV. Usa -Resume si existe su checkpoint; no se sobrescribe.'
    }
}
& $python (Join-Path $root 'tools\check_training_environment.py')
if ($LASTEXITCODE -ne 0) { throw 'El entorno Python no está listo.' }

$arguments = @($config, "--run-id=$runId", "--timeout-wait=$TimeoutWait")
if ($Resume) { $arguments += '--resume' }
else { $arguments += "--initialize-from=$sourceRunId" }
Write-Host "Run: $runId; Behavior: FrankaPickPlaceV3; arenas: 50; max_steps: 3000000"
Write-Host "Configuración: $config"
if (!$Resume) { Write-Host "Pesos iniciales: $sourceCheckpoint (último checkpoint de PlaceFix2 al iniciar)" }
Push-Location $root
try {
    & $learner @arguments
    exit $LASTEXITCODE
} finally {
    Pop-Location
}


