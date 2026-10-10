param([switch]$LiteOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$dist = Join-Path $root 'temp/release'
New-Item -ItemType Directory -Force $dist | Out-Null
$channels = if ($LiteOnly) { @('Lite') } else { @('Lite','Full') }
foreach ($channel in $channels) {
    $output = Join-Path $root "temp/package-staging/$version-$channel-$([guid]::NewGuid().ToString('N'))"
    $selfContained = if ($channel -eq 'Full') { 'true' } else { 'false' }
    dotnet publish (Join-Path $root 'src/Reader/DisplayControl.Reader.csproj') -c Release -r win-x64 --self-contained $selfContained -p:NuGetAudit=false -o $output
    if ($LASTEXITCODE -ne 0) { throw "$channel publish failed" }
    Copy-Item (Join-Path $root 'README.md') $output -Force
    Copy-Item (Join-Path $root 'LICENSE') $output -Force
    [IO.File]::WriteAllText((Join-Path $output 'distribution.json'), "{`"product`":`"DisplayControl.Reader`",`"version`":`"$version`",`"channel`":`"$channel`",`"automaticUpdates`":false}", [Text.UTF8Encoding]::new($false))
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $dist "DisplayControl-Reader-$version-$channel.zip") -Force
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($compiler) { $compiler.Source } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
    if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 is required. No private copy will be installed.' }
    & $iscc /Q "/DAppVersion=$version" "/DChannel=$channel" "/DPublishDir=$output" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'reader.iss')
    if ($LASTEXITCODE -ne 0) { throw "$channel installer failed" }
}
$sums = Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Name -match "^DisplayControl-Reader-$([regex]::Escape($version))-.*\.(zip|exe)$" } | Sort-Object Name | ForEach-Object { "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
