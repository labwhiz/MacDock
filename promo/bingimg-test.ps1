$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
$q = [uri]::EscapeDataString('RocketDock')
try {
  $r = Invoke-WebRequest -Uri "https://www.bing.com/images/search?q=$q&qft=filterui:photo-photo" -Headers $h -UseBasicParsing -TimeoutSec 25
  $c = $r.Content
  Write-Output "len=$($c.Length)"
  $murls = [regex]::Matches($c, 'murl&quot;:&quot;([^&]+?)&quot;')
  Write-Output "murl count=$($murls.Count)"
  $murls | Select-Object -First 6 | ForEach-Object { Write-Output "  $($_.Groups[1].Value)" }
  if ($murls.Count -eq 0) {
    $m2 = [regex]::Matches($c, 'murl="([^"]+)"')
    Write-Output "alt murl count=$($m2.Count)"
    $m2 | Select-Object -First 6 | ForEach-Object { Write-Output "  $($_.Groups[1].Value)" }
  }
} catch { Write-Output "ERR: $($_.Exception.Message)" }
