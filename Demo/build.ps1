# Build a Demo program for Windows: transpile the Orion, then compile it with the platform layer.
#
#   Demo\build.ps1 counter          -> Demo\build\counter.exe
#   Demo\build.ps1 counter -Run     -> build it and run it
#
# Two steps that are deliberately separable, as on Linux: the .src becomes C++, and that C++ is built
# for this machine. The program has no `main` of its own -- its `main` is `#build` and ran during the
# transpile -- so Platforms\Windows.cpp supplies one.
#
# The C++ toolchain is Visual Studio's. Note the -prerelease: an Insiders install is invisible to
# vswhere without it, and reports as no C++ toolchain at all.

param(
	[Parameter(Mandatory = $true)][string]$Program,
	[switch]$Run,
	# Cycles to run before stopping. 0 runs until Ctrl-C, which is what a deployed node does; a harness
	# that wants a transcript states a bound rather than relying on one compiled into the platform.
	[int]$Cycles = 0,

	# Stamp every cycle from zero and do not hold the rate, so two runs produce the same transcript.
	# What Demo/Tests compares against; a deployed node wants the real clock and leaves this off.
	[switch]$Deterministic
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $PSScriptRoot "build"
New-Item -ItemType Directory -Force $build | Out-Null

# ---- transpile ----

# Apps/ holds the programs -- the sources with a `#build main`. Everything beside it (Channels.src) is
# a library they share, and is reached through `#using` rather than named here.
$src = Join-Path $PSScriptRoot "Apps\$Program.src"
if (-not (Test-Path $src))
{
	$known = (Get-ChildItem (Join-Path $PSScriptRoot "Apps") -Filter *.src | ForEach-Object { $_.BaseName }) -join ", "
	throw "no such program: $Program. Apps/ holds: $known"
}

$orion = Join-Path $root "Src\Orion\bin\Debug\net9.0\Orion.exe"
if (-not (Test-Path $orion)) { throw "build the compiler first: dotnet build Src\Orion\Orion.csproj" }

$cpp = Join-Path $build "$Program.cpp"
Write-Host "== transpiling ==" -ForegroundColor Cyan
& $orion compile $src -o $cpp -l cpp -L
if ($LASTEXITCODE -ne 0) { throw "transpile failed" }

# ---- compile ----

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -prerelease -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "no Visual Studio with the C++ workload found" }

$vsdev = Join-Path $vs "Common7\Tools\VsDevCmd.bat"

# A runtime `main` is standalone: linking the executive beside it is two. No semicolon = a definition.
$standalone = Select-String -Path $cpp -Pattern '^[A-Za-z_][A-Za-z0-9_:<>]*\s+main\s*\([^;]*\)\s*$' -Quiet
if ($standalone)
{
	Write-Host "== standalone: the program owns main, so no platform layer ==" -ForegroundColor Cyan
	$sources = @($cpp)
}
else
{
	$sources = @(
		(Join-Path $PSScriptRoot "Platforms\Windows.cpp"),
		(Join-Path $PSScriptRoot "Platforms\Channels.cpp"),
		$cpp
	)
}

# $build too: the transpile wrote the program's own header there, and the platform TUs include it.
$includes = @((Join-Path $PSScriptRoot "Platforms"), (Join-Path $root "Runtimes\Cpp"), $build) |
	ForEach-Object { "/I`"$_`"" }

$exe = Join-Path $build "$Program.exe"
$epoch0 = if ($Deterministic) { 1 } else { 0 }
# The header is named after the program, so the platform layer is told which one rather than assuming.
# `\"` is what survives Windows argv parsing as a literal quote, so the macro expands to "<program>.h"
# -- an unescaped quote would end the argument and the macro would expand to a bare identifier.
$clArgs = "/nologo /std:c++20 /O2 /EHsc /W4 /DORION_CYCLES=$Cycles /DORION_EPOCH0=$epoch0 " +
	"/DORION_PROGRAM_HEADER=\`"$Program.h\`" " + ($includes -join " ") + " " +
	(($sources | ForEach-Object { "`"$_`"" }) -join " ") +
	" ws2_32.lib /Fe:`"$exe`""

# Through a batch file rather than `cmd /c "... && ..."`: the developer environment, the paths and the
# compiler flags are all quoted strings, and nesting them inside one more quoted string is where the
# quoting stops surviving. Compiled FROM the build directory so the .obj files land there, /Fo needing
# a trailing backslash inside quotes that cmd would read as escaping the quote.
$batch = Join-Path $build "_compile.cmd"
$installer = Split-Path -Parent $vswhere
@(
	"@echo off",
	# VsDevCmd shells out to vswhere and does not know where it lives, so put its own installer
	# directory on PATH first -- otherwise it reports 'vswhere.exe is not recognized' and gives up.
	"set `"PATH=$installer;%PATH%`"",
	"call `"$vsdev`" -arch=amd64 -host_arch=amd64 -no_logo >nul 2>&1",
	"cd /d `"$build`"",
	"cl $clArgs"
) | Set-Content -Path $batch -Encoding ascii

Write-Host "== compiling ==" -ForegroundColor Cyan
& cmd /c $batch
if ($LASTEXITCODE -ne 0) { throw "compile failed" }

Write-Host "built $exe" -ForegroundColor Green

if ($Run) {
	Write-Host "== running ==" -ForegroundColor Cyan
	& $exe
}
