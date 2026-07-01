<#
.SYNOPSIS
    Downloads FFmpeg shared libraries for Zenith Screen Recorder.
.DESCRIPTION
    Downloads pre-built FFmpeg shared libraries from BtbN/FFmpeg-Builds GitHub releases
    and extracts them to the lib/ffmpeg/ directory.
.PARAMETER Platform
    Target platform: win-x64 (default), linux-x64, osx-arm64
.EXAMPLE
    .\download-ffmpeg.ps1
    .\download-ffmpeg.ps1 -Platform linux-x64
#>

param(
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$Platform = "win-x64"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$LibDir = Join-Path $ProjectRoot "lib" "ffmpeg"

# FFmpeg version and download URLs (BtbN builds)
$FFmpegVersion = "n7.1"
$BaseUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest"

$DownloadUrls = @{
    "win-x64"   = "$BaseUrl/ffmpeg-$FFmpegVersion-win64-shared.zip"
    "linux-x64" = "$BaseUrl/ffmpeg-$FFmpegVersion-linux64-shared.tar.xz"
    "osx-arm64" = "" # macOS builds require separate handling (Homebrew or custom)
}

$OutputDirs = @{
    "win-x64"   = Join-Path $LibDir "win-x64"
    "linux-x64" = Join-Path $LibDir "linux-x64"
    "osx-arm64" = Join-Path $LibDir "osx-arm64"
}

$RequiredDlls = @(
    "avcodec-63", "avdevice-63", "avfilter-12",
    "avformat-63", "avutil-61", "swresample-7", "swscale-10"
)

function Test-FFmpegPresent {
    param([string]$Dir)
    if (-not (Test-Path $Dir)) { return $false }
    foreach ($dll in $RequiredDlls) {
        $ext = if ($Platform -eq "win-x64") { ".dll" } else { ".so" }
        if (-not (Test-Path (Join-Path $Dir "$dll$ext"))) { return $false }
    }
    return $true
}

$OutputDir = $OutputDirs[$Platform]

# Check if already present
if (Test-FFmpegPresent $OutputDir) {
    Write-Host "[OK] FFmpeg libraries already present in $OutputDir" -ForegroundColor Green
    exit 0
}

$Url = $DownloadUrls[$Platform]
if ([string]::IsNullOrEmpty($Url)) {
    Write-Host "[WARN] No automated download available for $Platform." -ForegroundColor Yellow
    Write-Host "       For macOS, install FFmpeg via Homebrew: brew install ffmpeg" -ForegroundColor Yellow
    Write-Host "       Then copy shared libraries to: $OutputDir" -ForegroundColor Yellow
    exit 1
}

Write-Host "[INFO] Downloading FFmpeg $FFmpegVersion for $Platform..." -ForegroundColor Cyan
Write-Host "       URL: $Url"

# Create temp directory
$TempDir = Join-Path $ProjectRoot "lib" ".ffmpeg-temp"
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

$ArchiveFile = Join-Path $TempDir "ffmpeg-archive"
if ($Platform -eq "win-x64") { $ArchiveFile += ".zip" } else { $ArchiveFile += ".tar.xz" }

try {
    # Download
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'  # Speed up download
    Invoke-WebRequest -Uri $Url -OutFile $ArchiveFile -UseBasicParsing
    Write-Host "[OK] Download complete." -ForegroundColor Green

    # Extract
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    if ($Platform -eq "win-x64") {
        Write-Host "[INFO] Extracting archive..."
        Expand-Archive -Path $ArchiveFile -DestinationPath $TempDir -Force

        # Find the bin directory inside the extracted folder
        $ExtractedDir = Get-ChildItem -Path $TempDir -Directory | Where-Object { $_.Name -like "ffmpeg-*" } | Select-Object -First 1
        $BinDir = Join-Path $ExtractedDir.FullName "bin"

        if (Test-Path $BinDir) {
            Get-ChildItem -Path $BinDir -Filter "*.dll" | Copy-Item -Destination $OutputDir -Force
            Write-Host "[OK] Extracted DLLs to $OutputDir" -ForegroundColor Green
        } else {
            Write-Host "[ERROR] Could not find bin/ directory in extracted archive." -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[INFO] Extracting tar.xz archive..."
        tar -xf $ArchiveFile -C $TempDir
        $ExtractedDir = Get-ChildItem -Path $TempDir -Directory | Where-Object { $_.Name -like "ffmpeg-*" } | Select-Object -First 1
        $LibSoDir = Join-Path $ExtractedDir.FullName "lib"
        if (Test-Path $LibSoDir) {
            Get-ChildItem -Path $LibSoDir -Filter "*.so*" | Copy-Item -Destination $OutputDir -Force
            Write-Host "[OK] Extracted shared libraries to $OutputDir" -ForegroundColor Green
        }
    }
} catch {
    Write-Host "[ERROR] Failed to download or extract FFmpeg: $_" -ForegroundColor Red
    exit 1
} finally {
    # Cleanup temp
    if (Test-Path $TempDir) {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    }
}

# Verify
if (Test-FFmpegPresent $OutputDir) {
    Write-Host "[OK] FFmpeg $FFmpegVersion installed successfully for $Platform!" -ForegroundColor Green
} else {
    Write-Host "[WARN] Some FFmpeg libraries may be missing. Check $OutputDir" -ForegroundColor Yellow
}
