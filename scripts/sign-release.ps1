[CmdletBinding(DefaultParameterSetName='Store')]
param(
 [Parameter(Mandatory=$true)][string]$ManifestPath,
 [Parameter(Mandatory=$true,ParameterSetName='File')][string]$PrivateKeyPath,
 [Parameter(Mandatory=$true,ParameterSetName='Environment')][switch]$FromEnvironment,
 [Parameter(ParameterSetName='Store')][string]$KeyName='plumess.PCL-Download-Repair.Release.v1',
 [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))+[IO.Path]::DirectorySeparatorChar
$payload=[IO.File]::ReadAllBytes([IO.Path]::GetFullPath($ManifestPath))
$parsed=[Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json
if($parsed.schema -ne 1 -or $parsed.version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $parsed.files.Count -ne 4){throw 'Invalid release manifest or MAJOR.MINOR.PATCH version'}
$rsa=$null
$storedKey=$null
try {
 if($PSCmdlet.ParameterSetName -eq 'Environment') {
  $encoded=$env:PCL_REPAIR_SIGNING_KEY
  if([string]::IsNullOrWhiteSpace($encoded)){throw 'Missing PCL_REPAIR_SIGNING_KEY Actions secret'}
  [Environment]::SetEnvironmentVariable('PCL_REPAIR_SIGNING_KEY',$null)
  $privateBytes=[Convert]::FromBase64String($encoded)
  try{
   $rsa=[Security.Cryptography.RSA]::Create()
   $rsa.FromXmlString([Text.Encoding]::UTF8.GetString($privateBytes))
  }finally{[Array]::Clear($privateBytes,0,$privateBytes.Length);$encoded=$null}
 }elseif($PSCmdlet.ParameterSetName -eq 'File') {
  $key=[IO.Path]::GetFullPath($PrivateKeyPath)
  if($key.StartsWith($project,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep the signing private key outside this project directory'}
  $rsa=[Security.Cryptography.RSA]::Create()
  $rsa.FromXmlString([IO.File]::ReadAllText($key))
 }else {
  $storedKey=[Security.Cryptography.CngKey]::Open($KeyName,[Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider)
  $rsa=New-Object Security.Cryptography.RSACng($storedKey)
 }
 if($rsa.KeySize -lt 2048){throw 'Use an RSA key of at least 2048 bits'}
 $signature=$rsa.SignData($payload,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $channel=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\manifests\channel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
 $verifier=[Security.Cryptography.RSA]::Create()
 try{
  $verifier.FromXmlString($channel.publicKeyXml)
  if(!$verifier.VerifyData($payload,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)){throw 'Signing key does not match channel.json; update trust configuration explicitly before publishing'}
 }finally{$verifier.Dispose()}
 $envelope=[ordered]@{payload=[Convert]::ToBase64String($payload);signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json
 [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),$envelope,(New-Object Text.UTF8Encoding($false)))
} finally {if($rsa){$rsa.Dispose()};if($storedKey){$storedKey.Dispose()}}
