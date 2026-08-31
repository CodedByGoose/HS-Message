# Builds HearthstoneChatBuffer.dll.
#
# Does NOT need administrator rights. It fetches BepInEx into a local lib\
# folder purely to compile against, and reads the Unity assemblies straight out
# of your Hearthstone install.
#
# Usage:
#   .\build.ps1
#   .\build.ps1 -HearthstoneDir "D:\Games\Hearthstone"
#   .\build.ps1 -Deploy          # also copy the DLL into the game (needs admin)

[CmdletBinding()]
param(
    [string] $HearthstoneDir = "C:\Program Files (x86)\Hearthstone",
    [string] $BepInExVersion = "5.4.23.5",
    [switch] $Deploy
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$lib = Join-Path $root "lib"

function Say($msg) { Write-Host $msg }

# --- locate dotnet ---------------------------------------------------------

$dotnet = $null
foreach ($candidate in @(
    (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"),
    "$env:ProgramFiles\dotnet\dotnet.exe")) {
    if (Test-Path $candidate) {
        # Must be an SDK install, not just a runtime.
        $sdkDir = Join-Path (Split-Path -Parent $candidate) "sdk"
        if (Test-Path $sdkDir) { $dotnet = $candidate; break }
    }
}

if (-not $dotnet) {
    throw "No .NET SDK found. Install it with: https://dot.net/v1/dotnet-install.ps1 -Channel 8.0"
}

Say "Using SDK at $dotnet"

# --- sanity check the game install ----------------------------------------

$managed = Join-Path $HearthstoneDir "Hearthstone_Data\Managed"
if (-not (Test-Path (Join-Path $managed "UnityEngine.CoreModule.dll"))) {
    throw "Could not find Unity assemblies under $managed. Pass -HearthstoneDir with the right path."
}

if (-not (Test-Path (Join-Path $managed "Assembly-CSharp.dll"))) {
    Write-Warning "Assembly-CSharp.dll missing. Is this really a Hearthstone install?"
}

# --- fetch BepInEx to compile against -------------------------------------

$coreDir = Join-Path $lib "BepInEx\core"
if (-not (Test-Path (Join-Path $coreDir "BepInEx.dll"))) {
    Say "Fetching BepInEx $BepInExVersion to compile against..."

    New-Item -ItemType Directory -Force $lib | Out-Null
    $zip = Join-Path $lib "BepInEx_win_x64_$BepInExVersion.zip"
    $url = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $lib)
    Remove-Item $zip -Force

    if (-not (Test-Path (Join-Path $coreDir "BepInEx.dll"))) {
        throw "BepInEx extracted but $coreDir\BepInEx.dll is missing. Layout may have changed."
    }
}

Say "BepInEx reference assemblies at $coreDir"

# --- build -----------------------------------------------------------------

Say "Building..."
& $dotnet build (Join-Path $root "HearthstoneChatBuffer.csproj") `
    -c Release `
    -p:HearthstoneDir="$HearthstoneDir" `
    -p:BepInExCoreDir="$coreDir"

if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

$dll = Join-Path $root "bin\Release\HearthstoneChatBuffer.dll"
if (-not (Test-Path $dll)) { throw "Build reported success but $dll is missing." }

Say ""
Say "Built: $dll"

# --- optional deploy -------------------------------------------------------

if ($Deploy) {
    $plugins = Join-Path $HearthstoneDir "BepInEx\plugins"
    if (-not (Test-Path $plugins)) {
        throw "BepInEx is not installed in the game yet. Run install.ps1 as administrator first."
    }

    try {
        Copy-Item $dll $plugins -Force
        Say "Deployed to $plugins"
    }
    catch {
        throw "Could not copy into $plugins. Run this from an elevated PowerShell. ($($_.Exception.Message))"
    }
}
else {
    Say "To install it, run install.ps1 as administrator, then: .\build.ps1 -Deploy"
}
