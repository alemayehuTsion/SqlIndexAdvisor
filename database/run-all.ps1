param(
    [string] $ServerInstance = 'localhost',
    [switch] $Recreate
)

$ErrorActionPreference = 'Stop'
$databaseName = 'SqlIndexAdvisorShop'

function Invoke-SqlScript {
    param([string] $Path, [string] $Query)

    $output = if ($Path) {
        sqlcmd -S $ServerInstance -E -b -i $Path 2>&1
    }
    else {
        sqlcmd -S $ServerInstance -E -b -Q $Query 2>&1
    }

    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed (exit code $LASTEXITCODE):`n$($output -join "`n")"
    }
}

if ($Recreate) {
    Write-Host "Dropping $databaseName on $ServerInstance"
    Invoke-SqlScript -Query @"
IF DB_ID(N'$databaseName') IS NOT NULL
BEGIN
    ALTER DATABASE $databaseName SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE $databaseName;
END;
"@
}

$scripts = Get-ChildItem -Path $PSScriptRoot -Filter '0*.sql' | Sort-Object Name

foreach ($script in $scripts) {
    $elapsed = Measure-Command { Invoke-SqlScript -Path $script.FullName }
    Write-Host ("{0,-24} {1,6:N1} s" -f $script.Name, $elapsed.TotalSeconds)
}
