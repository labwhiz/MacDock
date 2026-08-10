$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
function Search-Bing($query, $n = 8) {
  $q = [uri]::EscapeDataString($query)
  for ($i = 1; $i -le 5; $i++) {
    try {
      $r = Invoke-WebRequest -Uri "https://www.bing.com/search?q=$q&count=20" -Headers $h -UseBasicParsing -TimeoutSec 25
      if ($r.StatusCode -eq 200 -and $r.Content -match 'b_algo') {
        $m = [regex]::Matches($r.Content, '<h2[^>]*><a[^>]*href="([^"]+)"[^>]*>(.*?)</a>')
        $out = @()
        foreach ($x in $m) {
          $u = $x.Groups[1].Value; $t = $x.Groups[2].Value -replace '<[^>]+>', ''
          if ($u -notmatch 'bing\.com|microsoft\.com|go\.microsoft') { $out += [pscustomobject]@{ Title = [System.Net.WebUtility]::HtmlDecode($t); Url = $u } }
          if ($out.Count -ge $n) { break }
        }
        if ($out.Count -gt 0) { return $out }
        Write-Output "  [warn] no parseable results, retry $i"
      } else { Write-Output "  [warn] status/empty retry $i" }
    } catch { Write-Output "  [err] try $i : $($_.Exception.Message)" }
    Start-Sleep -Seconds (4 + $i * 2)
  }
  return @()
}
$queries = @(
  'RocketDock 缺点 不好用',
  'RocketDock Windows 11 problems outdated',
  'Winstep Nexus 缺点 弹窗',
  'ObjectDock 缺点 收费',
  'FalconX dock 缺点 问题',
  'MyDockFinder 缺点 广告 内存',
  'Windows 仿mac dock 软件 对比 推荐',
  'RocketDock vs Winstep Nexus vs ObjectDock 对比'
)
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('# 竞品差评与评测来源汇总')
[void]$sb.AppendLine("> 抓取时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
foreach ($q in $queries) {
  Write-Output "=== $q ==="
  [void]$sb.AppendLine("`n## $q")
  $res = Search-Bing $q
  if ($res.Count -eq 0) { [void]$sb.AppendLine('- (无结果)'); Write-Output '  -> no results' ; continue }
  foreach ($r in $res) { [void]$sb.AppendLine("- [$($r.Title)]($($r.Url))") }
  Start-Sleep -Seconds 5
}
$outFile = 'D:\AI\mcp-workspace\MacDock-Dev\promo\competitor-sources.md'
[System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output "saved: $outFile"
