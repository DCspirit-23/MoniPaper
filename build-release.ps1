$ErrorActionPreference = 'Stop'

$projectDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$projectFile = Join-Path $projectDir 'PaperCare.csproj'
$distDir = Join-Path $projectDir 'dist'
$artifactsDir = Join-Path $projectDir 'artifacts'
$candidateDir = Join-Path $artifactsDir 'monipaper-candidate'
$selfTestResult = Join-Path $artifactsDir 'self-test-results.json'

function Test-CanReplaceFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $true }
    try {
        $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $stream.Dispose()
        return $true
    }
    catch [System.IO.IOException] {
        return $false
    }
    catch [System.UnauthorizedAccessException] {
        return $false
    }
}

try {
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw "项目文件不存在：$projectFile"
    }

    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    Set-Location -LiteralPath $projectDir

    $publishDir = $distDir
    $publishedExe = Join-Path $distDir 'MoniPaper.exe'
    if (-not (Test-CanReplaceFile $publishedExe)) {
        $publishDir = $candidateDir
        $publishedExe = Join-Path $candidateDir 'MoniPaper.exe'
        Write-Warning "dist 中的 MoniPaper.exe 正在使用，发布到 $publishDir。"
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    $publishArguments = @(
        'publish', $projectFile,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-o', $publishDir
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败，退出码：$LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        throw "发布完成但找不到可执行文件：$publishedExe"
    }

    $selfTestArgument = '--self-test-output="' + $selfTestResult + '"'
    $selfTestProcess = Start-Process -FilePath $publishedExe `
        -ArgumentList @('--self-test', $selfTestArgument) `
        -WorkingDirectory $projectDir `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($selfTestProcess.ExitCode -ne 0) {
        throw "MoniPaper 自检失败，退出码：$($selfTestProcess.ExitCode)"
    }
    if (-not (Test-Path -LiteralPath $selfTestResult -PathType Leaf)) {
        throw "自检未生成结果文件：$selfTestResult"
    }

    $selfTest = Get-Content -LiteralPath $selfTestResult -Raw | ConvertFrom-Json
    if ($selfTest.passed -ne $true) {
        throw "自检结果未通过：$selfTestResult"
    }
    if ($selfTest.product -ne 'MoniPaper') {
        throw "自检 product 标记不是 MoniPaper：$selfTestResult"
    }

    $versionInfo = (Get-Item -LiteralPath $publishedExe).VersionInfo
    if ($versionInfo.ProductName -ne 'MoniPaper' -or $versionInfo.FileVersion -notlike '1.2.1*') {
        throw "可执行文件标记不符合 MoniPaper 1.2.1：$publishedExe"
    }

    $resolvedExe = (Resolve-Path -LiteralPath $publishedExe).Path
    $hash = (Get-FileHash -LiteralPath $resolvedExe -Algorithm SHA256).Hash
    Write-Host "发布完成：$resolvedExe"
    Write-Host "SHA256：$hash"
    Write-Host "自检结果：$selfTestResult"
    Write-Host "自检状态：PASS"
}
catch {
    Write-Error $_
    exit 1
}
