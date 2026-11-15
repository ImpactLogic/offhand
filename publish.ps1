# Portable ZIP by default; -Installer also builds conventional Setup.exe.
[CmdletBinding()]
param(
    [switch]$Installer,
    [string]$IsccPath,
    [string]$SigningScript,
    [switch]$RequireSigned
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if ($RequireSigned -and -not $SigningScript) { throw 'Release signing requires -SigningScript.' }
if ($SigningScript) {
    $SigningScript = (Resolve-Path -LiteralPath $SigningScript).Path
    if ($SigningScript.Contains('"') -or $SigningScript.Contains('$')) { throw 'Unsupported signing-script path.' }
}
if ($Installer) {
    if (-not $IsccPath) {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        )
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw 'Install Inno Setup 6/7 or pass -IsccPath.' }
}
function Assert-Signed([string]$Path) {
    if ((Get-AuthenticodeSignature -LiteralPath $Path).Status -ne 'Valid') { throw "Trusted signature verification failed: $Path" }
}
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
if ($SigningScript) {
    & $SigningScript (Join-Path $stage 'Offhand.exe')
    Assert-Signed (Join-Path $stage 'Offhand.exe')
} else { Write-Warning 'Unsigned review build: Windows will still show Unknown publisher.' }
$readmeSource = if (Test-Path -LiteralPath 'PUBLIC-README.md') { 'PUBLIC-README.md' } else { 'README.md' }
$readmeText = Get-Content -LiteralPath $readmeSource -Raw
# Packaged README links point at repository media, without bundling a demo in the installer.
$readmeText = $readmeText.Replace('](media/', '](https://github.com/ImpactLogic/offhand/blob/main/media/')
[IO.File]::WriteAllText((Join-Path $stage 'README.md'), $readmeText, (New-Object Text.UTF8Encoding($false)))
Copy-Item INSTALL.txt $stage
Copy-Item SIGN-IN.md $stage
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

if ($Installer) {
    $compilerArgs = @("/DAppVersion=$version", "/DStageDir=$stage")
    if ($SigningScript) {
        # Inno expands $q to a quote and $f to the quoted file being signed.
        $signCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File $q' + $SigningScript + '$q $f'
        $compilerArgs += '/DSignedBuild'
        $compilerArgs += ('/Soffhand=' + $signCommand)
    }
    $compilerArgs += (Join-Path $PSScriptRoot 'Offhand.iss')
    & $IsccPath @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $setup = Join-Path $PSScriptRoot "dist\Offhand-$version-Setup.exe"
    if ($SigningScript) { Assert-Signed $setup }
    Get-Item -LiteralPath $setup | Select-Object FullName, Length
}
