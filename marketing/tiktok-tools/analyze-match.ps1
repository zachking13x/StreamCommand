# Turns OCR + loudness of one Fortnite recording into a match list (mode, kills with timestamps, placement,
# the safe-cut time before any death/recap screen). Everything here is read off the screen; anything that
# can't be read twice the same way is reported as unknown rather than guessed.
param(
  [string]$FullTsv,      # ocr-frames.ps1 output on full frames
  [string]$HudTsv,       # ocr-frames.ps1 output on the thresholded HUD digit column
  [string]$LoudCsv,      # second,rms_db
  [hashtable]$PlacementReads = @{},  # frame seconds -> placement number read from the deskewed banner crop
  [double]$Fps = 2,
  [double]$Scale = 1,    # source height / 1080, HUD positions below are 1080p
  [string]$Player = 'SM3AG01',
  [double]$Duration
)

# OCR confuses these glyphs in Fortnite's HUD font; normalizing both sides makes the name match robust.
function Norm([string]$s) { ($s.ToUpper() -replace '[^A-Z0-9]', '').Replace('O', '0').Replace('Q', '0').Replace('I', '1').Replace('L', '1').Replace('B', '3').Replace('V', 'M').Replace('C', 'G').Replace('5', 'S') }
function Lev([string]$a, [string]$b) {
  $prev = 0..$b.Length
  for ($i = 1; $i -le $a.Length; $i++) {
    $cur = @($i) + (1..$b.Length | % { 0 })
    for ($j = 1; $j -le $b.Length; $j++) {
      $c = if ($a[$i - 1] -eq $b[$j - 1]) { 0 } else { 1 }
      $cur[$j] = [Math]::Min([Math]::Min($prev[$j] + 1, $cur[$j - 1] + 1), $prev[$j - 1] + $c) }
    $prev = $cur }
  $prev[$b.Length] }
$np = Norm $Player; $tol = if ($np.Length -ge 8) { 2 } else { 1 }
function IsPlayer([string]$s) { $n = Norm $s; $n.Length -ge ($np.Length - 1) -and (Lev $n.Substring(0, [Math]::Min($n.Length, $np.Length)) $np) -le $tol }

function Read-Tsv($path) {
  foreach ($l in [IO.File]::ReadAllLines($path)) { $c = $l.Split("`t"); if ($c.Count -lt 6) { continue }
    [pscustomobject]@{ T = ([int]$c[0].Substring(2) - 1) / $Fps; X = [int]$c[1] / $Scale; Y = [int]$c[2] / $Scale; W = [int]$c[3] / $Scale; H = [int]$c[4] / $Scale; Text = $c[5] } } }
$rows = @(Read-Tsv $FullTsv)

# --- Kill feed: left column, "<player> ... elim..." = a kill by us, "... elim... <player>" = we died.
$killFrames = @{}; $deaths = @()
foreach ($r in $rows | ? { $_.X -lt 600 -and $_.Text -match '(?i)el[il1]m' }) {
  $parts = $r.Text -split '(?i)el[il1]m\S*', 2
  if (IsPlayer $parts[0].Trim(' (.-•')) { $killFrames[$r.T] = 1 + [int]$killFrames[$r.T] }
  # Every death, not just the first in the file: a later match must still cut before its own death.
  elseif ($parts.Count -gt 1 -and (IsPlayer $parts[1].Trim())) { $deaths += $r.T } }

# --- Our KNOCKs (downing a player, feed reads "<player> knocked out ..."). A knock is NOT an elim and is never
# clipped, but it tells a clean team-mode kill (a fight we knocked open) apart from mopping up someone else's knock.
$knockT = New-Object 'Collections.Generic.HashSet[double]'
foreach ($r in $rows | ? { $_.X -lt 600 -and $_.Text -match '(?i)kn[o0]ck' }) {
  $who = ($r.Text -split '(?i)kn[o0]ck', 2)[0]
  if (IsPlayer $who.Trim(' (.-•')) { [void]$knockT.Add($r.T) } }

# --- Screens that end the usable footage (death cam, placement, recap) and screens that start a match.
# Case-sensitive on purpose: the kill feed's lowercase "eliminated B..." must not read as the death screen.
$endMarks = $rows | ? { $_.X -gt 40 -and $_.Text -cmatch 'VOVPUCED|PLACED|ELIMINATED B|REPORT PLAYER|SPECTAT|Total Match XP|RETURN TO LOBBY|VICTORY ROYALE' } | % T | Sort-Object -Unique
$startMarks = $rows | ? { $_.Text -match '(?i)BATTLE BUS LAUNCHING|DOORS WILL OPEN|thanked the bus driver|EVERYBODY OFF' } | % T | Sort-Object -Unique
# The lobby's playlist button ("2; Zero Build, Duo", left side ~y700) is on screen only in the lobby, so it both
# ends the match before it and names the mode of the match after it.
$lobbyRx = '(?i)\b(zero build|build|battle royale|reload)\s*[,.]\s*(solo|duo|trio|squad)s?\b'
$lobbyRows = @($rows | ? { $_.X -lt 300 -and $_.Y -gt 640 -and $_.Y -lt 780 -and $_.Text -match $lobbyRx })
$lobbyT = @($lobbyRows | % T | Sort-Object -Unique)
# Mode comes ONLY from two exact places. A loose "SQUADS anywhere" search read the map screen's "PLAYERS (SQUADS)"
# counter label, "SQUAD MEMBERS" and quest text as the mode (2026-10-29: squads clips captioned "solos").
#   in match:  the map screen's playlist header, "ZERO BUILD - DUOS", top-left
#   lobby:     the playlist button above, read just before the match starts
$mapRx = '(?i)^\W*(?:(zero build|build|battle royale|reload)\s*-\s*)?(solo|duo|trio|squad)s?\W*$'
$mapRows = @($rows | ? { $_.X -lt 300 -and $_.Y -gt 150 -and $_.Y -lt 260 -and $_.Text -match $mapRx })
function ModeName([string]$text, [string]$rx) {
  $m = [regex]::Match($text, $rx); if (-not $m.Success) { return $null }
  $prefix = if ($m.Groups[1].Value -match '(?i)zero') { 'Zero Build ' } else { '' }
  $prefix + @{ solo = 'Solos'; duo = 'Duos'; trio = 'Trios'; squad = 'Squads' }[$m.Groups[2].Value.ToLower()] }

# Group start markers into matches; a match runs until the first end marker after it.
$games = @(); $last = -1e9
foreach ($t in $startMarks) { if ($t -gt $last + 120) { $games += [pscustomobject]@{ Start = $t } }; $last = $t }
if (-not $games -and $rows) { $games = @([pscustomobject]@{ Start = 0 }) }

# --- HUD kill counter: first short number in the digit column, only trusted when the players-left line follows it.
$hud = @{}
if ($HudTsv -and (Test-Path $HudTsv)) {
  foreach ($g in Read-Tsv $HudTsv | Group-Object T) {
    $nums = @($g.Group | Sort-Object Y | % { (($_.Text -replace '[oOD]', '0' -replace '[lI|!]', '1') -replace '\s', '') } | ? { $_ -match '^\d{1,3}$' })
    if ($nums.Count -ge 2 -and [int]$nums[0] -lt [int]$nums[1] -and [int]$nums[0] -lt 60) { $hud[[double]$g.Name] = [int]$nums[0] } } }

# Index = second, value = RMS dB.
$loud = @(if ($LoudCsv -and (Test-Path $LoudCsv)) { Get-Content $LoudCsv | % { [double]$_.Split(',')[1] } })

$out = @()
for ($m = 0; $m -lt $games.Count; $m++) {
  $start = $games[$m].Start; $limit = if ($m + 1 -lt $games.Count) { $games[$m + 1].Start } else { $Duration }
  $end = $endMarks | ? { $_ -gt $start -and $_ -lt $limit } | Select-Object -First 1
  $died = $deaths | ? { $_ -gt $start -and $_ -lt $limit } | Select-Object -First 1
  # Back in the lobby = this match is over, even if no death/recap screen was read (otherwise the next game, whose
  # bus screen OCR missed, got glued onto this one: 2026-10-29, one "match" with two different skins).
  $lobby = $lobbyT | ? { $_ -gt $start + 30 -and $_ -lt $limit } | Select-Object -First 1
  $stop = @($end, $died, $lobby, $limit | ? { $_ }) | Measure-Object -Minimum | % Minimum
  $safeEnd = [Math]::Max($start, $stop - 2.0)   # never show the frames leading into a death cam / recap

  # Kill events: clusters of frames where our feed line is visible; a cluster with 2 simultaneous lines = 2 kills.
  $kills = @(); $cl = $null
  foreach ($t in $killFrames.Keys | ? { $_ -ge $start -and $_ -lt $stop } | Sort-Object) {
    if (-not $cl -or $t - $cl.Last -gt 4) { if ($cl) { $kills += $cl.Times }; $cl = @{ Last = $t; Times = @($t); Max = $killFrames[$t] } }
    else { $cl.Last = $t; if ($killFrames[$t] -gt $cl.Max) { $cl.Max = $killFrames[$t]; $cl.Times += $t } } }
  if ($cl) { $kills += $cl.Times }

  # HUD total: the highest value seen in at least 3 frames, so a single misread can't inflate it.
  $hv = $hud.Keys | ? { $_ -ge $start -and $_ -lt $stop } | % { $hud[$_] } | Group-Object | ? Count -ge 3 | % { [int]$_.Name }
  $hudKills = if ($hv) { ($hv | Measure-Object -Maximum).Maximum } else { $null }

  # In-match map header wins (it can't be stale); else the last lobby button read in the 10 min before the bus.
  $modeName = $mapRows | ? { $_.T -ge $start - 30 -and $_.T -lt $stop } | % { ModeName $_.Text $mapRx } | Group-Object | Sort-Object Count -Descending | Select-Object -First 1 | % Name
  if (-not $modeName) { $modeName = $lobbyRows | ? { $_.T -lt $start -and $_.T -gt $start - 600 } | Select-Object -Last 1 | % { ModeName $_.Text $lobbyRx } }
  if ($modeName -isnot [string] -or -not $modeName.Trim()) { $modeName = $null }   # nothing readable must stay null, not an empty object
  # Solos has no knocks and no teammate plate (bottom-left squad list). Seeing either means the read is wrong: say
  # nothing rather than caption a team game "solos".
  $ourKnocks = @($knockT | ? { $_ -ge $start -and $_ -lt $stop }).Count
  $mates = @($rows | ? { $_.T -ge $start -and $_.T -lt $stop -and $_.X -lt 330 -and $_.Y -gt 760 -and $_.Y -lt 1010 -and $_.Text -match '[A-Za-z]{3}\S*[\d_]' -and -not (IsPlayer ($_.Text.Trim(' .-•&') -split '\s+')[0]) }).Count
  $teamSeen = $ourKnocks -gt 0 -or $mates -ge 4
  $modeConflict = $modeName -match 'Solos' -and $teamSeen
  if ($modeConflict) { $modeName = $null }

  # Placement: only if two independent reads of the banner agree.
  $pr = $PlacementReads.Keys | ? { $_ -ge $start -and $_ -lt $limit } | % { $PlacementReads[$_] } | Group-Object | ? Count -ge 2 | Sort-Object Count -Descending | Select-Object -First 1
  $victory = $rows | ? { $_.T -ge $start -and $_.T -lt $limit -and $_.Text -cmatch 'VICTORY ROYALE' }
  $placement = if ($victory) { 1 } elseif ($pr) { [int]$pr.Name } else { $null }

  # Separate real, earned elims from mop-ups. Clean = solo (no knock mechanic), OR a team-mode elim we knocked open
  # just before (we won that fight), OR mode unreadable with no knocks seen all game (an inferred solo). A team-mode
  # elim with no knock of ours in front of it is finishing a player someone else downed - detected, but not clipped.
  $mKnocks = @($knockT | ? { $_ -ge $start -and $_ -lt $stop } | Sort-Object)
  $soloMode = [bool]($modeName -match '(?i)solo'); $modeUnknown = -not $modeName
  $clean = @(); $finishes = 0
  foreach ($kt in $kills) {
    $knockedFirst = @($mKnocks | ? { $_ -ge $kt - 15 -and $_ -le $kt + 1 }).Count -gt 0
    if ($soloMode -or $knockedFirst -or ($modeUnknown -and -not $teamSeen)) { $clean += $kt } else { $finishes++ } }
  # Blur the victim name plate at EVERY elim and knock banner, even the mop-ups we don't clip, so no stranger's name leaks.
  $banners = @(@($kills) + @($mKnocks) | Sort-Object -Unique)

  $verified = $hudKills -ne $null -and $hudKills -eq $kills.Count
  $out += [pscustomobject]@{
    index = $m + 1; start = $start; end = $stop; safeEnd = $safeEnd; mode = $modeName
    kills = @($clean | % { [Math]::Round($_, 1) }); banners = @($banners | % { [Math]::Round($_, 1) })
    feedKills = $kills.Count; cleanKills = $clean.Count; finishes = $finishes; knocks = $mKnocks.Count
    hudKills = $hudKills; countVerified = $verified
    placement = $placement; diedAt = $died
    notes = @(
      if (-not $verified) { "kill count NOT verified (feed $($kills.Count), HUD $hudKills) - titles leave the number out" }
      if ($finishes) { "$finishes mop-up finish(es) of already-downed players left out - not clipped" }
      if ($modeConflict) { 'mode read as solos but knocks/teammates seen - mode left out' }
      elseif (-not $modeName) { 'mode unreadable' }
      if ($lobby -and $stop -eq $lobby) { 'cut at return to lobby (no death/recap screen read)' }
      if (-not $placement) { 'placement unreadable' }) } }

# --- Other players' names that land inside the vertical crop (teammate tags, "SPECTATING x", victim plates the fixed
# box misses). Names are harvested from places that only ever hold names (kill feed, party list, spectate banner), then
# every OCR line in the crop that contains one gets blurred for that half-second.
$names = New-Object 'Collections.Generic.HashSet[string]'
# Weapon/rarity/feed words show up next to names ("... with a Pump Shotgun"); they must never become "names" or every
# pickup label would get blurred.
$stop = 'SHOTGUN|PISTOL|RIFLE|SMG|SNIPER|PUMP|SWEEPER|TACTICAL|ASSAULT|UNCOMMON|COMMON|RARE|EPIC|LEGENDARY|MYTHIC|EXOTIC|THANKED|DRIVER|ELIMINATED|PICKAXE|GRENADE|STORM|DESTINATION|EXTRACTION|MARKED|EXPLOSION|DAMAGE|LAUNCHER|BOW|BLADE|HEAL|SHIELD|POTION|BANDAGE|MEDKIT|SPRITE|VEHICLE|FALL|BUS|SITE'
$stopN = [regex]('^(' + (($stop -split '\|' | % { Norm $_ }) -join '|') + ')')
function Add-Name([string]$s, [switch]$TagOnly) {
  $s = ($s -replace '\(\d+\)', '' -replace '(?i)\bwit?h\b.*$', '').Trim(' .,:;-•''"')
  foreach ($tok in $s -split '\s+') {
    $n = Norm $tok
    if ($n.Length -lt 5 -or $n -match '^\d+$' -or $stopN.IsMatch($n)) { continue }
    if ($TagOnly -and $tok -notmatch '[\d_\[]') { continue }
    # Wider tolerance than kill matching: blurring our own name by mistake is harmless, missing a stranger's isn't.
    if ((Lev $n.Substring(0, [Math]::Min($n.Length, $np.Length)) $np) -le 2) { continue }
    [void]$names.Add($n) } }
foreach ($r in $rows) {
  if ($r.X -lt 600 -and $r.Text -match '(?i)^(.+?)\s+el[il1]m\S*\s*(.*)$') { Add-Name $Matches[1]; Add-Name $Matches[2] }
  elseif ($r.X -lt 600 -and $r.Text -match '(?i)^(.+?)\s+(has thanked|marked|knocked|is down|joined)') { Add-Name $Matches[1] }
  elseif ($r.Text -match '(?i)SPECTATING[:\s]+(.+)$') { Add-Name $Matches[1] }
  elseif ($r.X -lt 330 -and $r.Y -gt 760 -and $r.Y -lt 1010 -and $r.Text -match '[A-Za-z]{3}') { Add-Name $r.Text -TagOnly } }
$masks = @()
if ($names.Count) {
  $rx = [regex](($names | Sort-Object Length -Descending | % { [regex]::Escape($_) }) -join '|')
  foreach ($r in $rows | ? { $_.X -lt 1218 -and $_.X + $_.W -gt 610 -and $_.Text.Length -ge 5 }) {
    if ($rx.IsMatch((Norm $r.Text)) -or $r.Text -match '(?i)SPECTATING') {
      $masks += [pscustomobject]@{ x = [int]($r.X - 12); y = [int]($r.Y - 10); w = [int]($r.W + 24); h = [int]($r.H + 20); t0 = $r.T - 0.6; t1 = $r.T + 0.9; text = $r.Text } } }   # frames are 0.5 s apart: cover the gap before the first read
  # Merge repeats of the same box (a teammate tag stays put for seconds) so the filter graph stays small.
  $merged = @(); foreach ($m in $masks | Sort-Object t0) {
    $p = $merged | ? { [Math]::Abs($_.x - $m.x) -lt 60 -and [Math]::Abs($_.y - $m.y) -lt 40 -and $m.t0 -le $_.t1 + 1 } | Select-Object -Last 1
    if ($p) { $p.t1 = [Math]::Max($p.t1, $m.t1); $p.x = [Math]::Min($p.x, $m.x); $p.w = [Math]::Max($p.w, $m.w) } else { $merged += $m } }
  $masks = $merged }

# Per-second loudness rides along so the renderer can pick where each fight starts.
[pscustomobject]@{ matches = $out; loud = $loud; names = @($names); nameMasks = @($masks) } | ConvertTo-Json -Depth 5

