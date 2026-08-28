# Generates the embedded fixed catalog resource (embedded_catalog.tsv) from the
# source BOQ workbook (ts.xlsx). The plugin ships with this catalog baked in, so
# end users never provide a catalog Excel — only the machine/device workbook.
#
# Usage: pwsh ./scripts/GenerateEmbeddedCatalog.ps1 [-CatalogPath ts.xlsx] [-OutputPath src\UNCAD\Resources\embedded_catalog.tsv]
[CmdletBinding()]
param(
    [string]$CatalogPath = (Join-Path $PSScriptRoot "..\ts.xlsx"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\UNCAD\Resources\embedded_catalog.tsv"),
    [string]$BundlePath = (Join-Path $PSScriptRoot "..\bundle\UNCAD.bundle")
)
$ErrorActionPreference = "Stop"
$CatalogPath = [IO.Path]::GetFullPath($CatalogPath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$bundle = [IO.Path]::GetFullPath($BundlePath)

if (-not (Test-Path $CatalogPath)) { throw "Catalog workbook not found: $CatalogPath" }
@('ICSharpCode.SharpZipLib.dll', 'BouncyCastle.Crypto.dll', 'NPOI.dll',
    'NPOI.OpenXmlFormats.dll', 'NPOI.OpenXml4Net.dll', 'NPOI.OOXML.dll') | ForEach-Object {
    $dll = Join-Path $bundle $_
    if (-not (Test-Path $dll)) { throw "Missing NPOI assembly: $dll" }
    [Reflection.Assembly]::LoadFrom($dll) | Out-Null
}

# Mirrors ExcelColumnReader.CellToString so the baked resource matches runtime parsing.
function Get-CellText([NPOI.SS.UserModel.ICell]$cell) {
    if ($null -eq $cell) { return "" }
    switch ($cell.CellType) {
        ([NPOI.SS.UserModel.CellType]::String) {
            $v = [string]$cell.StringCellValue
            if ($null -eq $v) { return "" }
            return $v
        }
        ([NPOI.SS.UserModel.CellType]::Numeric) {
            $d = $cell.NumericCellValue
            if ([Math]::Abs($d - [Math]::Floor($d)) -lt 1e-9) {
                return $d.ToString('0', [Globalization.CultureInfo]::InvariantCulture)
            }
            return $d.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
        }
        ([NPOI.SS.UserModel.CellType]::Boolean) {
            return $(if ($cell.BooleanCellValue) { 'TRUE' } else { 'FALSE' })
        }
        ([NPOI.SS.UserModel.CellType]::Formula) {
            switch ($cell.CachedFormulaResultType) {
                ([NPOI.SS.UserModel.CellType]::String) { return [string]$cell.StringCellValue }
                ([NPOI.SS.UserModel.CellType]::Numeric) {
                    $d = $cell.NumericCellValue
                    if ([Math]::Abs($d - [Math]::Floor($d)) -lt 1e-9) {
                        return $d.ToString('0', [Globalization.CultureInfo]::InvariantCulture)
                    }
                    return $d.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
                }
                ([NPOI.SS.UserModel.CellType]::Boolean) {
                    return $(if ($cell.BooleanCellValue) { 'TRUE' } else { 'FALSE' })
                }
                default { return "" }
            }
        }
        default { return "" }
    }
}

function Get-RowText([NPOI.SS.UserModel.IRow]$row, [int]$column) {
    if ($null -eq $row -or $column -lt 0 -or $column -ge $row.LastCellNum) { return "" }
    return (Get-CellText $row.GetCell($column)).Trim()
}

# TSV escaping: backslash first, then tab/CR/LF as two-character escapes.
function Escape-Tsv([string]$value) {
    return ($value -replace '\\', '\\\\' -replace "\t", '\t' -replace "\r", '\r' -replace "\n", '\n')
}

# Open with read-write sharing so the workbook can be generated while the source
# xlsx is open in Excel (mirrors the plugin's FileShare policy).
$fs = [IO.FileStream]::new($CatalogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
    [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
$wb = [NPOI.XSSF.UserModel.XSSFWorkbook]::new($fs)
try {
    $sheet = $null
    for ($i = 0; $i -lt $wb.NumberOfSheets; $i++) {
        $sh = $wb.GetSheetAt($i)
        $hdr = $sh.GetRow(0)
        if ($null -eq $hdr) { continue }
        for ($c = 0; $c -lt $hdr.LastCellNum; $c++) {
            if ((Get-CellText $hdr.GetCell($c)) -eq "项目特征") { $sheet = $sh; break }
        }
        if ($null -ne $sheet) { break }
    }
    if ($null -eq $sheet) { throw "No list sheet with 项目特征 header in $CatalogPath" }

    $colCategory = -1; $colCode = -1; $colName = -1
    $colFeature = -1; $colUnit = -1; $colAlias = -1; $colAlias1 = -1
    $hdr = $sheet.GetRow(0)
    for ($c = 0; $c -lt $hdr.LastCellNum; $c++) {
        $text = (Get-CellText $hdr.GetCell($c)).Trim()
        switch -Regex ($text) {
            '^(类|类别)$' { $colCategory = $c }
            '^(编号|项次编码)$' { $colCode = $c }
            '^项目名称$' { $colName = $c }
            '^项目特征$' { $colFeature = $c }
            '^单位$' { $colUnit = $c }
            '^(别名|规格|型号规格)$' { $colAlias = $c }
            '^(别名1|迁移别名)$' { $colAlias1 = $c }
        }
    }
    if ($colCode -lt 0 -or $colName -lt 0 -or $colFeature -lt 0 -or $colUnit -lt 0) {
        throw "Catalog sheet is missing required headers 编号/项目名称/项目特征/单位."
    }
    if ($colAlias -lt 0) { $colAlias = 5 }

    $lines = New-Object System.Collections.Generic.List[string]
    for ($r = 1; $r -le $sheet.LastRowNum; $r++) {
        $row = $sheet.GetRow($r)
        if ($null -eq $row) { continue }
        $code = Get-RowText $row $colCode
        $name = Get-RowText $row $colName
        if ($code.Length -eq 0 -or $name.Length -eq 0) { continue }
        if ($name -eq '小计' -or $name -eq '合计' -or $name -like '*Note*') { continue }
        if ($code.IndexOf('.') -lt 0) { continue }

        $fields = @(
            (Escape-Tsv $code),
            (Escape-Tsv (Get-RowText $row $colCategory)),
            (Escape-Tsv $name),
            (Escape-Tsv (Get-RowText $row $colFeature)),
            (Escape-Tsv (Get-RowText $row $colUnit)),
            (Escape-Tsv (Get-RowText $row $colAlias)),
            (Escape-Tsv (Get-RowText $row $colAlias1))
        )
        $lines.Add(($fields -join "`t"))
    }

    $dir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
    Write-Output ("EMBEDDED_CATALOG_WRITTEN=" + $OutputPath)
    Write-Output ("ROWS=" + $lines.Count)
}
finally {
    $wb.Close()
    $fs.Dispose()
}
