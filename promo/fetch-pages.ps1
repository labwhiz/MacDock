$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
$targets = @{
  'rocketdock_wiki'      = 'https://en.wikipedia.org/wiki/RocketDock'
  'falconx_appinn'       = 'https://www.appinn.com/falconx-for-windows/'
  'mydockfinder'         = 'https://www.mydockfinder.com/'
  'winstep_cn'           = 'https://winstep.net.cn/'
  'objectdock_cn'        = 'https://objectdock.cn/'
  'mydockfinder_gh'      = 'https://github.com/gaxCat/Mydockfinder/releases'
  'rocketdock_net'       = 'https://rocketdock.net/'
  'objectdock_stardock'  = 'https://www.stardock.com/products/objectdock/'
}
$dir = 'D:\AI\mcp-workspace\MacDock-Dev\promo\pages'
foreach ($k in $targets.Keys) {
  $u = $targets[$k]
  try {
    $r = Invoke-WebRequest -Uri $u -Headers $h -UseBasicParsing -TimeoutSec 30
    $t = [regex]::Match($r.Content, '<title[^>]*>(.*?)</title>', 'IgnoreCase').Groups[1].Value
    $file = Join-Path $dir ($k + '.html')
    [System.IO.File]::WriteAllText($file, $r.Content, [System.Text.Encoding]::UTF8)
    Write-Output "OK  [$t] $u -> $file (len=$($r.Content.Length))"
  } catch { Write-Output "ERR $u : $($_.Exception.Message)" }
  Start-Sleep -Seconds 2
}
Write-Output '--- connectivity probes ---'
foreach ($u2 in @('https://www.sogou.com/web?query=test','https://www.baidu.com/s?wd=test')) {
  try { $r2 = Invoke-WebRequest -Uri $u2 -Headers $h -UseBasicParsing -TimeoutSec 15; Write-Output "OK len=$($r2.Content.Length) $u2" } catch { Write-Output "ERR $u2 : $($_.Exception.Message)" }
}
