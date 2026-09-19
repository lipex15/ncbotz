param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$brand = Join-Path $root 'branding'
$mark = Join-Path $brand 'pexbot-mark.png'
$original = Join-Path $brand 'pexbot-original.png'
$icon = Join-Path $brand 'PEXBOT.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$temporary = Join-Path $brand '.icon-build'
New-Item -ItemType Directory -Force -Path $temporary | Out-Null

try {
    $frames = @()
    foreach ($size in $sizes) {
        $file = Join-Path $temporary "$size.png"
        & ffmpeg -hide_banner -loglevel error -y -i $mark -vf "scale=${size}:${size}:flags=lanczos" -frames:v 1 $file
        if ($LASTEXITCODE -ne 0) { throw "Could not resize icon to $size pixels." }
        $frames += [PSCustomObject]@{ Size = $size; Bytes = [IO.File]::ReadAllBytes($file) }
    }

    $stream = [IO.File]::Create($icon)
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write($frame.Bytes) }
        $writer.Flush()
    }
    finally { $stream.Dispose() }

    & ffmpeg -hide_banner -loglevel error -y -i $original -vf 'scale=352:352:flags=lanczos,pad=352:600:0:124:color=0x071126' -frames:v 1 (Join-Path $brand 'wizard-main.png')
    if ($LASTEXITCODE -ne 0) { throw 'Could not create installer artwork.' }
    & ffmpeg -hide_banner -loglevel error -y -i $mark -vf 'scale=256:256:flags=lanczos' -frames:v 1 (Join-Path $brand 'wizard-small.png')
    if ($LASTEXITCODE -ne 0) { throw 'Could not create small installer artwork.' }
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        $resolvedBrand = [IO.Path]::GetFullPath($brand).TrimEnd([IO.Path]::DirectorySeparatorChar)
        $resolvedTemporary = [IO.Path]::GetFullPath($temporary)
        if (-not $resolvedTemporary.StartsWith($resolvedBrand + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The temporary icon directory is outside the branding folder.'
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
