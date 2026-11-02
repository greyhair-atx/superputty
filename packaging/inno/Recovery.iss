// Recovery is limited to this installer's fixed directory and own ARP key.
// User preference/session/layout files are never backed up or restored here.
[Code]
const
  OwnerKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#CandidateAppId}}_is1';
var
  RecoveryRoot: String;
  AppRoot: String;
  Shortcut: String;
  TransactionStarted: Boolean;
  TransactionCommitted: Boolean;
  SetupFailure: Integer;

function NativeCreateFile(Name: String; Access, Share: LongWord;
  Security: THandle; Creation, Flags: LongWord; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function NativeCloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function NativeFlush(Handle: THandle): Boolean;
  external 'FlushFileBuffers@kernel32.dll stdcall';
function NativeMove(OldName, NewName: String; Flags: LongWord): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';
function NativeAttributes(Name: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

procedure Require(Condition: Boolean; Message: String);
begin
  if not Condition then RaiseException(Message);
end;

procedure SafePath(Path: String);
var Attr: LongWord; Parent: String;
begin
  Require((CompareText(Path, AppRoot) = 0) or
    (CompareText(Copy(Path, 1, Length(AppRoot)+1), AppRoot+'\') = 0),
    'Recovery path is outside this installation.');
  Parent := Path;
  repeat
    Attr := NativeAttributes(Parent);
    Require((Attr = $FFFFFFFF) or ((Attr and $400) = 0),
      'Reparse points are not supported in the installation directory.');
    if CompareText(Parent, AppRoot) = 0 then Break;
    Parent := ExtractFileDir(Parent);
  until Length(Parent) < Length(AppRoot);
end;

procedure FlushFile(Path: String);
var Handle: THandle;
begin
  Handle := NativeCreateFile(Path, $40000000, 7, 0, 3, 0, 0);
  Require(Handle <> THandle(-1), 'Cannot flush recovery backup.');
  try Require(NativeFlush(Handle), 'Cannot persist recovery backup.');
  finally NativeCloseHandle(Handle); end;
end;

procedure DurableCopy(Source, Destination: String);
begin
  SafePath(Destination);
  Require(CopyFile(Source, Destination, False), 'Cannot copy recovery state.');
  FlushFile(Destination);
end;

procedure Marker(Name: String);
var Temp, Target, Contents: String;
begin
  Temp := RecoveryRoot+'\'+Name+'.tmp'; Target := RecoveryRoot+'\'+Name;
  Contents := 'SuperPuTTY recovery v1';
  if Name = 'ready' then Contents := GetSHA256OfFile(RecoveryRoot+'\state.ini');
  Require(SaveStringToFile(Temp, Contents, False), 'Cannot write recovery marker.');
  FlushFile(Temp);
  Require(NativeMove(Temp, Target, 9), 'Cannot persist recovery marker.');
end;

procedure RecordBackup(Name: String);
begin
  Require(SetIniString('Hashes',Name,GetSHA256OfFile(RecoveryRoot+'\'+Name),
    RecoveryRoot+'\state.ini'), 'Cannot record backup digest.');
end;

procedure VerifyBackup(Name: String);
var Expected: String;
begin
  Expected := GetIniString('Hashes',Name,'',RecoveryRoot+'\state.ini');
  Require((Length(Expected) = 64) and FileExists(RecoveryRoot+'\'+Name) and
    (CompareText(GetSHA256OfFile(RecoveryRoot+'\'+Name),Expected) = 0), 'Recovery backup integrity check failed.');
end;

function ValidVersion(Version: String): Boolean;
var I: Integer; Packed: Int64;
begin
  Result := False;
  if (Length(Version) = 0) or (Length(Version) > 40) then Exit;
  for I := 1 to Length(Version) do
    if Pos(Copy(Version,I,1), '0123456789.') = 0 then Exit;
  Result := StrToVersion(Version, Packed);
end;

function VersionDirectory(Version: String): String;
begin
  Require(ValidVersion(Version), 'Invalid version in recovery journal.');
  Result := AppRoot+'\versions\'+Version;
  SafePath(Result);
end;

procedure ParseManifestLine(Line: String; var Hash, Relative: String);
var I: Integer;
begin
  Require((Length(Line) > 66) and (Copy(Line,65,2) = ' *'), 'Invalid payload manifest.');
  Hash := Copy(Line,1,64); Relative := Copy(Line,67,Length(Line));
  for I := 1 to 64 do Require(Pos(Lowercase(Copy(Hash,I,1)), '0123456789abcdef') > 0, 'Invalid manifest digest.');
  Require((Relative <> '') and (Copy(Relative,1,1) <> '\') and
    (Pos('..',Relative) = 0) and (Pos(':',Relative) = 0) and
    (Pos('/',Relative) = 0) and (Pos(#9,Relative) = 0), 'Invalid manifest path.');
end;

procedure VerifyPayload(Directory, Manifest: String; CheckLocks: Boolean);
var Lines: TArrayOfString; I: Integer; Hash, Relative, Path: String; Handle: THandle;
begin
  SafePath(Directory);
  Require(LoadStringsFromFile(Manifest, Lines) and (GetArrayLength(Lines) > 0), 'Payload manifest missing.');
  for I := 0 to GetArrayLength(Lines)-1 do begin
    ParseManifestLine(Lines[I], Hash, Relative);
    Path := Directory+'\'+Relative; SafePath(Path);
    Require(FileExists(Path) and (CompareText(GetSHA256OfFile(Path),Hash) = 0), 'Installed payload differs from its manifest.');
    if CheckLocks then begin
      Handle := NativeCreateFile(Path, $40000000, 7, 0, 3, 0, 0);
      Require(Handle <> THandle(-1), 'Close SuperPuTTY and release locked application files before upgrading.');
      NativeCloseHandle(Handle);
    end;
  end;
end;

procedure RunRegistry(Arguments: String);
var ExitCode: Integer; Started: Boolean;
begin
  // The setup process can be 32-bit while the installation owns the 64-bit ARP view.
  Started := Exec(ExpandConstant('{sysnative}\reg.exe'), Arguments, '', SW_HIDE,
    ewWaitUntilTerminated, ExitCode);
  Log(Format('SPCE_RECOVERY: native registry process started=%d exit=%d', [Ord(Started), ExitCode]));
  Require(Started and (ExitCode = 0), 'Cannot preserve or restore installer registration.');
end;

function RegistryRootName: String;
begin
  if IsAdminInstallMode then Result := 'HKLM' else Result := 'HKCU';
end;

procedure RemoveKnownFile(Path: String);
begin
  SafePath(Path);
  if FileExists(Path) then Require(DeleteFile(Path), 'Cannot remove owned recovery file.');
  // A failed file write can leave an empty directory at a declared file path.
  // Never recurse: foreign/nonempty directories and reparse points stay blocked.
  if DirExists(Path) then Require(RemoveDir(Path), 'Cannot remove empty owned file-collision directory.');
end;

procedure ClearJournal;
var Names: TArrayOfString; I: Integer;
begin
  Names := ['ready','ready.tmp','committed','committed.tmp','state.ini',
    'old.reg','old-shortcut.lnk','old-unins000.exe','old-unins000.dat',
    'old-unins000.msg','candidate.manifest'];
  for I := 0 to GetArrayLength(Names)-1 do RemoveKnownFile(RecoveryRoot+'\'+Names[I]);
  if DirExists(RecoveryRoot) then Require(RemoveDir(RecoveryRoot), 'Unexpected files remain in the recovery directory.');
end;

procedure RemoveCandidate(Version: String);
var Directory, Hash, Relative, Parent: String; Lines: TArrayOfString; I: Integer;
begin
  Directory := VersionDirectory(Version);
  Require(LoadStringsFromFile(RecoveryRoot+'\candidate.manifest',Lines), 'Recovery manifest missing.');
  for I := 0 to GetArrayLength(Lines)-1 do begin
    ParseManifestLine(Lines[I],Hash,Relative);
    RemoveKnownFile(Directory+'\'+Relative);
  end;
  RemoveKnownFile(Directory+'\.payload.manifest');
  // Remove only empty parents of declared payload files, never a recursive tree.
  for I := 0 to GetArrayLength(Lines)-1 do begin
    ParseManifestLine(Lines[I],Hash,Relative);
    Parent := ExtractFileDir(Directory+'\'+Relative);
    while Length(Parent) >= Length(Directory) do begin
      SafePath(Parent); RemoveDir(Parent);
      if CompareText(Parent,Directory) = 0 then Break;
      Parent := ExtractFileDir(Parent);
    end;
  end;
  if DirExists(Directory) then Require(RemoveDir(Directory), 'Candidate directory contains unexpected or locked files.');
end;

procedure RemoveUninstallerStaging;
var Entry: TFindRec; Digest, Expected: String;
begin
  // Inno stages its signed replacement uninstaller beside unins000.exe.
  // A killed process leaves that random is-*.tmp file outside the payload tree.
  // Delete only bytes pinned by this transaction, never arbitrary temp files.
  Expected := GetIniString('Recovery','CandidateUninstallerSha256','',RecoveryRoot+'\state.ini');
  Require(Length(Expected) = 64, 'Candidate uninstaller digest missing.');
  if FindFirst(AppRoot+'\is-*.tmp',Entry) then begin
    try repeat
      if (Entry.Attributes and $10) = 0 then begin
        SafePath(AppRoot+'\'+Entry.Name);
        Digest := GetSHA256OfFile(AppRoot+'\'+Entry.Name);
        if (CompareText(Digest,Expected) = 0) or
          (CompareText(Digest,GetIniString('Hashes','old-unins000.exe','',RecoveryRoot+'\state.ini')) = 0) or
          (CompareText(Digest,GetIniString('Hashes','old-unins000.dat','',RecoveryRoot+'\state.ini')) = 0) or
          (CompareText(Digest,GetIniString('Hashes','old-unins000.msg','',RecoveryRoot+'\state.ini')) = 0) then
          RemoveKnownFile(AppRoot+'\'+Entry.Name);
      end;
    until not FindNext(Entry); finally FindClose(Entry); end;
  end;
end;

procedure Recover;
var Version: String; Extensions: TArrayOfString; I: Integer; Contents: AnsiString;
begin
  if not DirExists(RecoveryRoot) then Exit;
  SafePath(RecoveryRoot);
  if FileExists(RecoveryRoot+'\committed') then begin
    Require(LoadStringFromFile(RecoveryRoot+'\committed',Contents) and
      (Contents = 'SuperPuTTY recovery v1'), 'Invalid commit marker.');
    ClearJournal; Exit;
  end;
  if not FileExists(RecoveryRoot+'\ready') then begin ClearJournal; Exit; end;
  Require(LoadStringFromFile(RecoveryRoot+'\ready',Contents) and
    (CompareText(String(Contents),GetSHA256OfFile(RecoveryRoot+'\state.ini')) = 0), 'Recovery state integrity check failed.');
  Require(GetIniString('Recovery','Format','',''+RecoveryRoot+'\state.ini') = '1', 'Unrecognized recovery journal.');
  Version := GetIniString('Recovery','NewVersion','',RecoveryRoot+'\state.ini');
  VerifyBackup('candidate.manifest');
  if GetIniString('Recovery','HadShortcut','0',RecoveryRoot+'\state.ini') = '1' then VerifyBackup('old-shortcut.lnk');
  if GetIniString('Recovery','HadRegistration','0',RecoveryRoot+'\state.ini') = '1' then begin
    Extensions := ['exe','dat','msg'];
    VerifyBackup('old.reg');
    for I := 0 to GetArrayLength(Extensions)-1 do VerifyBackup('old-unins000.'+Extensions[I]);
    for I := 0 to GetArrayLength(Extensions)-1 do begin
      Require(FileExists(RecoveryRoot+'\old-unins000.'+Extensions[I]), 'Previous uninstall backup missing.');
      DurableCopy(RecoveryRoot+'\old-unins000.'+Extensions[I],AppRoot+'\unins000.'+Extensions[I]);
    end;
    Require(FileExists(RecoveryRoot+'\old.reg'), 'Previous registration backup missing.');
    if RegKeyExists(HKA,OwnerKey) then Require(RegDeleteKeyIncludingSubkeys(HKA,OwnerKey), 'Cannot reset owned registration.');
    RunRegistry('import "'+RecoveryRoot+'\old.reg"');
  end else begin
    if RegKeyExists(HKA,OwnerKey) then Require(RegDeleteKeyIncludingSubkeys(HKA,OwnerKey), 'Cannot remove incomplete registration.');
    RemoveKnownFile(AppRoot+'\unins000.exe'); RemoveKnownFile(AppRoot+'\unins000.dat'); RemoveKnownFile(AppRoot+'\unins000.msg');
  end;
  if GetIniString('Recovery','HadShortcut','0',RecoveryRoot+'\state.ini') = '1' then
    Require(CopyFile(RecoveryRoot+'\old-shortcut.lnk',Shortcut,False), 'Cannot restore previous shortcut.')
  else if FileExists(Shortcut) then Require(DeleteFile(Shortcut), 'Cannot remove incomplete shortcut.');
  if FileExists(ExtractFileDir(Shortcut)+'\.superputty-ce-next-{#CandidateAppId}.lnk') then
    Require(DeleteFile(ExtractFileDir(Shortcut)+'\.superputty-ce-next-{#CandidateAppId}.lnk'), 'Cannot remove shortcut staging file.');
  RemoveCandidate(Version);
  RemoveUninstallerStaging;
  RemoveKnownFile(AppRoot+'\.next-shortcut.lnk');
  ClearJournal;
  Log('SPCE_RECOVERY: previous installation restored');
end;

procedure BeginTransaction;
var Previous, State: String; OldVersion, NewVersion: Int64; Extensions: TArrayOfString; I: Integer; HadRegistration: Boolean; Entry, Child: TFindRec;
begin
  Recover;
#if TestFault == "recovery-only"
  RaiseException('SPCE_TEST_RECOVERY_ONLY: recovery completed before a new transaction');
#endif
  HadRegistration := RegKeyExists(HKA,OwnerKey);
  if not HadRegistration and FindFirst(AppRoot+'\*',Entry) then begin
    try repeat
      if (Entry.Name <> '.') and (Entry.Name <> '..') then begin
        Require(Entry.Name = 'versions', 'An unregistered installation directory contains foreign files.');
        SafePath(AppRoot+'\versions');
        if FindFirst(AppRoot+'\versions\*',Child) then begin
          try repeat
            Require((Child.Name = '.') or (Child.Name = '..'), 'Unregistered version payloads require manual review.');
          until not FindNext(Child); finally FindClose(Child); end;
        end;
      end;
    until not FindNext(Entry); finally FindClose(Entry); end;
  end;
  if HadRegistration then begin
    Require(RegQueryStringValue(HKA,OwnerKey,'DisplayVersion',Previous) and ValidVersion(Previous), 'Cannot establish previous version.');
    Require(StrToVersion(Previous,OldVersion) and StrToVersion('{#CandidateVersion}',NewVersion) and
      (ComparePackedVersion(OldVersion,NewVersion) < 0), 'Same-version reinstalls and downgrades require a separate reviewed repair procedure.');
    VerifyPayload(VersionDirectory(Previous),VersionDirectory(Previous)+'\.payload.manifest',True);
  end;
  Require(not DirExists(VersionDirectory('{#CandidateVersion}')), 'An unowned candidate directory already exists.');
  Require(ForceDirectories(RecoveryRoot), 'Cannot prepare recovery directory.');
  State := RecoveryRoot+'\state.ini';
  Require(Length('{#CandidateUninstallerSha256}') = 64, 'Use the completed, hash-pinned candidate build.');
  Require(SetIniString('Recovery','Format','1',State) and
    SetIniString('Recovery','NewVersion','{#CandidateVersion}',State) and
    SetIniString('Recovery','CandidateUninstallerSha256','{#CandidateUninstallerSha256}',State), 'Cannot write recovery state.');
  if HadRegistration then begin
    Require(SetIniString('Recovery','HadRegistration','1',State), 'Cannot record previous installation.');
    RunRegistry('export "'+RegistryRootName+'\'+OwnerKey+'" "'+RecoveryRoot+'\old.reg" /y');
    FlushFile(RecoveryRoot+'\old.reg');
    RecordBackup('old.reg');
    Extensions := ['exe','dat','msg'];
    for I := 0 to GetArrayLength(Extensions)-1 do begin
      Require(FileExists(AppRoot+'\unins000.'+Extensions[I]), 'Previous signed uninstaller is incomplete.');
      DurableCopy(AppRoot+'\unins000.'+Extensions[I],RecoveryRoot+'\old-unins000.'+Extensions[I]);
      RecordBackup('old-unins000.'+Extensions[I]);
    end;
  end;
  if FileExists(Shortcut) then begin
    DurableCopy(Shortcut,RecoveryRoot+'\old-shortcut.lnk');
    RecordBackup('old-shortcut.lnk');
    Require(SetIniString('Recovery','HadShortcut','1',State), 'Cannot record previous shortcut.');
  end;
  ExtractTemporaryFile('payload.manifest');
  DurableCopy(ExpandConstant('{tmp}\payload.manifest'),RecoveryRoot+'\candidate.manifest');
  RecordBackup('candidate.manifest');
  FlushFile(State); Marker('ready'); TransactionStarted := True;
end;

procedure ActivateCandidate;
var TemporaryShortcut, Previous: String;
begin
  VerifyPayload(VersionDirectory('{#CandidateVersion}'),RecoveryRoot+'\candidate.manifest',False);
  Require(RegQueryStringValue(HKA,OwnerKey,'DisplayVersion',Previous) and (Previous = '{#CandidateVersion}'), 'New registration was not finalized.');
  Require(ForceDirectories(ExtractFileDir(Shortcut)), 'Cannot create shortcut directory.');
  TemporaryShortcut := ExtractFileDir(Shortcut)+'\.superputty-ce-next-{#CandidateAppId}.lnk';
  Require(not FileExists(TemporaryShortcut), 'An unexpected shortcut staging file already exists.');
  CreateShellLink(TemporaryShortcut,'SuperPuTTY Community Edition',
    VersionDirectory('{#CandidateVersion}')+'\SuperPutty.exe','',
    VersionDirectory('{#CandidateVersion}'),'',0,SW_SHOWNORMAL);
  FlushFile(TemporaryShortcut);
  Require(NativeMove(TemporaryShortcut,Shortcut,9), 'Cannot activate new application shortcut.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  try
    AppRoot := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
    Require(CompareText(AppRoot,ExpandConstant('{autopf}\SuperPuTTY Community Edition')) = 0,
      'Only the fixed scope-specific application directory is supported.');
    SafePath(AppRoot);
    Shortcut := ExpandConstant('{autoprograms}\SuperPuTTY Community Edition.lnk');
    RecoveryRoot := AppRoot+'\.upgrade-recovery';
    BeginTransaction;
  except Result := GetExceptionMessage; end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    try
      ActivateCandidate;
      CandidateCheckpoint('activated');
      Marker('committed'); TransactionCommitted := True;
      ClearJournal;
    except
      Log('SPCE_RECOVERY: activation/commit error: '+GetExceptionMessage);
      SetupFailure := 8;
    end;
  end;
end;

procedure DeinitializeSetup;
begin
  if TransactionStarted and not TransactionCommitted then
    try Recover; except Log('SPCE_RECOVERY: retry installer to finish recovery: '+GetExceptionMessage); end;
end;

function GetCustomSetupExitCode: Integer;
begin Result := SetupFailure; end;

function InitializeUninstall: Boolean;
begin
  Result := OwnInstallerOperation;
  if not Result then Exit;
  AppRoot := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  RecoveryRoot := AppRoot+'\.upgrade-recovery';
  Result := not DirExists(RecoveryRoot);
  if not Result then begin
    Log('Finish installation recovery before uninstalling.');
    if not UninstallSilent then MsgBox('Run the reviewed installer again to finish recovery before uninstalling.',mbError,MB_OK);
  end;
end;
