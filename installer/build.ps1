param([switch]$Test)
$ErrorActionPreference = 'Stop'
$pbzCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pbzRoot = Split-Path $PSScriptRoot -Parent
$pbzOutput = Join-Path $pbzRoot 'dist'
New-Item -ItemType Directory -Force -Path $pbzOutput | Out-Null
$pbzArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    '/reference:System.Web.Extensions.dll', '/reference:System.Net.Http.dll',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    ('/resource:' + (Join-Path $pbzRoot 'PBZItemGiver-0.5.1.zip') + ',PBZ.Mod.zip'),
    ('/out:' + (Join-Path $pbzOutput 'PBZ-Item-Giver-Setup-0.5.1.exe')))
$pbzSources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
& $pbzCompiler @pbzArgs @pbzSources
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
if ($Test) {
    $pbzResults = Join-Path $PSScriptRoot 'test-output'
    $pbzProcess = Start-Process -FilePath (Join-Path $pbzOutput 'PBZ-Item-Giver-Setup-0.5.1.exe') -ArgumentList @('--self-test', ('"' + $pbzResults + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $pbzResults 'results.txt') -Encoding UTF8
    if ($pbzProcess.ExitCode -ne 0) { throw '自检失败' }
}
Get-Item -LiteralPath (Join-Path $pbzOutput 'PBZ-Item-Giver-Setup-0.5.1.exe') | Select-Object Name,Length
