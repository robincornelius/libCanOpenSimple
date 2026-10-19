param(
    [ValidateSet('x64', 'Win32', 'All')]
    [string]$Platform = 'All',
    [ValidateSet('Debug', 'Release', 'All')]
    [string]$Configuration = 'All'
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
$cmakeDirectory = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin'
$cmake = Join-Path $cmakeDirectory 'cmake.exe'
$ctest = Join-Path $cmakeDirectory 'ctest.exe'
if (-not (Test-Path $cmake))
{
    throw 'Install Visual Studio 2026 with v145 C++ tools and CMake tools.'
}
$platforms = if ($Platform -eq 'All') { @('x64', 'Win32') } else { @($Platform) }
$configurations = if ($Configuration -eq 'All') { @('Debug', 'Release') } else { @($Configuration) }
foreach ($architecture in $platforms)
{
    $buildDirectory = Join-Path $PSScriptRoot "build/$architecture"
    & $cmake -S $PSScriptRoot -B $buildDirectory -G 'Visual Studio 18 2026' -A $architecture -T v145
    if ($LASTEXITCODE -ne 0) { throw "Configure failed: $architecture" }
    foreach ($variant in $configurations)
    {
        & $cmake --build $buildDirectory --config $variant --parallel 4
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $architecture $variant" }
        & $ctest --test-dir $buildDirectory -C $variant --output-on-failure
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $architecture $variant" }
    }
}
