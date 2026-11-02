[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory,[int]$Port=9443,[string]$InstallerDirectory,
 [switch]$CandidateInstallers)
$ErrorActionPreference='Stop'
# A deliberately narrow REST fixture for the two reviewed manifest versions.
# Metadata-only by default. An optional hash-pinned prototype directory allows
# disposable localhost testing without publishing unsigned installers.
$catalog=Get-Content (Join-Path $Directory 'catalog.json') -Raw|ConvertFrom-Json
$downloads=@{}
if($InstallerDirectory){
    $InstallerDirectory=(Resolve-Path $InstallerDirectory).Path
    $hashManifest=if($CandidateInstallers){'candidate-hashes.json'}else{'prototype-hashes.json'}
    $allowed=if($CandidateInstallers){'^SuperPuTTY-CE-Candidate-[0-9.]+-none\.exe$'}else{'^SuperPuTTY-CE-Prototype-[0-9.]+-none\.exe$'}
    foreach($entry in (Get-Content (Join-Path $InstallerDirectory $hashManifest) -Raw|ConvertFrom-Json)){
        if($entry.name -notmatch $allowed){continue}
        $downloads[$entry.name]=[string]$entry.sha256
    }
}
$listener=[Net.HttpListener]::new()
$listener.Prefixes.Add("https://localhost:$Port/")
function Matches($Criterion){
    $field=[string]$Criterion.PackageMatchField
    $keyword=[string]$Criterion.RequestMatch.KeyWord
    $values=switch($field){
        'PackageIdentifier' {@($catalog.PackageIdentifier)}
        'PackageName' {@($catalog.PackageName)}
        'Publisher' {@($catalog.Publisher)}
        'ProductCode' {@($searchVersions|ForEach-Object {$_.ProductCodes})}
        'UpgradeCode' {@($searchVersions|ForEach-Object {$_.UpgradeCodes})}
        'Market' {@('US')}
        'NormalizedPackageNameAndPublisher' {
            $normalize={param($s) [regex]::Replace($s.ToLowerInvariant(),'[^a-z0-9]','')}
            return ((& $normalize $keyword) -eq (& $normalize $catalog.PackageName) -and
                (& $normalize ([string]$Criterion.RequestMatch.Additional)) -eq (& $normalize $catalog.Publisher))
        }
        default {return $false}
    }
    foreach($value in $values){
        switch([string]$Criterion.RequestMatch.MatchType){
            'Exact' {if($value -ceq $keyword){return $true}}
            'CaseInsensitive' {if($value -ieq $keyword){return $true}}
            'StartsWith' {if($value.StartsWith($keyword,[StringComparison]::OrdinalIgnoreCase)){return $true}}
            'Substring' {if($value.IndexOf($keyword,[StringComparison]::OrdinalIgnoreCase) -ge 0){return $true}}
            default {return $false}
        }
    }
    return $false
}
try{
    $listener.Start()
    'ready'|Set-Content (Join-Path $Directory 'catalog-ready.txt')
    while($listener.IsListening){
        $context=$listener.GetContext()
        $request=$context.Request
        $body=''
        $status=200
        $response=$null
        $download=$null
        try{
            $reader=[IO.StreamReader]::new($request.InputStream)
            $body=$reader.ReadToEnd()
            $reader.Dispose()
            $enabled=Get-Content (Join-Path $Directory 'available-versions.json') -Raw|ConvertFrom-Json
            $versions=@($catalog.Versions|Where-Object {$_.PackageVersion -in $enabled})
            $searchVersions=@($catalog.SearchVersions|Where-Object {$_.PackageVersion -in $enabled})
            $path=$request.Url.AbsolutePath.TrimEnd('/')
            if($request.HttpMethod -in @('GET','HEAD') -and $path.StartsWith('/installers/')){
                $name=$path.Substring('/installers/'.Length)
                if(-not $downloads.ContainsKey($name)){$status=404;$response=@{Data=@()}}
                else{
                    $file=Join-Path $InstallerDirectory $name
                    if((Get-FileHash $file).Hash -ne $downloads[$name]){throw 'Prototype download hash changed.'}
                    $download=$file
                }
            }elseif($path -eq '/information'){
                $response=@{Data=@{SourceIdentifier='SuperPuttyDisposableReview';ServerSupportedVersions=@('1.4.0')}}
            }elseif($path -eq '/manifestSearch' -and $request.HttpMethod -eq 'POST'){
                $query=$body|ConvertFrom-Json
                $match=$true
                if(@($query.Inclusions).Count -and $query.Inclusions){
                    $match=$false
                    foreach($criterion in $query.Inclusions){if(Matches $criterion){$match=$true}}
                }
                foreach($criterion in $query.Filters){if(-not (Matches $criterion)){$match=$false}}
                if($query.Query -and $query.Query.KeyWord){
                    $match=$match -and ($catalog.PackageIdentifier.IndexOf($query.Query.KeyWord,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or $catalog.PackageName.IndexOf($query.Query.KeyWord,[StringComparison]::OrdinalIgnoreCase) -ge 0)
                }
                $response=@{Data=@()}
                if($match){$response.Data=@(@{PackageIdentifier=$catalog.PackageIdentifier;PackageName=$catalog.PackageName;Publisher=$catalog.Publisher;Versions=$searchVersions})}
            }elseif($path -eq "/packageManifests/$($catalog.PackageIdentifier)"){
                $requestedVersion=$request.QueryString['Version']
                if($requestedVersion){$versions=@($versions|Where-Object PackageVersion -EQ $requestedVersion)}
                if(-not $versions.Count){$status=404}
                $response=@{Data=@{PackageIdentifier=$catalog.PackageIdentifier;Versions=$versions}}
            }else{
                $status=404
                $response=@{Data=@()}
            }
        }catch{
            $status=500
            $response=@{error=$_.Exception.Message}
        }
        $json=if($download){'[hash-verified prototype installer]'}else{ConvertTo-Json -InputObject $response -Depth 30 -Compress}
        # Assign inside each branch: emitting a byte[] through the PowerShell
        # pipeline boxes every byte and makes binary downloads unnecessarily slow.
        if($download){$bytes=[IO.File]::ReadAllBytes($download)}else{$bytes=[Text.Encoding]::UTF8.GetBytes($json)}
        $offset=[long]0;$count=$bytes.Length
        if($download){
            $context.Response.Headers['Accept-Ranges']='bytes'
            $range=$request.Headers['Range']
            if($range){
                if($range -match '^bytes=(\d+)-(\d*)$' -and [long]::TryParse($Matches[1],[ref]$offset)){
                    $last=[long]($bytes.Length-1)
                    if($Matches[2] -and -not [long]::TryParse($Matches[2],[ref]$last)){$status=416}
                    $last=[Math]::Min($last,$bytes.Length-1)
                    if($offset -gt $last -or $offset -ge $bytes.Length){$status=416}
                    if($status -ne 416){$status=206;$count=$last-$offset+1;$context.Response.Headers['Content-Range']="bytes $offset-$last/$($bytes.Length)"}
                }else{$status=416}
                if($status -eq 416){$offset=0;$count=0;$context.Response.Headers['Content-Range']="bytes */$($bytes.Length)"}
            }
        }
        [pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');method=$request.HttpMethod;url=$request.RawUrl;range=$request.Headers['Range'];body=$body;status=$status;response=$json}|ConvertTo-Json -Compress -Depth 5|Add-Content (Join-Path $Directory 'catalog-requests.jsonl')
        try{
            $context.Response.StatusCode=$status
            $context.Response.ContentType=if($download){'application/octet-stream'}else{'application/json; charset=utf-8'}
            $context.Response.ContentLength64=$count
            if($request.HttpMethod -ne 'HEAD'){$context.Response.OutputStream.Write($bytes,$offset,$count)}
        }catch{
            # Download clients may cancel a probe/range transfer. A disconnected
            # client must not terminate the fixture for later requests.
            [pscustomobject]@{url=$request.RawUrl;error=$_.Exception.Message}|ConvertTo-Json -Compress|Add-Content (Join-Path $Directory 'response-errors.jsonl')
        }finally{try{$context.Response.Close()}catch{}}
    }
}finally{$listener.Close()}
