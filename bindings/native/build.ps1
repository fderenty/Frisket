<#
.SYNOPSIS
    Publishes the native library for one target, on Windows.

.DESCRIPTION
    The settings - runtime identifier, configuration, output directory - are not here.
    They are in Properties\PublishProfiles\<target>.pubxml, so that Visual Studio's
    Publish dialog and this script say the same thing by reading the same file rather
    than by agreeing to. What stays here is what a profile cannot say.

    Which is this. ILCompiler finds the linker by running findvcvarsall.bat, which
    calls vcvarsall.bat and takes the FIRST line of its output as the path to link.exe.
    When vswhere.exe is not reachable, vcvarsall still works but prints a complaint
    first - and that complaint becomes the linker path. The build then fails with an
    error naming "'vswhere.exe' is not recognized" as a program, which is a long way
    from what is actually wrong.

    vswhere lives beside the installer and is not on PATH by default, so this puts it
    there. That is also what makes the Publish button work from inside Visual Studio,
    which runs with its own installer directory reachable.

    Loading the C++ environment by hand is kept as a fallback, for a machine where
    that is not enough: the vcvars script for the target, and
    -p:IlcUseEnvironmentalTools=true to tell ILCompiler to use what is on PATH rather
    than go looking.

    A win-arm64 build additionally needs the ARM64 C++ tools, a separate component of
    the Visual Studio installer. win-x86 needs nothing more: the x86 tools come with
    the Desktop development with C++ workload.

    Linux targets are built on Linux; see build.sh.

.PARAMETER Target
    win-x64, win-arm64 or win-x86.

.PARAMETER Version
    The version the library reports through fk_version - a release passes its tag.
    Left out, the build carries Directory.Build.props' version, with a -dev suffix.

.EXAMPLE
    ./bindings/native/build.ps1 -Target win-x64

.EXAMPLE
    ./bindings/native/build.ps1 -Target win-x86
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string] $Target = 'win-x64',

    [string] $Version,

    [switch] $UseEnvironmentalTools
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Frisket.Native.csproj'
$profilePath = Join-Path $PSScriptRoot "Properties\PublishProfiles\$Target.pubxml"

if (-not (Test-Path $profilePath)) { throw "No publish profile at $profilePath." }

# Where vswhere lives. Reachable rather than found: see above for what happens when it
# is not, and how little the resulting error says about it.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path (Join-Path $installer 'vswhere.exe')) {
    $env:PATH = "$installer;$env:PATH"
} else {
    Write-Warning "vswhere.exe is not beside the installer; falling back to vcvars."
    $UseEnvironmentalTools = $true
}

Write-Host "Target  : $Target"
Write-Host "Profile : $profilePath"

$versioned = if ($Version) { "-p:Version=$Version" } else { "" }

if ($UseEnvironmentalTools) {
    # The script that sets up a compiler for the target, from an x64 host. It was
    # vcvars64.bat whatever the target, which links x64 code into an ARM64 or x86 build.
    $script = @{ 'win-x64' = 'vcvars64.bat'; 'win-arm64' = 'vcvarsamd64_arm64.bat'; 'win-x86' = 'vcvarsamd64_x86.bat' }[$Target]
    $vcvars = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\$script"
        "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\$script"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $vcvars) { throw "No $script found. Install the Desktop development with C++ workload." }

    Write-Host "Toolchain: $vcvars"
    cmd /c "call `"$vcvars`" >nul 2>&1 && dotnet publish `"$project`" -p:PublishProfile=$Target -p:IlcUseEnvironmentalTools=true $versioned --nologo"
} else {
    if ($Version) {
        dotnet publish $project -p:PublishProfile=$Target -p:Version=$Version --nologo
    } else {
        dotnet publish $project -p:PublishProfile=$Target --nologo
    }
}

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed for $Target. For win-arm64, check that the ARM64 C++ build tools are installed."
}

# Where it went, read from the profile rather than guessed at. The script used to look
# in two places and take whichever existed, which is a way of not knowing.
$published = Join-Path $PSScriptRoot ([xml](Get-Content $profilePath)).Project.PropertyGroup.PublishDir
$library = Join-Path $published 'frisket.dll'

if (-not (Test-Path $library)) { throw "frisket.dll is not in $published." }

Write-Host ""
Get-Item $library | Select-Object Name, Length, LastWriteTime
