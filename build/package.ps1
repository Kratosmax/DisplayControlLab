param([switch]$LiteOnly, [switch]$PreviewOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$dist = Join-Path $root $(if ($PreviewOnly) { 'temp/preview' } else { 'temp/release' })
New-Item -ItemType Directory -Force $dist | Out-Null
$channels = if ($LiteOnly) { @('Lite') } else { @('Lite','Full') }
foreach ($channel in $channels) {
    $output = Join-Path $root "temp/package-staging/$version-$channel-$([guid]::NewGuid().ToString('N'))"
    $selfContained = if ($channel -eq 'Full') { 'true' } else { 'false' }
    dotnet publish (Join-Path $root 'src/Reader/DisplayControl.Reader.csproj') -c Release -r win-x64 --self-contained $selfContained -p:NuGetAudit=false -m:1 -o $output
    if ($LASTEXITCODE -ne 0) { throw "$channel publish failed" }
    dotnet publish (Join-Path $root 'src/Controller/DisplayControl.Controller.csproj') -c Release -r win-x64 --self-contained $selfContained -p:NuGetAudit=false -m:1 -o $output
    if ($LASTEXITCODE -ne 0) { throw "$channel controller publish failed" }
    dotnet publish (Join-Path $root 'src/Updater/DisplayControl.Updater.csproj') -c Release -r win-x64 --self-contained $selfContained -p:NuGetAudit=false -m:1 -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed' }
    Copy-Item (Join-Path $root 'README.md') $output -Force
    $readme = Get-Content (Join-Path $output 'README.md') -Raw -Encoding UTF8
    [IO.File]::WriteAllText((Join-Path $output 'README.md'), $readme.Replace('(CODEX_PROGRESS.md)', '(https://github.com/Kratosmax/DisplayControlLab/blob/main/CODEX_PROGRESS.md)'), [Text.UTF8Encoding]::new($false))
    Copy-Item (Join-Path $root 'LICENSE') $output -Force
    New-Item -ItemType Directory -Force (Join-Path $output 'examples') | Out-Null
    Copy-Item (Join-Path $root 'examples/aoc-q27g4zmn') (Join-Path $output 'examples/aoc-q27g4zmn') -Recurse -Force
    foreach ($adapter in @('msi-g273q')) {
        Copy-Item (Join-Path $root "examples/$adapter") (Join-Path $output "examples/$adapter") -Recurse -Force
    }
    New-Item -ItemType Directory -Force (Join-Path $output 'templates') | Out-Null
    Compress-Archive -Path (Join-Path $root 'examples/plugin-template/*.cs'),(Join-Path $root 'examples/plugin-template/*.csproj'),(Join-Path $root 'examples/plugin-template/*.json'),(Join-Path $root 'examples/plugin-template/*.md') -DestinationPath (Join-Path $output 'templates/plugin-template.zip') -Force
    New-Item -ItemType Directory -Force (Join-Path $output 'docs') | Out-Null
    Copy-Item (Join-Path $root 'docs/ADAPTERS.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/CAPABILITY_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/USER_GUIDE.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/VCP_RECOGNITION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/PRODUCT_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/LIVE_CONTROL_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/MONITOR_ADAPTER_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/UNIFIED_ADAPTER_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/OSD_CONTROL_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/COMPACT_CONTROL_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/UPDATE_VALIDATION.md') (Join-Path $output 'docs') -Force
    Copy-Item (Join-Path $root 'docs/releases') (Join-Path $output 'docs/releases') -Recurse -Force
    New-Item -ItemType Directory -Force (Join-Path $output 'config') | Out-Null
    Copy-Item (Join-Path $root 'config/observation.schema.json') (Join-Path $output 'config') -Force
    [IO.File]::WriteAllText((Join-Path $output 'distribution.json'), (@{product='DisplayControlLab';version=$version;channel=$channel;automaticUpdates=$true;localPreview=[bool]$PreviewOnly} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $owned = @(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/') }) + @('update-package.json','update-install.json')
    $identity = @{schemaVersion=1;productId='DisplayControlLab.Windows.Desktop';version=$version;channel=$channel;files=$owned} | ConvertTo-Json -Depth 4
    foreach ($marker in @('update-package.json','update-install.json')) { [IO.File]::WriteAllText((Join-Path $output $marker), $identity, [Text.UTF8Encoding]::new($false)) }
    [IO.File]::WriteAllText((Join-Path $dist ($channel.ToLowerInvariant() + '-path.txt')), $output, [Text.UTF8Encoding]::new($false))
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $dist "DisplayControlLab-$version-$channel.zip") -Force
    if ($PreviewOnly) {
        [IO.File]::WriteAllText((Join-Path $dist 'preview-path.txt'), $output, [Text.UTF8Encoding]::new($false))
        continue
    }
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($compiler) { $compiler.Source } elseif (Test-Path (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')) { Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe' } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
    if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 is required. No private copy will be installed.' }
    & $iscc /Q "/DAppVersion=$version" "/DChannel=$channel" "/DPublishDir=$output" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'suite.iss')
    if ($LASTEXITCODE -ne 0) { throw "$channel installer failed" }
}
$sums = Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Name -match "^DisplayControlLab-$([regex]::Escape($version))-.*\.(zip|exe)$" } | Sort-Object Name | ForEach-Object { "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
