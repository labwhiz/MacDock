$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
function Search-Bing($query, $n = 8) {
  $q = [uri]::EscapeDataString($query)
  for ($i = 1; $i -le 4; $i++) {
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
        if ($out.Count -gt 0) { return ,$out }
      }
    } catch {}
    Start-Sleep -Seconds 4
  }
  return @()
}
$queries = @(
  'RocketDock 停更 老 兼容性 吐槽',
  'RocketDock abandoned dead last update reddit',
  'Winstep Nexus 免费版 弹窗 升级 评价',
  'ObjectDock 收费 停更 评价 知乎',
  'MyDockFinder 卡顿 收费 广告 贴吧',
  '仿mac dock 软件 哪个好 知乎',
  'site:reddit.com RocketDock problems alternatives',
  'FalconX 任务栏居中 缺点 兼容性'
)
$outFile = 'D:\AI\mcp-workspace\MacDock-Dev\promo\competitor-sources.md'
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("`n---`n# 第二轮：差评/停更/收费定向搜索")
foreach ($q in $queries) {
  Write-Output "=== $q ==="
  [void]$sb.AppendLine("`n## $q")
  $res = Search-Bing $q
  if ($res.Count -eq 0) { [void]$sb.AppendLine('- (无结果)'); continue }
  foreach ($r in $res) { [void]$sb.AppendLine("- [$($r.Title)]($($r.Url))") }
  Start-Sleep -Seconds 4
}
[System.IO.File]::AppendAllText($outFile, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output "appended"
