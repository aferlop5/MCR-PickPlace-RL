param(
    [switch]$Resume,
    [switch]$RecordTimelapse,
    [switch]$CheckOnly,
    [ValidateRange(10, 600)][int]$TimeoutWait = 600
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId = 'franka_cube_replay_from_scratch_v2'
$python = Join-Path $root '.venv\Scripts\python.exe'
$learner = Join-Path $root '.venv\Scripts\mlagents-learn.exe'
$config = Join-Path $root 'trainer_config_cube_replay_from_scratch.yaml'
$scene = Join-Path $root 'Assets\Scenes\PickPlaceCubeReplay.unity'
$prefab = Join-Path $root 'Assets\Prefabs\TrainingArenaCubeReplay.prefab'
$agent = Join-Path $root 'Assets\Scripts\FrankaPickPlaceAgentCubeReplay.cs'
$checkpoint = Join-Path $root "results\$runId\FrankaCubeReplay\checkpoint.pt"
$resultDir = Join-Path $root "results\$runId"
$logDir = Join-Path $root "TrainingLogs\$runId"

foreach ($file in @($python, $learner, $config, $scene, $prefab, $agent)) {
    if (!(Test-Path -LiteralPath $file)) { throw "Falta $file" }
}
$sceneText = Get-Content -LiteralPath $scene -Raw
$prefabText = Get-Content -LiteralPath $prefab -Raw
$agentText = Get-Content -LiteralPath $agent -Raw
$configText = Get-Content -LiteralPath $config -Raw
if ($sceneText -notmatch '(?m)^\s*trainingRunName:\s*franka_cube_replay_from_scratch_v2\s*$' -or
    $sceneText -notmatch '(?m)^\s*numberOfArenas:\s*50\s*$' -or
    $sceneText -notmatch 'm_SourcePrefab: \{fileID: 100100000, guid: 4b2235e096a24759aae47694d7acd034, type: 3\}' -or
    $sceneText -notmatch '(?m)^\s*timeScale:\s*1\s*$') {
    throw 'Abre la escena PickPlaceCubeReplay: run independiente, 50 arenas y timeScale 1.'
}
if ($prefabText -notmatch '(?m)^\s*VectorObservationSize:\s*33\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_NumContinuousActions:\s*8\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_BehaviorName:\s*FrankaCubeReplay\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_LocalScale:\s*\{x: 0\.05, y: 0\.05, z: 0\.05\}\s*$' -or
    $prefabText -notmatch '(?m)^\s*MaxStep:\s*1000\s*$' -or
    $prefabText -notmatch '(?m)^\s*m_Model:\s*\{fileID: 0\}\s*$') {
    throw 'El prefab debe usar cubo de 5 cm, Behavior FrankaCubeReplay, 33 observaciones, 8 acciones, MaxStep 1000 y Model None.'
}
if ($agentText -match 'Random\.Range|Random\.value|Quaternion\.Euler\(0f,\s*cubeYaw') {
    throw 'El agente del cubo contiene aleatorización de orientación.'
}
if ($configText -match '(?m)^\s*init_path\s*:' -or
    $configText -notmatch '(?m)^\s*keep_checkpoints:\s*30\s*$' -or
    $configText -notmatch '(?m)^\s*- name:\s*Lift05cm\s*$' -or
    $configText -notmatch '(?m)^\s*- name:\s*PickPlace30cm\s*$') {
    throw 'Configuración de currículum o conservación de checkpoints incorrecta.'
}
if ($Resume) {
    if (!(Test-Path -LiteralPath $checkpoint)) { throw "No se puede reanudar sin checkpoint propio: $checkpoint" }
} elseif (Test-Path -LiteralPath $resultDir) {
    $resultsRoot = (Resolve-Path -LiteralPath (Join-Path $root 'results')).Path
    $existingRun = Get-Item -LiteralPath $resultDir
    $resolvedRun = (Resolve-Path -LiteralPath $resultDir).Path
    $expectedRun = Join-Path $resultsRoot $runId
    if ($resolvedRun -ne $expectedRun -or
        ($existingRun.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'La ruta del run no coincide con la carpeta esperada o es un enlace; no se mueve.'
    }
    $files = @(Get-ChildItem -LiteralPath $resultDir -Recurse -File -Force |
        ForEach-Object { $_.FullName.Substring($resolvedRun.Length + 1).Replace('\', '/') })
    $diagnosticFiles = @('configuration.yaml', 'run_logs/timers.json', 'run_logs/training_status.json')
    $onlyFailedHandshake = !(Test-Path -LiteralPath $logDir) -and
        !(Test-Path -LiteralPath $checkpoint) -and
        $files.Count -gt 0 -and
        @($files | Where-Object { $_ -notin $diagnosticFiles }).Count -eq 0
    if (!$onlyFailedHandshake) {
        throw 'El run ya contiene datos o archivos desconocidos. Usa -Resume si existe su checkpoint; no se sobrescribe.'
    }
    $archive = Join-Path $resultsRoot ($runId + '_failed_scene_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
    $archiveFull = [System.IO.Path]::GetFullPath($archive)
    if (!$archiveFull.StartsWith($resultsRoot + [System.IO.Path]::DirectorySeparatorChar,
                                  [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $archiveFull)) {
        throw 'La ruta de archivo del intento fallido no es segura o ya existe.'
    }
    Move-Item -LiteralPath $resolvedRun -Destination $archiveFull
    Write-Warning "Intento sin entrenamiento archivado en $archiveFull. El Run ID queda libre para un nuevo inicio del cubo."
} elseif (Test-Path -LiteralPath $logDir) {
    throw 'Ya hay CSV del run nuevo sin carpeta de resultados. No se sobrescriben.'
}
if (Test-Path -LiteralPath (Join-Path $root 'results\franka_cube_replay_from_scratch\FrankaPickPlaceV3\checkpoint.pt')) {
    Write-Warning 'Existe el intento anterior de 35 observaciones con el prisma. Se conserva, pero este comando inicia el cubo en un run V2 independiente.'
}
& $python (Join-Path $root 'tools\check_training_environment.py')
if ($LASTEXITCODE -ne 0) { throw 'El entorno Python de ML-Agents no está listo.' }
if ($CheckOnly) {
    Write-Host "PRECHECK OK: archivos de la escena del cubo, Behavior FrankaCubeReplay y run $runId listos."
    Write-Host 'Esta comprobación no puede ver qué escena está abierta ahora mismo en Unity.'
    return
}

$arguments = @($config, "--run-id=$runId", "--timeout-wait=$TimeoutWait", '--seed=12345')
if ($Resume) { $arguments += '--resume' }
if ($RecordTimelapse) {
    Write-Host 'Se conservarán hasta 30 checkpoints numerados. La reconstrucción visual posterior usa esos checkpoints.'
    Write-Host 'La cámara de la escena enfoca la primera arena mientras las 50 continúan entrenando.'
}
Write-Host "Run: $runId; pesos iniciales: $(if ($Resume) {'checkpoint propio'} else {'aleatorios nuevos'})"
Write-Host 'Abre Assets/Scenes/PickPlaceCubeReplay.unity en Unity ANTES de pulsar Play.'
Write-Host 'Si la escena abierta es PickPlaceV3 (prisma), el entrenador rechazará su Behavior de 35 observaciones.'
Write-Host 'Cuando aparezca Listening on port 5004, pulsa Play en PickPlaceCubeReplay.'
Push-Location $root
try {
    & $learner @arguments
    exit $LASTEXITCODE
} finally { Pop-Location }
