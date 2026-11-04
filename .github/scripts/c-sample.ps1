<#
.SYNOPSIS
    Builds bindings/native/samples/smoke.c for one Windows target, runs it against that
    target's frisket.dll, and holds the PDF it writes to the managed CLI's.

.DESCRIPTION
    The check a 32-bit host needs, and the only one in this repository that can see a
    calling convention. On 64-bit there is one convention, so nothing run there could tell
    cdecl from stdcall; the x86 library exported stdcall for months while frisket.h said
    cdecl, and a Win32 Delphi host crashed on its first call (CLAUDE.md 8). A C program is
    the caller the header is written for, and it settles the question before it runs: it
    links against frisket.lib, whose x86 names carry the convention - _fk_render for cdecl,
    _fk_render@24 for stdcall - so a library on the wrong side of it does not link. The
    PDF it then writes is compared with the managed CLI's, because a library that returns
    FK_OK proves nothing.

    check-against-cli.py runs on x86 too, under a 32-bit Python, for the CLI the release
    ships. It cannot guard the convention: ctypes calls through libffi, which puts the stack
    back after the call, and the stdcall library passed it under Python 3.15 in 32 bits,
    document and all (4 November 2026).

    Takes the library from bindings/native/bin/<target>/publish and the import library
    from bindings/native/bin/Release/net10.0/<target>/native, where build.ps1 leaves them,
    and the managed CLI from apps/cli/bin/Release/net10.0.

.PARAMETER Target
    win-x64 or win-x86 - what an x64 machine can build and run. win-x64 is the control:
    the same script, where the convention cannot be wrong.

.PARAMETER Out
    Where the program and the PDFs go. A temporary directory when left out.

.EXAMPLE
    ./.github/scripts/c-sample.ps1 -Target win-x86
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-x86')]
    [string] $Target,

    [string] $Out
)

$ErrorActionPreference = 'Stop'
$root = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
if (-not $Out) { $Out = Join-Path ([IO.Path]::GetTempPath()) "frisket-c-sample-$Target" }
New-Item -ItemType Directory -Force $Out | Out-Null

$native = Join-Path $root "bindings/native/bin/$Target/publish/frisket.dll"
$import = Join-Path $root "bindings/native/bin/Release/net10.0/$Target/native/frisket.lib"
$cli = Join-Path $root 'apps/cli/bin/Release/net10.0/Frisket.Cli.dll'
foreach ($needed in $native, $import, $cli) {
    if (-not (Test-Path $needed)) { throw "$needed is not there. Run build.ps1 -Target $Target, and build the CLI." }
}

# The compiler for the target, from an x64 host: vcvarsall's own spelling of the pair.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$env:PATH = "$installer;$env:PATH"
$vs = & (Join-Path $installer 'vswhere.exe') -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'No Visual Studio with the C++ tools.' }
$vcvarsall = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$arch = @{ 'win-x64' = 'x64'; 'win-x86' = 'x64_x86' }[$Target]

$exe = Join-Path $Out 'smoke.exe'
$source = Join-Path $root 'bindings\native\samples\smoke.c'

# cd first: vcvarsall gives up in silence on a working directory it cannot cope with
# (bindings/native/README.md).
cmd /c "cd /d `"$root`" && call `"$vcvarsall`" $arch >nul && cl /nologo /W4 /O2 /MT `"$source`" /Fo`"$Out\smoke.obj`" /Fe:`"$exe`" /link `"$import`""
if ($LASTEXITCODE -ne 0) { throw "smoke.c did not build against the $Target import library." }
Copy-Item $native $Out -Force

$template = Join-Path $root 'tests/output/acme/acme.template.json'
$data = Join-Path $root 'bindings/native/samples/accented.data.json'
$fonts = Join-Path $root 'engine/Fonts'
$face = Join-Path $root 'engine/Fonts/mulish-font/Mulish-Regular.ttf'
$fromC = Join-Path $Out 'smoke.pdf'

Push-Location $Out
try {
    & $exe $fonts $template $data $fromC $face
    if ($LASTEXITCODE -ne 0) { throw "smoke.exe failed on $Target (exit $LASTEXITCODE)." }
} finally {
    Pop-Location
}

# The same fonts as the sample, so the two documents can be the same one.
dotnet $cli render -t $template -d $data --fonts $fonts -o (Join-Path $Out 'cli') --format pdf | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The managed CLI did not render.' }

python (Join-Path $root 'tools/compare-pdf.py') $fromC (Join-Path $Out 'cli/accented.data.pdf')
if ($LASTEXITCODE -ne 0) { throw "The C sample on $Target and the managed CLI wrote different documents." }
Write-Host "$Target : the C sample links, runs and writes what the CLI writes"
