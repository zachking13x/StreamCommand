# Fortnite kill-highlight pipeline

Turns new Fortnite recordings into vertical TikToks for the Stream Command account. **It never posts.** Each run
leaves a dated queue on the Desktop (`StreamCommand_TikTok_Queue\<date>\`) with the videos, covers, sub-10 MB
upload copies and a `POSTS.md`. Zach watches the queue and approves each batch before anything is scheduled.

```powershell
powershell -ExecutionPolicy Bypass -File highlights.ps1          # only footage not seen before
powershell -ExecutionPolicy Bypass -File highlights.ps1 -Force   # reprocess everything
```

## Sources (in `[Environment]::GetFolderPath('MyVideos')`)
| Folder | Kind | Notes |
|---|---|---|
| `NVIDIA\Fortnite\*.mp4` | nvidia | main source; one mixed game-audio track, mic off in the NVIDIA app |
| `*.mp4`, `*.mkv` at the top level | obs | OBS's default output folder |
| `Stream Command Clips\**` | streamcommand | only these may be described as recorded with Stream Command; already-vertical shorts are skipped |
| `Twitch VODs\*.mp4` | twitch | filled by hand with yt-dlp, only with Zach's OK; may include his mic and on-stream music, so it's flagged for review |

`highlights-state.json` remembers processed files (path + size + mtime), so each weekly run picks up only new footage.

## How kills are found (all read off the screen, nothing guessed)
- `ocr-frames.ps1` runs Windows' built-in OCR (`Windows.Media.Ocr`) on 2 fps frames.
- **Kill feed** (left column): `SM3AG01 ... eliminated ...` lines. OCR glyph confusions (5/S, B/3, O/0, V/M, C/G)
  are normalized and fuzzy-matched. Two of our lines visible at once = a double kill.
- **Clean elims only get clipped** (Zach, 2026-10-28: a knock, or finishing a player someone else already downed,
  is not an actual elim). Our knocks (`SM3AG01 ... knocked out`) are read too. An elim counts as clean when the mode
  is Solos (no knock mechanic), when we knocked someone in the 15 s before it (we won that fight), or when the mode is
  unreadable and no knocks were seen all game (an inferred solo). Other team-mode elims are mop-ups: listed in the run
  summary, never clipped. The renderer still blurs the victim plate at every elim and knock banner, mop-ups included.
- **HUD kill counter** (top right, thresholded crop): the highest value seen in at least 3 frames.
  The count goes into titles only when the feed and the HUD agree (`countVerified`). Otherwise the title says "kill
  montage" or "one clean elim".
- **Mode** comes from exactly two places (2026-10-29 fix): the map screen's top-left playlist header
  ("ZERO BUILD - DUOS", read during the match), or else the lobby's playlist button ("2; Zero Build, Duo", left side
  ~y700) read in the 10 min before the bus. Searching for "SQUADS" anywhere was wrong: the map screen's player counter
  is labelled "PLAYERS (SQUADS)" in every mode, so duos games came out "Squads". If the mode says Solos but our knocks
  or a teammate plate (bottom-left squad list) show up, the mode is dropped rather than captioned wrong.
- **Match boundaries**: a match starts at the bus screen and ends at the first death (every death in the file, not
  just the first), recap/placement screen, or **return to the lobby** (the lobby playlist button). Without the lobby
  cut, a game whose bus screen OCR missed got glued onto the previous one.
- **Placement**: OCR reads the italic "YOU PLACED" banner as `VOVPUCED`. The banner is deskewed and thresholded, and
  `#N` is used only when two reads agree.
- **Fights**: per-second RMS (ffmpeg astats). A single clip starts where the gunfire before the kill starts, 9-16 s back.
- Tuned on `Fortnite 2026.10.14 - 11.56.54.02.mp4`: kills at 206 / 248.5 / 338.5 s, HUD 3, placed #10, all checked by eye.

## Safety rules the renderer enforces
- **Hard cut 2 s before** any death cam, placement, recap, spectate or "REPORT PLAYER" screen. "Eliminated by <name>"
  never makes it into a video.
- The victim name plate under the "ELIMINATION!" banner (1080p box 830,815 260x48) is **blurred** from 0.4 s before
  each kill until 5 s after.
- The vertical crop (x 610-1218) excludes the kill feed and party list. Each video still gets a human look.
- Game audio only, loudnorm -14 LUFS. Emote or lobby music can't be detected automatically, so POSTS.md tells Zach to listen.
- End card: streamcommand.netlify.app plus "Not affiliated with or endorsed by Epic Games" (from `overlays.ps1`).
- Posting checklist (in every POSTS.md): TikTok "Your brand" disclosure ON, Commercial Music Library sounds only.

## Files
- `highlights.ps1`: orchestrator (sources, frames, OCR, analysis, clip windows, render, POSTS.md, state)
- `analyze-match.ps1`: OCR + loudness -> matches (mode, kill times, verified count, placement, safe cut)
- `render-highlight.ps1`: segments -> 1080x1920 video with blur mask, hook, pill, chips, end card; cover; 2-pass upload copy
- `highlight-overlays.ps1`: hook title and "ELIM n/N" chip PNGs in the overlays.ps1 style
- `ocr-frames.ps1`: Windows OCR over a folder of frames -> TSV with line boxes

Gotchas: ffmpeg 9 uses `-/filter_complex <file>` (filter scripts avoid PowerShell 5.1 quoting bugs), and xfade needs
both inputs at `settb=1/30`. In PowerShell, `-match` is case-insensitive, so the feed's "eliminated B..." matched the
death screen's "ELIMINATED BY" until the end markers switched to `-cmatch`. Never name a helper `rd`, `rm`, `del`,
`ls`, etc.: PowerShell aliases beat functions, so `function rd {...}; rd $file` runs Remove-Item (it deleted two OCR
caches on 2026-10-29).
