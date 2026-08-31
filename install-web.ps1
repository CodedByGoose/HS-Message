# HS Message -- one line installer.
#
# Run this in PowerShell:
#
#   irm https://raw.githubusercontent.com/CodedByGoose/HS-Message/main/install-web.ps1 | iex
#
# It finds Hearthstone, installs BepInEx if it is not already there, downloads
# the latest HS Message release, and puts it in place. Nothing is installed
# system wide: everything lives inside your Hearthstone folder, and the
# uninstall step is deleting a couple of files.

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo = "CodedByGoose/HS-Message"
$BepInExVersion = "5.4.23.5"

function Say($msg) { Write-Host $msg }
function Problem($msg) { Write-Host "Problem: $msg" }

Say "HS Message installer"
Say ""

# --- find Hearthstone ------------------------------------------------------

function Find-Hearthstone {
    if ($env:HS_DIR -and (Test-Path (Join-Path $env:HS_DIR "Hearthstone.exe"))) {
        return $env:HS_DIR
    }

    $roots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    )

    foreach ($root in $roots) {
        $found = Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
            $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            if ($p.DisplayName -match 'Hearthstone' -and $p.InstallLocation) { $p.InstallLocation }
        } | Select-Object -First 1

        if ($found -and (Test-Path (Join-Path $found "Hearthstone.exe"))) { return $found }
    }

    foreach ($guess in @(
        "C:\Program Files (x86)\Hearthstone",
        "C:\Program Files\Hearthstone",
        "D:\Hearthstone",
        "D:\Games\Hearthstone")) {
        if (Test-Path (Join-Path $guess "Hearthstone.exe")) { return $guess }
    }

    return $null
}

$hs = Find-Hearthstone

if (-not $hs) {
    Say "Could not find Hearthstone automatically."
    $typed = Read-Host "Type the full path to your Hearthstone folder"
    if ($typed -and (Test-Path (Join-Path $typed "Hearthstone.exe"))) {
        $hs = $typed
    }
    else {
        Problem "That folder does not contain Hearthstone.exe. Nothing was changed."
        return
    }
}

Say "Found Hearthstone at $hs"

# --- checks ----------------------------------------------------------------

if (Get-Process -Name "Hearthstone" -ErrorAction SilentlyContinue) {
    Problem "Hearthstone is running. Close it completely, then run this again."
    return
}

if (-not (Test-Path (Join-Path $hs "Hearthstone_Data\Managed\Accessibility\Tolk.dll"))) {
    Say ""
    Say "Warning: Hearthstone Access does not appear to be installed here."
    Say "HS Message sits on top of it and does nothing without it."
    Say "Get it from https://hearthstoneaccess.com and then run this again."
    Say ""
}

$needsAdmin = $false
try {
    $probe = Join-Path $hs ".hsm_writetest"
    [IO.File]::WriteAllText($probe, "x")
    Remove-Item $probe -Force
}
catch {
    $needsAdmin = $true
}

if ($needsAdmin) {
    $elevated = ([Security.Principal.WindowsPrincipal] `
        [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($elevated) {
        Problem "Cannot write to $hs even as administrator. Check the folder permissions."
        return
    }

    Say ""
    Say "This needs permission to write to the Hearthstone folder."
    Say "Windows will ask you to confirm. Say yes, and the install carries on"
    Say "in a new window."
    Say ""

    $inner = "irm https://raw.githubusercontent.com/$Repo/main/install-web.ps1 | iex; " +
             "Write-Host ''; Read-Host 'Press Enter to close'"

    try {
        Start-Process powershell -Verb RunAs -ArgumentList @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $inner)
    }
    catch {
        Problem "Permission was refused, so nothing was changed."
        Say "You can try again, or reopen PowerShell as administrator yourself:"
        Say "  press the Windows key, type powershell, then press Shift+Ctrl+Enter"
    }

    return
}

$tmp = Join-Path $env:TEMP ("hsmessage_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $tmp | Out-Null

try {
    # --- BepInEx -----------------------------------------------------------

    if (Test-Path (Join-Path $hs "winhttp.dll")) {
        Say "BepInEx is already installed, leaving it alone."
    }
    else {
        Say "Downloading BepInEx $BepInExVersion..."
        $zip = Join-Path $tmp "bepinex.zip"
        Invoke-WebRequest -UseBasicParsing -OutFile $zip `
            -Uri "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"

        $extract = Join-Path $tmp "bepinex"
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)

        Say "Installing BepInEx..."
        Get-ChildItem $extract | ForEach-Object {
            Copy-Item $_.FullName -Destination $hs -Recurse -Force
        }

        if (-not (Test-Path (Join-Path $hs "winhttp.dll"))) {
            throw "BepInEx did not install correctly."
        }
    }

    $plugins = Join-Path $hs "BepInEx\plugins"
    New-Item -ItemType Directory -Force $plugins | Out-Null

    # Pre-release builds shipped under a different file name. Leaving it behind
    # would have BepInEx trying to load the same plugin twice.
    $legacy = Join-Path $plugins "HearthstoneChatBuffer.dll"
    if (Test-Path $legacy) {
        Remove-Item $legacy -Force
        Say "Removed an older copy under the previous file name."
    }

    # --- the plugin --------------------------------------------------------

    Say "Finding the latest HS Message release..."
    $release = Invoke-RestMethod -UseBasicParsing `
        -Uri "https://api.github.com/repos/$Repo/releases/latest" `
        -Headers @{ "User-Agent" = "HS-Message-Installer" }

    $asset = $release.assets | Where-Object { $_.name -eq "HSMessage.dll" } | Select-Object -First 1
    if (-not $asset) { throw "The latest release has no HSMessage.dll attached." }

    Say "Downloading HS Message $($release.tag_name)..."
    Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url `
        -OutFile (Join-Path $plugins "HSMessage.dll")

    Say ""
    Say "Done."
    Say ""
    Say "Start Hearthstone and press Alt+H to hear the list of commands."
    Say "Alt+1 reads the newest message someone sent you. Alt+M replies to them."
    Say ""
    Say "To remove it later, delete this file:"
    Say "  $plugins\HSMessage.dll"
    Say "To remove BepInEx as well, also delete winhttp.dll from $hs"
}
catch {
    Problem $_.Exception.Message
    Say "Nothing further was changed."
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
