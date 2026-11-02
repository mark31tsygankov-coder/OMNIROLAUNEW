# Генерация manifest.json для автообновления игры и лаунчера.
#
# Пример:
#   .\make-manifest.ps1 -GameDir "C:\OmniroGame" -BaseUrl "https://example.com/game" `
#       -LauncherVersion "1.0.1" -LauncherUrl "https://example.com/OmniroLauncher.exe" `
#       -LauncherExe ".\OmniroLauncher.exe"
#
# Затем загрузите содержимое папки игры по BaseUrl (с теми же относительными путями)
# и выложите manifest.json по адресу, указанному в manifestUrl.

param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [string]$Out = "manifest.json",
    [string]$LauncherVersion = "1.0.0",
    [string]$LauncherUrl = "",
    [string]$LauncherExe = ""
)

$root = (Resolve-Path $GameDir).Path.TrimEnd('\')

$files = Get-ChildItem -Path $root -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    [pscustomobject]@{
        path   = $rel
        url    = ($BaseUrl.TrimEnd('/') + '/' + [uri]::EscapeUriString($rel))
        sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
        size   = $_.Length
    }
}

$launcherHash = ""
if ($LauncherExe -ne "" -and (Test-Path $LauncherExe)) {
    $launcherHash = (Get-FileHash $LauncherExe -Algorithm SHA256).Hash.ToLower()
}

$manifest = [ordered]@{
    launcher = [ordered]@{
        version = $LauncherVersion
        url     = $LauncherUrl
        sha256  = $launcherHash
    }
    files = @($files)
    news  = @(
        [ordered]@{
            title = "Добро пожаловать на OMNIRO ONLINE"
            date  = (Get-Date -Format "dd.MM.yyyy")
            text  = "Замените этот текст своей новостью в manifest.json."
        }
    )
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $Out
Write-Host "Готово: $Out (файлов: $($files.Count))"
