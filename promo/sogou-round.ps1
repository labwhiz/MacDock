$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8'; 'Referer' = 'https://www.sogou.com/' }
function Search-Sogou($query, $n = 8) {
  $q = [uri]::EscapeDataString($query)
  for ($i = 1; $i -le 3; $i++) {
    try {
      $r = Invoke-WebRequest -Uri "https://www.sogou.com/web?query=$q" -Headers $h -UseBasicParsing -TimeoutSec 25
      if ($r.StatusCode -eq 200) {
        $m = [regex]::Matches($r.Content, '<h3[^>]*>[\s\S]*?<a[^>]*href="([^"]+)"[^>]*>(.*?)</a>')
        $out = @()
        foreach ($x in $m) {
          $t = $x.Groups[2].Value -replace '<[^>]+>', ''
          $t = [System.Net.WebUtility]::HtmlDecode($t).Trim()
          if ($t -match '更多') { continue }
          $u = $x.Groups[1].Value
          if ($u -like 'http*') { $full = $u } else { $full = 'https://www.sogou.com' + $u }
          $out += [pscustomobject]@{ Title = $t; Url = $full }
          if ($out.Count -ge $n) { break }
        }
        if ($out.Count -gt 0) { return ,$out }
      }
    } catch {}
    Start-Sleep -Seconds 6
  }
  return @()
}
$queries = @(
  'Winstep Nexus 免费版 弹窗 收费',
  'ObjectDock 收费 停止更新',
  'MyDockFinder 卡顿 内存 占用',
  'Windows 仿mac dock 软件 哪个好 知乎',
  'FalconX 任务栏 缺点 问题',
  'RocketDock Win10 闪退 兼容',
  '仿mac dock 软件 对比 评测',
  'MyDockFinder 视频 bilibili'
)
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("`n---`n# 第四轮：搜狗搜索")
[void]$sb.AppendLine("> 抓取时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
foreach ($q in $queries) {
  Write-Output "=== $q ==="
  [void]$sb.AppendLine("`n## $q")
  $res = Search-Sogou $q
  if ($res.Count -eq 0) { [void]$sb.AppendLine('- (无结果)'); continue }
  foreach ($r in $res) { [void]$sb.AppendLine("- [$($r.Title)]($($r.Url))") }
  Start-Sleep -Seconds 8
}
$outFile = 'D:\AI\mcp-workspace\MacDock-Dev\promo\competitor-sources.md'
[System.IO.File]::AppendAllText($outFile, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output "appended"
