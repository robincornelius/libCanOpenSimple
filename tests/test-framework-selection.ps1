param(
    [string]$DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild)
{
    throw 'Install Visual Studio MSBuild and the .NET Framework 4.8.1 targeting pack.'
}
$library = [IO.Path]::GetFullPath("$PSScriptRoot/../libCanopenSimple.csproj")
$folder = Join-Path $PSScriptRoot ("build/framework-selection-" + [Guid]::NewGuid().ToString('N'))
New-Item $folder -ItemType Directory -Force | Out-Null

foreach ($configuration in @('Debug', 'Release'))
{
    # Match an IDE build: both targets, without the solution helper's selection flags.
    & $msbuild $library /t:Restore,Rebuild /p:Platform=x64 "/p:Configuration=$configuration" /p:ImportDirectoryBuildProps=false /nr:false /nologo /v:quiet
    if ($LASTEXITCODE -ne 0)
    {
        throw "Dual-target library build failed: $configuration"
    }
    foreach ($framework in @('net481', 'net8.0'))
    {
        $consumer = Join-Path $folder "$configuration/$framework"
        New-Item $consumer -ItemType Directory -Force | Out-Null
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$framework</TargetFramework>
    <OutputType>Exe</OutputType>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$library" />
  </ItemGroup>
</Project>
"@ | Set-Content "$consumer/Consumer.csproj" -Encoding UTF8
        @'
using System;
using System.Reflection;
using System.Runtime.Versioning;

internal static class Program
{
    private static int Main()
    {
        var can = new libCanopenSimple.libCanopenSimple();
        string actual = can.GetType().Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName;
        can.close();
#if NETFRAMEWORK
        const string expected = ".NETFramework,Version=v4.8.1";
#else
        const string expected = ".NETCoreApp,Version=v8.0";
#endif
        Console.WriteLine("Expected " + expected + "; loaded " + actual);
        return actual == expected ? 0 : 1;
    }
}
'@ | Set-Content "$consumer/Program.cs" -Encoding UTF8
        # Do not rebuild the reference: that would conceal an overwritten output.
        & $msbuild "$consumer/Consumer.csproj" /t:Restore,Build /p:BuildProjectReferences=false /p:Platform=x64 "/p:Configuration=$configuration" /p:ImportDirectoryBuildProps=false /nr:false /nologo /v:minimal
        if ($LASTEXITCODE -ne 0)
        {
            throw "Consumer failed after dual-target build: $framework $configuration"
        }
        $output = "$consumer/bin/x64/$configuration/$framework/Consumer"
        if ($framework -eq 'net481')
        {
            & "$output.exe"
        }
        else
        {
            & $DotNet "$output.dll"
        }
        if ($LASTEXITCODE -ne 0)
        {
            throw "Consumer loaded the wrong runtime target: $framework $configuration"
        }
    }
}
Write-Host 'PASS: unfiltered dual-target builds preserve independent Framework/.NET 8 outputs and consumer references in Debug/Release'
