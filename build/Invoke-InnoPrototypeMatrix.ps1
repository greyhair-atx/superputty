[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerDirectory,[Parameter(Mandatory)][string]$StageRoot,
 [ValidateSet('user','machine')][string]$Scope='user',[ValidateSet('Direct','Catalog')][string]$TestSet='Direct',
 [Parameter(Mandatory)][string]$WinGetPath)
$ErrorActionPreference='Stop'
if($env:SUPERPUTTY_DISPOSABLE_TEST -ne '1'){throw 'Authorized disposable VM required.'}
if(Test-Path $StageRoot){throw 'Matrix stage already exists.'}
New-Item -ItemType Directory $StageRoot|Out-Null
$commands=if($TestSet -eq 'Direct'){@('none')}else{@('explicit','unscoped','all')}
foreach($command in $commands){
 $stage=Join-Path $StageRoot $command
 New-Item -ItemType Directory $stage|Out-Null
 Copy-Item "$InstallerDirectory/*.exe","$InstallerDirectory/prototype-hashes.json" $stage
 $migration=$TestSet -eq 'Direct' -and $Scope -eq 'user'
 if($migration){Copy-Item "$InstallerDirectory/previous.msi" $stage}
 try{
  & "$PSScriptRoot/Test-InnoPrototype.ps1" -StageDirectory $stage -Scope $Scope -WinGetPath $WinGetPath -CatalogCommand $command -TestMsiMigration:$migration
  $summary=Get-Content "$stage/results-$Scope/summary.json" -Raw|ConvertFrom-Json
  if(-not $summary.success){throw 'Scenario did not pass.'}
 }catch{$_|Out-String|Set-Content "$stage/error.txt";throw}
}
@{success=$true;scope=$Scope;testSet=$TestSet;commands=$commands;winGet=$WinGetPath}|ConvertTo-Json|Set-Content "$StageRoot/matrix-summary.json"
