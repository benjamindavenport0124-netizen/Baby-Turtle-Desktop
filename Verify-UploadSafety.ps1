param([switch]$SelfTest)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $paths=@(git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if($LASTEXITCODE -ne 0) {throw 'Could not enumerate the Git upload set.'}
    $patterns=@{
        'private key'='-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
        'GitHub token'='\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b'
        'AWS access key'='\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
        'OpenAI-style key'='\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}\b'
    }
    $problems=New-Object 'Collections.Generic.List[string]'
    $textCount=0
    foreach($path in $paths) {
        if(-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $path) -PathType Leaf)) {continue}
        if($path -match '(?i)(\.stl$|(?:^|/)(?:oak-planks\.jpg|turtle-texture-reference\.png|turtle-look-reference\.png)$)') {$problems.Add("Retired model/reference asset in upload set: $path")}
        if($path -match '(^|/)(?:\.env(?:\..*)?|[^/]+\.(?:pfx|p12|key))$' -and $path -notmatch '\.env\.example$') {$problems.Add("Sensitive file in upload set: $path")}
        if($path -match '(^dist/|^\.local/|\.settings$|\.log$|\.exe$)') {$problems.Add("Local/generated file in upload set: $path")}
        if($path -notmatch '\.(?:cs|ps1|py|cmd|md|ya?ml|json|toml|ini|txt)$' -and $path -ne 'LICENSE') {continue}
        $textCount++
        $lines=[IO.File]::ReadAllLines((Join-Path $PSScriptRoot $path))
        for($i=0;$i -lt $lines.Length;$i++) {
            foreach($rule in $patterns.Keys) {
                if($lines[$i] -match $patterns[$rule]) {$problems.Add("Possible $rule at ${path}:$($i+1) (value withheld)")}
            }
        }
    }
    if($problems.Count) {throw ($problems -join [Environment]::NewLine)}
    Write-Output "PASS: $($paths.Count) upload files checked; $textCount text files scanned; no matching credentials or local runtime files. This is a bounded pattern check, not a full secret scanner."
} finally {Pop-Location}

if($SelfTest) {
    $scratch=Join-Path ([IO.Path]::GetTempPath()) ('turtle-scanner-'+[Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($scratch)
    try {
        Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $scratch 'Verify-UploadSafety.ps1')
        git -C $scratch init --quiet
        if($LASTEXITCODE -ne 0) {throw 'Could not initialize scanner fixture.'}
        $fixture=Join-Path $scratch 'fixture.py'
        [IO.File]::WriteAllText($fixture,'print("benign fixture")')
        $output=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scratch 'Verify-UploadSafety.ps1') 2>&1 | Out-String
        if($LASTEXITCODE -ne 0) {throw 'Benign Python scanner fixture was rejected.'}
        # Synthetic pattern marker assembled at runtime, never an actual credential.
        $marker=('gh'+'p_')+('A'*36)
        [IO.File]::WriteAllText($fixture,('token = "'+$marker+'"'))
        $savedPreference=$ErrorActionPreference;$ErrorActionPreference='Continue'
        try {
            $output=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scratch 'Verify-UploadSafety.ps1') 2>&1 | Out-String
            $exitCode=$LASTEXITCODE
        } finally {$ErrorActionPreference=$savedPreference}
        if($exitCode -eq 0 -or $output -notmatch 'Possible GitHub token at fixture.py:1') {throw 'Python credential-pattern fixture was not detected.'}
        if($output.Contains($marker)) {throw 'Scanner exposed the fixture value.'}
        Write-Output 'PASS: Python files scanned; dummy credential pattern rejected with value withheld.'
    } finally {
        $resolved=[IO.Path]::GetFullPath($scratch)
        $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
        if(-not $resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^turtle-scanner-[0-9a-f]{32}$') {throw 'Unexpected scanner scratch cleanup path.'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
