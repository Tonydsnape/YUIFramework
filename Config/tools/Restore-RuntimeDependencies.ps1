param([string]$Cache = (Join-Path $PSScriptRoot '..\.test-work\nuget'))
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Assets\YUIFramework\Config\ThirdParty'))
$manifest = Get-Content (Join-Path $destination 'dependencies.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $Cache | Out-Null
$inventory = @()
foreach ($package in $manifest.packages) {
    $id = $package.id.ToLowerInvariant()
    $version = $package.version
    $zip = Join-Path $Cache "$id.$version.zip"
    $folder = Join-Path $Cache "$id.$version"
    if (!(Test-Path $zip)) {
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $zip -UseBasicParsing
    }
    if (!(Test-Path $folder)) { Expand-Archive -LiteralPath $zip -DestinationPath $folder }
    $source = Join-Path $folder $package.asset
    $target = Join-Path $destination ([IO.Path]::GetFileName($source))
    Copy-Item -LiteralPath $source -Destination $target
    $license = Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^licen[sc]e' } | Select-Object -First 1
    if ($license) { Copy-Item $license.FullName (Join-Path $destination "$id.LICENSE.txt") }
    elseif ($id -eq 'messagepack' -or $id -eq 'messagepack.annotations') {
        Invoke-WebRequest 'https://raw.githubusercontent.com/MessagePack-CSharp/MessagePack-CSharp/v3.1.4/LICENSE' -OutFile (Join-Path $destination "$id.LICENSE.txt") -UseBasicParsing
    }
    elseif ($id -eq 'microsoft.net.stringtools') {
        Invoke-WebRequest 'https://raw.githubusercontent.com/dotnet/msbuild/37eb419ad2c986ac5530292e6ee08e962390249e/LICENSE' -OutFile (Join-Path $destination "$id.LICENSE.txt") -UseBasicParsing
    }
    else { throw "No license file in $id $version; inspect the package before distribution." }
    $inventory += [ordered]@{ id = $package.id; version = $version; dll = [IO.Path]::GetFileName($source);
        sha256 = (Get-FileHash $target -Algorithm SHA256).Hash; nupkgSha256 = (Get-FileHash $zip -Algorithm SHA256).Hash }
}
$inventory | ConvertTo-Json | Set-Content (Join-Path $destination 'installed-sha256.json') -Encoding UTF8
