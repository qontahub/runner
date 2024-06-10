#!/usr/bin/env pwsh

param(
    [Parameter()]
    [ValidateSet('build','dist','package')]
    $command='build',
    [Parameter()] 
    [ValidateSet('debug','release')]
    $configuration='debug',
    $runtime,
    $version 
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

$currentPlatform = 'windows'
$runtimeId = 'win-x64'

if($currentPlatform -eq 'windows') {
    if($env:PROCESSOR_ARCHITECTURE -eq 'x86') {
        $runtimeId = 'win-x86'
    }
    if($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        $runtimeId = 'win-arm64'
    }
}

if($runtime) {
    $runtimeId = $runtime
}

$projectFiles = (
    'src/QontaHub.Runner/QontaHub.Runner.csproj'
)

$windowsOnlyProjectFiles = (
    'src/QontaHub.RunnerService/QontaHub.RunnerService.csproj'
)

# Write-Host "$([IntPtr]::Size)"
# Get-ChildItem env: | Sort-Object Name | Select-Object Name,Value 
function Write-Heading($message) {
    Write-Host 
    Write-Host
    Write-Host "---------------------------------------------"
    Write-Host "  $message"
    Write-Host "---------------------------------------------" 
}

function Build() {
    $projectFiles | ForEach-Object {
        Write-Host "Building $_ $configuration $runtimeId"
        dotnet restore $_
        dotnet publish $_ --self-contained true /p:AssemblyVersion=$version `
                --configuration $configuration `
                --runtime $runtimeId `
                --output dist/bin
    }

    if($currentPlatform -eq 'windows')
    {
        $windowsOnlyProjectFiles | ForEach-Object {
            dotnet publish $_ --self-contained true `
                /p:PublishTrimmed=true `
                /p:AssemblyVersion=$version `
                --configuration $configuration --runtime $runtimeId `
                --output dist/bin
        }
    }
}

function Dist() {
    Write-Host "Dist..."
    Build
    Copy-Item -Recurse assets/* -Destination dist -Force
}

function Package() {
    Write-Host "Packaging..."
    New-Item -Type Directory -Path packages -Force
    Dist
    if($currentPlatform -eq 'windows') {
        Compress-Archive dist/* -Force `
            -DestinationPath "packages/qontahub-runner-$runtimeId-$version.zip"
    }
}

switch($command) {
    'build' { 
        Write-Heading "Building..."
        Build 
        Break
    }
    'dist' {
        Write-Heading "Preparing Artifacts..."
        Dist
        Break
    }
    'package' {
        Write-Heading "Packaging..." 
        Package
        Break
    }
}
