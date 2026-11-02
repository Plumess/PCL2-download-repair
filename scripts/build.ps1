[CmdletBinding()]
param([string]$OutputDir = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=[regex]::Match([IO.File]::ReadAllText("$root\src\BuildInfo.cs"),'AdapterVersion="([0-9.]+)"').Groups[1].Value
if($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'){throw 'AdapterVersion must be a stable MAJOR.MINOR.PATCH version'}
$build=Join-Path $root '.local\build'
$deps=Join-Path $root '.local\deps'
New-Item -ItemType Directory -Force -Path $build,$deps,$OutputDir | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf=Join-Path (Split-Path $compiler) 'WPF'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Pinned upstream dependency. The archive stays in memory; only the two required
# framework binaries are kept in the ignored dependency cache.
$frameworkHarmony=Join-Path $deps 'net48_0Harmony.dll'
$modernHarmony=Join-Path $deps 'net8.0_0Harmony.dll'
if(!(Test-Path -LiteralPath $frameworkHarmony) -or !(Test-Path -LiteralPath $modernHarmony)) {
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 $client=New-Object Net.WebClient
 try {$bytes=$client.DownloadData('https://github.com/pardeike/Harmony/releases/download/v2.4.3/Harmony-Fat.2.4.3.0.zip')} finally {$client.Dispose()}
 $sha=[Security.Cryptography.SHA256]::Create()
 try {$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').ToLowerInvariant()} finally {$sha.Dispose()}
 if($hash -ne '2aad201cc048899e3e0d558cc08173d91cfa4b8a2d6d9a5231fca5c295081880'){throw 'Harmony dependency hash mismatch'}
 $memory=New-Object IO.MemoryStream(,$bytes)
 $zip=New-Object IO.Compression.ZipArchive($memory,[IO.Compression.ZipArchiveMode]::Read)
 try {foreach($tfm in @('net48','net8.0')){$entry=$zip.GetEntry("$tfm/0Harmony.dll");$input=$entry.Open();$output=[IO.File]::Create((Join-Path $deps ($tfm+'_0Harmony.dll')));try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}}} finally {$zip.Dispose();$memory.Dispose()}
}
if((Get-FileHash -LiteralPath $frameworkHarmony -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'ddc070b57f695ece6f92c43cf82cf4d863c69685de96a99ebd9d2fa1d8fa7a19' -or (Get-FileHash -LiteralPath $modernHarmony -Algorithm SHA256).Hash.ToLowerInvariant() -ne '119aec2f1f36c6c8c0aebd734370c39fb80f2f02bd234615bad86fde3f4933a2'){throw 'Cached Harmony DLL hash mismatch; remove the dependency cache and rebuild'}
$nuget=Join-Path $build 'NuGet.Config'
[IO.File]::WriteAllText($nuget,'<configuration><packageSources><clear/></packageSources></configuration>')
$payload=Join-Path $build 'payload'
if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($payload)) -ne [IO.Path]::GetFullPath($build)){throw 'Unsafe payload output path'}
if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload -Recurse -Force}
$bootstrap=Join-Path $payload 'runtime\bootstrap'
$framework=Join-Path $payload "runtime\versions\$version\framework"
$modern=Join-Path $payload "runtime\versions\$version\modern"
New-Item -ItemType Directory -Force -Path $bootstrap,$framework,$modern | Out-Null
& $compiler /nologo /optimize+ /target:library "/out:$bootstrap\PclRepair.Bootstrap.dll" "$root\src\Bootstrap.Framework\Manager.cs"
if($LASTEXITCODE -ne 0){throw 'Framework bootstrap build failed'}
& $compiler /nologo /optimize+ /target:library "/out:$framework\PclRepair.Adapter.dll" "/reference:$frameworkHarmony" /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" "$root\src\Adapters\*.cs" "$root\src\BuildInfo.cs"
if($LASTEXITCODE -ne 0){throw 'Framework adapter build failed'}
Copy-Item -LiteralPath $frameworkHarmony -Destination "$framework\0Harmony.dll" -Force
dotnet build "$root\src\Bootstrap.Modern\Bootstrap.csproj" -c Release -o "$build\modern-bootstrap" --configfile $nuget -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'Modern bootstrap build failed'}
Copy-Item -LiteralPath "$build\modern-bootstrap\PclRepair.StartupHook.dll" -Destination $bootstrap -Force
dotnet build "$root\src\Adapters\Adapters.Modern.csproj" -c Release -o "$build\modern-adapter" --configfile $nuget -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0){throw 'Modern adapter build failed'}
Copy-Item -LiteralPath "$build\modern-adapter\PclRepair.Adapter.dll" -Destination $modern -Force
Copy-Item -LiteralPath $modernHarmony -Destination "$modern\0Harmony.dll" -Force

$vswhere="${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if(!(Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio C++ build tools with the Windows SDK first'}
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
# This generated batch file contains fixed build arguments and local project
# paths only. Do not interpolate remote manifest fields into shell commands.
$batch=Join-Path $build 'compile-native.cmd'
$command="@call `"$vcvars`" x64 >nul`r`n@cl /nologo /O2 /EHsc /MT /LD `"$root\src\Bootstrap.Modern\hostfxr.cpp`" /Fo`"$build\hostfxr.obj`" /link Advapi32.lib /IMPLIB:`"$build\hostfxr.lib`" /OUT:`"$bootstrap\hostfxr-x64.dll`"`r`n@exit /b %errorlevel%`r`n"
[IO.File]::WriteAllText($batch,$command,[Text.Encoding]::Default)
& $env:ComSpec /d /c $batch
if($LASTEXITCODE -ne 0){throw 'Native forwarding bridge build failed'}
Copy-Item -LiteralPath "$root\manifests\channel.json" -Destination "$payload\runtime\channel.json" -Force
$payloadZip=Join-Path $build 'payload.zip'
if(Test-Path -LiteralPath $payloadZip){Remove-Item -LiteralPath $payloadZip}
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$payloadZip,[IO.Compression.CompressionLevel]::Optimal,$false)
$tool=Join-Path ([IO.Path]::GetFullPath($OutputDir)) 'PCL2-download-repair.exe'
& $compiler /nologo /optimize+ /target:winexe "/out:$tool" /reference:System.Core.dll /reference:System.Xml.Linq.dll /reference:System.IO.Compression.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" "/resource:$payloadZip,PclRepair.Payload" "$root\src\Installer\*.cs" "$root\src\BuildInfo.cs"
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
Write-Output "Built: $tool"
