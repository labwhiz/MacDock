$ErrorActionPreference = "Continue"
$h = @{ 'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'; 'Accept-Language' = 'zh-CN,zh;q=0.9,en;q=0.8' }
function Search-Baidu($query, $n = 8) {
  $q = [uri]::EscapeDataString($query)
  for ($i = 1; $i -le 3; $i++) {
    try {
      $r = Invoke-WebRequest -Uri "https://www.baidu.com/s?wd=$q&rn=20" -Headers $h -UseBasicParsing -TimeoutSec 25
      if ($r.StatusCode -eq 200) {
        $c = $r.Content
        $titles = [regex]::Matches($c, '<h3[^>]*class="[^"]*c-title[^"]*"[^>]*>[\s\S]*?<a[^>]*href="([^"]+)"[^>]*>(.*?)</a>')
        $out = @()
        foreach ($x in $titles) {
          $t = $x.Groups[2].Value -replace '<[^>]+>', ''
          $t = [System.Net.WebUtility]::HtmlDecode($t)
          $u = $x.Groups[1].Value
          $out += [pscustomobject]@{ Title = $t.Trim(); Url = $u }
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
  'RocketDock 停更 太老',
  'RocketDock 兼容性 问题 吐槽',
  'Winstep Nexus 弹窗 收费',
  'Winstep Nexus 免费版 限制',
  'ObjectDock 收费 停止更新',
  'MyDockFinder 收费 广告 捆绑',
  'MyDockFinder 卡顿 内存 占用',
  'Windows 仿mac dock 软件 推荐 知乎',
  'FalconX 任务栏 缺点 问题',
  '仿mac dock 哪个好 对比'
)
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("`n---`n# 第三轮：百度搜索（中文差评/收费/对比）")
[void]$sb.AppendLine("> 抓取时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
foreach ($q in $queries) {
  Write-Output "=== $q ==="
  [void]$sb.AppendLine("`n## $q")
  $res = Search-Baidu $q
  if ($res.Count -eq 0) { [void]$sb.AppendLine('- (无结果)'); continue }
  foreach ($r in $res) { [void]$sb.AppendLine("- [$($r.Title)]($($r.Url))") }
  Start-Sleep -Seconds 3
}
$outFile = 'D:\AI\mcp-workspace\MacDock-Dev\promo\competitor-sources.md'
[System.IO.File]::AppendAllText($outFile, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output "appended to $outFile"
