[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildReportPath,
    [Parameter(Mandatory)][string]$CaptureInventoryPath,
    [Parameter(Mandatory)][string]$ReplayHost
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot 'Universal.Common.psm1') -Force
$reportPath = (Resolve-Path -LiteralPath $BuildReportPath).Path
$report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
$inventory = Get-Content -Raw -LiteralPath $CaptureInventoryPath | ConvertFrom-Json
$frameRoot = (Resolve-Path -LiteralPath $inventory.sourceRoot).Path
$hostPath = (Resolve-Path -LiteralPath $ReplayHost).Path
$output = Assert-UniversalPath (Join-Path $root 'reports') `
    (Join-Path (Split-Path -Parent $reportPath) ('capture-replay-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')))
New-Item -ItemType Directory -Path $output | Out-Null
$cache = @{}
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "FAIL: $Message" }
    $checks.Add($Message)
}
function Read-CaptureBuffer($Draw, [string]$Binding) {
    $artifacts = @($Draw.artifacts | Where-Object { $_.binding -ceq $Binding -and $_.file.EndsWith('.buf') })
    if ($artifacts.Count -ne 1) { throw "Missing unique captured buffer: $($Draw.drawId)/$Binding" }
    $path = [IO.Path]::GetFullPath((Join-Path $frameRoot $artifacts[0].file))
    if (!$path.StartsWith($frameRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Captured artifact escapes the frame directory.'
    }
    $item = Get-Item -LiteralPath $path
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $item = $item.ResolveLinkTarget($true) }
    $resolved = Assert-UniversalPath $frameRoot $item.FullName
    if (!$cache.ContainsKey($resolved)) { $cache[$resolved] = [IO.File]::ReadAllBytes($resolved) }
    return ,$cache[$resolved]
}
function Write-Slice([string]$Path, [byte[]]$Bytes, [int]$Offset, [int]$Length) {
    if ($Offset -lt 0 -or $Length -le 0 -or [long]$Offset+$Length -gt $Bytes.Length) { throw 'Invalid captured buffer range.' }
    $slice = [byte[]]::new($Length)
    [Array]::Copy($Bytes, $Offset, $slice, 0, $Length)
    [IO.File]::WriteAllBytes($Path, $slice)
}
$formats = @{
    R32G32B32_FLOAT=6; R32G32_FLOAT=16; R32_FLOAT=41
    R8G8B8A8_UNORM=28; R8G8B8A8_SNORM=31
    R16G16B16A16_UNORM=11; R8G8B8A8_UINT=30
}
$fixtures = @{}
foreach ($id in @('000036','000074','000079','000086','000100')) {
    $draws = @($inventory.draws | Where-Object drawId -CEQ $id)
    if ($draws.Count -ne 1) { throw "Missing captured draw $id" }
    $draw = $draws[0]
    Check ($draw.parameters.InstanceCount -eq 1 -and $draw.parameters.StartInstanceLocation -eq 0 -and
        $draw.parameters.BaseVertexLocation -eq 0) "captured draw instance contract: $id"
    $fixture = Join-Path $output $id
    New-Item -ItemType Directory -Path $fixture | Out-Null
    $vertexCount = [int]$draw.vertexBuffers[0].metadata.'vertex count'
    $elements = @($draw.vertexBuffers[0].elements)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("EJ_CAPTURE_V1 $vertexCount $($elements.Count)")
    foreach ($element in $elements) {
        if (!$formats.ContainsKey($element.Format) -or $element.InputSlotClass -cne 'per-vertex' -or
            $element.InstanceDataStepRate -ne 0) { throw 'Unreviewed captured input layout.' }
        $lines.Add("$($element.SemanticName) $($element.SemanticIndex) $($formats[$element.Format]) $($element.InputSlot) $($element.AlignedByteOffset)")
    }
    $lines.Add([string]$draw.vertexBuffers.Count)
    foreach ($vertex in $draw.vertexBuffers) {
        $slot = [int]$vertex.binding.Substring(2)
        $stride = [int]$vertex.metadata.stride
        $offset = if ($vertex.metadata.PSObject.Properties['byte offset']) { [int]$vertex.metadata.'byte offset' } else { 0 }
        $bytes = Read-CaptureBuffer $draw $vertex.binding
        $length = if ($stride) { $stride * $vertexCount } else { $bytes.Length - $offset }
        Check ($vertex.metadata.'first vertex' -eq 0) "captured vertex starts at zero: $id/$slot"
        Write-Slice (Join-Path $fixture "vb$slot.bin") $bytes $offset $length
        $lines.Add("$slot $stride")
    }
    $lines.Add([string]$draw.nativeVertexConstantBuffers.Count)
    foreach ($constant in $draw.nativeVertexConstantBuffers) {
        Check ($constant.rangedApi -and $constant.constantCount -le 4096) "captured constant range: $id/$($constant.slot)"
        Write-Slice (Join-Path $fixture "cb$($constant.slot).bin") `
            (Read-CaptureBuffer $draw "vs-cb$($constant.slot)") $constant.byteOffset ($constant.constantCount*16)
        $lines.Add([string]$constant.slot)
    }
    [IO.File]::WriteAllBytes((Join-Path $fixture 't0.bin'), (Read-CaptureBuffer $draw 'vs-t0'))
    [IO.File]::WriteAllLines((Join-Path $fixture 'layout.txt'), $lines)
    $pass = @($report.passes | Where-Object hash -CEQ $draw.vertexShaderHash)
    if ($pass.Count -ne 1) { throw "No candidate program for draw $id" }
    $fixtures[$id] = [pscustomobject]@{ path=$fixture; pass=$pass[0]; draw=$draw; count=$vertexCount }
}
$zeroPath = Join-Path $output 'control-zero.bin'
[IO.File]::WriteAllBytes($zeroPath, [byte[]]::new(64))
function Replay($Fixture, [string]$Shader, [string]$Control, [string]$Label) {
    $path = Join-Path $Fixture.path "$Label.bin"
    $result = & $hostPath $Fixture.path $Shader $Control $path 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Replay failed ($Label): $result" }
    $layout = ($result -join "`n") | ConvertFrom-Json
    $layout | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Fixture.path "$Label.json") -Encoding utf8
    Check ($layout.primitivesWritten -eq $Fixture.count) "complete replay: $($Fixture.draw.drawId)/$Label"
    return [pscustomobject]@{ path=$path; bytes=[IO.File]::ReadAllBytes($path); layout=$layout }
}
function Attribute($Replay, [string]$Name, [int]$Index) {
    $matches = @($Replay.layout.outputs | Where-Object { $_.name -ceq $Name -and $_.index -eq $Index })
    if ($matches.Count -ne 1) { throw "Missing output $Name/$Index" }
    return $matches[0]
}
function Vector($Replay, $Attribute, [int]$Vertex) {
    $offset = $Vertex * $Replay.layout.stride + $Attribute.byteOffset
    return ,([single[]]@(0..($Attribute.components-1) | ForEach-Object { [BitConverter]::ToSingle($Replay.bytes,$offset+4*$_) }))
}
$baseline = @{}
foreach ($id in $fixtures.Keys | Sort-Object) {
    $fixture = $fixtures[$id]
    $native = Replay $fixture $fixture.pass.originalBinary $zeroPath 'native-zero'
    $patched = Replay $fixture $fixture.pass.binary $zeroPath 'patched-zero'
    Check ((Get-FileHash -LiteralPath $native.path).Hash -ceq (Get-FileHash -LiteralPath $patched.path).Hash) "zero control preserves all native output bits: $id"
    $baseline[$id] = $native
}
$skin = $baseline['000074']
$skinWorld = Attribute $skin 'TEXCOORD' 1
$camera = [IO.File]::ReadAllBytes((Join-Path $fixtures['000074'].path 'cb0.bin'))
$metrics = @()
foreach ($anchorVertex in @(800, 2400, 4000)) {
    $anchorRelative = Vector $skin $skinWorld $anchorVertex
    $control = [single[]]@(0.012,0.008,0.020,1,0,0,0,0.12,-0.007,0.004,0.012,2,1,0,0,0)
    for ($axis=0; $axis -lt 3; $axis++) {
        $control[4+$axis] = $anchorRelative[$axis]+[BitConverter]::ToSingle($camera,44*16+4*$axis)
    }
    $controlBytes = [byte[]]::new(64)
    [Buffer]::BlockCopy($control,0,$controlBytes,0,64)
    $controlPath = Join-Path $output "control-$anchorVertex.bin"
    [IO.File]::WriteAllBytes($controlPath,$controlBytes)
    $active = @{}
    foreach ($id in $fixtures.Keys | Sort-Object) {
        $active[$id] = Replay $fixtures[$id] $fixtures[$id].pass.binary $controlPath "active-$anchorVertex"
        $floatAttributes = @($active[$id].layout.outputs | Where-Object float)
        $finite = $true
        foreach ($attribute in $floatAttributes) {
            for ($vertex=0; $vertex -lt $fixtures[$id].count; $vertex++) {
                $offset = $vertex*$active[$id].layout.stride+$attribute.byteOffset
                for ($component=0; $component -lt $attribute.components; $component++) {
                    if (![single]::IsFinite([BitConverter]::ToSingle($active[$id].bytes,$offset+4*$component))) { $finite=$false; break }
                }
            }
        }
        Check $finite "finite captured output: $id/$anchorVertex"
    }
    $worlds = @{}
    foreach ($id in @('000036','000074','000086')) { $worlds[$id] = Attribute $active[$id] 'TEXCOORD' 1 }
    $maximumError = 0.0
    $oldMismatch = 0.0
    $maximumDisplacement = 0.0
    $movedVertices = 0
    for ($vertex=0; $vertex -lt $fixtures['000074'].count; $vertex++) {
        $point = Vector $active['000074'] $worlds['000074'] $vertex
        $old = Vector $skin $skinWorld $vertex
        $distanceSquared = 0.0
        for ($axis=0; $axis -lt 3; $axis++) { $distanceSquared += [Math]::Pow($point[$axis]-$old[$axis],2) }
        if ($distanceSquared -gt 1e-12) { $movedVertices++ }
        $maximumDisplacement = [Math]::Max($maximumDisplacement,[Math]::Sqrt($distanceSquared))
        foreach ($id in @('000036','000086')) {
            $other = Vector $active[$id] $worlds[$id] $vertex
            for ($axis=0; $axis -lt 3; $axis++) {
                $maximumError = [Math]::Max($maximumError,[Math]::Abs($point[$axis]-$other[$axis]))
                $oldMismatch = [Math]::Max($oldMismatch,[Math]::Abs($old[$axis]-$other[$axis]))
            }
        }
    }
    Check ($movedVertices -gt 0 -and $maximumDisplacement -gt 0.001 -and $maximumDisplacement -lt 0.04) "bounded nonzero captured deformation: $anchorVertex"
    Check ($maximumError -le 0.00001) "color/prepass/outline world positions agree: $anchorVertex"
    Check ($oldMismatch -gt 0.001) "previous missing-color defect reproduced: $anchorVertex"
    $metrics += [ordered]@{
        anchorVertex=$anchorVertex; vertexCount=$fixtures['000074'].count
        movedVertices=$movedVertices; maximumDisplacement=$maximumDisplacement
        maximumPassError=$maximumError; previousCandidateMismatch=$oldMismatch
    }
}
$testPath = Join-Path $output 'test-report.json'
[ordered]@{
    passed=$true; checks=$checks.ToArray(); metrics=$metrics
    buildReportSha256=(Get-FileHash -LiteralPath $reportPath).Hash
    inventorySha256=(Get-FileHash -LiteralPath $CaptureInventoryPath).Hash
    replayHostSha256=(Get-FileHash -LiteralPath $hostPath).Hash
    capturedDraws=@($fixtures.Keys | Sort-Object); syntheticControl=$true
    sourceResourcesUnmodified=$true; gameStarted=$false; liveInstallationModified=$false
    inGameVerified=$false; limitation='Vertex output replay, not full pixel/material/frame reproduction.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $testPath -Encoding utf8
Write-Output "CAPTURE_REPLAY_REPORT=$testPath"
$metrics | ForEach-Object { [pscustomobject]$_ } | Format-Table
