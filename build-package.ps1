param(
    # Toolbox.exe ships with an installed Playnite. It is the only supported way
    # to produce a .pext, so the path is required rather than guessed at: a
    # wrong guess would silently produce a package Playnite refuses to install.
    [Parameter(Mandatory = $true)]
    [string]$ToolboxPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\MGA.Playnite\MGA.Playnite.csproj"
$buildOutput = Join-Path $repositoryRoot "src\MGA.Playnite\bin\Release\net462"
$artifactsDirectory = Join-Path $repositoryRoot "artifacts"

# The version comes from the manifest the package will carry, so the file name
# and the manifest can never disagree about which release this is.
$manifestPath = Join-Path $repositoryRoot "src\MGA.Playnite\extension.yaml"
$versionLine = Select-String -LiteralPath $manifestPath -Pattern '^\s*Version:\s*(.+?)\s*$'
if (-not $versionLine)
{
    throw "extension.yaml does not declare a Version."
}
$version = $versionLine.Matches[0].Groups[1].Value
$versionTag = $version.Replace('.', '_')

$stagingDirectory = Join-Path $artifactsDirectory "MyGamesAnywhere_$versionTag"
$artifactsFullPath = [System.IO.Path]::GetFullPath($artifactsDirectory).TrimEnd('\') + '\'
$stagingFullPath = [System.IO.Path]::GetFullPath($stagingDirectory)

if (-not $stagingFullPath.StartsWith($artifactsFullPath, [System.StringComparison]::OrdinalIgnoreCase))
{
    throw "Package staging must remain below the repository artifacts directory."
}

if (-not (Test-Path -LiteralPath $ToolboxPath -PathType Leaf))
{
    throw "Playnite Toolbox was not found at '$ToolboxPath'. It is installed alongside Playnite."
}

dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0)
{
    throw "Release build failed."
}

if (Test-Path -LiteralPath $stagingDirectory)
{
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagingDirectory "Resources") | Out-Null

# Only the plugin's own assembly is packaged. The Playnite SDK is referenced
# with ExcludeAssets=runtime precisely so a second copy is never shipped:
# Playnite cannot load a different version of an assembly it already holds.
Copy-Item -LiteralPath (Join-Path $buildOutput "MGA.Playnite.dll") -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $buildOutput "extension.yaml") -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $buildOutput "LICENSE") -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $buildOutput "Resources\mga.png") -Destination (Join-Path $stagingDirectory "Resources")

& $ToolboxPath pack $stagingDirectory $artifactsDirectory
if ($LASTEXITCODE -ne 0)
{
    throw "Playnite Toolbox packaging failed."
}

$toolboxPackage = Get-ChildItem -LiteralPath $artifactsDirectory -Filter "*.pext" |
    Where-Object { $_.Name -like "*$versionTag*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $toolboxPackage)
{
    throw "Playnite Toolbox did not create a package for version $version."
}

$releasePackage = Join-Path $artifactsDirectory "MyGamesAnywhere_$versionTag.pext"
if ($toolboxPackage.FullName -ne $releasePackage)
{
    Move-Item -LiteralPath $toolboxPackage.FullName -Destination $releasePackage -Force
}
Write-Host "Release package created at $releasePackage"
