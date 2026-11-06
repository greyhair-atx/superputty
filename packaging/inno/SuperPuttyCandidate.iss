; Production authoring. PackagingTestFixture is reserved for disposable tests.
#ifndef PayloadDirectory
  #error PayloadDirectory is required
#endif
#ifndef PayloadManifest
  #error PayloadManifest is required
#endif
#ifndef CandidateVersion
  #error CandidateVersion is required
#endif
#ifndef TestFault
  #define TestFault "none"
#endif
#ifndef CandidateUninstallerSha256
  #define CandidateUninstallerSha256 ""
#endif
#ifndef SignedUninstallerDirectory
  #error SignedUninstallerDirectory is required
#endif
#ifndef PackagingTestFixture
  #if TestFault != "none"
    #error Fault hooks must not be compiled into a production installer
  #endif
#endif
#define CandidateAppId "EBCA29DD-46C1-4A93-9340-79DF5375711D"

[Setup]
AppId={{{#CandidateAppId}}
AppName=SuperPuTTY Community Edition
AppVersion={#CandidateVersion}
AppPublisher=Chris Thornton
DefaultDirName={autopf}\SuperPuTTY Community Edition
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableWelcomePage=no
WizardStyle=modern
AppPublisherURL=https://github.com/greyhair-atx/superputty
AppSupportURL=https://github.com/greyhair-atx/superputty/issues
AppUpdatesURL=https://github.com/greyhair-atx/superputty/releases
SetupMutex=Global\GreyhairATX.SuperPuTTYCE.Installer
#ifdef PackagingTestFixture
OutputBaseFilename=SuperPuTTY-CE-Candidate-{#CandidateVersion}-{#TestFault}
#else
OutputBaseFilename=SuperPuTTY-CE-{#CandidateVersion}-setup-win-x64
#endif
SignedUninstaller=yes
SignedUninstallerDir={#SignedUninstallerDirectory}
#ifdef SigningConfigured
SignTool=project
#endif
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\versions\{#CandidateVersion}\SuperPutty.exe
LicenseFile=..\..\License.txt
CloseApplications=no
RestartApplications=no
SetupLogging=yes
VersionInfoDescription=SuperPuTTY Community Edition Setup
UninstallDisplayName=SuperPuTTY Community Edition

[Files]
Source: "{#PayloadManifest}"; DestName: "payload.manifest"; Flags: dontcopy
Source: "{#PayloadDirectory}\*"; DestDir: "{app}\versions\{#CandidateVersion}"; Flags: recursesubdirs createallsubdirs
Source: "{#PayloadManifest}"; DestName: ".payload.manifest"; DestDir: "{app}\versions\{#CandidateVersion}"; BeforeInstall: CandidateManifestBeforeInstall; AfterInstall: CandidateFilesWritten

#if TestFault == "registry"
[Registry]
Root: HKA; Subkey: "{code:CandidateFailureRegistryKey}"; ValueType: string; ValueName: "Test"; ValueData: "failure"
#endif

[UninstallDelete]
Type: files; Name: "{autoprograms}\SuperPuTTY Community Edition.lnk"

[Code]
var OperationMutex: THandle;
function CreateOperationMutex(Security: THandle; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function OperationLastError: LongWord;
  external 'GetLastError@kernel32.dll stdcall';
function OwnInstallerOperation: Boolean;
var Error: LongWord;
begin
  OperationMutex := CreateOperationMutex(0,True,'Global\GreyhairATX.SuperPuTTYCE.Operation');
  Error := OperationLastError;
  Result := (OperationMutex <> 0) and (Error <> 183);
  if not Result then Log('SPCE_OPERATION_BUSY: another installer or uninstaller is running');
  // Windows releases ownership and closes this handle on process termination,
  // including a forced kill. Keep it for the entire setup/uninstall lifetime.
end;

procedure NativeSleep(Milliseconds: LongWord);
  external 'Sleep@kernel32.dll stdcall';
procedure CandidateCheckpoint(Point: String);
begin
#if TestFault == "pause-files"
  if Point = 'files' then begin Log('SPCE_TEST_PAUSE_FILES'); NativeSleep(300000); end;
#endif
#if TestFault == "pause-activation"
  if Point = 'activated' then begin Log('SPCE_TEST_PAUSE_ACTIVATION'); NativeSleep(300000); end;
#endif
end;

procedure CandidateFilesWritten;
begin CandidateCheckpoint('files'); end;

procedure CandidateManifestBeforeInstall;
begin
#if TestFault == "payload"
  if not CreateDir(ExpandConstant('{app}\versions\{#CandidateVersion}\.payload.manifest')) then
    RaiseException('Could not create file-write failure fixture.');
  Log('SPCE_TEST_ENGINE_FILE_FAILURE: candidate payload written before file collision');
#endif
end;

#if TestFault == "registry"
function CandidateFailureRegistryKey(Param: String): String;
var I: Integer;
begin
  Log('SPCE_TEST_ENGINE_REGISTRY_FAILURE: candidate files written before invalid registry key');
  Result := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#CandidateAppId}}_is1\';
  for I := 1 to 300 do Result := Result+'X';
end;
#endif

function MsiEnumRelatedProducts(UpgradeCode: String; Reserved, Index: LongWord; ProductCode: String): LongWord;
  external 'MsiEnumRelatedProductsW@msi.dll stdcall';

function InitializeSetup: Boolean;
var Codes: TArrayOfString; Product, Message: String; I: Integer; Status, FrameworkRelease: LongWord;
begin
  Result := False;
  if not OwnInstallerOperation then Exit;
  FrameworkRelease := 0;
  if not RegQueryDWordValue(HKLM64,'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full','Release',FrameworkRelease) or
    (FrameworkRelease < 528040) then begin
    Message := 'SuperPuTTY requires .NET Framework 4.8 or later. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48 and run Setup again.';
    Log(Message);
    if not WizardSilent then MsgBox(Message,mbError,MB_OK);
    Exit;
  end;
  Codes := ['{3E4059D9-E5A5-48DE-B787-FC88268C1B95}',
    '{98CB966B-60D4-46C3-AADA-1990464696B8}','{42567F59-2F27-4E5B-A900-9141DC2DD929}'];
  for I := 0 to GetArrayLength(Codes)-1 do begin
    SetLength(Product,39); Status := MsiEnumRelatedProducts(Codes[I],0,0,Product);
    if Status <> 259 then begin
      Log('SPCE_MIGRATION_REQUIRED: existing MSI or unresolved MSI context');
      if Status = 0 then begin
        Product := Copy(Product,1,38);
        Message := 'An existing MSI installation must be removed first ('+Product+').' + #13#10#13#10 +
          'Close SuperPuTTY. Back up SuperPuTTY.settings and the configured sessions/layouts folder. Keep the matching signed MSI for recovery.' + #13#10#13#10 +
          'Uninstall it in Windows Settings, or run:' + #13#10 +
          'winget uninstall --product-code "'+Product+'" --exact' + #13#10#13#10 +
          'Then run this Setup again in the same installation scope. Removing the MSI and installing this version are separate operations.';
      end else
        Message := 'Windows Installer inventory could not be read (error '+IntToStr(Status)+'). No MSI was removed. Resolve the inventory error before retrying Setup.';
      Message := Message + #13#10#13#10 + 'Migration guide: https://github.com/greyhair-atx/superputty/blob/master/docs/manual/upgrading.md';
      Log(Message);
      if not WizardSilent then MsgBox(Message,mbError,MB_OK);
      Exit;
    end;
  end;
  Result := True;
end;

#include "Recovery.iss"
