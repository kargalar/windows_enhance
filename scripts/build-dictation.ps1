param([string]$Configuration = 'Release', [switch]$Cuda, [string]$CudaArchitectures = '75-virtual;89-real')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repositoryRoot '.upstream/whisper.cpp'
$revision = '2eeeba56e9edd762b4b38467bab96c2517163158'
$gitCommand = Get-Command git -ErrorAction SilentlyContinue
if (!$gitCommand) {
    $gitExe = @(
        (Join-Path $env:ProgramFiles 'Git/cmd/git.exe'),
        (Join-Path "${env:ProgramFiles(x86)}" 'Git/cmd/git.exe'),
        (Join-Path $env:LocalAppData 'Programs/Git/cmd/git.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($gitExe) { $env:Path = "$(Split-Path -Parent $gitExe);$env:Path" }
}
if (!(Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git for Windows is required to build the dictation engine.' }
if (!(Test-Path -LiteralPath $source)) {
    git clone --depth 1 --branch v1.8.3 https://github.com/ggml-org/whisper.cpp.git $source
    if ($LASTEXITCODE -ne 0) { throw 'whisper.cpp indirilemedi.' }
}
if ((git -C $source rev-parse HEAD) -ne $revision) { throw 'Beklenmeyen whisper.cpp sürümü.' }
$cmake = Get-ChildItem "${env:ProgramFiles(x86)}/Microsoft Visual Studio" -Recurse -Filter cmake.exe | Select-Object -First 1 -ExpandProperty FullName
if (!$cmake) { throw 'Visual Studio CMake araçları gerekli.' }
$vswhere = Join-Path "${env:ProgramFiles(x86)}" 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsVersion = if (Test-Path -LiteralPath $vswhere) { & $vswhere -latest -products '*' -property installationVersion }
$vsMajor = if ($vsVersion -match '^(\d+)\.') { [int]$Matches[1] } else { 17 }
$generator = switch ($vsMajor) {
    18 { 'Visual Studio 18 2026' }
    17 { 'Visual Studio 17 2022' }
    16 { 'Visual Studio 16 2019' }
    default { throw "Unsupported Visual Studio major version: $vsMajor" }
}
$backend = if ($Cuda) { 'cuda' } else { 'cpu' }
$build = Join-Path $repositoryRoot "native/WinForge.Whisper/bin/$backend"
$gpuOption = if ($Cuda) { 'ON' } else { 'OFF' }
$configureArguments = @()
$cachePath = Join-Path $build 'CMakeCache.txt'
if (Test-Path -LiteralPath $cachePath) {
    $sourceDirectory = (Join-Path $repositoryRoot 'native/WinForge.Whisper').Replace('\', '/')
    $cachedHome = Get-Content -LiteralPath $cachePath | Where-Object { $_ -like 'CMAKE_HOME_DIRECTORY:INTERNAL=*' } | Select-Object -First 1
    if ($cachedHome -and $cachedHome.Substring('CMAKE_HOME_DIRECTORY:INTERNAL='.Length) -ne $sourceDirectory) { $configureArguments += '--fresh' }
}
& $cmake @configureArguments -S (Join-Path $repositoryRoot 'native/WinForge.Whisper') -B $build -G $generator -A x64 "-DWHISPER_SOURCE=$source" "-DGGML_CUDA=$gpuOption" '-DGGML_NATIVE=OFF' '-DGGML_CCACHE=OFF' '-DCMAKE_WARN_DEPRECATED=OFF' "-DCMAKE_CUDA_ARCHITECTURES=$CudaArchitectures" '-DCMAKE_CUDA_FLAGS=-diag-suppress=177'
if ($LASTEXITCODE -ne 0) { throw 'Whisper yapılandırılamadı.' }
$buildLog = Join-Path $build 'build.log'
& $cmake --build $build --config $Configuration --parallel 4 *> $buildLog
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $buildLog -Tail 40; throw 'Whisper derlenemedi.' }
$destination = Join-Path $repositoryRoot "native/WinForge.Whisper/runtime/$Configuration/$backend"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $build "$Configuration/WinForge.Whisper.dll") -Destination $destination
if ($Cuda) {
    $toolkit = Get-ChildItem "$env:ProgramFiles/NVIDIA GPU Computing Toolkit/CUDA" -Directory | Sort-Object Name -Descending | Select-Object -First 1
    foreach ($pattern in @('cublas64*.dll', 'cublasLt64*.dll')) {
        Get-ChildItem -LiteralPath $toolkit.FullName -Recurse -Filter $pattern | Select-Object -First 1 | Copy-Item -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $toolkit.FullName 'EULA.txt') -Destination (Join-Path $destination 'NVIDIA-EULA.txt')
}
@{ revision = $revision; backend = $backend; sha256 = (Get-FileHash -LiteralPath (Join-Path $destination 'WinForge.Whisper.dll') -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'engine.json') -Encoding utf8
$licenses = Join-Path $repositoryRoot 'third_party/Whisper'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $licenses 'LICENSE')
