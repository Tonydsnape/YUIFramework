param(
    [Parameter(Mandatory=$true)][string]$VendorSource,
    [string]$ProjectRoot
)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$source = (Resolve-Path -LiteralPath $VendorSource).Path
$project = (Resolve-Path -LiteralPath $ProjectRoot).Path
$version = Join-Path $source 'Scripts\Version 2.5.3.txt'
if (-not (Test-Path -LiteralPath $version)) { throw 'Expected licensed SuperScrollView 2.5.3 Scripts directory.' }
if ((Get-Content -LiteralPath $version -Raw).Trim() -ne 'Package Version 2.5.3') { throw 'Only SuperScrollView 2.5.3 is validated.' }
$scripts = @(Get-ChildItem (Join-Path $source 'Scripts') -Filter '*.cs' -Recurse -File)
if ($scripts.Count -ne 14) { throw "Expected 14 vendor runtime scripts, found $($scripts.Count)." }
$exclude = & git -C $project rev-parse --git-path info/exclude
if ($LASTEXITCODE -ne 0) { throw 'The target must be a local Git checkout.' }
if (-not [IO.Path]::IsPathRooted($exclude)) { $exclude = Join-Path $project $exclude }
$patterns = @('/Assets/YUIFramework.SuperScrollView.Local/', '/Assets/YUIFramework.SuperScrollView.Local.meta')
$existing = if (Test-Path $exclude) { Get-Content $exclude -Raw } else { '' }
foreach ($pattern in $patterns) {
    if ($existing -notmatch [regex]::Escape($pattern)) {
        [IO.File]::AppendAllText($exclude, "`n$pattern`n", [Text.UTF8Encoding]::new($false))
    }
}
$target = Join-Path $project 'Assets\YUIFramework.SuperScrollView.Local'
New-Item -ItemType Directory -Path (Join-Path $target 'Vendor') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'Scripts') -Destination (Join-Path $target 'Vendor') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $source 'Scripts.meta') -Destination (Join-Path $target 'Vendor\Scripts.meta') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VendorAssembly\SuperScrollView.asmdef') -Destination (Join-Path $target 'Vendor\SuperScrollView.asmdef') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VendorAssembly\SuperScrollView.asmdef.meta') -Destination (Join-Path $target 'Vendor\SuperScrollView.asmdef.meta') -Force
foreach ($folder in @('Runtime','Examples','Tests')) {
    $destination = Join-Path $target $folder
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $folder) -File | Copy-Item -Destination $destination -Force
}
Write-Output "Installed licensed vendor runtime and original adapter at $target. No Demo/Editor/media copied. Do not force-add this local directory."
