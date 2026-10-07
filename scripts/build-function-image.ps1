#!/usr/bin/env pwsh
# Builds the Function App container image used by the integration tests.
param(
    [string]$Image = 'sample-functions:test'
)
$ErrorActionPreference = 'Stop'
$repository, $tag = $Image.Split(':', 2)
if (-not $tag) { $tag = 'latest' }
$root = Split-Path -Parent $PSScriptRoot

dotnet publish (Join-Path $root 'src/Sample.Functions') `
    -c Release -r linux-x64 -t:PublishContainer --nologo `
    "-p:ContainerRepository=$repository" "-p:ContainerImageTag=$tag"
if ($LASTEXITCODE -ne 0) { throw "Image build failed ($LASTEXITCODE)" }
Write-Host "Built $Image"
