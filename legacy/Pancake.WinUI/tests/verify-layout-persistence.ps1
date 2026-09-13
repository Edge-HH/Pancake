param(
    [ValidateSet('Dark', 'Light')] [string]$Theme = 'Dark',
    [ValidateSet('display', 'settings')] [string]$View = 'display'
)
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDirectory = Join-Path $projectRoot 'src\Pancake\bin\Release\net8.0-windows10.0.19041.0\win-x64'
$executablePath = Join-Path $outputDirectory 'Pancake.exe'

if (-not (Test-Path -LiteralPath $executablePath)) {
    throw 'Build Release x64 before running the layout persistence test.'
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('Pancake-layout-persistence-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    Copy-Item -Path (Join-Path $outputDirectory '*') -Destination $testRoot -Recurse -Force
    $dataDirectory = Join-Path $testRoot 'data'
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    $statePath = Join-Path $dataDirectory 'pancake.json'
    $fixture = @'
{
  "SchemaVersion": 1,
  "Settings": {
    "Theme": "Dark",
    "MicrophoneSampleRate": 16000,
    "MicrophoneCalibrationDb": 0,
    "WeatherCityName": "Beijing",
    "WeatherCityCode": "101010100",
    "GridSnappingEnabled": true,
    "AutoUpdateEnabled": false,
    "UpdateRepository": "Edge-HH/Pancake"
  },
  "Subjects": [
    { "Name": "Math", "AccentHex": "#65D46E", "X": 48, "Y": 48, "Width": 384, "Height": 240, "Entries": [], "InkStrokes": [] },
    { "Name": "English", "AccentHex": "#7567FF", "X": 528, "Y": 48, "Width": 384, "Height": 240, "Entries": [], "InkStrokes": [] },
    { "Name": "Physics", "AccentHex": "#60A5FA", "X": 528, "Y": 336, "Width": 480, "Height": 288, "Entries": [], "InkStrokes": [] }
  ]
}
'@
    $fixture = $fixture.Replace('"Theme": "Dark"', ('"Theme": "' + $Theme + '"'))
    [System.IO.File]::WriteAllText($statePath, $fixture)
    $before = [System.IO.File]::ReadAllText($statePath) | ConvertFrom-Json

    $process = Start-Process `
        -FilePath (Join-Path $testRoot 'Pancake.exe') `
        -ArgumentList '--windowed', "--view=$View" `
        -WorkingDirectory $testRoot `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Seconds 4
    if ($process.HasExited) { throw 'The test process exited before normal shutdown.' }
    $null = $process.CloseMainWindow()
    if (-not $process.WaitForExit(5000)) {
        throw 'The test process did not close normally within five seconds.'
    }

    $library = [System.IO.File]::ReadAllText((Join-Path $dataDirectory 'projects.json')) | ConvertFrom-Json
    $after = [PSCustomObject]@{ Settings = $library.Settings; Subjects = $library.Projects[0].Subjects }
    if ($after.Settings.Theme -ne $Theme) { throw 'The selected theme was not preserved.' }
    $beforeLayout = $before.Subjects | ForEach-Object { "$($_.Name):$($_.X),$($_.Y),$($_.Width),$($_.Height)" }
    $afterLayout = $after.Subjects | ForEach-Object { "$($_.Name):$($_.X),$($_.Y),$($_.Width),$($_.Height)" }

    if (Compare-Object -ReferenceObject $beforeLayout -DifferenceObject $afterLayout) {
        Write-Error "Layout changed after startup and normal shutdown. Before: $($beforeLayout -join '; '); After: $($afterLayout -join '; ')"
        exit 1
    }

    Write-Output "PASS: $Theme / $View startup and normal shutdown preserve theme and tile layout."
}
finally {
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
    }

    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTestRoot).StartsWith('Pancake-layout-persistence-')) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
