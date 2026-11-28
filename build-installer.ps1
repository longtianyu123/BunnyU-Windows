param([string]$Compiler,[string]$SigningKeyPath,[switch]$SkipPublish)
$ErrorActionPreference='Stop'
$version=([xml](Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'BunnyU.WinForms\BunnyU.WinForms.csproj'))).Project.PropertyGroup.Version
if(-not $SkipPublish){& (Join-Path $PSScriptRoot 'build-winforms.ps1') -Publish}
if(-not $Compiler){$Compiler=Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\inno\ISCC.exe'}
if(-not(Test-Path -LiteralPath $Compiler)){$Compiler=(Get-Command ISCC.exe -ErrorAction Stop).Source}
$publish=Join-Path $PSScriptRoot "artifacts\winforms-x64-v$version"
& $Compiler "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'installer\BunnyU.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$installer=Join-Path $PSScriptRoot "artifacts\BunnyU-Setup-$version.exe"
$manifest=[ordered]@{version=$version;installerUrl="https://github.com/longtianyu123/BunnyU-Windows/releases/download/v$version/BunnyU-Setup-$version.exe";sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant();notes='无障碍列表、完整章节加载、后台并行下载、静默自动进度和自动检查更新。'}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts\update.json'),($manifest|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
if(-not $SigningKeyPath){$SigningKeyPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\bunny-update-signing-key.pem'}
if(Test-Path -LiteralPath $SigningKeyPath){
 $dotnet=Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\dotnet10\dotnet.exe'
 if(-not(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet).Source}
 & $dotnet run --project (Join-Path $PSScriptRoot 'BuildTools.UpdateSigner\BuildTools.UpdateSigner.csproj') -c Release -- sign $SigningKeyPath (Join-Path $PSScriptRoot 'artifacts\update.json')
 if($LASTEXITCODE -ne 0){throw 'Update manifest signing failed'}
}else{Write-Warning 'No release signing key supplied. Installer built; unsigned update.json must not be published as an official update.'}
Write-Output $installer
