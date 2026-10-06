param([switch]$ForceReinstall)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$basePython = Join-Path $projectRoot '.python31012\cpython-3.10.12-windows-x86_64-none\python.exe'
$venvPython = Join-Path $projectRoot '.venv\Scripts\python.exe'
$checkScript = Join-Path $projectRoot 'tools\check_training_environment.py'
$source = Join-Path $projectRoot '.mlagents-src'
$expectedCommit = 'ee0a08ccae597094003844d0121317f9790a1676'

Push-Location $projectRoot
try {
    if ((Test-Path -LiteralPath $venvPython) -and (Test-Path -LiteralPath $basePython) -and
        (Test-Path -LiteralPath $source) -and !$ForceReinstall) {
        $actualCommit = git -C $source rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actualCommit.Trim() -ne $expectedCommit) {
            throw "El código de ML-Agents debe ser $expectedCommit; encontrado $actualCommit. No se cambia el clon automáticamente."
        }
        & $venvPython $checkScript
        if ($LASTEXITCODE -eq 0) {
            & $venvPython -m pip check
            if ($LASTEXITCODE -eq 0) {
                Write-Host 'El entorno ya está correcto; no se reinstaló nada.'
                exit 0
            }
        }
    }

    if (!(Test-Path -LiteralPath $basePython)) {
        $uvZip = Join-Path $env:TEMP 'pickplacerl-uv-win64.zip'
        $uvHome = Join-Path $env:TEMP 'pickplacerl-uv-win64'
        Invoke-WebRequest 'https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip' -OutFile $uvZip
        Expand-Archive -LiteralPath $uvZip -DestinationPath $uvHome -Force
        & (Join-Path $uvHome 'uv.exe') python install 3.10.12 --install-dir '.python31012' --no-bin --no-registry
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo instalar Python 3.10.12.' }
    }
    & $basePython --version
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.10.12 no arranca.' }
    if (!(Test-Path -LiteralPath $venvPython)) {
        & $basePython -m venv .venv
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el venv.' }
    }

    if (!(Test-Path -LiteralPath $source)) {
        git clone --depth 1 --branch release/4.1.0 https://github.com/Unity-Technologies/ml-agents.git .mlagents-src
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo clonar ML-Agents.' }
    }
    $actualCommit = git -C $source rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actualCommit.Trim() -ne $expectedCommit) {
        throw "El código de ML-Agents debe ser $expectedCommit; encontrado $actualCommit. No se cambia el clon automáticamente."
    }

    & $venvPython -m pip install --upgrade 'pip==26.2.1' 'setuptools==80.9.0' 'wheel==0.48.0'
    if ($LASTEXITCODE -ne 0) { throw 'Falló la instalación de pip/setuptools/wheel.' }
    & $venvPython -m pip install 'torch==2.2.2'
    if ($LASTEXITCODE -ne 0) { throw 'Falló la instalación de PyTorch.' }
    & $venvPython -m pip install (Join-Path $source 'ml-agents-envs')
    if ($LASTEXITCODE -ne 0) { throw 'Falló ml-agents-envs.' }
    & $venvPython -m pip install (Join-Path $source 'ml-agents')
    if ($LASTEXITCODE -ne 0) { throw 'Falló ml-agents.' }
    & $venvPython -m pip install 'setuptools==80.9.0' 'protobuf==3.20.3' 'numpy==1.23.5' 'tensorboard==2.20.0'
    if ($LASTEXITCODE -ne 0) { throw 'Falló el pin final de dependencias.' }
    & $venvPython -m pip check
    if ($LASTEXITCODE -ne 0) { throw 'pip check encontró dependencias rotas.' }
    & $venvPython $checkScript
    if ($LASTEXITCODE -ne 0) { throw 'La comprobación del entorno falló.' }
} finally {
    Pop-Location
}
