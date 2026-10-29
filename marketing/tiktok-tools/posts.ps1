# What the daily TikTok task should post, and the record of what it did.
#   posts.ps1 -Next              -> JSON: today's promo pick, plain pick, and (when due) the weekly montage
#   posts.ps1 -Mark <id> -As promo|plain|weekly|skipped   (skipped = checked by eye and rejected; never picked again)
#   posts.ps1 -List              -> every clip with its post status
param([switch]$Next, [string]$Mark, [ValidateSet('promo', 'plain', 'weekly', 'skipped')][string]$As, [switch]$List)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\library.ps1"
$lib = Read-Library

if ($Mark) {
  $e = $lib | ? { $_.id -eq $Mark } | Select-Object -First 1
  if (-not $e) { throw "no clip '$Mark' in the library" }
  if ($e.postedAs) { throw "'$Mark' was already posted ($($e.postedAs), $($e.postedAt)); refusing to mark it twice" }
  $e.postedAs = $As; $e.postedAt = (Get-Date).ToString('o'); Write-Library $lib; "marked $Mark as $As"; return }

if ($List) { $lib | Sort-Object created | Select-Object id, kind, seconds, postedAs, postedAt | Format-Table -AutoSize | Out-String -Width 200; return }

if ($Next) {
  $all = @($lib | ? { -not $_.postedAs -and $_.kind -ne 'weekly' -and (Test-Path $_.upload) } | Sort-Object { [datetime]$_.recorded } -Descending)
  # Multi-kill montages only. A single elim isn't enough to hold a viewer (Zach, 2026-10-26: the 8 am single-kill
  # post got 1 view). When the montages run out, post nothing rather than fall back to a single.
  $fresh = @($all | ? { $_.kind -eq 'montage' })
  $longs = @($all | ? { $_.kind -eq 'long' })
  # Promo gets the newest montage (branded, short). Plain prefers an unbranded 60 s+ cut, the only kind Creator
  # Rewards can pay on; failing that, a different montage, preferably from a different match.
  $promo = $fresh | Sort-Object { -[datetime]::Parse($_.recorded).Ticks } | Select-Object -First 1
  $match = if ($promo) { $promo.id -replace '-montage$', '' } else { '' }
  $plain = $longs | Sort-Object { if (($_.id -replace '-long$', '') -eq $match) { 1 } else { 0 } }, { -[datetime]::Parse($_.recorded).Ticks } | Select-Object -First 1
  if (-not $plain) { $plain = $fresh | ? { $_.id -ne $promo.id } | Sort-Object { if (($_.id -replace '-montage$', '') -eq $match) { 1 } else { 0 } }, { -[datetime]::Parse($_.recorded).Ticks } | Select-Object -First 1 }
  # Entries from before the flag existed were all branded.
  $branded = { param($e) if ($e -and $e.PSObject.Properties['branded']) { [bool]$e.branded } else { $true } }
  # One of each per calendar day: a manual post earlier today counts, so the 6 pm run doesn't double up.
  $today = (Get-Date).Date
  if ($lib | ? { $_.postedAs -eq 'promo' -and ([datetime]$_.postedAt).Date -eq $today }) { $promo = $null }
  if ($lib | ? { $_.postedAs -eq 'plain' -and ([datetime]$_.postedAt).Date -eq $today }) { $plain = $null }
  $lastWeekly = $lib | ? { $_.kind -eq 'weekly' -and $_.postedAt } | Sort-Object postedAt | Select-Object -Last 1
  $weeklyDue = -not $lastWeekly -or ([datetime]$lastWeekly.postedAt) -lt (Get-Date).AddDays(-6)
  $weekly = if ($weeklyDue) { $lib | ? { $_.kind -eq 'weekly' -and -not $_.postedAs -and (Test-Path $_.upload) } | Sort-Object created | Select-Object -Last 1 }
  [pscustomobject]@{
    # brandDisclosure: turn on "Disclose post content -> Your brand" only when the video carries Stream Command branding.
    promo = $promo | Select-Object id, upload, seconds, captionPromo, @{ n = 'brandDisclosure'; e = { & $branded $_ } }
    plain = $plain | Select-Object id, upload, seconds, captionPlain, @{ n = 'brandDisclosure'; e = { & $branded $_ } }
    weekly = $weekly | Select-Object id, upload, seconds, captionPlain, @{ n = 'brandDisclosure'; e = { & $branded $_ } }
    weeklyDue = $weeklyDue; unpostedMontages = $fresh.Count; unpostedLongCuts = $longs.Count } | ConvertTo-Json -Depth 4 }
