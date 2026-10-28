param([ValidateRange(10, 600)][int]$TimeoutWait = 600)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $root 'results\franka_pickplace_v3_placefix4\FrankaPickPlaceV3\checkpoint.pt'
$target = Join-Path $root 'results\franka_cube_final_evaluation_v2\FrankaCubeReplay\checkpoint.pt'
$scene = Join-Path $root 'Assets\Scenes\PickPlaceCubeFinalReplay.unity'
$config = Join-Path $root 'trainer_config_cube_final_evaluation.yaml'
$learner = Join-Path $root '.venv\Scripts\mlagents-learn.exe'
foreach ($file in @($source, $scene, $config, $learner)) {
    if (!(Test-Path -LiteralPath $file)) { throw "Falta $file" }
}
$sceneText = Get-Content -LiteralPath $scene -Raw
if ($sceneText -notmatch '(?m)^\s*trainingRunName:\s*franka_cube_final_evaluation_v2\s*$' -or
    $sceneText -notmatch '(?m)^\s*numberOfArenas:\s*1\s*$') {
    throw 'Abre PickPlaceCubeFinalReplay, con una arena y logs independientes.'
}
if (!(Test-Path -LiteralPath $target)) {
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
if ($sourceHash -ne $targetHash) { throw 'El checkpoint de evaluación no coincide con PlaceFix4; no se sobrescribe.' }
Write-Host 'Evaluación sin PPO del checkpoint PlaceFix4, desde una copia verificada.'
Write-Host 'Cuando ML-Agents escuche, pulsa Play en PickPlaceCubeFinalReplay.'
Push-Location $root
try {
    & $learner $config '--run-id=franka_cube_final_evaluation_v2' '--resume' '--inference' '--deterministic' "--timeout-wait=$TimeoutWait"
    exit $LASTEXITCODE
} finally { Pop-Location }
