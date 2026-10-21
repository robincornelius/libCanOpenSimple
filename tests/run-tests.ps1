param(
    [ValidateSet('x64', 'x86', 'All')]
    [string]$Platform = 'All',
    [ValidateSet('Debug', 'Release', 'All')]
    [string]$Configuration = 'All',
    [ValidateSet('net48', 'net481', 'net8')]
    [string]$Framework = 'net481',
    [string]$DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if ($Framework -eq 'net8' -and $Platform -ne 'x64')
{
    throw 'Use -Platform x64 for the validated .NET 8 test configuration.'
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild)
{
    throw 'Install MSBuild and the .NET Framework 4.8.1 targeting pack.'
}
$platforms = if ($Platform -eq 'All') { @('x64', 'x86') } else { @($Platform) }
$configurations = if ($Configuration -eq 'All') { @('Debug', 'Release') } else { @($Configuration) }
foreach ($architecture in $platforms)
{
    foreach ($variant in $configurations)
    {
        $properties = if ($Framework -eq 'net8') { @('/p:BuildNet8=true', '/p:BuildNet481=false') } else { @('/p:BuildNet8=false', '/p:BuildNet481=true') }
        $targetFramework = if ($Framework -eq 'net8') { 'net8.0' } else { $Framework }
        $properties += "/p:TargetFramework=$targetFramework"
        & $msbuild "$PSScriptRoot/ListenerRegressionTests.csproj" /t:Restore,Rebuild "/p:Platform=$architecture" "/p:Configuration=$variant" @properties /nr:false /nologo /v:minimal
        if ($LASTEXITCODE -ne 0)
        {
            throw "Listener test build failed: $architecture $variant"
        }
        if ($Framework -eq 'net8')
        {
            & $DotNet "$PSScriptRoot/build/$architecture/$variant/net8.0/ListenerRegressionTests.dll"
        }
        else
        {
            $frameworkDirectory = if ($Framework -eq 'net48') { 'net48/' } else { '' }
            & "$PSScriptRoot/build/$architecture/$variant/${frameworkDirectory}ListenerRegressionTests.exe"
        }
        if ($LASTEXITCODE -ne 0)
        {
            throw "Listener tests failed: $architecture $variant"
        }
    }
}
