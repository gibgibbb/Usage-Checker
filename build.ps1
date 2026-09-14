$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$wpf = Join-Path $framework 'WPF'
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Web.Extensions.dll')
& $compiler /nologo /optimize+ /target:exe "/out:$output\CodexMonitor.HookRelay.exe" @refs "$PSScriptRoot\src\Core.cs" "$PSScriptRoot\src\HookRelay.cs"
if ($LASTEXITCODE -ne 0) { throw 'Hook relay build failed' }
if (Test-Path "$PSScriptRoot\src\App.cs") {
    & $compiler /nologo /optimize+ /target:winexe "/out:$output\CodexMonitor.exe" @refs /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$wpf\PresentationFramework.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\WindowsBase.dll" /r:System.Xaml.dll "$PSScriptRoot\src\Core.cs" "$PSScriptRoot\src\App.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Widget build failed' }
}
if (Test-Path "$PSScriptRoot\tests\CoreTests.cs") {
    & $compiler /nologo /optimize+ /target:exe "/out:$output\CoreTests.exe" @refs "$PSScriptRoot\src\Core.cs" "$PSScriptRoot\tests\CoreTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Tests build failed' }
    & "$output\CoreTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
