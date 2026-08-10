$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
$base = 'D:\AI\mcp-workspace\MacDock-Dev\promo\images'
New-Item -ItemType Directory -Force -Path $base | Out-Null
function Get-BingImages($query, $n = 12) {
  $q = [uri]::EscapeDataString($query)
  for ($i = 1; $i -le 3; $i++) {
    try {
      $r = Invoke-WebRequest -Uri "https://www.bing.com/images/search?q=$q&qft=filterui:photo-photo" -Headers $h -UseBasicParsing -TimeoutSec 25
      if ($r.StatusCode -eq 200) {
        $murls = [regex]::Matches($r.Content, 'murl&quot;:&quot;([^&]+?)&quot;')
        $out = @()
        foreach ($m in $murls) {
          $u = $m.Groups[1].Value
          if ($u -match '^https?://' -and $u -notmatch 'bing\.com') { $out += $u }
          if ($out.Count -ge $n) { break }
        }
        if ($out.Count -gt 0) { return $out }
      }
    } catch {}
    Start-Sleep -Seconds 4
  }
  return @()
}
function Save-Image($url, $dir, $name) {
  try {
    $ext = 'jpg'
    if ($url -match '\.(png|gif|webp|jpe?g)(\?|$)') { $ext = $Matches[1] }
    $file = Join-Path $dir ($name + '.' + $ext)
    $wc = New-Object System.Net.WebClient
    $wc.Headers.Add('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36')
    $wc.Headers.Add('Referer', 'https://www.bing.com/')
    $wc.DownloadFile($url, $file)
    $wc.Dispose()
    $len = (Get-Item $file).Length
    if ($len -lt 3000) { Remove-Item $file -Force; return $null }
    return $file
  } catch { return $null }
}
$plan = @(
  @{ Name = 'rocketdock';  Queries = @('RocketDock dock windows','RocketDock 仿苹果dock') },
  @{ Name = 'winstep';     Queries = @('Winstep Nexus dock','Winstep Nexus 桌面') },
  @{ Name = 'objectdock';  Queries = @('ObjectDock','Stardock ObjectDock') },
  @{ Name = 'falconx';     Queries = @('FalconX 任务栏 居中','FalconX taskbar windows') },
  @{ Name = 'mydockfinder'; Queries = @('MyDockFinder','MyDockFinder 仿mac') }
)
$log = [System.Text.StringBuilder]::new()
foreach ($p in $plan) {
  $d = Join-Path $base $p.Name
  New-Item -ItemType Directory -Force -Path $d | Out-Null
  $seen = @{}
  $got = 0
  foreach ($qq in $p.Queries) {
    $urls = Get-BingImages $qq 12
    foreach ($u in $urls) {
      if ($got -ge 14 -or $seen.ContainsKey($u)) { continue }
      $seen[$u] = $true
      $f = Save-Image $u $d ("{0:D2}" -f $got)
      if ($f) { $got++; [void]$log.AppendLine("OK  $($p.Name): $f"); Write-Output "OK $($p.Name) [$got]: $u" }
    }
    Start-Sleep -Seconds 4
  }
  [void]$log.AppendLine("-- $($p.Name): $got images --")
}
# FalconX demo GIF from appinn article
$gifDir = Join-Path $base 'falconx'
$g = Save-Image 'https://i.loli.net/2019/12/04/KC6hx2SzD3E8m7Q.gif' $gifDir 'demo'
Write-Output "falconx demo gif: $g"
# MyDockFinder GitHub README raw images
try {
  $rm = Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/mydockfinder/mydockfinder-for-Win10-Win11/master/README.md' -Headers $h -UseBasicParsing -TimeoutSec 20
  $imgs = [regex]::Matches($rm.Content, '!\[[^\]]*\]\(([^)]+)\)') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -match '^https?://' }
  Write-Output "mydockfinder README imgs: $($imgs.Count)"
  $i = 0
  foreach ($iu in $imgs) { $f = Save-Image $iu (Join-Path $base 'mydockfinder') ("readme{0:D2}" -f $i); if ($f) { $i++; Write-Output "  README img OK: $iu" } }
} catch { Write-Output "README fetch failed: $($_.Exception.Message)" }
[System.IO.File]::WriteAllText('D:\AI\mcp-workspace\MacDock-Dev\promo\image-download-log.txt', $log.ToString(), [System.Text.Encoding]::UTF8)
Write-Output 'DONE'
