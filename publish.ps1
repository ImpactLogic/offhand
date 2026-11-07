# Builds a self-contained Offhand (no .NET install needed) and zips it for sharing.
# Output: tools\pad-launcher\dist\Offhand-<version>-win-x86.zip  (Offhand.exe, README.md, INSTALL.txt)
# The exe asks for admin (FFXI keeps the pad setting under HKLM). ViGEmBus is a separate one-time install,
# only needed for the virtual-pad option; see INSTALL.txt.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$version = ([xml](Get-Content PadLauncher.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "dist\Offhand-$version"))
$expectedStageParent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "dist"))
if ((Split-Path $stage) -ne $expectedStageParent -or (Split-Path $stage -Leaf) -ne "Offhand-$version") { throw "Unexpected publish destination." }
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish PadLauncher.csproj -c Release -r win-x86 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $stage
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Get-ChildItem $stage -Filter *.pdb | Remove-Item -Force
Copy-Item README.md (Join-Path $stage "README.md")
Copy-Item INSTALL.txt $stage
Copy-Item TESTER-GUIDE.md $stage
Copy-Item THIRD-PARTY-NOTICES.md $stage
Copy-Item LICENSE $stage
Copy-Item RELEASE-NOTES.md $stage
# Fixed entry times: the zip must not carry the build machine's clock or time zone.
Get-ChildItem $stage -Recurse -File | ForEach-Object { $_.LastWriteTime = [datetime]'2026-11-01 00:00:00' }
$zip = "$stage-win-x86.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
"{0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)
