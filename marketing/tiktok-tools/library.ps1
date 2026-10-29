# clips-library.json: every rendered clip, its two captions, and whether/when/how it was posted. Dot-sourced by
# highlights.ps1 (adds clips) and posts.ps1 (picks and marks posts). Posted entries are never re-picked.
$LibFile = Join-Path $PSScriptRoot 'clips-library.json'

# PS 5.1's ConvertFrom-Json emits a JSON array as ONE object; the extra ForEach unrolls it into entries.
function Read-Library { $l = New-Object Collections.ArrayList; if (Test-Path $LibFile) { (Get-Content $LibFile -Raw -Encoding UTF8 | ConvertFrom-Json) | % { $_ } | % { [void]$l.Add($_) } }; , $l }
function Write-Library($lib) { [IO.File]::WriteAllText($LibFile, (ConvertTo-Json @($lib) -Depth 5), [Text.UTF8Encoding]::new($false)) }

function Add-LibraryClip($lib, $c, $r, [string]$srcKind, $srcFile, [string]$mode) {
  $tag = if ($mode -match 'Zero Build') { '#zerobuild ' } else { '' }
  $entry = [pscustomobject]@{
    id = $c.Name; kind = $c.Kind; mode = $mode; srcKind = $srcKind; source = $srcFile.FullName
    recorded = $srcFile.LastWriteTime.ToString('o'); created = (Get-Date).ToString('o')
    master = $r.video; upload = $r.upload; cover = $r.upload -replace '-upload\.mp4$', '-cover.jpg'; uploadMB = $r.uploadMB; seconds = $r.seconds
    # Never say the footage was made with Stream Command: only the app's own Clips are, and those aren't used here.
    captionPromo = "$($c.What). I'm a solo dev and I built Stream Command so I can run my Twitch + OBS stream without alt-tabbing out mid-fight. link in bio #fortnite $tag#fortniteclips #twitchstreamer #obs #streamingsetup"
    captionPlain = "$($c.What) #fortnite $tag#fortniteclips #gaming"
    # Unbranded cuts (no pill / end card) are plain Fortnite videos: no "Your brand" disclosure, Creator Rewards eligible.
    branded = -not $c.Plain
    postedAs = $null; postedAt = $null }
  $old = $lib | ? { $_.id -eq $entry.id } | Select-Object -First 1
  # A re-render keeps the post history: re-rendering must never make a posted clip look new.
  if ($old) { $entry.postedAs = $old.postedAs; $entry.postedAt = $old.postedAt; $lib.Remove($old) }
  [void]$lib.Add($entry); $entry }
