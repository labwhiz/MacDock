$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Referer' = 'https://image.baidu.com/' }
function Get-BaiduImages($word, $pn = 0, $rn = 20) {
  $w = [uri]::EscapeDataString($word)
  $url = "https://image.baidu.com/search/acjson?tn=resultjson_com&ipn=rj&word=$w&pn=$pn&rn=$rn&ie=utf-8&oe=utf-8&cl=2&lm=-1&st=-1&fr=common"
  $r = Invoke-WebRequest -Uri $url -Headers $h -UseBasicParsing -TimeoutSec 25
  $c = $r.Content
  $murls = [regex]::Matches($c, '"middleURL":"([^"]+)"')
  $titles = [regex]::Matches($c, '"fromPageTitleEnc":"([^"]*)"')
  $out = @()
  for ($i = 0; $i -lt $murls.Count; $i++) {
    $u = $murls[$i].Groups[1].Value
    $t = if ($i -lt $titles.Count) { $titles[$i].Groups[1].Value } else { '' }
    $t = [System.Net.WebUtility]::HtmlDecode($t) -replace '<[^>]+>',''
    if ($u) { $out += [pscustomobject]@{ Url = $u; Title = $t } }
  }
  return $out
}
$res = Get-BaiduImages 'RocketDock' 0 20
Write-Output "count=$($res.Count)"
$res | Select-Object -First 10 | ForEach-Object { Write-Output "T: $($_.Title) | $($_.Url)" }
