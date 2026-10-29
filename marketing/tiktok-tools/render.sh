#!/usr/bin/env bash
# Renders one vertical TikTok video: Fortnite segment (center crop) + hook + brand pill +
# shortcut callout + end card. Usage: render.sh <start_s> <dur_s> <hook.png> <out.mp4>
set -euo pipefail
W="$(cd "$(dirname "$0")" && pwd)"
FF=/c/Users/Zachk/AppData/Local/Microsoft/WinGet/Links/ffmpeg.exe
SRC="C:/Users/Zachk/Videos/NVIDIA/Fortnite/Fortnite 2026.10.14 - 11.56.54.02.mp4"
START=$1; DUR=$2; HOOK=$3; OUT=$4
END=3                                  # end card length
XF=0.4                                 # crossfade into the end card
OFF=$(awk "BEGIN{print $DUR-$XF}")
AOUT=$(awk "BEGIN{print $DUR-0.8}")

"$FF" -v error -y \
  -ss "$START" -t "$DUR" -i "$SRC" \
  -loop 1 -t "$DUR" -i "$W/$HOOK" \
  -loop 1 -t "$DUR" -i "$W/pill.png" \
  -loop 1 -t "$DUR" -i "$W/callout.png" \
  -loop 1 -t "$END" -i "$W/endcard.png" \
  -f lavfi -t "$END" -i "anullsrc=r=48000:cl=stereo" \
  -filter_complex "
    [0:v]fps=30,crop=608:1080:610:0,scale=1080:1920:flags=lanczos,setsar=1,eq=saturation=1.08:contrast=1.03[g];
    [1:v]format=rgba,fade=in:st=0:d=0.25:alpha=1,fade=out:st=2.9:d=0.4:alpha=1[h];
    [2:v]format=rgba,fade=in:st=1.0:d=0.4:alpha=1[p];
    [3:v]format=rgba,scale=900:-1,fade=in:st=4.0:d=0.3:alpha=1,fade=out:st=6.6:d=0.4:alpha=1[c];
    [g][h]overlay=0:150:enable='lt(t,3.4)'[v1];
    [v1][p]overlay=40:1430:enable='gte(t,1)'[v2];
    [v2][c]overlay=(W-w)/2:720:enable='between(t,4,7)'[v3];
    [4:v]fps=30,scale=1080:1920,setsar=1,format=yuv420p[e];
    [v3]format=yuv420p[v4];
    [v4][e]xfade=transition=fade:duration=$XF:offset=$OFF[v];
    [0:a]aresample=48000,loudnorm=I=-14:TP=-1.5:LRA=11,afade=t=out:st=$AOUT:d=0.8[ga];
    [ga][5:a]acrossfade=d=$XF[a]" \
  -map "[v]" -map "[a]" \
  -c:v libx264 -preset slow -crf 18 -profile:v high -pix_fmt yuv420p -r 30 \
  -c:a aac -b:a 192k -ar 48000 -movflags +faststart \
  "$W/$OUT"
echo "rendered $OUT"
