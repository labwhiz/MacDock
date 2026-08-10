$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8'; 'Referer' = 'https://www.sogou.com/' }
$q = [uri]::EscapeDataString('MyDockFinder 收费 广告')
try {
  $r = Invoke-WebRequest -Uri "https://www.sogou.com/web?query=$q" -Headers $h -UseBasicParsing -TimeoutSec 25
  Write-Output "len=$($r.Content.Length)"
  $c = $r.Content
  foreach ($pat in @('vr-title','results','<h3')) { Write-Output "pat=$pat idx=$($c.IndexOf($pat))" }
  $m = [regex]::Matches($c, '<h3[^>]*>[\s\S]*?<a[^>]*href="([^"]+)"[^>]*>(.*?)</a>')
  Write-Output "h3 count=$($m.Count)"
  $m | Select-Object -First 5 | ForEach-Object { $t = $_.Groups[2].Value -replace '<[^>]+>',''; Write-Output "T: $t | $($_.Groups[1].Value)" }
} catch { Write-Output "ERR: $($_.Exception.Message)" }
