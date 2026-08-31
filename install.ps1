# Installs BepInEx into Hearthstone, then drops the chat buffer plugin in.
#
# MUST be run from an elevated PowerShell, because Hearthstone lives under
# Program Files. To do that: press the Windows key, type "powershell",
# then press Shift+Ctrl+Enter to launch it as administrator.
#
# Usage (elevated):
#   .\install.ps1
#   .\install.ps1 -HearthstoneDir "D:\Games\Hearthstone"

[CmdletBinding()]
param(
    [string] $HearthstoneDir = "C:\Program Files (x86)\Hearthstone",
    [string] $BepInExVersion = "5.4.23.5"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition

function Say($msg) { Write-Host $msg }

# --- checks ----------------------------------------------------------------

if (-not (Test-Path (Join-Path $HearthstoneDir "Hearthstone.exe"))) {
    throw "No Hearthstone.exe in $HearthstoneDir. Pass -HearthstoneDir with the right path."
}

if (-not (Test-Path (Join-Path $HearthstoneDir "Hearthstone_Data\Managed\Accessibility\Tolk.dll"))) {
    Write-Warning "Hearthstone Access does not appear to be installed here. The plugin needs it; chat review will be silent without it."
}

# Some Hearthstone installs grant the current user write access even under
# Program Files, so test what actually matters rather than demanding admin.
try {
    $probe = Join-Path $HearthstoneDir ".hcb_writetest"
    [IO.File]::WriteAllText($probe, "x")
    Remove-Item $probe -Force
}
catch {
    throw "Cannot write to $HearthstoneDir. Reopen PowerShell as administrator (Windows key, type powershell, then Shift+Ctrl+Enter) and run this again."
}

# --- BepInEx ---------------------------------------------------------------

$winhttp = Join-Path $HearthstoneDir "winhttp.dll"

if (Test-Path $winhttp) {
    Say "BepInEx already present, leaving it alone."
}
else {
    Say "Downloading BepInEx $BepInExVersion..."

    $tmp = Join-Path $env:TEMP "bepinex_$BepInExVersion"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Force $tmp | Out-Null

    $zip = Join-Path $tmp "bepinex.zip"
    $url = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $tmp)

    Say "Installing into $HearthstoneDir..."
    Get-ChildItem $tmp -Exclude "bepinex.zip" | ForEach-Object {
        Copy-Item $_.FullName -Destination $HearthstoneDir -Recurse -Force
    }

    Remove-Item $tmp -Recurse -Force

    if (-not (Test-Path $winhttp)) {
        throw "Installed BepInEx but winhttp.dll is not where expected. The release layout may have changed."
    }

    Say "BepInEx installed."
}

$plugins = Join-Path $HearthstoneDir "BepInEx\plugins"
New-Item -ItemType Directory -Force $plugins | Out-Null

# --- plugin ----------------------------------------------------------------

$dll = Join-Path $root "bin\Release\HearthstoneChatBuffer.dll"
if (Test-Path $dll) {
    Copy-Item $dll $plugins -Force
    Say "Plugin installed to $plugins"
}
else {
    Say "No built plugin found. Run .\build.ps1 first, then .\build.ps1 -Deploy"
}

Say ""
Say "Done. Start Hearthstone and press Alt+H for the command list."
Say "If the game will not launch, delete winhttp.dll from $HearthstoneDir and it returns to normal."
