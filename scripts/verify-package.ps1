[CmdletBinding()]
param([string]$OutputDir=(Join-Path $PSScriptRoot '..\dist'),[switch]$RequireSignature)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDir)
$version=[regex]::Match([IO.File]::ReadAllText("$root\src\BuildInfo.cs"),'AdapterVersion="([0-9.]+)"').Groups[1].Value
if($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'){throw 'Invalid release version'}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-ZipBytes($entry){
 $input=$entry.Open();$memory=New-Object IO.MemoryStream
 try{$input.CopyTo($memory);return ,$memory.ToArray()}finally{$input.Dispose();$memory.Dispose()}
}
function Hash-Bytes([byte[]]$bytes){
 $sha=[Security.Cryptography.SHA256]::Create()
 try{return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
}
$public=@(Get-Content -LiteralPath "$root\public-files.txt" -Encoding UTF8 | Where-Object {$_ -and !$_.StartsWith('#')})
$prefix='PCL2-download-repair/'
$releaseFiles=@('PCL2-download-repair.exe','README.md','USER-GUIDE.md','RELEASE-NOTES.md','docs/assets/repair-tool.png','LICENSE','THIRD-PARTY-NOTICES.md','licenses/Harmony.txt','licenses/MonoMod.txt','licenses/Mono.Cecil.txt')
$archives=@("PCL2-download-repair-release-$version.zip","PCL2-download-repair-release-$version-source.zip")
foreach($name in $archives){
 $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $output $name))
 try{
  $expected=if($name.EndsWith('-source.zip')){@($public | ForEach-Object {$prefix+$_})}else{@($releaseFiles | ForEach-Object {$prefix+$_})}
  $actual=@($zip.Entries | ForEach-Object {$_.FullName})
  if($actual.Count -ne $expected.Count -or @($actual | Sort-Object -Unique).Count -ne $actual.Count -or @(Compare-Object $expected $actual).Count -ne 0){throw "Archive allowlist mismatch: $name"}
  foreach($entry in $zip.Entries){
   if($entry.FullName -match '[^\x20-\x7e]' -or $entry.FullName.Contains('\') -or $entry.FullName -match '(^|/)\.\.(/|$)' -or $entry.FullName -match '(^|/)(\.local|tests?|runtime|dist|bin|obj|\.git)(/|$)'){throw "Forbidden archive path: $($entry.FullName)"}
   $relative=$entry.FullName.Substring($prefix.Length)
   if($name.EndsWith('-source.zip')){
    if((Hash-Bytes (Read-ZipBytes $entry)) -ne (Get-FileHash -LiteralPath (Join-Path $root $relative)).Hash.ToLowerInvariant()){throw "Source archive differs from checkout: $relative"}
   }
  }
 }finally{$zip.Dispose()}
}
$manifest=Get-Content -LiteralPath (Join-Path $output 'update\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($manifest.schema -ne 1 -or $manifest.version -ne $version -or $manifest.files.Count -ne 4){throw 'Invalid component manifest'}
$allowed=@('framework/PclRepair.Adapter.dll','framework/0Harmony.dll','modern/PclRepair.Adapter.dll','modern/0Harmony.dll')
if(@(Compare-Object $allowed @($manifest.files.path)).Count -ne 0){throw 'Unexpected component path'}
$repo='https://github.com/plumess/PCL2-download-repair/releases/download/release-'+$version+'/'
$assets=@($archives | ForEach-Object {Join-Path $output $_})
foreach($item in $manifest.files){
 if(!$item.url.StartsWith($repo,[StringComparison]::Ordinal) -or $item.url.Substring($repo.Length) -notmatch '^(framework|modern)-(adapter|harmony)\.dll$'){throw 'Unexpected component download URL'}
 $file=Join-Path $output ('update\'+$item.url.Substring($repo.Length))
 if((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -ne $item.sha256 -or (Get-Item -LiteralPath $file).Length -ne $item.size){throw 'Component hash or size mismatch'}
 $expectedName=if($item.path.EndsWith('0Harmony.dll')){'0Harmony'}else{'PclRepair.Adapter'}
 if([Reflection.AssemblyName]::GetAssemblyName($file).Name -ne $expectedName){throw 'Component assembly identity mismatch'}
 $assets+=$file
}
if($RequireSignature){
 $signed=Join-Path $output 'update\stable.signed.json'
 $envelope=Get-Content -LiteralPath $signed -Raw -Encoding UTF8 | ConvertFrom-Json
 $bytes=[Convert]::FromBase64String($envelope.payload)
 if((Hash-Bytes $bytes) -ne (Get-FileHash -LiteralPath (Join-Path $output 'update\manifest.json')).Hash.ToLowerInvariant()){throw 'Signed payload differs from component manifest'}
 $channel=Get-Content -LiteralPath "$root\manifests\channel.json" -Raw -Encoding UTF8 | ConvertFrom-Json
 $rsa=[Security.Cryptography.RSA]::Create()
 try{
  $rsa.FromXmlString($channel.publicKeyXml)
  if($rsa.KeySize -lt 2048 -or !$rsa.VerifyData($bytes,[Convert]::FromBase64String($envelope.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'Release signature does not match the installed update key'}
 }finally{$rsa.Dispose()}
 $assets+=$signed
}else{$assets+=Join-Path $output 'update\manifest.json'}
$checksums=@($assets | Sort-Object {Split-Path $_ -Leaf} | ForEach-Object {(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)})
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'),(($checksums -join "`n")+"`n"),(New-Object Text.UTF8Encoding($false)))
Write-Output "Verified release-$version archives and four update components."
