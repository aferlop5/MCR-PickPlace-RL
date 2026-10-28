param(
    [switch]$Resume,
    [ValidateRange(10, 600)]
    [int]$TimeoutWait = 600
)

$ErrorActionPreference = 'Stop'
$runId = 'franka_pickplace_v3_prism_v3'
$transferRunId = 'franka_pickplace_v3_prism_v3_transfer'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $root '.venv\Scripts\python.exe'
$learner = Join-Path $root '.venv\Scripts\mlagents-learn.exe'
$config = Join-Path $root 'trainer_config_v3_prism_v3.yaml'
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
if ($sceneText -notmatch '(?m)^\s*trainingRunName:\s*franka_pickplace_v3_prism_v3\s*$' -or
    $sceneText -notmatch '(?m)^\s*numberOfArenas:\s*50\s*$' -or
    $sceneText -notmatch '(?m)^\s*timeScale:\s*1\s*$') {
    throw 'La escena debe usar Prism V3, 50 arenas y timeScale 1.'
}
if ($prefabText -notmatch '(?m)^\s*m_BehaviorName:\s*FrankaPickPlaceV3\s*$' -or
    $prefabText -notmatch '(?m)^\s*VectorObservationSize:\s*35\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_NumContinuousActions:\s*8\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_Model:\s*\{fileID: 0\}\s*$' -or
    $prefabText -notmatch '(?m)^\s*MaxStep:\s*1000\s*$') {
    throw 'El prefab debe usar FrankaPickPlaceV3, 35 observaciones, 8 acciones, Model None y MaxStep 1000.'
}
$levelMatches = [regex]::Matches($configText, '(?m)^\s*value:\s*([123])\s*$')
if ($configText -notmatch '(?m)^\s*max_steps:\s*4000000\s*$' -or
    $configText -notmatch '(?m)^\s*- name:\s*PickPlace30cm\s*$' -or
    $configText -notmatch '(?m)^\s*value:\s*0\.30\s*$' -or
    $configText -notmatch '(?m)^\s*prism_yaw_level:\s*$' -or
    $configText -notmatch '(?m)^\s*prism_focus_probability:\s*$' -or
    $configText -notmatch '(?m)^\s*- name:\s*FocusedEdgeSampling\s*$' -or
    $configText -notmatch '(?m)^\s*- name:\s*ManualOrientationLevel\s*$' -or
    $levelMatches.Count -ne 1 -or
    $configText -match '(?m)^\s*init_path\s*:') {
    throw 'El YAML debe tener 4M steps, PickPlace30cm, nivel manual 1, 2 o 3 y probabilidad focused.'
}
$level = [int]$levelMatches[0].Groups[1].Value
$focusMatch = [regex]::Match($configText, '(?ms)^\s*prism_focus_probability:\s*\r?\n\s*curriculum:\s*\r?\n\s*- name:\s*FocusedEdgeSampling\s*\r?\n\s*value:\s*([0-9.]+)\s*$')
if (!$focusMatch.Success) { throw 'Falta probabilidad focused fija.' }
$focusProbability = [double]::Parse($focusMatch.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
if ($focusProbability -lt 0 -or $focusProbability -gt 1) { throw 'prism_focus_probability debe estar entre 0 y 1.' }
if ($Resume) {
    $checkpoint = Join-Path $runDirectory 'FrankaPickPlaceV3\checkpoint.pt'
    if (!(Test-Path -LiteralPath $checkpoint)) { throw "Falta checkpoint propio para reanudar: $checkpoint" }
} else {
    foreach ($path in @($transferCheckpoint, $transferManifest)) {
        if (!(Test-Path -LiteralPath $path)) { throw "Falta transferencia Prism V2: $path" }
    }
    $manifest = Get-Content -LiteralPath $transferManifest -Raw | ConvertFrom-Json
    if ($manifest.transfer_run -ne $transferRunId -or $manifest.target_step -ne 0 -or
        $manifest.observations -ne 35 -or $manifest.continuous_actions -ne 8) {
        throw 'La transferencia preparada no corresponde a Prism V3.'
    }
    if ($manifest.source.Replace('\', '/') -ne 'results/franka_pickplace_v3_prism_v2/FrankaPickPlaceV3/checkpoint.pt') {
        throw 'La transferencia no procede de Prism V2.'
    }
    if ((Test-Path -LiteralPath $runDirectory) -or (Test-Path -LiteralPath $logsDirectory)) {
        throw 'Prism V3 ya tiene resultados o CSV. Usa -Resume si existe su checkpoint; no se sobrescribe.'
    }
}
& $python (Join-Path $root 'tools\check_training_environment.py')
if ($LASTEXITCODE -ne 0) { throw 'El entorno Python no está listo.' }

$bounds = @('30-150', '15-165', '0-180')[$level - 1]
$arguments = @($config, "--run-id=$runId", "--timeout-wait=$TimeoutWait")
if ($Resume) { $arguments += '--resume' }
else { $arguments += "--initialize-from=$transferRunId" }
Write-Host "Run: $runId; nivel $level ($bounds grados mod 180); focused: $focusProbability; arenas: 50; max_steps: 4000000"
if (!$Resume) { Write-Host "Pesos de Prism V2: $transferCheckpoint (step nuevo 0, Adam nuevo)" }
Push-Location $root
try {
    & $learner @arguments
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
