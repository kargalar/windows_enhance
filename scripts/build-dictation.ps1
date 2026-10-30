param([string]$Configuration = 'Release', [switch]$Cuda, [string]$CudaArchitectures = '75-virtual;89-real')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repositoryRoot '.upstream/whisper.cpp'
$revision = '2eeeba56e9edd762b4b38467bab96c2517163158'
if (!(Test-Path -LiteralPath $source)) {
    git clone --depth 1 --branch v1.8.3 https://github.com/ggml-org/whisper.cpp.git $source
    if ($LASTEXITCODE -ne 0) { throw 'whisper.cpp indirilemedi.' }
}
if ((git -C $source rev-parse HEAD) -ne $revision) { throw 'Beklenmeyen whisper.cpp sürümü.' }
$cmake = Get-ChildItem "${env:ProgramFiles(x86)}/Microsoft Visual Studio" -Recurse -Filter cmake.exe | Select-Object -First 1 -ExpandProperty FullName
if (!$cmake) { throw 'Visual Studio CMake araçları gerekli.' }
$backend = if ($Cuda) { 'cuda' } else { 'cpu' }
$build = Join-Path $repositoryRoot "native/WinForge.Whisper/bin/$backend"
$gpuOption = if ($Cuda) { 'ON' } else { 'OFF' }
& $cmake -S (Join-Path $repositoryRoot 'native/WinForge.Whisper') -B $build -G 'Visual Studio 17 2022' -A x64 "-DWHISPER_SOURCE=$source" "-DGGML_CUDA=$gpuOption" '-DGGML_NATIVE=OFF' '-DGGML_CCACHE=OFF' '-DCMAKE_WARN_DEPRECATED=OFF' "-DCMAKE_CUDA_ARCHITECTURES=$CudaArchitectures" '-DCMAKE_CUDA_FLAGS=-diag-suppress=177'
if ($LASTEXITCODE -ne 0) { throw 'Whisper yapılandırılamadı.' }
$buildLog = Join-Path $build 'build.log'
& $cmake --build $build --config $Configuration --parallel 4 *> $buildLog
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $buildLog -Tail 40; throw 'Whisper derlenemedi.' }
dotnet build (Join-Path $repositoryRoot 'src/WinForge.Dictation.Worker/WinForge.Dictation.Worker.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Dikte worker derlenemedi.' }
$destination = Join-Path $repositoryRoot "src/WinForge.Dictation.Worker/bin/$Configuration/net10.0/$backend"
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
