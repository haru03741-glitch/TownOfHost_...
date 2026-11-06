<#
.SYNOPSIS
    設定 (OptionItem) の ID 重複をビルド前に静的に見つける。

.DESCRIPTION
    OptionItem は ID をキーに 1 つの辞書へ登録される (Modules/OptionItem/OptionItem.cs)。
    ID が重複すると 2 つめ以降は登録されず、その設定はメニューから黙って消える。
    実行時の IdDuplicated は DEBUG ビルドでしか立たないので、ここでソースから同じ重複を探す。

    ID を消費する書き方 (OptionHolder.cs / SimpleRoleInfo.cs で確認):
      SimpleRoleInfo.Create(..., configId, ...)        → configId, configId+1 (出現率と人数)
      XxxOptionItem.Create(RoleInfo, offset, ...)      → ConfigId + offset
      XxxOptionItem.Create(<式>, ...)                  → <式>
      Options.SetupRoleOptions(<式>, ...)              → <式>, <式>+1
      OverrideTasksData.Create(RoleInfo, offset, ...)  → ConfigId + offset から 4 個
      OverrideTasksData.Create(<式>, ...)              → <式> から 4 個
      Options.SetUpAddOnOptions(<式>, ...)             → <式>, <式>+10 からバフ数, その後 10 空けてデバフ数

    <式> は数値、(int)offsetId.X、RoleInfo.ConfigId、同じファイルの const / static readonly int を
    + と - でつないだものだけ解く。それ以外 (ループ変数や引数) は解かずに件数だけ数える。

    メニューに出さない役職 (SimpleRoleInfo.Create の flags に RoleFlags.DontShowOption) は出現率と人数を数えない。その役職のファイルにある
    Create(RoleInfo, N) は実際には登録されないこともあるが、手で登録する役職 (JackOLantern など) が
    あるので数えたままにしている。ここで重複が出たら誤検知のこともある。

.PARAMETER ShowUnresolved
    解けなかった呼び出しを一覧で出す。
#>
param(
    [switch]$ShowUnresolved
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# コメントを空白に置き換える。文字列の中の // は残し、行番号がずれないよう改行は保つ。
function Remove-Comments([string]$src) {
    $sb = [System.Text.StringBuilder]::new($src.Length)
    $i = 0; $n = $src.Length
    while ($i -lt $n) {
        $c = $src[$i]
        $next = if ($i + 1 -lt $n) { $src[$i + 1] } else { [char]0 }
        if ($c -eq '/' -and $next -eq '/') {
            while ($i -lt $n -and $src[$i] -ne "`n") { [void]$sb.Append(' '); $i++ }
            continue
        }
        if ($c -eq '/' -and $next -eq '*') {
            [void]$sb.Append('  '); $i += 2
            while ($i -lt $n -and -not ($src[$i] -eq '*' -and $i + 1 -lt $n -and $src[$i + 1] -eq '/')) {
                [void]$sb.Append($(if ($src[$i] -eq "`n") { "`n" } else { ' ' })); $i++
            }
            [void]$sb.Append('  '); $i += 2
            continue
        }
        if ($c -eq '@' -and $next -eq '"') {
            [void]$sb.Append('@"'); $i += 2
            while ($i -lt $n) {
                if ($src[$i] -eq '"') {
                    if ($i + 1 -lt $n -and $src[$i + 1] -eq '"') { [void]$sb.Append('""'); $i += 2; continue }
                    break
                }
                [void]$sb.Append($src[$i]); $i++
            }
            [void]$sb.Append('"'); $i++
            continue
        }
        if ($c -eq '"' -or $c -eq "'") {
            $q = $c
            [void]$sb.Append($c); $i++
            while ($i -lt $n -and $src[$i] -ne $q -and $src[$i] -ne "`n") {
                if ($src[$i] -eq '\') { [void]$sb.Append($src[$i]); $i++ }
                if ($i -lt $n) { [void]$sb.Append($src[$i]); $i++ }
            }
            if ($i -lt $n) { [void]$sb.Append($src[$i]); $i++ }
            continue
        }
        [void]$sb.Append($c); $i++
    }
    $sb.ToString()
}

# $start (開き括弧の直後) から、括弧の深さ 0 の , で区切った引数を ) まで読む。
function Get-Args([string]$text, [int]$start) {
    $list = [System.Collections.Generic.List[string]]::new()
    $depth = 0; $cur = [System.Text.StringBuilder]::new(); $i = $start
    while ($i -lt $text.Length) {
        $c = $text[$i]
        if ($c -eq '"') {
            [void]$cur.Append($c); $i++
            while ($i -lt $text.Length -and $text[$i] -ne '"') {
                if ($text[$i] -eq '\') { [void]$cur.Append($text[$i]); $i++ }
                [void]$cur.Append($text[$i]); $i++
            }
        }
        elseif ($c -in '(', '[', '{') { $depth++ }
        elseif ($c -in ')', ']', '}') {
            if ($depth -eq 0) { $list.Add($cur.ToString().Trim()); return , $list }
            $depth--
        }
        elseif ($c -eq ',' -and $depth -eq 0) {
            $list.Add($cur.ToString().Trim()); [void]$cur.Clear(); $i++; continue
        }
        [void]$cur.Append($c); $i++
    }
    , $list
}

# 解けた値を返す。解けなければ $null。
function Resolve-Expr([string]$expr, [hashtable]$consts, $configId) {
    $e = ($expr -replace '\s', '')
    while ($e.StartsWith('(') -and $e.EndsWith(')') -and $e -notmatch '^\(int\)') { $e = $e.Substring(1, $e.Length - 2) }
    if ($e -eq '') { return $null }
    $total = 0
    foreach ($m in [regex]::Matches($e, '([+-]?)([^+-]+)')) {
        $term = $m.Groups[2].Value
        $v = $null
        if ($term -match '^\d+$') { $v = [int]$term }
        elseif ($term -match '^\(int\)(?:Options\.)?offsetId\.(\w+)$') {
            if ($script:offsets.ContainsKey($Matches[1])) { $v = $script:offsets[$Matches[1]] }
        }
        elseif ($term -eq 'RoleInfo.ConfigId') { $v = $configId }
        elseif ($consts.ContainsKey($term)) { $v = $consts[$term] }
        if ($null -eq $v) { return $null }
        if ($m.Groups[1].Value -eq '-') { $total -= $v } else { $total += $v }
    }
    $total
}

# --- offsetId の値 ---
$holder = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Modules/OptionHolder.cs')
$enumBody = [regex]::Match($holder, 'enum\s+offsetId\s*\{([^}]*)\}').Groups[1].Value
$script:offsets = @{}
foreach ($m in [regex]::Matches((Remove-Comments $enumBody), '(\w+)\s*=\s*(\d+)')) {
    $script:offsets[$m.Groups[1].Value] = [int]$m.Groups[2].Value
}
if ($script:offsets.Count -eq 0) { throw 'offsetId を読めませんでした (Modules/OptionHolder.cs)' }

# --- SetUpAddOnOptions が並べるバフ / デバフの数 (属性の定義 AddOnInfo の見出し) ---
$addOnText = (Get-ChildItem -Path (Join-Path $root 'Roles/AddOns') -Recurse -Filter *.cs | ForEach-Object { Remove-Comments (Get-Content -Raw -Encoding UTF8 $_.FullName) }) -join "`n"
$buffCount = [regex]::Matches($addOnText, 'AddOnGroup\.Buff\b').Count
$debuffCount = [regex]::Matches($addOnText, 'AddOnGroup\.Debuff\b').Count
if ($buffCount -eq 0 -or $debuffCount -eq 0) { throw 'バフ / デバフの属性を読めませんでした (Roles/AddOns)' }

# --- 各ファイルを走査 ---
$uses = @{}          # id -> 使用箇所の配列
$unresolved = [System.Collections.Generic.List[string]]::new()
$resolvedCount = 0

function Add-Use([int]$id, [string]$where) {
    if (-not $uses.ContainsKey($id)) { $uses[$id] = [System.Collections.Generic.List[string]]::new() }
    $uses[$id].Add($where)
    $script:resolvedCount++
}

$files = Get-ChildItem -Path $root -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs)[\\/]' -and $_.FullName -notmatch '[\\/]Modules[\\/]OptionItem[\\/]' }

foreach ($f in $files) {
    $text = Remove-Comments (Get-Content -Raw -Encoding UTF8 $f.FullName)
    if ($text -notmatch 'OptionItem\.Create|SetupRoleOptions|OverrideTasksData\.Create|SetUpAddOnOptions|SimpleRoleInfo\.Create') { continue }
    $rel = [System.IO.Path]::GetRelativePath($root, $f.FullName) -replace '\\', '/'

    $lineStarts = [System.Collections.Generic.List[int]]::new(); $lineStarts.Add(0)
    for ($k = 0; $k -lt $text.Length; $k++) { if ($text[$k] -eq "`n") { $lineStarts.Add($k + 1) } }
    $lineOf = { param($idx) $lo = 0; $hi = $lineStarts.Count - 1
        while ($lo -lt $hi) { $mid = [int][math]::Ceiling(($lo + $hi) / 2); if ($lineStarts[$mid] -le $idx) { $lo = $mid } else { $hi = $mid - 1 } }
        $lo + 1 }

    # 役職の ConfigId (1 ファイル 1 役職)
    $configId = $null
    $roleMatches = [regex]::Matches($text, '\bSimpleRoleInfo\.Create\(')
    if ($roleMatches.Count -gt 1) { $unresolved.Add("${rel}: SimpleRoleInfo.Create が複数あるので RoleInfo を解かない") }
    elseif ($roleMatches.Count -eq 1) {
        $a = Get-Args $text ($roleMatches[0].Index + $roleMatches[0].Length)
        $line = & $lineOf $roleMatches[0].Index
        if ($a.Count -ge 6) { $configId = Resolve-Expr $a[5] @{} $null }
        $roleName = if ($a.Count -ge 3) { $a[2] -replace '^CustomRoles\.', '' } else { '' }
        if ($null -eq $configId) { $unresolved.Add("${rel}:${line}: SimpleRoleInfo.Create の configId '$($a[5])'") }
        # メニューに出さない役職は SetupRoleOptions(info) を通らないので、出現率と人数の ID を使わない
        elseif (-not ($a | Where-Object { $_ -match '^\s*flags\s*:' -and $_ -match 'RoleFlags\.DontShowOption' })) {
            Add-Use $configId "${rel}:${line} (役職の出現率)"
            Add-Use ($configId + 1) "${rel}:${line} (役職の人数)"
        }
    }

    # ファイル内の int 定数。C# は大文字小文字を区別するので、@{} ではなく Ordinal の表に入れる。
    # 同じ名前の int 引数があるファイルでは、どちらを指すか分からないので使わない。
    $consts = [hashtable]::new([StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($text, '\b(?:const|static\s+readonly)\s+int\s+(\w+)\s*=\s*([^;]+);')) {
        $v = Resolve-Expr $m.Groups[2].Value $consts $configId
        if ($null -ne $v) { $consts[$m.Groups[1].Value] = $v }
    }
    foreach ($m in [regex]::Matches($text, '[(,]\s*int\s+(\w+)\s*[,)=]')) { $consts.Remove($m.Groups[1].Value) }

    foreach ($m in [regex]::Matches($text, '\b(?<kind>(?!Client)\w+OptionItem\.Create|SetupRoleOptions|OverrideTasksData\.Create|SetUpAddOnOptions)\(')) {
        # メソッドの定義そのものは飛ばす
        $before = $text.Substring([math]::Max(0, $m.Index - 40), [math]::Min(40, $m.Index))
        if ($before -match '\b(void|OverrideTasksData|static)\s+$') { continue }

        $kind = $m.Groups['kind'].Value
        $a = Get-Args $text ($m.Index + $m.Length)
        $line = & $lineOf $m.Index
        if ($a.Count -eq 0) { continue }
        # SetupRoleOptions(info) は役職の登録 (上で数えた) なので飛ばす
        if ($kind -eq 'SetupRoleOptions' -and $a.Count -eq 1) { continue }

        $base = $null; $label = ''
        if ($a[0] -eq 'RoleInfo' -and $a.Count -ge 2) {
            if ($null -ne $configId) {
                $off = Resolve-Expr $a[1] $consts $configId
                if ($null -ne $off) { $base = $configId + $off }
            }
            if ($a.Count -ge 3) { $label = $a[2] }
        }
        else {
            $base = Resolve-Expr $a[0] $consts $configId
            if ($a.Count -ge 2) { $label = $a[1] }
        }
        if ($null -eq $base) { $unresolved.Add("${rel}:${line}: $kind($($a[0]), ...)"); continue }

        $where = "${rel}:${line} $label"
        switch ($kind) {
            'SetupRoleOptions' { Add-Use $base "$where (出現率)"; Add-Use ($base + 1) "$where (人数)" }
            'OverrideTasksData.Create' { for ($k = 0; $k -lt 4; $k++) { Add-Use ($base + $k) "$where (タスク上書き $k)" } }
            'SetUpAddOnOptions' {
                Add-Use $base "$where (バフ割り当て)"
                for ($k = 0; $k -lt $buffCount; $k++) { Add-Use ($base + 10 + $k) "$where (バフ $k)" }
                $d = $base + 10 + $buffCount
                Add-Use $d "$where (デバフ割り当て)"
                for ($k = 0; $k -lt $debuffCount; $k++) { Add-Use ($d + 10 + $k) "$where (デバフ $k)" }
            }
            default { Add-Use $base $where }
        }
    }
}

$dups = $uses.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 } | Sort-Object Key
Write-Host "解けた ID: $resolvedCount 件 / 解けなかった呼び出し: $($unresolved.Count) 件"
if ($ShowUnresolved) { $unresolved | ForEach-Object { Write-Host "  - $_" } }

if ($dups) {
    Write-Host ''
    Write-Host "ID の重複が $(@($dups).Count) 件あります:" -ForegroundColor Red
    foreach ($d in $dups) {
        Write-Host "  ID $($d.Key)" -ForegroundColor Red
        $d.Value | ForEach-Object { Write-Host "    $_" }
    }
    exit 1
}
Write-Host 'ID の重複はありません。' -ForegroundColor Green
