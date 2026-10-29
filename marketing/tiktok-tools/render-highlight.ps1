# Renders one vertical highlight (1080x1920 @30) from one or more gameplay segments of a 16:9 Fortnite source:
# center crop like render.sh, victim name plate under the "ELIMINATION!" banner blurred, hook + brand pill +
# optional ELIM n/N chips, crossfade into the shared end card. Also writes a cover jpg and a <10 MB upload copy.
param(
  [string]$Src,
  [object[]]$Segments,      # @{ s; e } in source time; a segment may carry its own src/kills/masks (multi-match montages)
  [double[]]$Kills,         # every kill time in the source, so any banner that lands in a segment gets masked
  [object[]]$Masks = @(),   # other players' names found by OCR: @{ x; y; w; h; t0; t1 } in 1080p source px/sec
  [string]$Assets,          # folder with pill.png + endcard.png (overlays.ps1) and <Name>-hook/chip pngs
  [string]$Name,
  [string]$OutDir,
  [switch]$Chips,
  # Fortnite-only cut: no Stream Command pill or end card. TikTok Creator Rewards won't pay on promotional posts,
  # so the 60 s+ Fortnite videos have to carry no brand at all to ever earn.
  [switch]$Plain
)
$ErrorActionPreference = 'Stop'
$ff = 'C:\Users\Zachk\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe'
$inv = [Globalization.CultureInfo]::InvariantCulture
function F([double]$v) { $v.ToString('0.###', $inv) }
$endLen = 3; $xf = 0.4
# Name plate box in 1080p source pixels (centered under the banner, measured on the Oct 14 recording).
$mx = 830; $my = 815; $mw = 260; $mh = 48

$ffa = @('-v', 'error', '-y'); $fc = New-Object Text.StringBuilder; $t0 = 0.0; $chipWin = @()
for ($i = 0; $i -lt $Segments.Count; $i++) {
  $seg = $Segments[$i]; $s = [double]$seg.s; $d = [double]$seg.e - $s
  $segSrc = if ($seg.src) { $seg.src } else { $Src }
  $segKills = if ($seg.kills) { @($seg.kills) } else { $Kills }
  $segMasks = if ($seg.masks) { @($seg.masks) } else { $Masks }
  $ffa += '-ss', (F $s), '-t', (F $d), '-i', $segSrc
  # Kill times come from the kill feed, which lags the "ELIMINATION!" banner: on a squad wipe the victim's name was
  # readable ~1 s before a 0.4 s lead kicked in (2026-10-26). Blurring empty screen early costs nothing.
  $win = @($segKills | ? { $_ -gt $s - 6 -and $_ -lt $s + $d } | % { "between(t,{0},{1})" -f (F ($_ - $s - 2.5)), (F ($_ - $s + 5)) })
  $en = if ($win) { $win -join '+' } else { '0' }
  [void]$fc.AppendLine("[${i}:v]scale=1920:1080,fps=30,setpts=PTS-STARTPTS,split[a$i][b$i];")
  [void]$fc.AppendLine("[b$i]crop=${mw}:${mh}:${mx}:${my},boxblur=luma_radius=12:luma_power=4:chroma_radius=6:chroma_power=4[bl$i];")
  [void]$fc.AppendLine("[a$i][bl$i]overlay=${mx}:${my}:enable='$en'[m${i}_0];")
  # One blur box per OCR'd name that overlaps this segment.
  $cur = "m${i}_0"; $j = 0
  foreach ($m in $segMasks | ? { $_.t1 -gt $s -and $_.t0 -lt $s + $d }) {
    $bx = [Math]::Max(0, [int]$m.x); $by = [Math]::Max(0, [int]$m.y)
    $bw = [Math]::Min(1920 - $bx, [int]$m.w); $bh = [Math]::Min(1080 - $by, [int]$m.h)
    if ($bw -lt 8 -or $bh -lt 8) { continue }
    $lr = [Math]::Min(10, [int][Math]::Floor([Math]::Min($bw, $bh) / 2) - 1); $cr = [Math]::Max(0, [Math]::Min(5, [int][Math]::Floor([Math]::Min($bw, $bh) / 4) - 1))
    $j++; [void]$fc.AppendLine("[$cur]split[p${i}_$j][q${i}_$j];[q${i}_$j]crop=${bw}:${bh}:${bx}:${by},boxblur=luma_radius=${lr}:luma_power=4:chroma_radius=${cr}:chroma_power=4[r${i}_$j];[p${i}_$j][r${i}_$j]overlay=${bx}:${by}:enable='between(t,$(F ($m.t0 - $s)),$(F ($m.t1 - $s)))'[m${i}_$j];")
    $cur = "m${i}_$j" }
  [void]$fc.AppendLine("[$cur]crop=608:1080:610:0,scale=1080:1920:flags=lanczos,setsar=1,eq=saturation=1.08:contrast=1.03,format=yuv420p[v$i];")
  [void]$fc.AppendLine("[${i}:a]aresample=48000,asetpts=PTS-STARTPTS,afade=t=in:d=0.05,afade=t=out:st=$(F ($d - 0.08)):d=0.08[s$i];")
  $chipWin += , @($t0, ($t0 + $d)); $t0 += $d }
$n = $Segments.Count; $dur = $t0
$ix = $n; $ffa += '-loop', '1', '-t', (F $dur), '-i', "$Assets\$Name-hook.png"; $hook = $ix++
if (-not $Plain) { $ffa += '-loop', '1', '-t', (F $dur), '-i', "$Assets\pill.png"; $pill = $ix++ }
$chipIx = @(); if ($Chips) { for ($c = 1; $c -le $n; $c++) { $ffa += '-loop', '1', '-t', (F $dur), '-i', "$Assets\$Name-chip$c.png"; $chipIx += $ix++ } }
if (-not $Plain) {
  $ffa += '-loop', '1', '-t', $endLen, '-i', "$Assets\endcard.png"; $end = $ix++
  $ffa += '-f', 'lavfi', '-t', $endLen, '-i', 'anullsrc=r=48000:cl=stereo'; $sil = $ix++ }

[void]$fc.AppendLine(((0..($n - 1) | % { "[v$_][s$_]" }) -join '') + "concat=n=${n}:v=1:a=1[cv][ca];")
[void]$fc.AppendLine("[${hook}:v]format=rgba,fade=in:st=0:d=0.25:alpha=1,fade=out:st=2.9:d=0.4:alpha=1[h];")
[void]$fc.AppendLine("[cv][h]overlay=0:150:enable='lt(t,3.4)'[o0];")
$last = 'o0'
if (-not $Plain) {
  [void]$fc.AppendLine("[${pill}:v]format=rgba,fade=in:st=1.0:d=0.4:alpha=1[p];[o0][p]overlay=40:1580:enable='gte(t,1)'[o1];")
  $last = 'o1' }
for ($c = 0; $c -lt $chipIx.Count; $c++) {
  $a = $chipWin[$c][0]; $b = $chipWin[$c][1]
  [void]$fc.AppendLine("[$($chipIx[$c]):v]format=rgba[c$c];[$last][c$c]overlay=(W-w)/2:540:enable='between(t,$(F ($a + 0.2)),$(F ($b - 0.1)))'[k$c];"); $last = "k$c" }
if ($Plain) {
  [void]$fc.AppendLine("[$last]fps=30,format=yuv420p,fade=t=out:st=$(F ($dur - 0.5)):d=0.5[v];")
  [void]$fc.AppendLine("[ca]loudnorm=I=-14:TP=-1.5:LRA=11,aresample=48000,afade=t=out:st=$(F ($dur - 0.8)):d=0.8[a]") }
else {
  [void]$fc.AppendLine("[${end}:v]fps=30,scale=1080:1920,setsar=1,settb=1/30,format=yuv420p[e];")
  [void]$fc.AppendLine("[$last]fps=30,settb=1/30,format=yuv420p[vv];[vv][e]xfade=transition=fade:duration=${xf}:offset=$(F ($dur - $xf))[v];")
  [void]$fc.AppendLine("[ca]loudnorm=I=-14:TP=-1.5:LRA=11,aresample=48000,afade=t=out:st=$(F ($dur - 0.8)):d=0.8[ga];[ga][${sil}:a]acrossfade=d=${xf}[a]") }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$fcFile = Join-Path $env:TEMP "hl-$Name.fc.txt"; [IO.File]::WriteAllText($fcFile, $fc.ToString())
$out = Join-Path $OutDir "$Name.mp4"
& $ff @ffa '-/filter_complex' $fcFile -map '[v]' -map '[a]' -c:v libx264 -preset slow -crf 18 -profile:v high -pix_fmt yuv420p -r 30 -c:a aac -b:a 192k -ar 48000 -movflags +faststart $out
if ($LASTEXITCODE) { throw "render failed: $Name" }

# Cover = a frame while the hook is up. Upload copy: two-pass to ~9.3 MB so it clears the 10 MB upload tool limit.
& $ff -v error -y -ss 1.5 -i $out -frames:v 1 -q:v 2 (Join-Path $OutDir "$Name-cover.jpg")
$total = if ($Plain) { $dur } else { $dur + $endLen - $xf }
$vk = [int]((9.3 * 8 * 1024) / $total - 128)
# Long cuts (weekly montage) get ~1 Mbps under the 10 MB cap; at that rate 720p looks clean where 1080p goes blocky.
$sc = if ($total -gt 35) { @('-vf', 'scale=720:1280:flags=lanczos') } else { @() }
$up = Join-Path $OutDir "$Name-upload.mp4"; $log = Join-Path $env:TEMP "hl-$Name-2pass"
& $ff -v error -y -i $out @sc -c:v libx264 -preset slow -b:v "${vk}k" -pass 1 -passlogfile $log -an -f mp4 NUL
& $ff -v error -y -i $out @sc -c:v libx264 -preset slow -b:v "${vk}k" -pass 2 -passlogfile $log -c:a aac -b:a 128k -movflags +faststart $up
Remove-Item "$log*" -ErrorAction SilentlyContinue
[pscustomobject]@{ name = $Name; video = $out; upload = $up; uploadMB = [Math]::Round((Get-Item $up).Length / 1MB, 2); seconds = [Math]::Round($total, 1) }

