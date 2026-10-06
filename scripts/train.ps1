param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]*$')]
    [string]$RunId,
    [switch]$Resume,
    [switch]$Force,
    [ValidateRange(10, 600)]
    [int]$TimeoutWait = 120
)

$ErrorActionPreference = 'Stop'
if ($Resume -and $Force) { throw 'Elige -Resume o -Force; no se pueden combinar.' }
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $projectRoot '.venv\Scripts\python.exe'
$learner = Join-Path $projectRoot '.venv\Scripts\mlagents-learn.exe'
$scene = Join-Path $projectRoot 'Assets\Scenes\SampleScene.unity'
$configuration = Join-Path $projectRoot 'trainer_config.yaml'
$runDirectory = Join-Path (Join-Path $projectRoot 'results') $RunId

if (!(Test-Path -LiteralPath $python) -or !(Test-Path -LiteralPath $learner)) {
    throw 'Falta el venv. Ejecuta primero .\scripts\setup_training_env.ps1.'
}
& $python (Join-Path $projectRoot 'tools\check_training_environment.py')
if ($LASTEXITCODE -ne 0) { throw 'El entorno Python no está listo.' }

$sceneText = Get-Content -LiteralPath $scene -Raw
if ($sceneText -notmatch '(?m)^\s*trainingRunName:\s*(\S+)\s*$') {
    throw 'SampleScene no tiene trainingRunName en TrainingArenaSpawner.'
}
if ($Matches[1] -ne $RunId) {
    throw "El Inspector/escena usa trainingRunName=$($Matches[1]). Guarda el mismo nombre $RunId antes de entrenar."
}
if ($Resume) {
    $checkpoint = Join-Path $runDirectory 'FrankaLift\checkpoint.pt'
    if (!(Test-Path -LiteralPath $checkpoint)) { throw "No existe el checkpoint para reanudar: $checkpoint" }
} elseif ((Test-Path -LiteralPath $runDirectory) -and !$Force) {
    throw "Ya existe $runDirectory. Usa -Resume para continuar o -Force solo si quieres sobrescribirlo."
}

$arguments = @($configuration, "--run-id=$RunId", "--timeout-wait=$TimeoutWait")
if ($Resume) { $arguments += '--resume' }
if ($Force) {
    Write-Warning "-Force permite a ML-Agents sobrescribir results/$RunId. TrainingLogs no se borra."
    $arguments += '--force'
}
Push-Location $projectRoot
try {
    & $learner @arguments
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
