# Create a NEW GitHub repository from this extracted source folder.
# Requires git and GitHub CLI (https://cli.github.com/), authenticated with `gh auth login`.
# Defaults to PRIVATE visibility so the owner can review the repo before publication. The code is licensed under GPL-3.0-only.
param(
    [string]$RepositoryName = 'White2UpgradeStudio',
    [ValidateSet('private', 'public')][string]$Visibility = 'private'
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..'))

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required. Install Git for Windows, then retry.'
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI is required. Install it from https://cli.github.com/.'
}

gh auth status
if ($LASTEXITCODE -ne 0) { throw 'Sign in first with: gh auth login' }

if (-not (Test-Path .git)) {
    git init -b main
    if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
    git add .
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
    git commit -m 'Initial White 2 Upgrade Studio source'
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed. Check git user.name and user.email.' }
}

$visibilityArg = if ($Visibility -eq 'public') { '--public' } else { '--private' }
gh repo create $RepositoryName --source . $visibilityArg --remote origin --push
if ($LASTEXITCODE -ne 0) { throw 'GitHub repository creation or push failed. Check the repository name and authentication.' }

Write-Host 'Repository created and pushed. Open it in the GitHub browser or with: gh repo view --web'
