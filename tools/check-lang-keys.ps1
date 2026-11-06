<#
.SYNOPSIS
    翻訳 CSV (Resources/string.csv) の崩れと、コードから引いているのに CSV に無いキーを見つける。

.DESCRIPTION
    読み込みは Modules/Translator.cs の LoadLangs と Modules/CsvLite.cs に合わせる。
    次の 4 つを失敗として扱う。
      0. キーが空の行                        → LoadLangs が例外で止まり、翻訳がすべて読まれない
      1. 同じキーの行が 2 つ以上ある         → 2 つめ以降は読まれない
      2. 値の数が見出しより多い行            → 見出しの添字が足りず例外になり、その行が丸ごと読まれない
      3. GetString("キー") / HasString("キー") のキーが CSV に無い → 画面に <INVALID:キー> が出る
    3 は文字列リテラルで書かれたキーだけを見る。Enum や変数から作るキーは静的に分からないので見ない。
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csvPath = Join-Path $root 'Resources/string.csv'

# CsvLite.Split と同じ規則で 1 行を分ける
function Split-CsvLine([string]$line) {
    $values = [System.Collections.Generic.List[string]]::new()
    $start = 0; $inQuotes = $false
    for ($i = 0; $i -le $line.Length; $i++) {
        if ($i -lt $line.Length) {
            $c = $line[$i]
            if ($c -eq '"') { $inQuotes = -not $inQuotes; continue }
            if ($c -ne ',' -or $inQuotes) { continue }
        }
        $raw = $line.Substring($start, $i - $start)
        if ($raw.Length -ge 2 -and $raw[0] -eq '"' -and $raw[-1] -eq '"') {
            $raw = $raw.Substring(1, $raw.Length - 2).Replace('""', '"')
        }
        $values.Add($raw)
        $start = $i + 1
    }
    , $values
}

$problems = [System.Collections.Generic.List[string]]::new()
$keys = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)

$lines = [System.IO.File]::ReadAllLines($csvPath, [System.Text.Encoding]::UTF8)
$headerCount = (Split-CsvLine $lines[0]).Count
for ($n = 1; $n -lt $lines.Count; $n++) {
    $line = $lines[$n]
    if ($line.Length -eq 0 -or $line[0] -eq '#') { continue }
    $values = Split-CsvLine $line
    $key = $values[0]
    $lineNo = $n + 1
    if ($key.Length -eq 0) {
        $problems.Add("string.csv:${lineNo}: キーが空 (LoadLangs が例外で止まる)")
        continue
    }
    if ($key[0] -eq '#') { continue }
    if ($values.Count -gt $headerCount) {
        $problems.Add("string.csv:${lineNo}: '$key' の値が $($values.Count) 個あり、見出しの $headerCount 個より多い (行ごと読まれない)")
        continue
    }
    if ($keys.ContainsKey($key)) {
        $problems.Add("string.csv:${lineNo}: '$key' は $($keys[$key]) 行目と重複している")
        continue
    }
    $keys[$key] = $lineNo
}

# コード中の文字列リテラルのキー
$files = Get-ChildItem -Path $root -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs)[\\/]' }
$literal = [regex]'\b(?:GetString|HasString)\(\s*"((?:[^"\\]|\\.)*)"\s*[,)]'
$checked = 0
foreach ($f in $files) {
    $rel = [System.IO.Path]::GetRelativePath($root, $f.FullName) -replace '\\', '/'
    $fileLines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    for ($n = 0; $n -lt $fileLines.Count; $n++) {
        $code = $fileLines[$n]
        $comment = $code.IndexOf('//')
        foreach ($m in $literal.Matches($code)) {
            if ($comment -ge 0 -and $m.Index -gt $comment) { continue }
            $key = $m.Groups[1].Value
            $checked++
            if (-not $keys.ContainsKey($key)) { $problems.Add("${rel}:$($n + 1): '$key' が string.csv に無い") }
        }
    }
}

Write-Host "string.csv のキー: $($keys.Count) 個 / コードから引いているキー (文字列リテラル): $checked 箇所"
if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host "問題が $($problems.Count) 件あります:" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host '問題はありません。' -ForegroundColor Green
