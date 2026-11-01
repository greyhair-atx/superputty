[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference='Stop'
$file=(Resolve-Path $Path).Path
$installer=New-Object -ComObject WindowsInstaller.Installer
$db=$installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@($file,0))
function Values([string]$Sql){
 $view=$db.GetType().InvokeMember('OpenView','InvokeMethod',$null,$db,@($Sql))
 try{
  $null=$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
  while($record=$view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)){
   try{$record.GetType().InvokeMember('StringData','GetProperty',$null,$record,1)}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)}
  }
 }finally{$null=$view.GetType().InvokeMember('Close','InvokeMethod',$null,$view,$null);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}
}
try{
 $initialize=[int](Values 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''InstallInitialize''')
 $remove=[int](Values 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''RemoveExistingProducts''')
 $finalize=[int](Values 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''InstallFinalize''')
 $conditions=@(Values 'SELECT `Condition` FROM `LaunchCondition`')
 if($initialize -le 0 -or $remove -le $initialize -or $remove -ge $finalize){throw 'MSI removal must be inside its installation transaction.'}
 if(-not @($conditions|Where-Object {($_ -replace '\s','') -eq 'InstalledORNOT(WIX_UPGRADE_DETECTEDORLEGACYCOMMUNITYFOUND)ORNOTRollbackDisabled'}).Count){throw 'MSI must reject an upgrade when rollback is disabled.'}
 [pscustomobject]@{path=$file;installInitialize=$initialize;removeExistingProducts=$remove;installFinalize=$finalize;rollbackGuard=$true}|ConvertTo-Json
}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)}
