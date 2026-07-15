[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '1.0.0'
$workspaceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$distRoot = [IO.Path]::GetFullPath((Join-Path $workspaceRoot 'dist'))
$packageDirectory = [IO.Path]::GetFullPath((Join-Path $distRoot "WindowsNeverSleep-$version"))
$buildDirectory = [IO.Path]::GetFullPath((Join-Path $distRoot "_build-$version"))
$zipPath = [IO.Path]::GetFullPath((Join-Path $distRoot "WindowsNeverSleep-$version.zip"))

foreach ($path in @($packageDirectory, $buildDirectory, $zipPath)) {
    if (-not $path.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe build path: $path"
    }
}

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw 'The .NET Framework C# compiler was not found.'
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
foreach ($directory in @($packageDirectory, $buildDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$mainExe = Join-Path $buildDirectory 'WindowsNeverSleep.exe'
$uninstallExe = Join-Path $packageDirectory 'Uninstall.exe'
$installExe = Join-Path $packageDirectory 'Install.exe'

& $csc /nologo /target:winexe /platform:anycpu "/out:$mainExe" (Join-Path $workspaceRoot 'src\WindowsNeverSleep.cs')
if ($LASTEXITCODE -ne 0) { throw 'Failed to compile WindowsNeverSleep.exe.' }

& $csc /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll "/out:$uninstallExe" (Join-Path $workspaceRoot 'src\Uninstall.cs')
if ($LASTEXITCODE -ne 0) { throw 'Failed to compile Uninstall.exe.' }

& $csc /nologo /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll "/out:$installExe" "/resource:$mainExe,WindowsNeverSleep.exe" "/resource:$uninstallExe,Uninstall.exe" (Join-Path $workspaceRoot 'src\Install.cs')
if ($LASTEXITCODE -ne 0) { throw 'Failed to compile Install.exe.' }

New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'README.zh-CN.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'LICENSE') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs\TECHNICAL.md') -Destination (Join-Path $packageDirectory 'docs')
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'docs\BUILDING.md') -Destination (Join-Path $packageDirectory 'docs')

$hashLines = @(
    ('{0}  Install.exe' -f (Get-FileHash -LiteralPath $installExe -Algorithm SHA256).Hash)
    ('{0}  Uninstall.exe' -f (Get-FileHash -LiteralPath $uninstallExe -Algorithm SHA256).Hash)
)
$hashLines | Set-Content -LiteralPath (Join-Path $packageDirectory 'SHA256SUMS.txt') -Encoding ASCII

Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $buildDirectory -Recurse -Force

Write-Host "Install EXE: $installExe" -ForegroundColor Green
Write-Host "Uninstall EXE: $uninstallExe" -ForegroundColor Green
Write-Host "Release ZIP: $zipPath" -ForegroundColor Green
$hashLines | ForEach-Object { Write-Host $_ }
