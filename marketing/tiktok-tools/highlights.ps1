# Kill-highlight pipeline: new Fortnite footage -> OCR'd kills -> vertical TikToks in a dated folder on the Desktop,
# each registered in clips-library.json (posts.ps1 hands the daily task two never-posted clips from it).
#   powershell -ExecutionPolicy Bypass -File highlights.ps1            # footage not seen before
#   powershell -ExecutionPolicy Bypass -File highlights.ps1 -Weekly    # + the >=60 s montage of the last 7 days
#   powershell -ExecutionPolicy Bypass -File highlights.ps1 -Force     # re-render everything (OCR is cached)
#   powershell -ExecutionPolicy Bypass -File highlights.ps1 -Backfill  # + 60 s+ Fortnite-only cuts of matches already done
param([switch]$Force, [switch]$Weekly, [switch]$Backfill, [string[]]$Only, [string]$Player = 'SM3AG01')
$ErrorActionPreference = 'Stop'
$T = $PSScriptRoot
$ff = 'C:\Users\Zachk\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe'; $fp = 'C:\Users\Zachk\AppData\Local\Microsoft\WinGet\Links\ffprobe.exe'
$inv = [Globalization.CultureInfo]::InvariantCulture
$videos = [Environment]::GetFolderPath('MyVideos')
$queue = Join-Path ([Environment]::GetFolderPath('Desktop')) "StreamCommand_TikTok_Queue\$(Get-Date -Format yyyy-MM-dd)"
$work = Join-Path $env:TEMP 'sc-highlights'
$stateFile = Join-Path $T 'highlights-state.json'
# Always loaded: -Force only stops it from skipping files, so a -Force -Only run doesn't forget every other source.
$state = @{}; if (Test-Path $stateFile) { (Get-Content $stateFile -Raw | ConvertFrom-Json).PSObject.Properties | % { $state[$_.Name] = $_.Value } }
. "$T\library.ps1"
$lib = Read-Library

# --- 1. Sources. Kind decides what we may say about the footage.
$sources = @(
  Get-ChildItem "$videos\NVIDIA\Fortnite" -Filter *.mp4 -ErrorAction SilentlyContinue | % { @{ File = $_; Kind = 'nvidia' } }
  # OBS's output folder; Stream Command's "save full stream" recordings land here too.
  Get-ChildItem $videos -File -ErrorAction SilentlyContinue | ? Extension -in '.mp4', '.mkv' | % { @{ File = $_; Kind = 'obs' } }
  Get-ChildItem "$videos\Stream Command Clips" -Recurse -Filter *.mp4 -ErrorAction SilentlyContinue | % { @{ File = $_; Kind = 'streamcommand' } })
if ($Only) { $sources = $sources | ? { $Only -contains $_.File.FullName } }
$key = { param($f) '{0}|{1}|{2:o}' -f $f.FullName, $f.Length, $f.LastWriteTimeUtc }

$summary = New-Object Collections.Generic.List[string]; $made = 0
function Note($s) { $summary.Add($s); Write-Host $s }
function Ocr($dir, $tsv) { & "$T\ocr-frames.ps1" -Dir $dir -OutTsv $tsv }
function Brand($dir) { if (-not (Test-Path "$dir\endcard.png")) { New-Item -ItemType Directory -Force $dir | Out-Null; & "$T\overlays.ps1" -Out $dir | Out-Null } }

# Where a fight starts: walk back from the kill while the audio stays loud (gunfire), allowing 2 s lulls.
function FightStart([double]$kill, [double]$floor, $loud, [double]$med) {
  $t = [int]$kill; $quiet = 0; $s = $t
  while ($t -gt $floor -and $kill - $t -lt 16) { if ($loud[$t] -gt $med + 8) { $s = $t; $quiet = 0 } elseif (++$quiet -gt 2) { break }; $t-- }
  $s }

function Render-Clip($c, $assets, $kills, $banners, $masks, $src) {
  & "$T\highlight-overlays.ps1" -Out $assets -Hook $c.Hook -Name $c.Name -Total $(if ($c.Chips) { $kills.Count } else { 0 })
  # Payoff first: the first 1-3 s decide whether anyone keeps watching, so open on a 1.4 s flash of the final
  # kill, then play the fights in order. (Chips count segments, so they're off whenever a teaser is prepended.)
  $segs = if ($c.Teaser) { @($c.Teaser) + @($c.Segs) } else { $c.Segs }
  & "$T\render-highlight.ps1" -Src $src -Segments $segs -Kills $banners -Masks $masks -Assets $assets -Name $c.Name -OutDir $queue -Chips:($c.Chips -and -not $c.Teaser) -Plain:([bool]$c.Plain) }

# The 60-90 s Fortnite-only cut of one match: every clean elim with its whole fight, no Stream Command branding.
# Creator Rewards only pays on originals over a minute, so this is the one format that can ever earn on TikTok.
function Build-LongClip($m, $kills, $loud, [double]$med, [string]$stamp, [string]$mode, [int]$total) {
  if ($kills.Count -lt 2) { return $null }
  $segs = @()
  foreach ($pre in 10, 14, 18, 24) {
    $segs = @(); foreach ($kt in $kills) {
      $lead = [Math]::Max([Math]::Min($pre, 24), [Math]::Min(24, $kt - (FightStart $kt $m.start $loud $med) + 1))
      $s = [Math]::Max($m.start, $kt - $lead); $e = [Math]::Min($m.safeEnd, $kt + 3)
      if ($e -le $s + 2) { continue }
      if ($segs -and $s -le $segs[-1].e + 1) { $segs[-1].e = $e } else { $segs += @{ s = $s; e = $e } } }
    if ((($segs | % { $_.e - $_.s } | Measure-Object -Sum).Sum) -ge 62) { break } }
  # Too long for a sharp 10 MB upload: drop the earliest fights until it fits.
  while ($segs.Count -gt 1 -and (($segs | % { $_.e - $_.s } | Measure-Object -Sum).Sum) -gt 90) { $segs = @($segs | Select-Object -Skip 1) }
  $len = ($segs | % { $_.e - $_.s } | Measure-Object -Sum).Sum
  if ($len -lt 62 -or $len -gt 95) { return $null }
  $last = $kills | ? { $_ -le $segs[-1].e } | Select-Object -Last 1
  $what = if ($total) { "$total elims, one $mode match" } else { "one $mode match, every fight" }
  @{ Name = "$stamp-m$($m.index)-long"; Hook = "wait for the last one`n$(if ($total) { "$mode · $total elims" } else { $mode })"; What = $what
     Segs = $segs; Teaser = @{ s = $last - 0.6; e = $last + 0.8 }; Chips = $false; Kind = 'long'; Plain = $true } }

foreach ($src in $sources) {
  $f = $src.File; $k = & $key $f
  if ($state.ContainsKey($k) -and -not $Force) { continue }
  $probe = (& $fp -v error -select_streams v:0 -show_entries stream=width,height:format=duration -of csv=p=0 $f.FullName) -join ',' -split ','
  $w = [int]$probe[0]; $h = [int]$probe[1]; $dur = [double]::Parse($probe[2], $inv)
  if ([Math]::Abs($w / $h - 16 / 9) -gt 0.02) { Note "- skipped $($f.Name): ${w}x$h is not 16:9 gameplay (Stream Command shorts are already vertical)"; $state[$k] = 'skipped-aspect'; continue }

  $id = [IO.Path]::GetFileNameWithoutExtension($f.Name) -replace '[^\w.-]', '_'
  $wd = Join-Path $work $id
  # OCR is the slow part (~2 min per 10 min of footage), so it's cached per source file and reused on re-renders.
  $cached = (Test-Path "$wd\full.tsv") -and (Test-Path "$wd\source.key") -and (Get-Content "$wd\source.key" -Raw).Trim() -eq $k
  if (-not $cached) {
    Remove-Item $wd -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force "$wd\full", "$wd\hud", "$wd\place" | Out-Null
    Write-Host "analyzing $($f.Name) ($([int]$dur) s)..."
    # 2 fps is enough: the kill feed line stays up ~5 s and the HUD counter never goes back down.
    & $ff -v error -y -i $f.FullName -vf 'fps=2,scale=1920:1080' -q:v 3 "$wd\full\f_%05d.jpg"
    & $ff -v error -y -i $f.FullName -vf "fps=2,scale=1920:1080,crop=100:140:1478:15,format=gray,lut=y='if(gt(val,225),0,255)',scale=300:-1:flags=neighbor,pad=500:620:100:100:white" -q:v 2 "$wd\hud\f_%05d.jpg"
    # ametadata writes relative to the cwd; run it from the work dir instead of fighting Windows path escaping.
    Push-Location $wd; & $ff -v error -i $f.FullName -vn -af 'aresample=48000,asetnsamples=48000,astats=metadata=1:reset=1,ametadata=print:key=lavfi.astats.Overall.RMS_level:file=loud.txt' -f null -; Pop-Location
    $sec = 0; (Get-Content "$wd\loud.txt") | % { if ($_ -match 'pts_time:([\d.]+)') { $sec = [int][Math]::Round([double]::Parse($Matches[1], $inv)) } elseif ($_ -match 'RMS_level=(.+)$') { $v = if ($Matches[1] -match 'inf') { -90 } else { [double]::Parse($Matches[1], $inv) }; "$sec,$($v.ToString($inv))" } } | Set-Content "$wd\loud.csv"
    Ocr "$wd\full" "$wd\full.tsv"; Ocr "$wd\hud" "$wd\hud.tsv"

    # Placement banner ("YOU PLACED #N", OCR reads the italic as VOVPUCED): deskew + threshold the banner and read the number.
    foreach ($r in Get-Content "$wd\full.tsv" | ? { $_ -cmatch 'VOVPUCED|PLACED' }) {
      $c = $r.Split("`t"); $x = [Math]::Max(0, [int]$c[1] - 20); $y = [Math]::Max(0, [int]$c[2] - 30)
      & $ff -v error -y -i "$wd\full\$($c[0]).jpg" -vf "crop=480:$([int]$c[4] + 60):${x}:${y},format=gray,lut=y='if(gt(val,190),0,255)',scale=1440:-1,shear=shx=0.25:fillcolor=white" "$wd\place\$($c[0]).jpg" }
    if (Get-ChildItem "$wd\place") { Ocr "$wd\place" "$wd\place.tsv" } else { Set-Content "$wd\place.tsv" '' }
    Remove-Item "$wd\full", "$wd\hud", "$wd\place" -Recurse -Force   # frames are big; the tsvs are all later runs need
    Set-Content "$wd\source.key" $k }

  $rows = Get-Content "$wd\full.tsv"
  if (@($rows | ? { $_ -match '(?i)eliminated|PICKAXE|STORM|thanked the bus' }).Count -lt 20) { Note "- skipped $($f.Name): no Fortnite HUD found"; $state[$k] = 'skipped-not-fortnite'; continue }

  $reads = @{}
  Get-Content "$wd\place.tsv" | ? { $_ } | % { $c = $_.Split("`t"); if ($c[5] -match '#\s*([\dIlOo|]{1,3})(?![A-Za-z0-9])') {
    # Same glyph fixes as the HUD reader ("#IO" = #10). A partial token like "#lv" is dropped, not guessed, and
    # #1 never comes from here: only the VICTORY ROYALE screen can say Zach won.
    $n = $Matches[1] -replace '[Il|]', '1' -replace '[Oo]', '0'; if ($n -match '^\d+$' -and [int]$n -ge 2 -and [int]$n -le 100) { $reads[([int]$c[0].Substring(2) - 1) / 2] = [int]$n } } }

  $a = & "$T\analyze-match.ps1" -FullTsv "$wd\full.tsv" -HudTsv "$wd\hud.tsv" -LoudCsv "$wd\loud.csv" -PlacementReads $reads -Player $Player -Duration $dur | ConvertFrom-Json
  $loud = @($a.loud); $med = ($loud | Sort-Object)[[int]($loud.Count / 2)]
  $masks = @($a.nameMasks)
  Set-Content "$wd\analysis.json" ($a | ConvertTo-Json -Depth 5)
  Set-Content "$wd\meta.json" (@{ src = $f.FullName; kind = $src.Kind; recorded = $f.LastWriteTime.ToString('o'); duration = $dur } | ConvertTo-Json)

  $assets = Join-Path $wd 'assets'; Brand $assets
  $stamp = $f.LastWriteTime.ToString('MMdd-HHmm')
  if ($masks.Count) { Note "  $($masks.Count) on-screen name(s) of other players will be blurred" }
  $produced = New-Object 'Collections.Generic.HashSet[string]'

  foreach ($m in $a.matches) {
    $kills = @($m.kills | % { [double]$_ })   # clean, earned elims -> what gets clipped
    $banners = if ($m.PSObject.Properties['banners']) { @($m.banners | % { [double]$_ }) } else { $kills }   # every elim+knock banner -> blur
    $label = "match $($m.index) of $($f.Name): $(if ($m.mode) { $m.mode } else { 'mode unreadable' }), clean elims $($m.cleanKills) of $($m.feedKills)$(if ($m.finishes) { " ($($m.finishes) mop-up left out)" }), knocks $($m.knocks), HUD $($m.hudKills), placement $(if ($m.placement) { "#$($m.placement)" } else { 'unreadable' })"
    if (-not $kills) { Note "- $label -> no kills, nothing made"; continue }
    Note "- $label"; foreach ($n in $m.notes) { Note "    note: $n" }
    $mode = if ($m.mode) { $m.mode.ToLower() } else { 'fortnite' }
    $total = if ($m.countVerified) { $kills.Count } else { 0 }
    $place = if ($m.placement) { "placed #$($m.placement)" } else { $null }
    $clips = @()

    # (b) one clip per kill: the fight leading in, the kill, ~3.5 s after; never past the safe cut.
    for ($i = 0; $i -lt $kills.Count; $i++) {
      $kt = $kills[$i]; $pre = [Math]::Min(16, [Math]::Max(9, $kt - (FightStart $kt $m.start $loud $med)))
      $s = [Math]::Max($m.start, $kt - $pre); $e = [Math]::Min($m.safeEnd, $kt + 3.5)
      if ($e - $s -lt 12) { $s = [Math]::Max($m.start, $e - 12) }
      if ($e - $kt -lt 1.5) { Note "    kill $($i + 1) at $kt s is too close to the death screen, skipped"; continue }
      $hook = if ($total) { "$mode`nelim $($i + 1) of $total" } else { "$mode`none clean elim" }
      $what = if ($total) { "$mode, elim $($i + 1) of $total" } else { "$mode, one clean elim" }
      $clips += @{ Name = "$stamp-m$($m.index)-kill$($i + 1)"; Hook = $hook; What = $what; Segs = @(@{ s = $s; e = $e }); Chips = $false; Kind = 'single' } }

    # (a) montage when there's more than one kill: ~7.5 s per kill, overlaps merged, trimmed to fit 30 s.
    if ($kills.Count -ge 2) {
      $pre = 5.0; do {
        $segs = @(); foreach ($kt in $kills) { $s = [Math]::Max($m.start, $kt - $pre); $e = [Math]::Min($m.safeEnd, $kt + 2.5); if ($e -gt $s + 2) {
          if ($segs -and $s -le $segs[-1].e) { $segs[-1].e = $e } else { $segs += @{ s = $s; e = $e } } } }
        $len = ($segs | % { $_.e - $_.s } | Measure-Object -Sum).Sum; $pre -= 0.5 } while ($len -gt 26.5 -and $pre -gt 2)
      # A curiosity line on top, the verified facts underneath (no invented counts, ever).
      $hook = "wait for the last one`n$mode$(if ($total) { " · $total kills" })$(if ($place) { " · $place" })"
      $what = if ($total) { "$total elims in $mode$(if ($place) { ", $place" })" } else { "$mode kill montage" }
      $lastKill = $kills | ? { $_ -le $segs[-1].e } | Select-Object -Last 1
      $clips = @(@{ Name = "$stamp-m$($m.index)-montage"; Hook = $hook; What = $what; Segs = $segs; Teaser = @{ s = $lastKill - 0.6; e = $lastKill + 0.8 }; Chips = $false; Kind = 'montage' }) + $clips
      $long = Build-LongClip $m $kills $loud $med $stamp $mode $total
      if ($long) { $clips = @($long) + $clips } else { Note "    not enough fight footage for a 60 s+ Fortnite-only cut" } }

    foreach ($c in $clips) {
      $r = Render-Clip $c $assets $kills $banners $masks $f.FullName; $made++; [void]$produced.Add($c.Name)
      Add-LibraryClip $lib $c $r $src.Kind $f $m.mode | Out-Null } }
  # A re-analysis can drop clips (a kill reclassified as a mop-up, a mode that was misread). Unposted leftovers from
  # this source would keep their old, wrong caption, so they leave the library; posted ones stay as history.
  foreach ($old in @($lib | ? { $_.source -eq $f.FullName -and $_.kind -ne 'weekly' -and -not $_.postedAs -and -not $produced.Contains($_.id) })) {
    $lib.Remove($old); Note "    retired $($old.id): the re-analysis no longer produces it" }
  $state[$k] = 'done'
}

# --- Backfill: 60 s+ Fortnite-only cuts for matches analyzed before that format existed (reuses cached OCR).
if ($Backfill) {
  foreach ($d in Get-ChildItem $work -Directory -ErrorAction SilentlyContinue | ? { Test-Path "$($_.FullName)\analysis.json" }) {
    $meta = Get-Content "$($d.FullName)\meta.json" -Raw | ConvertFrom-Json
    if (-not (Test-Path $meta.src)) { continue }
    $a = Get-Content "$($d.FullName)\analysis.json" -Raw | ConvertFrom-Json
    $loud = @($a.loud); $med = ($loud | Sort-Object)[[int]($loud.Count / 2)]
    $stamp = ([datetime]$meta.recorded).ToString('MMdd-HHmm'); $assets = Join-Path $d.FullName 'assets'; Brand $assets
    foreach ($m in $a.matches) {
      $kills = @($m.kills | % { [double]$_ })
      $banners = if ($m.PSObject.Properties['banners']) { @($m.banners | % { [double]$_ }) } else { $kills }
      $mode = if ($m.mode) { $m.mode.ToLower() } else { 'fortnite' }
      $long = Build-LongClip $m $kills $loud $med $stamp $mode $(if ($m.countVerified) { $kills.Count } else { 0 })
      if (-not $long -or ($lib | ? { $_.id -eq $long.Name })) { continue }
      $r = Render-Clip $long $assets $kills $banners @($a.nameMasks) $meta.src; $made++
      Add-LibraryClip $lib $long $r $meta.kind (Get-Item $meta.src) $m.mode | Out-Null
      Note "- backfilled $($long.Name): $([int]$r.seconds) s" } } }

# --- Weekly: one Fortnite-only montage of at least 60 s from every kill recorded in the last 7 days.
if ($Weekly) {
  $since = (Get-Date).AddDays(-7); $segs = @(); $nKills = 0
  foreach ($d in Get-ChildItem $work -Directory -ErrorAction SilentlyContinue | ? { Test-Path "$($_.FullName)\analysis.json" }) {
    $meta = Get-Content "$($d.FullName)\meta.json" -Raw | ConvertFrom-Json
    if ([datetime]$meta.recorded -lt $since -or -not (Test-Path $meta.src)) { continue }
    $a = Get-Content "$($d.FullName)\analysis.json" -Raw | ConvertFrom-Json
    $loud = @($a.loud); $med = ($loud | Sort-Object)[[int]($loud.Count / 2)]
    foreach ($m in $a.matches) {
      $kills = @($m.kills | % { [double]$_ })   # clean elims anchor the segments
      $banners = if ($m.PSObject.Properties['banners']) { @($m.banners | % { [double]$_ }) } else { $kills }   # all banners -> blur
      foreach ($kt in $kills) {
      $fs = FightStart $kt $m.start $loud $med
      $segs += @{ src = $meta.src; recorded = [datetime]$meta.recorded; kt = $kt; fight = $fs; start = $m.start; safeEnd = $m.safeEnd; kills = $banners; masks = @($a.nameMasks) }; $nKills++ } } }
  # Grow each kill's lead-in (up to where its fight started, max 16 s) until the cut reaches 60 s.
  $built = $null
  foreach ($pre in 5, 7, 9, 11, 13, 16) {
    $built = @(); foreach ($x in $segs | Sort-Object { $_.recorded }, { $_.kt }) {
      $s = [Math]::Max($x.start, $x.kt - [Math]::Min($pre, [Math]::Max(5, $x.kt - $x.fight + 1))); $e = [Math]::Min($x.safeEnd, $x.kt + 2.5)
      if ($e -le $s + 2) { continue }
      $last = if ($built) { $built[-1] } else { $null }
      if ($last -and $last.src -eq $x.src -and $s -le $last.e) { $last.e = $e; $last.n++ } else { $built += @{ src = $x.src; s = $s; e = $e; kills = $x.kills; masks = $x.masks; n = 1; i = $built.Count } } }
    if ((($built | % { $_.e - $_.s } | Measure-Object -Sum).Sum) -ge 60) { break } }
  # A big week can be minutes of kills; past ~80 s the 10 MB upload copy turns to mush. Keep the multi-kill
  # stretches first (then the newest), stop once there's a minute, and play them back in recorded order.
  if ((($built | % { $_.e - $_.s } | Measure-Object -Sum).Sum) -gt 80) {
    $keep = @(); $acc = 0
    foreach ($b in $built | Sort-Object { -$_.n }, { -$_.i }) { if ($acc -ge 60) { break }; if ($acc + ($b.e - $b.s) -gt 80) { continue }; $keep += $b; $acc += $b.e - $b.s }
    $built = @($keep | Sort-Object { $_.i }); $nKills = ($built | % { $_.n } | Measure-Object -Sum).Sum }
  $len = ($built | % { $_.e - $_.s } | Measure-Object -Sum).Sum
  if ($len -lt 60) { Note "- weekly montage: only $([int]$len) s of kill footage this week ($nKills kills) - need 60 s, nothing made" }
  else {
    $assets = Join-Path $work 'weekly-assets'; Brand $assets
    $name = "weekly-$(Get-Date -Format yyyyMMdd)"
    $top = $built | Sort-Object { -$_.n } | Select-Object -First 1   # the busiest fight; its last kill sits 2.5 s before its end
    $c = @{ Name = $name; Hook = "this week in fortnite`n$nKills elims"; What = "$nKills elims from this week's fortnite"; Segs = $built; Chips = $false; Kind = 'weekly'; Plain = $true
            Teaser = @{ src = $top.src; s = $top.e - 3.1; e = $top.e - 1.7; kills = $top.kills; masks = $top.masks } }
    $r = Render-Clip $c $assets @() @() @() $built[0].src; $made++
    Add-LibraryClip $lib $c $r 'mixed' (Get-Item $built[0].src) $null | Out-Null
    Note "- weekly montage: $nKills kills, $([int]$r.seconds) s -> $name" } }

$state | ConvertTo-Json | Set-Content $stateFile
Write-Library $lib
if ($made) {
  New-Item -ItemType Directory -Force $queue | Out-Null
  $mine = $lib | ? { $_.master -like "$queue\*" }
  $body = ($mine | % { "## $($_.id)`n- $($_.kind), $($_.seconds) s, upload ``$(Split-Path $_.upload -Leaf)`` ($($_.uploadMB) MB)`n- Stream Command caption: $($_.captionPromo)`n- Fortnite caption: $($_.captionPlain)`n" }) -join "`n"
  Set-Content (Join-Path $queue 'POSTS.md') @"
# Stream Command TikTok queue - $(Get-Date -Format 'yyyy-MM-dd')
The daily task posts two never-posted clips a day from clips-library.json (one with the Stream Command caption, one
Fortnite-only), plus the weekly montage. Every post: "Your brand" disclosure ON, original game audio, no added music.

Safety pass applied: cut >= 2 s before any death cam / placement / recap screen; victim name plate under "ELIMINATION!"
and every other player name OCR found inside the crop are blurred; kill feed and party list are outside the vertical crop.

## Run summary
$($summary -join "`n")

$body
"@ -Encoding utf8 }
Write-Host "`n$made video(s) made$(if ($made) { " -> $queue" })"
$summary -join "`n"
