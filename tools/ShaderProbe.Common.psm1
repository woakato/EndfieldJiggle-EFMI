Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-EmbeddedShaderAssembly {
    param([Parameter(Mandatory)][string]$Text)
    $blocks = @([regex]::Matches($Text, '(?s)/\*(?<body>.*?)\*/') |
        Where-Object { $_.Groups["body"].Value -match '(?m)^vs_5_0\s*$' })
    if ($blocks.Count -ne 1) {
        throw "Expected exactly one embedded vs_5_0 assembly block."
    }
    $assembly = $blocks[0].Groups["body"].Value
    $start = $assembly.IndexOf("// Input signature:", [StringComparison]::Ordinal)
    if ($start -lt 0 -or $assembly -notmatch '// Output signature:') {
        throw "Both original signature tables are required."
    }
    return $assembly.Substring($start).Trim() + "`n"
}

function Get-ShaderSignature {
    param(
        [Parameter(Mandatory)][string]$Assembly,
        [Parameter(Mandatory)][ValidateSet("Input", "Output")][string]$Kind
    )
    $table = [regex]::Match($Assembly,
        "(?s)// $Kind signature:(?<table>.*?)(?:// (?:Input|Output|Patch Constant) signature:|(?:\r?\n)vs_\d+_\d+)")
    if (-not $table.Success) {
        throw "Missing $Kind signature."
    }
    $entries = @([regex]::Matches($table.Groups["table"].Value,
        '(?m)^//\s+(?<name>\w+)\s+(?<index>\d+)\s+(?<mask>[xyzw]+)\s+(?<register>\d+)\s+(?<system>\w+)\s+(?<format>\w+)\s*(?<used>[xyzw]*)\s*$') |
        ForEach-Object {
            [ordered]@{
                name = $_.Groups["name"].Value
                index = [int]$_.Groups["index"].Value
                mask = $_.Groups["mask"].Value
                register = [int]$_.Groups["register"].Value
                systemValue = $_.Groups["system"].Value
                format = $_.Groups["format"].Value
                used = $_.Groups["used"].Value
            }
        })
    if ($entries.Count -eq 0) {
        throw "No readable entries in $Kind signature."
    }
    return $entries
}

function ConvertTo-CanonicalShaderProgram {
    param([Parameter(Mandatory)][string]$Assembly)
    $started = $false
    $program = [Collections.Generic.List[string]]::new()
    foreach ($line in ($Assembly -split '\r?\n')) {
        if ($line -match '^\s*vs_5_0\s*$') {
            $started = $true
        }
        if (-not $started) {
            continue
        }
        $instruction = ($line -replace '//.*$', '').Trim()
        if ($instruction -eq "") {
            continue
        }
        # Compare immediate operands by their 32-bit representation, not printing style.
        $instruction = [regex]::Replace($instruction, 'l\((?<values>[^)]*)\)', {
            param($match)
            $values = foreach ($value in $match.Groups["values"].Value.Split(",")) {
                $value = $value.Trim()
                if ($value -match '^0x[0-9a-fA-F]+$') {
                    $bits = [Convert]::ToUInt32($value.Substring(2), 16)
                } elseif ($value -match '[.eE]') {
                    $number = [single]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
                    $bits = [BitConverter]::ToUInt32([BitConverter]::GetBytes($number), 0)
                } else {
                    $number = [long]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
                    $bits = [uint32]($number -band 0xffffffffL)
                }
                "0x{0:x8}" -f $bits
            }
            return "l(" + ($values -join ",") + ")"
        })
        $instruction = $instruction -replace '\bCB(?=\d)', 'cb'
        $program.Add(($instruction -replace '\s+', ''))
    }
    if ($program.Count -eq 0) {
        throw "No vs_5_0 instruction stream found."
    }
    return $program.ToArray()
}

function Find-UniqueAssemblyLine {
    param([Collections.Generic.List[string]]$Lines, [string]$Expected)
    $canonical = $Expected -replace '\s+', ''
    $indices = @(for ($index = 0; $index -lt $Lines.Count; $index++) {
        if (($Lines[$index] -replace '\s+', '') -ceq $canonical) {
            $index
        }
    })
    if ($indices.Count -ne 1) {
        throw "Expected one verified adapter instruction: $Expected"
    }
    return [int]$indices[0]
}

function New-EndfieldDisplacementAssembly {
    param([Parameter(Mandatory)][string]$Assembly)
    $required = @(
        "vs_5_0",
        "dcl_constantbuffer CB0[82], immediateIndexed",
        "dcl_constantbuffer CB1[20], immediateIndexed",
        "dcl_constantbuffer CB2[4091], dynamicIndexed",
        "dcl_constantbuffer CB3[11], immediateIndexed",
        "dcl_resource_structured t0, 16",
        "dcl_input_sgv v9.x, instance_id",
        "dcl_temps 15",
        "mul r7.xyzw, r4.yyyy, cb0[33].xyzw",
        "mad r7.xyzw, cb0[32].xyzw, r4.xxxx, r7.xyzw",
        "mad r7.xyzw, cb0[34].xyzw, r4.zzzz, r7.xyzw",
        "add r7.xyzw, r7.xyzw, cb0[35].xyzw"
    )
    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]]($Assembly -split '\r?\n'))
    foreach ($line in $required) {
        $null = Find-UniqueAssemblyLine -Lines $lines -Expected $line
    }
    if ($Assembly -match '(?m)^[^/]*\b(?:t119|r15)\b') {
        throw "The probe's temporary resource/register is already occupied."
    }
    $tempIndex = Find-UniqueAssemblyLine -Lines $lines -Expected "dcl_temps 15"
    $lines[$tempIndex] = "dcl_temps 16"
    $inputIndex = Find-UniqueAssemblyLine -Lines $lines -Expected "dcl_input v0.xyz"
    $lines.Insert($inputIndex, "dcl_resource_buffer (float,float,float,float) t119")

    $currentIndex = Find-UniqueAssemblyLine -Lines $lines -Expected "add r4.xyz, r4.xyzx, r7.xyzx"
    $currentPatch = [string[]]@(
        "// EndfieldJiggle probe: bounded control is supplied by the INI, off by default.",
        "ld_indexable(buffer)(float,float,float,float) r15.xyzw, l(0), t119.xyzw",
        "if_nz r15.w",
        "  add r4.xyz, r4.xyzx, r15.xyzx",
        "endif"
    )
    $lines.InsertRange($currentIndex + 1, $currentPatch)
    $previousIndex = Find-UniqueAssemblyLine -Lines $lines -Expected "add r0.xyz, r0.xzwx, r3.yzwy"
    if ($previousIndex -le $currentIndex) {
        throw "Unexpected order of current and previous world-position instructions."
    }
    $lines.InsertRange($previousIndex + 1, [string[]]@(
        "// Apply the same static offset before the previous-frame projection.",
        "if_nz r15.w",
        "  add r0.xyz, r0.xyzx, r15.xyzx",
        "endif"
    ))
    return ($lines -join "`n") + "`n"
}

function Invoke-ShaderAssembler {
    param(
        [Parameter(Mandatory)][string]$AssemblerPath,
        [Parameter(Mandatory)][string]$AssemblyPath
    )
    $output = & $AssemblerPath --assemble --stop-on-failure $AssemblyPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Assembly failed: $($output -join [Environment]::NewLine)"
    }
    $binaryPath = [IO.Path]::ChangeExtension($AssemblyPath, ".shdr")
    if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
        throw "Assembler did not produce $binaryPath"
    }
    $bytes = [IO.File]::ReadAllBytes($binaryPath)
    if ($bytes.Length -lt 32 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne "DXBC") {
        throw "Assembler output is not a DXBC container."
    }
    return $binaryPath
}

function Test-ShaderAssemblyRoundtrip {
    param(
        [Parameter(Mandatory)][string]$AssemblerPath,
        [Parameter(Mandatory)][string]$AssemblyPath,
        [Parameter(Mandatory)][string]$BinaryPath
    )
    $roundtripBinary = [IO.Path]::ChangeExtension($AssemblyPath, ".roundtrip.shdr")
    Copy-Item -LiteralPath $BinaryPath -Destination $roundtripBinary -Force
    $output = & $AssemblerPath --disassemble --validate --stop-on-failure $roundtripBinary 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Binary roundtrip validation failed: $($output -join [Environment]::NewLine)"
    }
    $disassemblyPath = [IO.Path]::ChangeExtension($roundtripBinary, ".asm")
    $source = [IO.File]::ReadAllText($AssemblyPath)
    $roundtrip = [IO.File]::ReadAllText($disassemblyPath)
    foreach ($kind in @("Input", "Output")) {
        $before = @(Get-ShaderSignature -Assembly $source -Kind $kind) | ConvertTo-Json -Depth 4 -Compress
        $after = @(Get-ShaderSignature -Assembly $roundtrip -Kind $kind) | ConvertTo-Json -Depth 4 -Compress
        if ($before -cne $after) {
            throw "$kind signature changed during assembly."
        }
    }
    $before = (ConvertTo-CanonicalShaderProgram -Assembly $source) -join "`n"
    $after = (ConvertTo-CanonicalShaderProgram -Assembly $roundtrip) -join "`n"
    if ($before -cne $after) {
        $firstDifference = @(Compare-Object `
            (ConvertTo-CanonicalShaderProgram -Assembly $source) `
            (ConvertTo-CanonicalShaderProgram -Assembly $roundtrip) -CaseSensitive |
            Select-Object -First 6)
        throw "Instruction stream changed during assembly: $($firstDifference | Out-String)"
    }
    return [ordered]@{
        inputSignaturePreserved = $true
        outputSignaturePreserved = $true
        instructionStreamPreserved = $true
        binaryReassemblyValidated = $true
        instructionAndDeclarationLines = @(ConvertTo-CanonicalShaderProgram -Assembly $source).Count
        disassemblyFile = $disassemblyPath
    }
}

Export-ModuleMember -Function Get-EmbeddedShaderAssembly, Get-ShaderSignature,
    ConvertTo-CanonicalShaderProgram, New-EndfieldDisplacementAssembly,
    Invoke-ShaderAssembler, Test-ShaderAssemblyRoundtrip
