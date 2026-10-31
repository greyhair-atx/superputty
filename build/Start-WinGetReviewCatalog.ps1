[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory,[int]$Port=9443)
$ErrorActionPreference='Stop'
# A deliberately narrow REST fixture for the two reviewed manifest versions.
# It serves metadata only; installer downloads retain their public signed URLs.
$catalog=Get-Content (Join-Path $Directory 'catalog.json') -Raw|ConvertFrom-Json
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
        try{
            $reader=[IO.StreamReader]::new($request.InputStream)
            $body=$reader.ReadToEnd()
            $reader.Dispose()
            $enabled=Get-Content (Join-Path $Directory 'available-versions.json') -Raw|ConvertFrom-Json
            $versions=@($catalog.Versions|Where-Object {$_.PackageVersion -in $enabled})
            $searchVersions=@($catalog.SearchVersions|Where-Object {$_.PackageVersion -in $enabled})
            $path=$request.Url.AbsolutePath.TrimEnd('/')
            if($path -eq '/information'){
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
        $json=ConvertTo-Json -InputObject $response -Depth 30 -Compress
        [pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');method=$request.HttpMethod;url=$request.RawUrl;body=$body;status=$status;response=$json}|ConvertTo-Json -Compress -Depth 5|Add-Content (Join-Path $Directory 'catalog-requests.jsonl')
        $bytes=[Text.Encoding]::UTF8.GetBytes($json)
        $context.Response.StatusCode=$status
        $context.Response.ContentType='application/json; charset=utf-8'
        $context.Response.ContentLength64=$bytes.Length
        $context.Response.OutputStream.Write($bytes,0,$bytes.Length)
        $context.Response.Close()
    }
}finally{$listener.Close()}
