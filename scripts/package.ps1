[CmdletBinding()]
param([string]$OutputDir=(Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDir)
$version=[regex]::Match([IO.File]::ReadAllText("$root\src\BuildInfo.cs"),'AdapterVersion="([0-9.]+)"').Groups[1].Value
if($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'){throw 'AdapterVersion must be a stable MAJOR.MINOR.PATCH version'}
$releaseName="PCL2-download-repair-release-$version"
New-Item -ItemType Directory -Force -Path $output | Out-Null
$tool=Join-Path $output 'PCL2-download-repair.exe'
if(!(Test-Path -LiteralPath $tool)){throw 'Run scripts/build.ps1 first'}
if([Reflection.AssemblyName]::GetAssemblyName($tool).Version -ne [Version]($version+'.0')){throw 'Installer version differs from BuildInfo; rebuild before packaging'}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function New-AllowlistZip([string]$path,[object[]]$files) {
 if([IO.Path]::GetFileName($path) -match '[^\x20-\x7e]'){throw 'Release filenames must use ASCII characters'}
 foreach($file in $files){if($file.Entry -match '[^\x20-\x7e]' -or $file.Entry -match '(^|/)\.\.(/|$)' -or $file.Entry.Contains('\')){throw "Invalid release entry path: $($file.Entry)"}}
 if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}
 $stream=[IO.File]::Create($path)
 $zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
 try {foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.Source,$file.Entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}} finally {$zip.Dispose();$stream.Dispose()}
}
$releaseReadme=Join-Path $root '.local\build\release-README.md'
New-Item -ItemType Directory -Force -Path (Split-Path $releaseReadme) | Out-Null
$readmeText=[IO.File]::ReadAllText("$root\README.md").Replace('](docs/USER-GUIDE.md)','](USER-GUIDE.md)').Replace('](docs/MAINTAINER-GUIDE.md)','](https://github.com/Plumess/PCL2-download-repair/blob/main/docs/MAINTAINER-GUIDE.md)')
[IO.File]::WriteAllText($releaseReadme,$readmeText,(New-Object Text.UTF8Encoding($false)))
$release=@(
 @{Source=$tool;Entry='PCL2-download-repair/PCL2-download-repair.exe'},
 @{Source=$releaseReadme;Entry='PCL2-download-repair/README.md'},
 @{Source="$root\docs\USER-GUIDE.md";Entry='PCL2-download-repair/USER-GUIDE.md'},
 @{Source="$root\docs\RELEASE-NOTES.md";Entry='PCL2-download-repair/RELEASE-NOTES.md'},
 @{Source="$root\docs\assets\repair-tool.png";Entry='PCL2-download-repair/docs/assets/repair-tool.png'},
 @{Source="$root\LICENSE";Entry='PCL2-download-repair/LICENSE'},
 @{Source="$root\THIRD-PARTY-NOTICES.md";Entry='PCL2-download-repair/THIRD-PARTY-NOTICES.md'},
 @{Source="$root\licenses\Harmony.txt";Entry='PCL2-download-repair/licenses/Harmony.txt'}
 @{Source="$root\licenses\MonoMod.txt";Entry='PCL2-download-repair/licenses/MonoMod.txt'}
 @{Source="$root\licenses\Mono.Cecil.txt";Entry='PCL2-download-repair/licenses/Mono.Cecil.txt'}
)
New-AllowlistZip (Join-Path $output ($releaseName+'.zip')) $release
$source=@()
foreach($relative in Get-Content -LiteralPath "$root\public-files.txt" -Encoding utf8) {
 if(!$relative -or $relative.StartsWith('#')){continue}
 $absolute=[IO.Path]::GetFullPath((Join-Path $root $relative))
 if(!$absolute.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $relative -match '(^|/)(\.local|tests?|runtime|dist|bin|obj)(/|$)' -or $relative -match '\.(exe|dll|zip|pfx|key|p12|log|bak)$'){throw "Non-public source path rejected: $relative"}
 if(!(Test-Path -LiteralPath $absolute -PathType Leaf)){throw "Public file missing: $relative"}
 $source+=@{Source=$absolute;Entry=('PCL2-download-repair/'+$relative.Replace('\','/'))}
}
New-AllowlistZip (Join-Path $output ($releaseName+'-source.zip')) $source
$update=Join-Path $output 'update'
New-Item -ItemType Directory -Force -Path $update | Out-Null
$entries=@()
foreach($item in @(@('framework/PclRepair.Adapter.dll','framework-adapter.dll'),@('framework/0Harmony.dll','framework-harmony.dll'),@('modern/PclRepair.Adapter.dll','modern-adapter.dll'),@('modern/0Harmony.dll','modern-harmony.dll'))) {
 $sourceFile=Join-Path $root ('.local\build\payload\runtime\versions\'+$version+'\'+$item[0])
 $destination=Join-Path $update $item[1]
 Copy-Item -LiteralPath $sourceFile -Destination $destination -Force
 $entries+= [ordered]@{path=$item[0];url=("https://github.com/plumess/PCL2-download-repair/releases/download/release-$version/"+$item[1]);sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant();size=(Get-Item -LiteralPath $destination).Length}
}
$manifest=[ordered]@{schema=1;version=$version;minimumBootstrap=3;files=$entries} | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $update 'manifest.json'),$manifest,(New-Object Text.UTF8Encoding($false)))
Get-FileHash -LiteralPath (Join-Path $output ($releaseName+'.zip')),(Join-Path $output ($releaseName+'-source.zip')) -Algorithm SHA256
