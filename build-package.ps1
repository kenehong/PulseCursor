#requires -Version 7.0
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version = '1.0.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$versionNumber = [version]::Parse($Version)
if ($versionNumber.Major -eq 0 -or
    @($versionNumber.Major, $versionNumber.Minor, $versionNumber.Build, $versionNumber.Revision |
        Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw 'MSIX version components must be 0..65535 and the major version must be greater than zero.'
}
$sdkRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$makeAppx = Get-ChildItem (Join-Path $sdkRoot 'bin\*\x64\makeappx.exe') |
    Sort-Object { [version] $_.Directory.Parent.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $makeAppx) { throw 'Windows SDK MakeAppx.exe is required.' }

$buildRoot = Join-Path $PSScriptRoot "artifacts\package-$([guid]::NewGuid().ToString('N'))"
$payload = Join-Path $buildRoot 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
$appVersion = "$($versionNumber.Major).$($versionNumber.Minor).$($versionNumber.Build)"
& dotnet publish (Join-Path $PSScriptRoot 'LoadCursor.csproj') -c Release -r win-x64 --self-contained true `
    "-p:Version=$appVersion" "-p:FileVersion=$Version" "-p:AssemblyVersion=$Version" `
    "-p:OutputPath=$buildRoot\build\" -p:DebugType=none -p:DebugSymbols=false -o $payload --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

foreach ($file in @('PulseCursor.exe', 'PulseCursor.dll', 'coreclr.dll', 'hostfxr.dll',
        'System.Windows.Forms.dll', 'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll',
        'PulseCursor.pri', 'App.xbf', 'PeekCard.xbf', 'SettingsWindow.xbf',
        'ColorSelector.xbf', 'PulseMeter.xbf')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file))) {
        throw "The self-contained payload is missing $file."
    }
}
$runtime = Get-Content (Join-Path $payload 'PulseCursor.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.PSObject.Properties.Name -contains 'frameworks' -or
    $runtime.runtimeOptions.PSObject.Properties.Name -contains 'framework') {
    throw 'The published app still requires a shared .NET runtime.'
}

$restore = Get-Content (Join-Path $PSScriptRoot 'obj\project.assets.json') -Raw | ConvertFrom-Json
$packagePaths = @($restore.libraries.PSObject.Properties |
    Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Value.path })
$packagePaths += @($runtime.runtimeOptions.includedFrameworks |
    ForEach-Object { "$($_.name.ToLowerInvariant()).runtime.win-x64\$($_.version)" })
foreach ($packagePath in $packagePaths | Select-Object -Unique) {
    $folder = $restore.packageFolders.PSObject.Properties.Name |
        ForEach-Object { Join-Path $_ $packagePath.Replace('/', '\') } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $folder) { throw "Cannot find restored package notices: $packagePath" }
    $notices = Join-Path $payload "ThirdPartyNotices\$($packagePath.Replace('/', '\'))"
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    Get-ChildItem -LiteralPath $folder -File |
        Where-Object { $_.Name -match '^(license|notice|third[-_.]?party[-_.]?notices)(\..*)?$' -or $_.Extension -eq '.nuspec' } |
        Copy-Item -Destination $notices
}

$check = Start-Process (Join-Path $payload 'PulseCursor.exe') -ArgumentList '--self-test' -PassThru `
    -RedirectStandardOutput (Join-Path $buildRoot 'self-test.out.log') `
    -RedirectStandardError (Join-Path $buildRoot 'self-test.err.log')
if (-not $check.WaitForExit(90000)) { throw "Self-test timed out; inspect PID $($check.Id)." }
if ($check.ExitCode -ne 0) {
    Get-Content (Join-Path $buildRoot 'self-test.err.log') | Write-Host
    throw "Self-test failed with exit code $($check.ExitCode)."
}

$assets = Join-Path $payload 'Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$points = [System.Drawing.PointF[]] @(
    [System.Drawing.PointF]::new(25, 16), [System.Drawing.PointF]::new(25, 77),
    [System.Drawing.PointF]::new(42, 63), [System.Drawing.PointF]::new(55, 88),
    [System.Drawing.PointF]::new(68, 81), [System.Drawing.PointF]::new(55, 57),
    [System.Drawing.PointF]::new(78, 57)
)
foreach ($asset in @(
        @{ Name = 'StoreLogo'; Size = 50 },
        @{ Name = 'Square44x44Logo'; Size = 44 },
        @{ Name = 'Square150x150Logo'; Size = 150 })) {
    $bitmap = [System.Drawing.Bitmap]::new($asset.Size, $asset.Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F5B82E'))
    $outline = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#07090D'), 4)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($asset.Size / 100.0, $asset.Size / 100.0)
        $graphics.FillPolygon($fill, $points)
        $graphics.DrawPolygon($outline, $points)
        $bitmap.Save((Join-Path $assets "$($asset.Name).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $outline.Dispose()
        $fill.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

[xml] $manifest = Get-Content (Join-Path $PSScriptRoot 'packaging\AppxManifest.xml') -Raw
$manifest.Package.Identity.Version = $Version
$manifest.Save((Join-Path $payload 'AppxManifest.xml'))
$package = Join-Path $buildRoot "PulseCursor_${Version}_x64.msix"
& $makeAppx pack /d $payload /p $package /no *> (Join-Path $buildRoot 'makeappx.log')
if ($LASTEXITCODE -ne 0) {
    Get-Content (Join-Path $buildRoot 'makeappx.log') -Tail 30 | Write-Host
    throw "MSIX validation/pack failed with exit code $LASTEXITCODE."
}
Get-FileHash -LiteralPath $package -Algorithm SHA256 |
    Select-Object Algorithm, Hash, Path | ConvertTo-Json
Write-Host "Unsigned MSIX: $package"
Write-Host 'No certificate, system setting, installation, upload or publication was changed.'
