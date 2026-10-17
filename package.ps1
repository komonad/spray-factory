$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet build (Join-Path $root 'SprayFactory.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$staging = Join-Path $root ('obj/package-' + [guid]::NewGuid().ToString('N'))
$plugin = Join-Path $staging 'BepInEx/plugins/SprayFactory'
New-Item -ItemType Directory -Path $plugin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin/Release/SprayFactory.dll') -Destination $plugin
foreach ($file in @('manifest.json','README.md','icon.png')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $staging
}
$readmePath = Join-Path $staging 'README.md'
$readme = [IO.File]::ReadAllText($readmePath).Replace('(docs/images/factory.jpg)', '(https://raw.githubusercontent.com/komonad/spray-factory/main/docs/images/factory.jpg)')
[IO.File]::WriteAllText($readmePath, $readme, [Text.UTF8Encoding]::new($false))
$images = Join-Path $staging 'docs/images'
New-Item -ItemType Directory -Path $images -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs/images/factory.jpg') -Destination $images
New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force | Out-Null
$version = (Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json).version_number
$zip = Join-Path $root ('dist/SprayFactory-' + $version + '.zip')
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
Write-Output $zip
