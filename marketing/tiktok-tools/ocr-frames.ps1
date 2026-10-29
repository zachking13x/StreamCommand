# OCR every frame jpg in a folder with Windows' built-in OCR (Windows.Media.Ocr) and write one TSV row
# per recognized line: frame, x, y, w, h, text. Frames are named f_00001.jpg... so the caller maps
# frame -> seconds from its own sample rate.
param([string]$Dir, [string]$OutTsv)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, [Type]$t) { $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); $task.Wait(); $task.Result }
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
$sb = New-Object Text.StringBuilder
foreach ($f in Get-ChildItem $Dir -Filter *.jpg | Sort-Object Name) {
  $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($f.FullName)) ([Windows.Storage.StorageFile])
  $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  $dec = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
  $bmp = Await ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
  $res = Await ($engine.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])
  foreach ($l in $res.Lines) {
    # A line's box is the union of its word boxes; position is what tells a banner from the kill feed.
    $r = $l.Words | % { $_.BoundingRect }
    $x = ($r | % X | Measure-Object -Minimum).Minimum; $y = ($r | % Y | Measure-Object -Minimum).Minimum
    $x2 = ($r | % { $_.X + $_.Width } | Measure-Object -Maximum).Maximum; $y2 = ($r | % { $_.Y + $_.Height } | Measure-Object -Maximum).Maximum
    $t = $l.Text -replace "[`t`r`n]", ' '
    [void]$sb.AppendLine(("{0}`t{1:0}`t{2:0}`t{3:0}`t{4:0}`t{5}" -f $f.BaseName, $x, $y, ($x2 - $x), ($y2 - $y), $t)) }
  $stream.Dispose(); $bmp.Dispose() }
[IO.File]::WriteAllText($OutTsv, $sb.ToString(), [Text.UTF8Encoding]::new($false))
