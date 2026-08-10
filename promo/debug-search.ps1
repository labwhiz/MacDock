$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
$query = 'RocketDock 缺点 不好用'
$q = [uri]::EscapeDataString($query)
Write-Output "query encoded: $q"
for ($i = 1; $i -le 3; $i++) {
  try {
    $r = Invoke-WebRequest -Uri "https://www.bing.com/search?q=$q&count=20" -Headers $h -UseBasicParsing -TimeoutSec 25
    Write-Output "try $i status=$($r.StatusCode) len=$($r.Content.Length) algo=$($r.Content -match 'b_algo')"
    if ($r.StatusCode -eq 200 -and $r.Content -match 'b_algo') {
      $m = [regex]::Matches($r.Content, '<h2[^>]*><a[^>]*href="([^"]+)"[^>]*>(.*?)</a>')
      Write-Output "h2 count=$($m.Count)"
      if ($m.Count -gt 0) { $x = $m[0]; Write-Output "first: $($x.Groups[2].Value -replace '<[^>]+>','') | $($x.Groups[1].Value)"; break }
    }
  } catch { Write-Output "try $i ERR: $($_.Exception.Message)" }
  Start-Sleep -Seconds 3
}
