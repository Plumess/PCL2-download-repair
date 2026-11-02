[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$OutputDir,
 [Parameter(Mandatory=$true)][string]$Version,
 [Parameter(Mandatory=$true)][string]$Repository,
 [Parameter(Mandatory=$true)][string]$Commit,
 [switch]$DraftOnly
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $Commit -notmatch '^[a-fA-F0-9]{40}$'){throw 'Invalid release identity'}
if($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -ne $Repository -or $env:GITHUB_SHA -ne $Commit){throw 'Publishing must run in the matching GitHub Actions job'}
& (Join-Path $PSScriptRoot 'verify-package.ps1') -OutputDir $OutputDir -RequireSignature
$output=[IO.Path]::GetFullPath($OutputDir)
$tag='release-'+$Version
$assets=@((Join-Path $output "PCL2-download-repair-release-$Version.zip"),(Join-Path $output "PCL2-download-repair-release-$Version-source.zip"),(Join-Path $output 'SHA256SUMS.txt'))
$assets+=@('framework-adapter.dll','framework-harmony.dll','modern-adapter.dll','modern-harmony.dll','stable.signed.json' | ForEach-Object {Join-Path $output ('update\'+$_)})
foreach($path in $assets){if(!(Test-Path -LiteralPath $path -PathType Leaf) -or [IO.Path]::GetFileName($path) -match '[^\x20-\x7e]'){throw 'Invalid release asset'}}

# A failed upload may be retried only while the matching Release is still a draft.
# Published assets and versions are immutable in this pipeline.
$all=gh api "repos/$Repository/releases?per_page=100"
if($LASTEXITCODE -ne 0){throw 'Cannot inspect existing releases'}
$existing=@(($all | ConvertFrom-Json) | Where-Object {$_.tag_name -eq $tag})
if($existing.Count -gt 1){throw 'Ambiguous release identity'}
if($existing.Count -eq 1){
 $release=$existing[0]
 if(!$release.draft){throw 'This version is already published; increment AdapterVersion instead of replacing it'}
 if($release.target_commitish -ne $Commit){throw 'Existing draft belongs to a different source commit'}
 $names=@($assets | ForEach-Object {[IO.Path]::GetFileName($_)})
 if(@($release.assets | Where-Object {$_.name -notin $names}).Count -gt 0){throw 'Existing draft has unrelated assets; inspect it before retrying'}
}else{
 $notes=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\docs\RELEASE-NOTES.md'))
 if(!(Test-Path -LiteralPath $notes -PathType Leaf)){throw 'Release notes are missing'}
 gh release create $tag --repo $Repository --target $Commit --draft --title $tag --notes-file $notes
 if($LASTEXITCODE -ne 0){throw 'Draft release creation failed'}
}
gh release upload $tag @assets --repo $Repository --clobber
if($LASTEXITCODE -ne 0){throw 'Asset upload failed; Release remains a draft'}
$raw=gh api "repos/$Repository/releases?per_page=100"
if($LASTEXITCODE -ne 0){throw 'Cannot verify uploaded assets'}
$matches=@(($raw | ConvertFrom-Json) | Where-Object {$_.tag_name -eq $tag})
if($matches.Count -ne 1){throw 'Cannot identify the uploaded draft'}
$release=$matches[0]
if(!$release.draft -or $release.target_commitish -ne $Commit -or $release.assets.Count -ne $assets.Count){throw 'Unexpected draft release state'}
foreach($file in $assets){
 $name=[IO.Path]::GetFileName($file)
 $remote=@($release.assets | Where-Object {$_.name -eq $name})
 $expected='sha256:'+(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()
 if($remote.Count -ne 1 -or $remote[0].state -ne 'uploaded' -or $remote[0].size -ne (Get-Item -LiteralPath $file).Length -or $remote[0].digest -ne $expected){throw "Remote asset verification failed: $name"}
}
if($DraftOnly){
 Write-Output "Prepared verified $tag draft for native app verification."
 if($env:GITHUB_STEP_SUMMARY){"Verified [$tag draft](https://github.com/$Repository/releases) from $Commit; waiting for optional native app verification." | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8}
 return
}
gh release edit $tag --repo $Repository --draft=false --latest
if($LASTEXITCODE -ne 0){throw 'Release publication failed'}
Write-Output "Published $Repository $tag from $Commit with verified assets."
if($env:GITHUB_STEP_SUMMARY){
 "Published [$tag](https://github.com/$Repository/releases/tag/$tag) from commit $Commit. All 8 asset digests verified." | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
