$ErrorActionPreference = 'Stop'

$projectDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$projectFile = Join-Path $projectDir 'PaperCare.csproj'
$distDir = Join-Path $projectDir 'dist'
$artifactsDir = Join-Path $projectDir 'artifacts'
$selfTestResult = Join-Path $artifactsDir 'self-test-results.json'

try {
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw "项目文件不存在：$projectFile"
    }

    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    Set-Location -LiteralPath $projectDir

    $publishArguments = @(
        'publish', $projectFile,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-o', $distDir
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败，退出码：$LASTEXITCODE"
    }

    $publishedExe = Join-Path $distDir 'PaperCare.exe'
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
        throw "PaperCare 自检失败，退出码：$($selfTestProcess.ExitCode)"
    }
    if (-not (Test-Path -LiteralPath $selfTestResult -PathType Leaf)) {
        throw "自检未生成结果文件：$selfTestResult"
    }

    $selfTest = Get-Content -LiteralPath $selfTestResult -Raw | ConvertFrom-Json
    if ($selfTest.passed -ne $true) {
        throw "自检结果未通过：$selfTestResult"
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
