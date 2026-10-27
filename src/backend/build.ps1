# language: PowerShell, file: build.ps1, target: Windows 11 / .NET 8 WPF
$ErrorActionPreference='Stop'
if(-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'glass.ico'))) {
    Add-Type -AssemblyName System.Drawing
    $taskBitmap=[Drawing.Bitmap]::new(64,64)
    $taskGraphics=[Drawing.Graphics]::FromImage($taskBitmap)
    $taskGraphics.SmoothingMode='AntiAlias'
    $taskGraphics.Clear([Drawing.Color]::Transparent)
    $taskPoints=@(@(3,3,58,58,16),@(13,14,25,33,7),@(28,20,24,30,7))
    foreach($taskShape in $taskPoints) {
        $taskX,$taskY,$taskW,$taskH,$taskR=$taskShape
        $taskPath=[Drawing.Drawing2D.GraphicsPath]::new()
        $taskD=$taskR*2
        $taskPath.AddArc($taskX,$taskY,$taskD,$taskD,180,90);$taskPath.AddArc($taskX+$taskW-$taskD,$taskY,$taskD,$taskD,270,90)
        $taskPath.AddArc($taskX+$taskW-$taskD,$taskY+$taskH-$taskD,$taskD,$taskD,0,90);$taskPath.AddArc($taskX,$taskY+$taskH-$taskD,$taskD,$taskD,90,90);$taskPath.CloseFigure()
        $taskBrush=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new($taskX,$taskY),[Drawing.Point]::new($taskX+$taskW,$taskY+$taskH),[Drawing.Color]::FromArgb(240,112,205,235),[Drawing.Color]::FromArgb(240,37,64,108))
        $taskGraphics.FillPath($taskBrush,$taskPath);$taskPen=[Drawing.Pen]::new([Drawing.Color]::FromArgb(200,231,251,255),1.2);$taskGraphics.DrawPath($taskPen,$taskPath)
        $taskPen.Dispose();$taskBrush.Dispose();$taskPath.Dispose()
    }
    $taskIcon=[Drawing.Icon]::FromHandle($taskBitmap.GetHicon());$taskStream=[IO.File]::Create((Join-Path $PSScriptRoot 'glass.ico'));$taskIcon.Save($taskStream);$taskStream.Dispose();$taskIcon.Dispose();$taskGraphics.Dispose();$taskBitmap.Dispose()
}
& dotnet publish (Join-Path $PSScriptRoot 'Panneaux.Glass.csproj') -c Release -o $PSScriptRoot --nologo -v minimal
if($LASTEXITCODE -ne 0){throw 'Compilation Glass échouée.'}
