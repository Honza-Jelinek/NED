<#
.SYNOPSIS
    Přegeneruje všechny node manifesty v repu.

.DESCRIPTION
    Manifest nahrazuje reflexi nad doménovou assembly — editor ho čte místo toho,
    aby doménu znal. Musí se tedy přegenerovat po každé změně anotovaných typů.

    Zapomenuté spuštění chytí test ManifestDriftTests (dotnet test), takže se
    manifest nemůže tiše rozejít s kódem.

    ponytail: záměrně skript, ne MSBuild target. Automatická generace při buildu
    by potřebovala cross-TFM ProjectReference na generátor (netstandard2.0 doména
    → net10.0 exe) a umí zaseknout build celého řešení. Až bude NED.Abstractions
    NuGet balík, patří to do jeho .targets jako tool package.
#>
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

dotnet build "$root\NED.sln" -v q --nologo

$gen = "$root\src\NED.Manifest.Generator\bin\Debug\net10.0\ned-manifest.exe"

$targets = @(
    @{ Dll = "src\Sandbox\bin\Debug\netstandard2.0\Sandbox.dll";     Out = "src\Sandbox\Sandbox.nodes.json" }
    @{ Dll = "src\NED.Core\bin\Debug\net10.0\NED.Core.dll";          Out = "src\NED.Core\Manifest\ned.builtin.nodes.json" }
)

foreach ($t in $targets) {
    & $gen "$root\$($t.Dll)" -o "$root\$($t.Out)"
    if ($LASTEXITCODE -ne 0) { throw "ned-manifest selhal pro $($t.Dll)" }
}

# Běžící debug shell sleduje kopie manifestů vedle své binárky. Build proběhl před
# generováním, proto do něj pošli čerstvé soubory atomickou výměnou ve stejné složce.
$shellManifests = "$root\src\NED.Shell.Wpf\bin\Debug\net10.0-windows10.0.19041.0\manifests"
if (Test-Path $shellManifests) {
    foreach ($name in @("Sandbox.nodes.json")) {
        $source = Join-Path $root "src\$($name -replace '\.nodes\.json$','')\$name"
        $destination = Join-Path $shellManifests $name
        $temporary = Join-Path $shellManifests (".$name." + [Guid]::NewGuid().ToString("N") + ".tmp")
        [IO.File]::Copy($source, $temporary, $true)
        if ([IO.File]::Exists($destination)) {
            $backup = Join-Path $shellManifests (".$name." + [Guid]::NewGuid().ToString("N") + ".bak")
            [IO.File]::Replace($temporary, $destination, $backup)
            [IO.File]::Delete($backup)
        } else {
            [IO.File]::Move($temporary, $destination)
        }
    }
}
