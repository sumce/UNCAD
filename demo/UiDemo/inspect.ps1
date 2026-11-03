$ErrorActionPreference = 'Continue'
$dir = Join-Path $env:USERPROFILE '.nuget\packages\wpf-ui\4.0.2\lib\net472'
$a = [Reflection.Assembly]::LoadFrom((Join-Path $dir 'Wpf.Ui.dll'))
$f = $a.GetType('Wpf.Ui.Controls.FluentWindow')
'--- FluentWindow own properties ---'
$f.GetProperties([Reflection.BindingFlags]'Public,Instance,DeclaredOnly') | ForEach-Object { $_.Name + ' : ' + $_.PropertyType.Name }
'--- WindowBackdropType values ---'
$e = $a.GetType('Wpf.Ui.Controls.WindowBackdropType')
if ($e) { [Enum]::GetNames($e) -join ', ' }
'--- attached props on WindowBackdrop ---'
$wb = $a.GetType('Wpf.Ui.Controls.WindowBackdrop')
if ($wb) { $wb.GetFields([Reflection.BindingFlags]'Public,Static') | Where-Object { $_.Name -match 'Background' } | ForEach-Object { $_.Name } }
