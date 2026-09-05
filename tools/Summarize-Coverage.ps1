param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [string]$Module = 'Netsphere'
)

$ErrorActionPreference = 'Stop'
[xml]$report = Get-Content -LiteralPath $Path -Raw
$package = $report.coverage.packages.package | Where-Object { $_.name -eq $Module }
if ($null -eq $package) {
    throw "Module '$Module' was not found in '$Path'."
}

# Count each source line once, including async methods, but exclude generated files.
$files = @{}
foreach ($class in $package.classes.class) {
    $filename = [string]$class.filename
    if ($filename -match '[\\/](obj|bin|Generated)[\\/]') {
        continue
    }

    if (-not $files.ContainsKey($filename)) {
        $files[$filename] = @{}
    }

    foreach ($line in $class.lines.line) {
        $number = [int]$line.number
        $files[$filename][$number] = $files[$filename][$number] -or ([int]$line.hits -gt 0)
    }
}

$rows = @(
    foreach ($filename in ($files.Keys | Sort-Object)) {
        $lines = $files[$filename]
        $covered = @($lines.Values | Where-Object { $_ }).Count
        [pscustomobject]@{
            File = $filename
            CoveredLines = $covered
            TotalLines = $lines.Count
            LineCoverage = if ($lines.Count) { [math]::Round(100 * $covered / $lines.Count, 2) } else { 0 }
        }
    }
)

$coveredTotal = ($rows | Measure-Object -Property CoveredLines -Sum).Sum
$lineTotal = ($rows | Measure-Object -Property TotalLines -Sum).Sum
[pscustomobject]@{
    Module = $Module
    CoveredLines = $coveredTotal
    TotalLines = $lineTotal
    LineCoverage = if ($lineTotal) { [math]::Round(100 * $coveredTotal / $lineTotal, 2) } else { 0 }
    Files = $rows
}
