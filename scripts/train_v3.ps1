param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]*$')]
    [string]$RunId = 'franka_pickplace_v3_safe50',
    [switch]$Resume,
    [switch]$Force,
    [ValidateRange(10, 600)]
    [int]$TimeoutWait = 120
)

$ErrorActionPreference = 'Stop'
if ($Resume -and $Force) { throw 'Elige -Resume o -Force; no se pueden combinar.' }
if ($RunId -match '^franka_reach_') { throw 'Un Run ID de Reach/V2 no puede usarse con V3.' }
if ($RunId -eq 'franka_pickplace_v3' -or $RunId -eq 'franka_pickplace_v3_fixed30' -or
    $RunId -eq 'franka_pickplace_v3_lift36') {
    throw 'Este Run ID pertenece a un entrenamiento anterior. Usa franka_pickplace_v3_safe50.'
}
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $projectRoot '.venv\Scripts\python.exe'
$learner = Join-Path $projectRoot '.venv\Scripts\mlagents-learn.exe'
$scene = Join-Path $projectRoot 'Assets\Scenes\PickPlaceV3.unity'
$configuration = Join-Path $projectRoot 'trainer_config_v3_safe50.yaml'
$prefab = Join-Path $projectRoot 'Assets\Prefabs\TrainingArenaV3.prefab'
$runDirectory = Join-Path (Join-Path $projectRoot 'results') $RunId
$logsDirectory = Join-Path (Join-Path $projectRoot 'TrainingLogs') $RunId

if (!(Test-Path -LiteralPath $python) -or !(Test-Path -LiteralPath $learner)) {
    throw 'Falta el venv. Ejecuta primero .\scripts\setup_training_env.ps1.'
}
if (!(Test-Path -LiteralPath $scene) -or !(Test-Path -LiteralPath $configuration) -or
    !(Test-Path -LiteralPath $prefab)) {
    throw 'Faltan la escena, el prefab o el trainer_config_v3_safe50.yaml.'
}
& $python (Join-Path $projectRoot 'tools\check_training_environment.py')
if ($LASTEXITCODE -ne 0) { throw 'El entorno Python no está listo.' }

$sceneText = Get-Content -LiteralPath $scene -Raw
if ($sceneText -notmatch '(?m)^\s*trainingRunName:\s*(\S+)\s*$') {
    throw 'PickPlaceV3 no tiene trainingRunName en TrainingArenaSpawnerV3.'
}
if ($Matches[1] -ne $RunId) {
    throw "La escena V3 usa trainingRunName=$($Matches[1]). Guarda el mismo nombre $RunId antes de entrenar."
}
if ($sceneText -notmatch '(?m)^\s*numberOfArenas:\s*(\d+)\s*$') {
    throw 'PickPlaceV3 no tiene numberOfArenas.'
}
$arenas = [int]$Matches[1]
if ($arenas -ne 50) { throw "Esta variante debe empezar con 50 arenas; la escena tiene $arenas" }
if ($sceneText -notmatch '(?m)^\s*columns:\s*10\s*$' -or
    $sceneText -notmatch '(?m)^\s*spacing:\s*5\s*$') {
    throw 'PickPlaceV3 debe usar 10 columnas y 5 m de separación.'
}
if ($sceneText -notmatch '(?m)^\s*timeScale:\s*1\s*$') {
    throw 'PickPlaceV3 debe comenzar con timeScale 1.'
}
$prefabText = Get-Content -LiteralPath $prefab -Raw
if ($prefabText -notmatch '(?m)^\s*m_BehaviorName:\s*FrankaPickPlaceV3\s*$' -or
    $prefabText -notmatch '(?m)^\s*VectorObservationSize:\s*33\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_NumContinuousActions:\s*8\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_Model:\s*\{fileID: 0\}\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_BehaviorType:\s*0\s*$' -or
    $prefabText -notmatch '(?m)^\s*liftThreshold:\s*0\.3\s*$' -or
    $prefabText -match '(?m)^\s*randomizeRobotBase:\s*1\s*$') {
    throw 'TrainingArenaV3 debe usar FrankaPickPlaceV3, 33 observaciones, Lift 0.30 m, base fija, 8 acciones, Behavior Type Default y ningún modelo.'
}
$configText = Get-Content -LiteralPath $configuration -Raw
if ($configText -notmatch '(?m)^\s{2}FrankaPickPlaceV3:\s*$' -or
    $configText -notmatch '(?m)^\s{2}time_scale:\s*1\s*$') {
    throw 'trainer_config_v3_safe50.yaml debe contener FrankaPickPlaceV3 y time_scale 1.'
}
if ($Resume) {
    $checkpoint = Join-Path $runDirectory 'FrankaPickPlaceV3\checkpoint.pt'
    if (!(Test-Path -LiteralPath $checkpoint)) { throw "No existe el checkpoint V3: $checkpoint" }
} elseif ((Test-Path -LiteralPath $runDirectory) -and !$Force) {
    throw "Ya existe $runDirectory. Usa -Resume o un Run ID nuevo; -Force sobrescribe deliberadamente."
}
$sourceCheckpoint = Join-Path $projectRoot 'results\franka_pickplace_v3_lift36\FrankaPickPlaceV3\checkpoint.pt'
if (!$Resume -and !(Test-Path -LiteralPath $sourceCheckpoint)) {
    throw "Falta el checkpoint de partida: $sourceCheckpoint"
}

Write-Host "Run ID: $RunId"
Write-Host 'Behavior: FrankaPickPlaceV3'
Write-Host "Arenas esperadas: $arenas"
Write-Host "Configuración: $configuration"
Write-Host "Resultados: $runDirectory"
Write-Host "CSV: $logsDirectory"
$arguments = @($configuration, "--run-id=$RunId", "--timeout-wait=$TimeoutWait")
if ($Resume) { $arguments += '--resume' }
else { $arguments += '--initialize-from=franka_pickplace_v3_lift36' }
if ($Force) {
    Write-Warning "-Force sobrescribe results/$RunId; TrainingLogs no se borra."
    $arguments += '--force'
}
Push-Location $projectRoot
try {
    & $learner @arguments
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
