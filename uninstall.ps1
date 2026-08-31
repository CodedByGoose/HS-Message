# Removes the chat buffer plugin, and optionally BepInEx along with it.
#
# Run from an elevated PowerShell.
#
#   .\uninstall.ps1              # remove just the plugin
#   .\uninstall.ps1 -All         # remove BepInEx entirely, back to stock

[CmdletBinding()]
param(
    [string] $HearthstoneDir = "C:\Program Files (x86)\Hearthstone",
    [switch] $All,
    [switch] $KeepLogs
)

$ErrorActionPreference = "Stop"

function Say($msg) { Write-Host $msg }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script needs administrator rights. Reopen PowerShell as administrator."
}

$plugin = Join-Path $HearthstoneDir "BepInEx\plugins\HearthstoneChatBuffer.dll"
if (Test-Path $plugin) {
    Remove-Item $plugin -Force
    Say "Removed the plugin."
}
else {
    Say "Plugin was not installed."
}

if ($All) {
    if (-not $KeepLogs) {
        $logs = Join-Path $HearthstoneDir "BepInEx\chat-logs"
        if (Test-Path $logs) {
            Say "Note: chat logs at $logs are about to be deleted. Re-run with -KeepLogs to save them."
        }
    }
    else {
        $logs = Join-Path $HearthstoneDir "BepInEx\chat-logs"
        if (Test-Path $logs) {
            $dest = Join-Path $env:USERPROFILE "Documents\hearthstone-chat-logs"
            New-Item -ItemType Directory -Force $dest | Out-Null
            Copy-Item "$logs\*" $dest -Force
            Say "Chat logs copied to $dest"
        }
    }

    foreach ($item in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "BepInEx")) {
        $path = Join-Path $HearthstoneDir $item
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force
            Say "Removed $item"
        }
    }

    Say "BepInEx fully removed. Hearthstone is back to stock plus Hearthstone Access."
}
else {
    Say "BepInEx left in place. Re-run with -All to remove it entirely."
}
