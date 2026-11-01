; Disposable evaluation only. Never use this AppId/name for a public release.
#ifndef PayloadDirectory
  #error PayloadDirectory must point to a verified extracted portable bundle
#endif
#ifndef PrototypeVersion
  #define PrototypeVersion "1.8.4.100"
#endif
#ifndef FailurePoint
  #define FailurePoint "none"
#endif

[Setup]
AppId={{3C8A31DE-74CA-48B7-889B-F44B19DE8714}
AppName=SuperPuTTY CE Packaging Prototype
AppVersion={#PrototypeVersion}
AppPublisher=Chris Thornton
DefaultDirName={autopf}\SuperPuTTY-CE-Prototype
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputBaseFilename=SuperPuTTY-CE-Prototype-{#PrototypeVersion}-{#FailurePoint}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName=SuperPuTTY CE Packaging Prototype
UninstallDisplayIcon={app}\versions\{#PrototypeVersion}\SuperPutty.exe
LicenseFile=..\..\License.txt
CloseApplications=no
RestartApplications=no
SetupLogging=yes
VersionInfoDescription=Disposable SuperPuTTY packaging evaluation

[Files]
; Old payloads stay intact until uninstall; do not overwrite the active version.
Source: "{#PayloadDirectory}\*"; DestDir: "{app}\versions\{#PrototypeVersion}"; Flags: recursesubdirs createallsubdirs; AfterInstall: PayloadWritten
#if FailurePoint == "payload"
Source: "{#PayloadDirectory}\SuperPutty.exe"; DestDir: "{app}\versions\{#PrototypeVersion}"; DestName: "failure-checkpoint.exe"; BeforeInstall: BlockCheckpointFile
#endif

[Icons]
Name: "{autoprograms}\SuperPuTTY CE Packaging Prototype"; Filename: "{app}\versions\{#PrototypeVersion}\SuperPutty.exe"; WorkingDir: "{app}\versions\{#PrototypeVersion}"

[Registry]
; Last pre-registration checkpoint, after shortcuts have actually been updated.
Root: HKA; Subkey: "{code:PrototypeRegistryKey}"; ValueType: string; ValueName: "Version"; ValueData: "{#PrototypeVersion}"; Flags: uninsdeletekey; BeforeInstall: BeforeRegistration

[Code]
var
  SavedShortcut: String;
  ShortcutPath: String;
  ReachedCommit: Boolean;
  CheckpointDirectory: String;

function MsiEnumRelatedProducts(UpgradeCode: String; Reserved, Index: LongWord;
  ProductCode: String): LongWord;
  external 'MsiEnumRelatedProductsW@msi.dll stdcall';

function InitializeSetup: Boolean;
var
  Codes: TArrayOfString;
  Product: String;
  Status: LongWord;
  I: Integer;
begin
  Result := False;
  Codes := ['{3E4059D9-E5A5-48DE-B787-FC88268C1B95}',
    '{98CB966B-60D4-46C3-AADA-1990464696B8}',
    '{42567F59-2F27-4E5B-A900-9141DC2DD929}'];
  for I := 0 to GetArrayLength(Codes)-1 do begin
    SetLength(Product, 39);
    Status := MsiEnumRelatedProducts(Codes[I], 0, 0, Product);
    if Status <> 259 then begin
      Log('Prototype blocked: existing MSI or MSI enumeration error ' + IntToStr(Status));
      if not WizardSilent then
        MsgBox('Back up settings and use the reviewed MSI migration procedure before installing this prototype.', mbError, MB_OK);
      Exit;
    end;
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Previous: String;
  OldVersion, NewVersion: Int64;
begin
  Result := '';
  if RegQueryStringValue(HKA, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3C8A31DE-74CA-48B7-889B-F44B19DE8714}_is1', 'DisplayVersion', Previous) then begin
    if not StrToVersion(Previous, OldVersion) or
       not StrToVersion('{#PrototypeVersion}', NewVersion) then begin
      Result := 'Cannot establish installed prototype version.';
      Exit;
    end;
    if ComparePackedVersion(OldVersion, NewVersion) >= 0 then begin
      Result := 'Prototype requires a strictly newer installer version.';
      Exit;
    end;
  end;
  if DirExists(ExpandConstant('{app}\versions\{#PrototypeVersion}')) then begin
    Result := 'Candidate payload directory already exists; inspect the previous attempt.';
    Exit;
  end;
  ShortcutPath := ExpandConstant('{autoprograms}\SuperPuTTY CE Packaging Prototype.lnk');
  if FileExists(ShortcutPath) then begin
    SavedShortcut := ExpandConstant('{tmp}\previous-shortcut.lnk');
    if not FileCopy(ShortcutPath, SavedShortcut, False) then
      Result := 'Cannot preserve the previous application shortcut.';
  end;
end;

procedure PayloadWritten;
begin
#if FailurePoint == "payload"
  if FileExists(ExpandConstant('{app}\versions\{#PrototypeVersion}\SuperPutty.exe')) then begin
    Log('SPCE_TEST_FAILURE: replacement executable was written');
  end;
#endif
end;

procedure BeforeRegistration;
begin
#if FailurePoint == "registration"
  Log('SPCE_TEST_FAILURE: payload and shortcut were written before registration');
#endif
end;

procedure BlockCheckpointFile;
begin
  CheckpointDirectory := ExpandConstant('{app}\versions\{#PrototypeVersion}\failure-checkpoint.exe');
  if not CreateDir(CheckpointDirectory) then
    RaiseException('Cannot create disposable file-conflict checkpoint');
end;

function PrototypeRegistryKey(Param: String): String;
var I: Integer;
begin
  Result := 'Software\SuperPuTTY-CE-Prototype';
#if FailurePoint == "registration"
  // An oversized registry key component produces a real engine write failure.
  // It does not change registry permissions or touch another application's keys.
  Result := Result + '\';
  for I := 1 to 300 do Result := Result + 'X';
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then ReachedCommit := True;
end;

procedure DeinitializeSetup;
begin
  // Restore our shortcut backup for handled pre-commit failures only.
  // Process termination is not covered by this prototype.
  if not ReachedCommit and (SavedShortcut <> '') and FileExists(SavedShortcut) then
    if not FileCopy(SavedShortcut, ShortcutPath, False) then
      Log('ERROR: previous shortcut could not be restored');
  // Remove only the empty directory created by this test's file-conflict hook.
  if CheckpointDirectory <> '' then begin
    RemoveDir(CheckpointDirectory);
    RemoveDir(ExpandConstant('{app}\versions\{#PrototypeVersion}'));
  end;
end;
