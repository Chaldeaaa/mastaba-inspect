param(
    [string]$Solution = (Join-Path $PSScriptRoot 'MastabaInspect.slnx'),
    [string]$BepInExDir = "$env:APPDATA\com.kesomannen.gale\gtfo\profiles\Default\BepInEx"
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$solutionPath = (Resolve-Path -LiteralPath $Solution).Path
[xml]$solutionDocument = [IO.File]::ReadAllText($solutionPath)
$runtimeDir = Join-Path (Split-Path $BepInExDir) 'dotnet'
$runtimeVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $runtimeDir 'System.Private.CoreLib.dll')).Version
if ($runtimeVersion.Major -ne 6) {
    throw "Expected .NET 6, found $runtimeVersion"
}
$binDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $binDir -Force | Out-Null
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp10)
foreach ($project in $solutionDocument.Solution.Project) {
    $projectPath = Join-Path (Split-Path $solutionPath) $project.Path
    [xml]$projectDocument = [IO.File]::ReadAllText($projectPath)
    $projectDir = Split-Path $projectPath
    $assemblyName = $projectDocument.Project.PropertyGroup.AssemblyName
    $isExecutable = $projectDocument.Project.PropertyGroup.OutputType -eq 'Exe'
    $references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
    $dlls = @(Get-ChildItem -LiteralPath $runtimeDir -Filter '*.dll')
    foreach ($reference in $projectDocument.Project.ItemGroup.Reference) {
        if ($reference.Include) {
            $referencePath = $reference.Include.Replace('$(BepInExDir)', $BepInExDir)
            $dlls += @(Get-ChildItem -Path $referencePath)
        }
    }
    $names = @{}
    foreach ($dll in $dlls) {
        try {
            $name = [Reflection.AssemblyName]::GetAssemblyName($dll.FullName).Name
        }
        catch [BadImageFormatException] {
            continue
        }
        if ($names.ContainsKey($name)) {
            continue
        }
        $names[$name] = $true
        $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($dll.FullName))
    }
    $trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
    foreach ($include in $projectDocument.Project.ItemGroup.Compile) {
        foreach ($source in Get-ChildItem -Path (Join-Path $projectDir $include.Include)) {
            $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($source.FullName), $parseOptions, $source.FullName))
        }
    }
    $kind = if ($isExecutable) {
        [Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication
    }
    else {
        [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary
    }
    $options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new($kind).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
    $compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($assemblyName, $trees, $references, $options)
    $outputPath = Join-Path $binDir "$assemblyName.dll"
    $stream = [IO.File]::Create($outputPath)
    try {
        $result = $compilation.Emit($stream)
    }
    finally {
        $stream.Dispose()
    }
    foreach ($diagnostic in $result.Diagnostics) {
        Write-Output $diagnostic.ToString()
    }
    if (-not $result.Success) {
        throw "Compilation failed: $assemblyName"
    }
    Write-Output "Built $assemblyName from $solutionPath"
    if ($isExecutable) {
        foreach ($item in $projectDocument.Project.ItemGroup.None) {
            if ($item.CopyToOutputDirectory) {
                Copy-Item -LiteralPath (Join-Path $projectDir $item.Include) -Destination (Join-Path $binDir ([IO.Path]::GetFileName($item.Include)))
            }
        }
        $configuration = '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.0"},"rollForward":"Major"}}'
        [IO.File]::WriteAllText((Join-Path $binDir "$assemblyName.runtimeconfig.json"), $configuration + "`n", [Text.UTF8Encoding]::new($false))
        $testOutput = @(& dotnet $outputPath)
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed: $LASTEXITCODE"
        }
        $report = $testOutput[-1]
        [IO.File]::WriteAllText((Join-Path $binDir "$assemblyName.result.json"), $report + "`n", [Text.UTF8Encoding]::new($false))
        $summary = $report | ConvertFrom-Json
        Write-Output "$assemblyName checks: passed=$($summary.passed), assertions=$($summary.assertions), frames=$($summary.framesChecked)"
    }
}
