$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8'; 'Referer' = 'https://image.baidu.com/' }
$word = [uri]::EscapeDataString('RocketDock 仿苹果Dock栏')
$url = "https://image.baidu.com/search/acjson?tn=resultjson_com&ipn=rj&word=$word&pn=0&rn=20&ie=utf-8&oe=utf-8&cl=2&lm=-1&st=-1&fr=common&width=&height=&face=0&istype=0"
try {
  $r = Invoke-WebRequest -Uri $url -Headers $h -UseBasicParsing -TimeoutSec 25
  Write-Output "len=$($r.Content.Length)"
  $json = $r.Content | ConvertFrom-Json
  Write-Output "data count=$($json.data.Count)"
  $json.data | Select-Object -First 8 | ForEach-Object { Write-Output "T: $($_.fromPageTitleEnc -replace '<[^>]+>','') | $($_.middleURL) | $($_.thumbURL)" }
} catch { Write-Output "ERR: $($_.Exception.Message)" }
