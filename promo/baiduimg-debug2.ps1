$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Referer' = 'https://image.baidu.com/' }
$w = [uri]::EscapeDataString('RocketDock')
$url = "https://image.baidu.com/search/acjson?tn=resultjson_com&ipn=rj&word=$w&pn=0&rn=10&ie=utf-8&oe=utf-8&cl=2&lm=-1&st=-1&fr=common"
$r = Invoke-WebRequest -Uri $url -Headers $h -UseBasicParsing -TimeoutSec 25
Write-Output $r.Content
