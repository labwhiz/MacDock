$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Referer' = 'https://image.baidu.com/' }
$w = [uri]::EscapeDataString('RocketDock')
$url = "https://image.baidu.com/search/acjson?tn=resultjson_com&ipn=rj&word=$w&pn=0&rn=10&ie=utf-8&oe=utf-8&cl=2&lm=-1&st=-1&fr=common"
try {
  $r = Invoke-WebRequest -Uri $url -Headers $h -UseBasicParsing -TimeoutSec 25
  $c = $r.Content
  Write-Output "len=$($c.Length)"
  $i = $c.IndexOf('"data"')
  Write-Output "data idx=$i"
  $i2 = $c.IndexOf('thumbURL')
  Write-Output "thumbURL idx=$i2"
  $i3 = $c.IndexOf('middleURL')
  Write-Output "middleURL idx=$i3"
  $i4 = $c.IndexOf('objURL')
  Write-Output "objURL idx=$i4"
  if ($i2 -ge 0) { $c.Substring([Math]::Max(0,$i2-300), 900) }
} catch { Write-Output "ERR: $($_.Exception.Message)" }
