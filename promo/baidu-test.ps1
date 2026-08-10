$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
$q = [uri]::EscapeDataString('MyDockFinder 广告 收费')
try {
  $r = Invoke-WebRequest -Uri "https://www.baidu.com/s?wd=$q&rn=20" -Headers $h -UseBasicParsing -TimeoutSec 25
  Write-Output "len=$($r.Content.Length)"
  $c = $r.Content
  $i = $c.IndexOf('result')
  Write-Output "result idx=$i"
  $titles = [regex]::Matches($c, '<h3[^>]*class="[^"]*c-title[^"]*"[^>]*>[\s\S]*?<a[^>]*href="([^"]+)"[^>]*>(.*?)</a>')
  Write-Output "title count=$($titles.Count)"
  $titles | Select-Object -First 5 | ForEach-Object { $t = $_.Groups[2].Value -replace '<[^>]+>',''; Write-Output "T: $t | $($_.Groups[1].Value)" }
  $snippets = [regex]::Matches($c, '<span class="content-right_8Zs40">(.*?)</span>')
  Write-Output "snip count=$($snippets.Count)"
} catch { Write-Output "ERR: $($_.Exception.Message)" }
