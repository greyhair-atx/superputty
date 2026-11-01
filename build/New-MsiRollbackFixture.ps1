[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateMsi,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$candidate=(Resolve-Path $CandidateMsi).Path
if($candidate -notmatch 'rollback-test.*\.msi$' -or (Get-AuthenticodeSignature $candidate).Status -ne 'NotSigned'){throw 'Only an unsigned rollback-test MSI may be used to create this fixture.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $output){throw 'Fixture output must not already exist.'}
New-Item -ItemType Directory $output|Out-Null
Copy-Item $candidate "$output/candidate.msi"
Copy-Item $candidate "$output/failing-candidate.msi"
$installer=New-Object -ComObject WindowsInstaller.Installer
$db=$installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@("$output/failing-candidate.msi",1))
function Query([string]$Sql){
 $view=$db.GetType().InvokeMember('OpenView','InvokeMethod',$null,$db,@($Sql))
 $null=$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
 return $view
}
function First([string]$Sql){
 $view=Query $Sql
 $record=$view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)
 $result=$null
 if($record){$result=$record.GetType().InvokeMember('StringData','GetProperty',$null,$record,1);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)}
 $null=$view.GetType().InvokeMember('Close','InvokeMethod',$null,$view,$null)
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
 return $result
}
function Change([string]$Sql){$view=Query $Sql;$null=$view.GetType().InvokeMember('Close','InvokeMethod',$null,$view,$null);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}
$code=First 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode'''
$version=First 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductVersion'''
$initialize=[int](First 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''InstallInitialize''')
$remove=[int](First 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''RemoveExistingProducts''')
$finalize=[int](First 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''InstallFinalize''')
if($remove -le $initialize -or $remove -ge $finalize){throw 'Old-product removal is outside the transaction.'}
$execute=[int](First 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''InstallExecute''')
# Flush the installation script before the deliberate immediate error. This
# exercises rollback after old removal and execution of the new payload script.
if(-not $execute){Change 'INSERT INTO `InstallExecuteSequence` (`Action`,`Sequence`) VALUES (''InstallExecute'',6500)';$execute=6500}
$failure=[Math]::Max($remove,$execute)+1
if($failure -ge $finalize){throw 'No sequence slot for failure before finalization.'}
Change 'INSERT INTO `CustomAction` (`Action`,`Type`,`Target`) VALUES (''ForceUpgradeRollback'',19,''Disposable test: fail after installing replacement payload'')'
Change ('INSERT INTO `InstallExecuteSequence` (`Action`,`Condition`,`Sequence`) VALUES (''ForceUpgradeRollback'',''WIX_UPGRADE_DETECTED'',{0})' -f $failure)
$null=$db.GetType().InvokeMember('Commit','InvokeMethod',$null,$db,$null)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
[pscustomobject]@{candidateProductCode=$code;candidateVersion=$version;candidateSha256=(Get-FileHash "$output/candidate.msi").Hash;failingCandidateSha256=(Get-FileHash "$output/failing-candidate.msi").Hash;installInitializeSequence=$initialize;removeExistingProductsSequence=$remove;forcedFailureSequence=$failure;installFinalizeSequence=$finalize}|ConvertTo-Json|Set-Content "$output/fixture.json"

