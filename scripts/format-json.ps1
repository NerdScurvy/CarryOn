param(
    [string]$TreeRoot = "resources"
)

$ErrorActionPreference = "Stop"
$MaxInlineArrayItems = 8
$SingleLineObjectContexts = @(
    "faces",
    "elements",
    "children",
    "shapeByType",
    "textures",
    "texturesByType"
)
$SingleLinePrimitiveArrayContexts = @(
    "states"
)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

if ([System.IO.Path]::IsPathRooted($TreeRoot)) {
    $ResolvedTreeRoot = $TreeRoot
} else {
    $ResolvedTreeRoot = Join-Path $RepoRoot $TreeRoot
}

if (-not (Test-Path -LiteralPath $ResolvedTreeRoot)) {
    throw "TreeRoot path not found: $ResolvedTreeRoot"
}

function ConvertTo-JsonEscapedString([string]$s) {
    '"' + ($s -replace '\\','\\\\' -replace '"','\\"') + '"'
}

function Test-Primitive($v) {
    $null -eq $v -or
    $v -is [string] -or
    $v -is [bool] -or
    $v -is [byte] -or $v -is [sbyte] -or
    $v -is [int16] -or $v -is [uint16] -or
    $v -is [int32] -or $v -is [uint32] -or
    $v -is [int64] -or $v -is [uint64] -or
    $v -is [single] -or $v -is [double] -or $v -is [decimal]
}

function Format-Primitive($v) {
    if ($null -eq $v) { return "null" }
    if ($v -is [bool]) { return $v.ToString().ToLowerInvariant() }
    if ($v -is [string]) { return ConvertTo-JsonEscapedString $v }
    if ($v -is [single] -or $v -is [double] -or $v -is [decimal]) {
        return [System.Convert]::ToString($v, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    "$v"
}

function Test-NumericArray($arr) {
    if ($null -eq $arr) { return $false }
    if (-not ($arr -is [System.Collections.IEnumerable]) -or $arr -is [string]) { return $false }

    foreach ($item in $arr) {
        if (
            $item -isnot [byte] -and $item -isnot [sbyte] -and
            $item -isnot [int16] -and $item -isnot [uint16] -and
            $item -isnot [int32] -and $item -isnot [uint32] -and
            $item -isnot [int64] -and $item -isnot [uint64] -and
            $item -isnot [single] -and $item -isnot [double] -and $item -isnot [decimal]
        ) {
            return $false
        }
    }

    $true
}

function Test-PrimitiveArray($arr) {
    if ($null -eq $arr) { return $false }
    if (-not ($arr -is [System.Collections.IEnumerable]) -or $arr -is [string]) { return $false }

    foreach ($item in $arr) {
        if (-not (Test-Primitive $item)) {
            return $false
        }
    }

    $true
}

function Test-CompactObjectContext([string]$contextKey) {
    if ([string]::IsNullOrWhiteSpace($contextKey)) {
        return $false
    }

    $SingleLineObjectContexts -contains $contextKey
}

function Test-CompactPrimitiveArrayContext([string]$contextKey) {
    if ([string]::IsNullOrWhiteSpace($contextKey)) {
        return $false
    }

    $SingleLinePrimitiveArrayContexts -contains $contextKey
}

function Format-PrimitiveArray($arr) {
    $vals = @()
    foreach ($item in $arr) { $vals += (Format-Primitive $item) }
    "[ " + ($vals -join ", ") + " ]"
}

function Test-CompactObject($obj) {
    if ($null -eq $obj) { return $false }
    if (Test-Primitive $obj) { return $false }
    if ($obj -is [System.Collections.IEnumerable] -and $obj -isnot [System.Management.Automation.PSCustomObject] -and $obj -isnot [hashtable]) {
        return $false
    }

    $isObjectLike = $obj -is [System.Management.Automation.PSCustomObject] -or $obj -is [hashtable]
    if (-not $isObjectLike) { return $false }

    $props = @($obj.PSObject.Properties)
    if ($props.Count -eq 0) { return $true }

    foreach ($p in $props) {
        $v = $p.Value
        if (Test-Primitive $v) { continue }
        if (Test-NumericArray $v) { continue }
        return $false
    }

    $true
}

function Format-CompactObject($obj) {
    $parts = @()
    foreach ($p in $obj.PSObject.Properties) {
        $name = ConvertTo-JsonEscapedString $p.Name
        $v = $p.Value

        if (Test-Primitive $v) {
            $parts += ($name + ": " + (Format-Primitive $v))
        } elseif (Test-NumericArray $v) {
            $parts += ($name + ": " + (Format-PrimitiveArray $v))
        } else {
            return $null
        }
    }

    "{" + ($parts -join ", ") + "}"
}

function Get-CompactObjectMapFormat($obj, [int]$indentLevel) {
    if ($null -eq $obj) { return $null }

    $isObjectLike = $obj -is [System.Management.Automation.PSCustomObject] -or $obj -is [hashtable]
    if (-not $isObjectLike) { return $null }

    $props = @($obj.PSObject.Properties)
    if ($props.Count -eq 0) { return "{}" }

    foreach ($prop in $props) {
        if (-not (Test-CompactObject $prop.Value)) {
            return $null
        }
    }

    $indent = ("`t" * $indentLevel)
    $nextIndent = ("`t" * ($indentLevel + 1))
    $lines = @("{")

    for ($i = 0; $i -lt $props.Count; $i++) {
        $prop = $props[$i]
        $comma = if ($i -lt $props.Count - 1) { "," } else { "" }
        $propName = ConvertTo-JsonEscapedString $prop.Name
        $propValue = Format-CompactObject $prop.Value
        $lines += ($nextIndent + $propName + ": " + $propValue + $comma)
    }

    $lines += ($indent + "}")
    return ($lines -join "`n")
}

function Format-Value($value, [int]$indentLevel, [string]$contextKey) {
    $indent = ("`t" * $indentLevel)
    $nextIndent = ("`t" * ($indentLevel + 1))

    if (Test-Primitive $value) {
        return (Format-Primitive $value)
    }

    if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
        $items = @($value)

        if (Test-NumericArray $items) {
            return (Format-PrimitiveArray $items)
        }

        if ((Test-CompactPrimitiveArrayContext $contextKey) -and (Test-PrimitiveArray $items) -and ($items.Count -le $MaxInlineArrayItems)) {
            return (Format-PrimitiveArray $items)
        }

        if ($items.Count -eq 0) { return "[]" }

        $lines = @("[")
        for ($i = 0; $i -lt $items.Count; $i++) {
            if ((Test-CompactObjectContext $contextKey) -and $null -ne $items[$i] -and (Test-CompactObject $items[$i])) {
                $compactItem = Format-CompactObject $items[$i]
                if ($null -ne $compactItem) {
                    $itemStr = $compactItem
                } else {
                    $itemStr = Format-Value $items[$i] ($indentLevel + 1) $contextKey
                }
            } else {
                $itemStr = Format-Value $items[$i] ($indentLevel + 1) $contextKey
            }
            $comma = if ($i -lt $items.Count - 1) { "," } else { "" }
            $lines += ($nextIndent + $itemStr + $comma)
        }
        $lines += ($indent + "]")
        return ($lines -join "`n")
    }

    $props = @($value.PSObject.Properties)
    if ($props.Count -eq 0) { return "{}" }

    $lines = @("{")
    for ($i = 0; $i -lt $props.Count; $i++) {
        $p = $props[$i]
        $name = ConvertTo-JsonEscapedString $p.Name
        $v = $p.Value
        $comma = if ($i -lt $props.Count - 1) { "," } else { "" }

        if (Test-CompactObjectContext $p.Name) {
            $compactMap = Get-CompactObjectMapFormat $v ($indentLevel + 1)
            if ($null -ne $compactMap) {
                $lines += ($nextIndent + $name + ": " + $compactMap + $comma)
                continue
            }
        }

        $arrayItems = @($v)
        if ((Test-CompactPrimitiveArrayContext $p.Name) -and (Test-PrimitiveArray $arrayItems) -and ($arrayItems.Count -le $MaxInlineArrayItems)) {
            $lines += ($nextIndent + $name + ": " + (Format-PrimitiveArray $arrayItems) + $comma)
            continue
        }

        if ((Test-CompactObjectContext $contextKey) -and $null -ne $v -and (Test-CompactObject $v)) {
            $compact = Format-CompactObject $v
            if ($null -ne $compact) {
                $lines += ($nextIndent + $name + ": " + $compact + $comma)
                continue
            }
        }

        $childContext = if (Test-CompactObjectContext $contextKey) { $contextKey } else { $p.Name }
        $formattedValue = Format-Value $v ($indentLevel + 1) $childContext
        $lines += ($nextIndent + $name + ": " + $formattedValue + $comma)
    }
    $lines += ($indent + "}")

    ($lines -join "`n")
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$files = Get-ChildItem -LiteralPath $ResolvedTreeRoot -Filter "*.json" -Recurse -File

foreach ($file in $files) {
    $rawJson = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    $jsonObj = $rawJson | ConvertFrom-Json
    $formatted = Format-Value $jsonObj 0 ""
    [System.IO.File]::WriteAllText($file.FullName, $formatted + "`n", $utf8NoBom)
}

"Reformatted $($files.Count) JSON files under $ResolvedTreeRoot"
