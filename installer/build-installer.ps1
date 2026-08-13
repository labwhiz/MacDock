# build-installer.ps1
# 一键重新打包 MacDock-Setup.exe（发布 MacDock + 生成负载 + 编译安装器）
$ErrorActionPreference = 'Stop'

$instDir = $PSScriptRoot
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'MacDock\MacDock.csproj'
$publishDir = Join-Path $root 'publish\MacDock-fd'
# 动态查找 csc：优先 64 位、回退 32 位，避免路径硬编码在 ARM64/精简系统上失效（9.5）
$csc = $null
$cscCandidates = @(
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
foreach ($c in $cscCandidates) {
  if (Test-Path -LiteralPath $c) { $csc = $c; break }
}
if (-not $csc) { throw '未找到 csc.exe（需要 .NET Framework 4.x 的 C# 编译器，用于编译安装器）' }
$ico = Join-Path $root 'MacDock\Assets\app.ico'

# 1) 仅关闭从工作区启动的 MacDock（不影响其他位置已安装的版本），然后发布
#    （目标 .NET Framework 4.8，Win10/11 内置，无需安装 .NET 8 运行时）
Get-Process MacDock -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and $_.Path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
} | Stop-Process -Force
# 清空旧发布目录，避免残留 net8 时代的 MacDock.dll / deps.json / runtimeconfig.json
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
dotnet publish $proj -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

# 2) 生成内嵌负载 Payload.cs
# net48 发布产物：exe（托管主程序）、exe.config、依赖 dll、pdb（若有）
$files = Get-ChildItem -LiteralPath $publishDir -File | Where-Object { $_.Extension -in @('.exe', '.dll', '.config', '.pdb') } |
    ForEach-Object { @{ Path = $_.FullName; Name = $_.Name } }
if ($files.Count -eq 0) { throw '未找到发布产物，dotnet publish 输出为空' }
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('// Auto-generated payload (built by build-installer.ps1). Do not edit by hand.')
[void]$sb.AppendLine('namespace MacDockSetup')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    internal static class Payload')
[void]$sb.AppendLine('    {')
$names = New-Object System.Collections.Generic.List[string]
foreach ($f in $files) {
  $field = ($f.Name -replace '[\W]','_')
  $names.Add($field)
  $bytes = [System.IO.File]::ReadAllBytes($f.Path)
  [void]$sb.AppendLine("        public static readonly byte[] $field = new byte[] {")
  for ($i = 0; $i -lt $bytes.Length; $i += 18) {
    $end = [Math]::Min($i + 18, $bytes.Length)
    $line = ($bytes[$i..($end-1)] | ForEach-Object { '0x{0:X2},' -f $_ }) -join ' '
    [void]$sb.AppendLine("            $line")
  }
  [void]$sb.AppendLine('        };')
}
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('    internal static class PayloadFiles')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        public static readonly string[] Names =')
[void]$sb.AppendLine('        {')
foreach ($f in $files) { [void]$sb.AppendLine("            `"$($f.Name)`",") }
[void]$sb.AppendLine('        };')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('        public static readonly byte[][] Data = new byte[][]')
[void]$sb.AppendLine('        {')
foreach ($n in $names) { [void]$sb.AppendLine("            Payload.$n,") }
[void]$sb.AppendLine('        };')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('}')
[System.IO.File]::WriteAllText((Join-Path $instDir 'Payload.cs'), $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

# 3) 编译安装器
$outExe = Join-Path $instDir 'MacDock-Setup.exe'
$csInstaller = Join-Path $instDir 'Installer.cs'
$csSetupForm = Join-Path $instDir 'SetupForm.cs'
$csPayload = Join-Path $instDir 'Payload.cs'
& $csc /nologo /target:winexe /optimize+ /out:$outExe /win32icon:$ico $csInstaller $csSetupForm $csPayload
