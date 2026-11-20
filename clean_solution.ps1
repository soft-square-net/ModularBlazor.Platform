[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Position = 0)]
    [string]$Path = "."
)

if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    Write-Error "Directory '$Path' does not exist."
    exit 1
}

Write-Host "Cleaning 'bin' and 'obj' folders under '$Path'..."

Get-ChildItem -LiteralPath $Path -Directory -Recurse -Force |
    Where-Object { $_.Name -in @("bin", "obj") } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
        if ($PSCmdlet.ShouldProcess($_.FullName, "Remove directory recursively")) {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
    }

Write-Host "Done."