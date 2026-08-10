function Get-Text($path, $enc = 'UTF8') {
  $html = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::$enc)
  $html = [regex]::Replace($html, '<script[\s\S]*?</script>', ' ')
  $html = [regex]::Replace($html, '<style[\s\S]*?</style>', ' ')
  $html = [regex]::Replace($html, '<[^>]+>', ' ')
  $html = [System.Net.WebUtility]::HtmlDecode($html)
  $html = [regex]::Replace($html, '\s+', ' ')
  return $html
}
Write-Output '===== rocketdock_net: version/date/features ====='
$t = Get-Text 'D:\AI\mcp-workspace\MacDock-Dev\promo\pages\rocketdock_net.html'
foreach ($pat in @('version','Version','1\.3','20\d\d','free','Free','zoom','transparen','Windows 11','Win11')) {
  $m = [regex]::Matches($t, ".{0,70}$pat.{0,90}", 'IgnoreCase')
  if ($m.Count) { $m | Select-Object -First 3 | ForEach-Object { Write-Output "  [$pat] ...$($_.Value)..." } }
}
Write-Output ''
Write-Output '===== mydockfinder_gh: latest release tags ====='
$g = Get-Text 'D:\AI\mcp-workspace\MacDock-Dev\promo\pages\mydockfinder_gh.html'
[regex]::Matches($g, '(v\d+\.\d+(?:\.\d+)?)', 'IgnoreCase') | Select-Object -First 10 | ForEach-Object { Write-Output "  tag: $($_.Groups[1].Value)" }
$m2 = [regex]::Matches($g, '(20\d\d[-/]\d{1,2}[-/]\d{1,2})')
if ($m2.Count) { $m2 | Select-Object -First 8 | ForEach-Object { Write-Output "  date: $($_.Value)" } }
Write-Output ''
Write-Output '===== winstep_cn: decode GBK ====='
$bytes = [System.IO.File]::ReadAllBytes('D:\AI\mcp-workspace\MacDock-Dev\promo\pages\winstep_cn.html')
$gbk = [System.Text.Encoding]::GetEncoding('GB18030')
$t2 = $gbk.GetString($bytes)
$t2 = [regex]::Replace($t2, '<script[\s\S]*?</script>', ' ')
$t2 = [regex]::Replace($t2, '<style[\s\S]*?</style>', ' ')
$t2 = [regex]::Replace($t2, '<[^>]+>', ' ')
$t2 = [regex]::Replace($t2, '\s+', ' ')
foreach ($pat in @('免费','购买','收费','Ultimate','广告','元','¥','\$')) {
  $m = [regex]::Matches($t2, ".{0,60}$pat.{0,80}")
  if ($m.Count) { $m | Select-Object -First 3 | ForEach-Object { Write-Output "  [$pat] ...$($_.Value)..." } }
}
