param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [string]$DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'NuclearOption-Simple-Missile-Bomb-Tracker-and-Tac-Map-Trails.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$assemblyName = $projectXml.Project.PropertyGroup.AssemblyName
$version = $projectXml.Project.PropertyGroup.Version
$output = Join-Path $PSScriptRoot "bin/Release/netstandard2.1/$assemblyName.dll"
$zipPath = Join-Path $PSScriptRoot "$assemblyName-v$version.zip"

if (Test-Path -LiteralPath $zipPath) {
    throw 'The release ZIP already exists. Choose a new version or move the existing ZIP first.'
}

& $DotNet build $project -c Release "-p:NuclearOptionRoot=$GameRoot" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ([Reflection.AssemblyName]::GetAssemblyName($output).Version.ToString() -ne "$version.0") {
    throw 'The built DLL version does not match the release version.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in @($output, (Join-Path $PSScriptRoot 'README.md'), (Join-Path $PSScriptRoot 'LICENSE'))) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file, [IO.Path]::GetFileName($file), [IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
} finally {
    $zip.Dispose()
}

$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($zip.Entries.Count -ne 3 -or @($zip.Entries | Where-Object { $_.FullName -match '[/\\]' }).Count -gt 0) {
        throw 'The release must contain only the DLL, README and license at the ZIP root.'
    }
} finally {
    $zip.Dispose()
}

Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
