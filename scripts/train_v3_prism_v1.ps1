param(
    [switch]$Resume,
    [ValidateRange(10, 600)]
    [int]$TimeoutWait = 600
)

$ErrorActionPreference = 'Stop'
$runId = 'franka_pickplace_v3_prism_v1'
$transferRunId = 'franka_pickplace_v3_prism_v1_transfer'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $root '.venv\Scripts\python.exe'
$learner = Join-Path $root '.venv\Scripts\mlagents-learn.exe'
$config = Join-Path $root 'trainer_config_v3_prism_v1.yaml'
$scene = Join-Path $root 'Assets\Scenes\PickPlaceV3.unity'
$prefab = Join-Path $root 'Assets\Prefabs\TrainingArenaV3.prefab'
$transferCheckpoint = Join-Path $root "results\$transferRunId\FrankaPickPlaceV3\checkpoint.pt"
$transferManifest = Join-Path $root "results\$transferRunId\FrankaPickPlaceV3\transfer_manifest.json"
$runDirectory = Join-Path $root "results\$runId"
$logsDirectory = Join-Path $root "TrainingLogs\$runId"

foreach ($path in @($python, $learner, $config, $scene, $prefab)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Falta: $path" }
}
$sceneText = Get-Content -LiteralPath $scene -Raw
$prefabText = Get-Content -LiteralPath $prefab -Raw
$configText = Get-Content -LiteralPath $config -Raw
if ($sceneText -notmatch '(?m)^\s*trainingRunName:\s*franka_pickplace_v3_prism_v1\s*$' -or
    $sceneText -notmatch '(?m)^\s*numberOfArenas:\s*50\s*$' -or
    $sceneText -notmatch '(?m)^\s*timeScale:\s*1\s*$') {
    throw 'La escena debe usar prism_v1, 50 arenas y timeScale 1.'
}
if ($prefabText -notmatch '(?m)^\s*m_BehaviorName:\s*FrankaPickPlaceV3\s*$' -or
    $prefabText -notmatch '(?m)^\s*VectorObservationSize:\s*35\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_NumContinuousActions:\s*8\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_Model:\s*\{fileID: 0\}\s*$' -or
    $prefabText -notmatch '(?m)^\s*MaxStep:\s*1000\s*$') {
    throw 'El prefab debe usar Behavior FrankaPickPlaceV3, 35 observaciones, 8 acciones, Model None y MaxStep 1000.'
}
if ($configText -notmatch '(?m)^\s*max_steps:\s*4000000\s*$' -or
    $configText -notmatch '(?m)^\s*- name:\s*PickPlace30cm\s*$' -or
    $configText -notmatch '(?m)^\s*value:\s*0\.30\s*$' -or
    $configText -match '(?m)^\s*init_path\s*:') {
    throw 'La configuración debe usar 4M steps y PickPlace30cm = 0.30.'
}
if ($Resume) {
    $checkpoint = Join-Path $runDirectory 'FrankaPickPlaceV3\checkpoint.pt'
    if (!(Test-Path -LiteralPath $checkpoint)) { throw "Falta checkpoint propio para reanudar: $checkpoint" }
} else {
    foreach ($path in @($transferCheckpoint, $transferManifest)) {
        if (!(Test-Path -LiteralPath $path)) { throw "Falta transferencia Prism V1: $path" }
    }
    $manifest = Get-Content -LiteralPath $transferManifest -Raw | ConvertFrom-Json
    if ($manifest.transfer_run -ne $transferRunId -or $manifest.target_step -ne 0 -or
        $manifest.old_observations -ne 33 -or $manifest.new_observations -ne 35) {
        throw 'Los metadatos de transferencia no corresponden a Prism V1.'
    }
    if ((Test-Path -LiteralPath $runDirectory) -or (Test-Path -LiteralPath $logsDirectory)) {
        throw 'Prism V1 ya tiene resultados o CSV. Usa -Resume si existe su checkpoint; no se sobrescribe.'
    }
}
& $python (Join-Path $root 'tools\check_training_environment.py')
if ($LASTEXITCODE -ne 0) { throw 'El entorno Python no está listo.' }

$arguments = @($config, "--run-id=$runId", "--timeout-wait=$TimeoutWait")
if ($Resume) { $arguments += '--resume' }
else { $arguments += "--initialize-from=$transferRunId" }
Write-Host "Run: $runId; Behavior: FrankaPickPlaceV3; arenas: 50; max_steps: 4000000"
Write-Host "Configuración: $config"
if (!$Resume) { Write-Host "Pesos transferidos: $transferCheckpoint (contador nuevo en step 0)" }
Push-Location $root
try {
    & $learner @arguments
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
