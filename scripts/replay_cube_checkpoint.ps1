param(
    [Parameter(Mandatory=$true)][ValidateRange(1, 20000000)][int]$Step,
    [ValidateRange(10, 600)][int]$TimeoutWait = 600
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $root "results\franka_cube_replay_from_scratch_v2\FrankaCubeReplay\FrankaCubeReplay-$Step.pt"
$runId = "franka_cube_timelapse_step_$Step"
$target = Join-Path $root "results\$runId\FrankaCubeReplay\checkpoint.pt"
$template = Join-Path $root 'Assets\Scenes\PickPlaceCubeFinalReplay.unity'
$scene = Join-Path $root "Assets\Scenes\PickPlaceCubeTimelapse_$Step.unity"
$config = Join-Path $root 'trainer_config_cube_final_evaluation.yaml'
$learner = Join-Path $root '.venv\Scripts\mlagents-learn.exe'
foreach ($file in @($source, $template, $config, $learner)) {
    if (!(Test-Path -LiteralPath $file)) { throw "Falta $file" }
}
$sourceInfo = & (Join-Path $root '.venv\Scripts\python.exe') -c "import torch,sys; x=torch.load(sys.argv[1],map_location='cpu'); print(int(next(iter(x['global_step'].values())).item()))" $source
if ($LASTEXITCODE -ne 0 -or [int]$sourceInfo -ne $Step) { throw 'El step interno del checkpoint no coincide con su nombre.' }
if (!(Test-Path -LiteralPath $target)) {
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
    throw 'La copia del checkpoint no coincide; no se sobrescribe.'
}
$sceneText = (Get-Content -LiteralPath $template -Raw).Replace(
    'trainingRunName: franka_cube_final_evaluation_v2', "trainingRunName: $runId")
if (!(Test-Path -LiteralPath $scene)) {
    Set-Content -LiteralPath $scene -Value $sceneText -NoNewline
    $guid = [guid]::NewGuid().ToString('N')
    $meta = "fileFormatVersion: 2`nguid: $guid`nDefaultImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
    Set-Content -LiteralPath "$scene.meta" -Value $meta -NoNewline
} elseif ((Get-Content -LiteralPath $scene -Raw) -ne $sceneText) {
    throw 'La escena de este step ya existe con contenido distinto.'
}
Write-Host "Checkpoint real: PPO step $Step. Abre y guarda $scene en Unity."
Write-Host "Graba el episodio como TimelapseCube\clips\step_$Step.mp4."
Write-Host 'Cuando ML-Agents escuche, pulsa Play. No se entrena ni se modifican pesos.'
Push-Location $root
try {
    & $learner $config "--run-id=$runId" '--resume' '--inference' '--deterministic' "--timeout-wait=$TimeoutWait"
    exit $LASTEXITCODE
} finally { Pop-Location }
