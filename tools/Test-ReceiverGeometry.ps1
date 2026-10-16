[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$ReplayHost
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ShaderProbe.Common.psm1') -Force -DisableNameChecking
$report = Get-Content -Raw -LiteralPath $BuildReportPath | ConvertFrom-Json
$directory = Split-Path -Parent (Resolve-Path -LiteralPath $BuildReportPath).Path
$output = Join-Path $directory ('geometry-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$metrics = [Collections.Generic.List[object]]::new()
function Check([bool]$Condition,[string]$Name) {
    if (!$Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
}
function Save-Floats([string]$Path,[single[]]$Values) {
    $bytes = [byte[]]::new($Values.Length*4)
    [Buffer]::BlockCopy($Values,0,$bytes,0,$bytes.Length)
    [IO.File]::WriteAllBytes($Path,$bytes)
}
function Identity([single[]]$Values,[int]$Start,[switch]$Homogeneous) {
    $Values[$Start*4]=1
    $Values[($Start+1)*4+1]=1
    $Values[($Start+2)*4+2]=1
    if ($Homogeneous) { $Values[($Start+3)*4+3]=1 }
}
function Run([string]$Fixture,[string]$Shader,[string]$Control,[string]$Label) {
    $path = Join-Path $Fixture "$Label.bin"
    $result = & $ReplayHost $Fixture $Shader $Control $path 2>&1
    if ($LASTEXITCODE) { throw "Geometry replay failed ($Label): $result" }
    $layout = ($result -join "`n") | ConvertFrom-Json
    $layout | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Fixture "$Label.json") -Encoding utf8
    return [pscustomobject]@{path=$path;bytes=[IO.File]::ReadAllBytes($path);layout=$layout}
}
$zero = Join-Path $output 'zero.bin'
$control = Join-Path $output 'control.bin'
Save-Floats $zero ([single[]]::new(16))
Save-Floats $control ([single[]]@(0.012,0.008,0.020,1,0,0,0,0.12,-0.007,0.004,0.012,2,1,0,0,0))
foreach ($pass in @($report.passes)+@($report.variants)) {
    $assembly = [IO.File]::ReadAllText((Join-Path $directory "originals\$($pass.hash).original.asm"))
    $signature = @(Get-ShaderSignature $assembly Input | Where-Object systemValue -CEQ 'NONE')
    $fixture = Join-Path $output $pass.hash
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("EJ_CAPTURE_V1 9 $($signature.Count)")
    $stride = 0
    foreach ($input in $signature) {
        $size = $input.mask.Length
        $format = if ($input.format -ceq 'uint') { @{1=42;2=17;3=7;4=3}[$size] }
            else { @{1=41;2=16;3=6;4=2}[$size] }
        if (!$format) { throw 'Unsupported synthetic input format.' }
        $lines.Add("$($input.name) $($input.index) $format 0 $stride")
        $stride += 4*$size
    }
    $vertices = [byte[]]::new($stride*9)
    for ($vertex=0; $vertex -lt 9; $vertex++) {
        $offset = $vertex*$stride
        $position = [single[]]@((($vertex%3-1)*0.035),(([Math]::Floor($vertex/3)-1)*0.035),0)
        foreach ($input in $signature) {
            $value = switch ($input.name) {
                'POSITION' { @($position)+@(1) }
                'NORMAL' { @(0.2,0.3,0.9327379,0) }
                'TANGENT' { @(0.9778,0,-0.2095,1) }
                'BLENDWEIGHTS' { @(1,0,0,0) }
                'BLENDINDICES' { @(0,0,0,0) }
                'COLOR' { @(1,1,1,1) }
                default { @($position)+@(1) }
            }
            for ($component=0; $component -lt $input.mask.Length; $component++) {
                $bytes = if ($input.format -ceq 'uint') { [BitConverter]::GetBytes([uint32]$value[$component]) }
                    else { [BitConverter]::GetBytes([single]$value[$component]) }
                [Array]::Copy($bytes,0,$vertices,$offset,4)
                $offset += 4
            }
        }
    }
    [IO.File]::WriteAllBytes((Join-Path $fixture 'vb0.bin'),$vertices)
    $lines.Add('1')
    $lines.Add("0 $stride")
    $lines.Add('4')
    $buffers = @{}
    foreach ($slot in 0..3) {
        $buffers[$slot]=[single[]]::new(4096*4)
        $lines.Add([string]$slot)
    }
    Identity $buffers[0] 8 -Homogeneous
    Identity $buffers[0] 32 -Homogeneous
    Identity $buffers[0] 57 -Homogeneous
    Identity $buffers[[int]$pass.instanceSlot] 0
    Identity $buffers[[int]$pass.instanceSlot] 6
    $lightProjection = [regex]::Match($assembly,'(?m)^mul r\d+\.xyzw, r\d+\.[xyzw]{4}, cb(?<slot>[123])\[1\]\.xyzw$')
    if ($lightProjection.Success) {
        Identity $buffers[[int]$lightProjection.Groups['slot'].Value] 0 -Homogeneous
    }
    $buffers[1][5*4]=1920
    $buffers[1][5*4+1]=1080
    if ($pass.instanceSlot -eq 1) { $buffers[1][5*4]=0; $buffers[1][5*4+1]=0 }
    else {
        $buffers[1][1*4]=1920
        $buffers[1][1*4+1]=1080
        $buffers[1][1*4+2]=1.0/1920
        $buffers[1][1*4+3]=1.0/1080
        $buffers[1][3*4+2]=1
        $buffers[1][4*4+3]=1
    }
    foreach ($slot in 0..3) { Save-Floats (Join-Path $fixture "cb$slot.bin") $buffers[$slot] }
    $bones = [single[]]::new(64*4)
    foreach ($start in @(0,3,6,9,12,15)) { Identity $bones $start }
    Save-Floats (Join-Path $fixture 't0.bin') $bones
    [IO.File]::WriteAllLines((Join-Path $fixture 'layout.txt'),$lines)
    $native = Run $fixture $pass.originalBinary $zero 'native-zero'
    $disabled = Run $fixture $pass.binary $zero 'patched-zero'
    Check ((Get-FileHash -LiteralPath $native.path).Hash -ceq (Get-FileHash -LiteralPath $disabled.path).Hash) "all native output bits preserved: $($pass.hash)"
    $active = Run $fixture $pass.binary $control 'active'
    $positionOutput = @($native.layout.outputs | Where-Object name -CEQ 'SV_Position')[0]
    $maximum = 0.0
    $newNonfinite = 0
    foreach ($attribute in $native.layout.outputs | Where-Object float) {
        for ($vertex=0; $vertex -lt 9; $vertex++) {
            for ($component=0; $component -lt $attribute.components; $component++) {
                $offset = $vertex*$native.layout.stride+$attribute.byteOffset+$component*4
                $before = [BitConverter]::ToSingle($native.bytes,$offset)
                $after = [BitConverter]::ToSingle($active.bytes,$offset)
                if ([single]::IsFinite($before) -and ![single]::IsFinite($after)) { $newNonfinite++ }
                if ($attribute.name -ceq 'SV_Position' -and [single]::IsFinite($before) -and [single]::IsFinite($after)) {
                    $maximum = [Math]::Max($maximum,[Math]::Abs($after-$before))
                }
            }
        }
    }
    Check ($newNonfinite -eq 0) "deformation introduces no nonfinite output: $($pass.hash)"
    Check ($maximum -gt 0.00001) "native projection receives nonzero deformation: $($pass.hash)"
    $metrics.Add([ordered]@{hash=$pass.hash;maximumClipComponentChange=$maximum;newNonfinite=$newNonfinite})
}
$testPath = Join-Path $output 'test-report.json'
[ordered]@{
    passed=$true; checks=$checks.ToArray(); metrics=$metrics.ToArray()
    buildReportSha256=(Get-FileHash -LiteralPath $BuildReportPath).Hash
    replayHostSha256=(Get-FileHash -LiteralPath $ReplayHost).Hash
    syntheticGeometry=$true; inGameVerified=$false; liveInstallationModified=$false
} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $testPath -Encoding utf8
Write-Output "Geometry checks passed: $($checks.Count)"
Write-Output "GEOMETRY_REPORT=$testPath"
