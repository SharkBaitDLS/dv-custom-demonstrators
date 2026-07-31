param (
	[switch]$NoArchive,
	[string]$OutputDirectory = $PSScriptRoot
)

Set-Location "$PSScriptRoot"
# DemonstratorPosters ships as a folder players drop their own pictures into. It carries a README so the
# folder survives being zipped and unzipped, and so it explains itself where they will actually read it.
$FilesToInclude = "info.json","build/*","LICENSE","DemonstratorPosters"

$modInfo = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$modId = $modInfo.Id

$DistDir = "$OutputDirectory/dist"
if ($NoArchive) {
	$ZipWorkDir = "$OutputDirectory"
} else {
	$ZipWorkDir = "$DistDir/tmp"
}
$ZipOutDir = "$ZipWorkDir/$modId"

# Start from a clean staging dir so files removed since the last build don't linger.
if (Test-Path "$ZipOutDir") { Remove-Item -Recurse -Force "$ZipOutDir" }
New-Item "$ZipOutDir" -ItemType Directory -Force
Copy-Item -Force -Recurse -Path $FilesToInclude -Destination "$ZipOutDir"

if (!$NoArchive)
{
	$FILE_NAME = "$DistDir/${modId}.zip"
	# Remove any existing archive so the zip only contains the current file set.
	if (Test-Path "$FILE_NAME") { Remove-Item -Force "$FILE_NAME" }

	# Compress-Archive violates the zip spec and writes a backslash separator for paths
	try { Add-Type -AssemblyName System.IO.Compression } catch { }
	try { Add-Type -AssemblyName System.IO.Compression.FileSystem } catch { }

	$SourceRoot = (Resolve-Path "$ZipOutDir").Path.TrimEnd('\', '/')
	$ArchivePath = Join-Path (Resolve-Path "$DistDir").Path "${modId}.zip"

	$Archive = [System.IO.Compression.ZipFile]::Open($ArchivePath, [System.IO.Compression.ZipArchiveMode]::Create)
	try {
		foreach ($File in Get-ChildItem -Path "$SourceRoot" -Recurse -File) {
			$EntryName = $File.FullName.Substring($SourceRoot.Length + 1).Replace('\', '/')
			[void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
				$Archive, $File.FullName, $EntryName, [System.IO.Compression.CompressionLevel]::Fastest)
		}
	} finally {
		$Archive.Dispose()
	}
}
