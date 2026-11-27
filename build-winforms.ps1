param([switch]$Publish,[switch]$LiveTests)
$ErrorActionPreference='Stop'
$dotnet=Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\dotnet10\dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet).Source}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$version=([xml](Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'BunnyU.WinForms\BunnyU.WinForms.csproj'))).Project.PropertyGroup.Version
$publishDir="artifacts\winforms-x64-v$version"
Push-Location $PSScriptRoot
try {
 & $dotnet build 'BunnyU.WinForms\BunnyU.WinForms.csproj' -c Release
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
 & $dotnet build 'BunnyU.Tests\BunnyU.Tests.csproj' -c Release
 if($LASTEXITCODE -ne 0){throw 'Test build failed'}
 $arguments=@('BunnyU.Tests\bin\Release\net10.0\BunnyU.Tests.dll')
 if($LiveTests){$arguments+='--live'}
 & $dotnet @arguments
 if($LASTEXITCODE -ne 0){throw 'Tests failed'}
 if($Publish){
  & $dotnet publish 'BunnyU.WinForms\BunnyU.WinForms.csproj' -c Release -r win-x64 --self-contained true -o $publishDir
  if($LASTEXITCODE -ne 0){throw 'Publish failed'}
  Copy-Item -LiteralPath 'README-WinForms.md','ACCESSIBILITY-WinForms.md','THIRD-PARTY-NOTICES.md' -Destination $publishDir
  Copy-Item -LiteralPath 'licenses' -Destination $publishDir -Recurse -Force
 }
}finally{Pop-Location}
