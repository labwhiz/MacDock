$dir = 'D:\AI\mcp-workspace\MacDock-Dev\promo\pages'
function Get-Text($path) {
  $html = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
  $html = [regex]::Replace($html, '<script[\s\S]*?</script>', ' ')
  $html = [regex]::Replace($html, '<style[\s\S]*?</style>', ' ')
  $html = [regex]::Replace($html, '<[^>]+>', ' ')
  $html = [System.Net.WebUtility]::HtmlDecode($html)
  $html = [regex]::Replace($html, '\s+', ' ')
  return $html
}
foreach ($f in Get-ChildItem $dir -Filter *.html) {
  $t = Get-Text $f.FullName
  Write-Output "===== $($f.BaseName) (chars=$($t.Length)) ====="
  $kw = @('免费','收费','价格','购买','付费','广告','版本','更新','Ultimate','Pro','$','元')
  foreach ($k in $kw) {
    $i = $t.IndexOf($k)
    if ($i -ge 0) { $s = [Math]::Max(0, $i - 60); $len = [Math]::Min(160, $t.Length - $s); Write-Output "  [$k] ...$($t.Substring($s, $len))..." }
  }
}
