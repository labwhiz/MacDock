$ErrorActionPreference = "Continue"
$dir = 'D:\AI\mcp-workspace\MacDock-Dev\promo\pages'
$map = @{
  'rocketdock_net' = 'rocketdock'
  'mydockfinder'   = 'mydockfinder'
  'mydockfinder_gh' = 'mydockfinder'
  'falconx_appinn' = 'falconx'
  'objectdock_stardock' = 'objectdock'
  'winstep_cn'     = 'winstep'
}
foreach ($f in Get-ChildItem $dir -Filter *.html) {
  $c = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
  $srcs = [regex]::Matches($c, '(?:src|data-src|murl)="([^"]+\.(?:png|jpg|jpeg|gif|webp)(?:\?[^"]*)?)"', 'IgnoreCase') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -match '^https?://' } | Select-Object -Unique
  Write-Output "== $($f.BaseName): $($srcs.Count) imgs =="
  $srcs | Select-Object -First 12 | ForEach-Object { Write-Output "  $_" }
}
