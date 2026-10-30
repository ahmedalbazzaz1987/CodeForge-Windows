# Opens CodeForge in several situations and saves a screenshot of each (used by CI; works on any Windows PC).
param([string]$App = "publish/CodeForge/CodeForge.exe", [string]$Out = "screenshots")
$ErrorActionPreference = "Stop"
$App = (Resolve-Path $App).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$work = Join-Path $env:RUNNER_TEMP "cf-samples"
if (-not $env:RUNNER_TEMP) { $work = Join-Path $env:TEMP "cf-samples" }
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null

# Sample projects: a VB.NET Windows Forms app, and a VB.NET console app with a mistake in it.
dotnet new winforms -lang VB -o "$work\WinFormsApp1" -n WinFormsApp1 | Out-Null
dotnet new console -lang VB -o "$work\BrokenVB" -n BrokenVB | Out-Null
Set-Content "$work\BrokenVB\Program.vb" "Imports System`r`n`r`nModule Program`r`n    Sub Main(args As String())`r`n        Dim total As Integer = 5`r`n        Console.WriteLine(totl)`r`n    End Sub`r`nEnd Module`r`n"
Set-Content "$work\hello.py" "name = 'CodeForge'`r`nprint(f'Hello from {name}!')`r`n"

$failed = @()
function Shot([string]$Name, [hashtable]$Env) {
    $settings = Join-Path $work "settings-$Name"
    New-Item -ItemType Directory -Force $settings | Out-Null
    $vars = @{ CF_SNAPSHOT = (Join-Path $Out "$Name.png"); CF_SETTINGS_DIR = $settings; CF_FRESH = "1"; CF_NO_BROWSER = "1" } + $Env
    foreach ($k in $vars.Keys) { Set-Item "env:$k" $vars[$k] }
    $p = Start-Process -FilePath $App -PassThru
    if (-not $p.WaitForExit(120000)) { $p.Kill(); $script:failed += "$Name (timed out)" }
    elseif ($p.ExitCode -ne 0) { $script:failed += "$Name (exit code $($p.ExitCode))" }
    foreach ($k in $vars.Keys) { Remove-Item "env:$k" }
    if (-not (Test-Path (Join-Path $Out "$Name.png"))) { $script:failed += "$Name (no image)" }
    Write-Host "$Name done"
}

Shot "1-start-page" @{}
Shot "2-winforms-vb" @{ CF_OPEN = "$work\WinFormsApp1;$work\WinFormsApp1\Form1.vb" }
Shot "3-build-errors" @{ CF_OPEN = "$work\BrokenVB;$work\BrokenVB\Program.vb"; CF_ACTIONS = "build"; CF_DELAY = "45" }
Shot "4-dark-python-run" @{ CF_OPEN = "$work\hello.py"; CF_ACTIONS = "dark,start"; CF_DELAY = "12" }
Shot "5-blue-terminal" @{ CF_OPEN = "$work\WinFormsApp1;$work\WinFormsApp1\Form1.Designer.vb"; CF_ACTIONS = "blue,terminal"; CF_DELAY = "10" }
Shot "6-new-project" @{ CF_ACTIONS = "newproject"; CF_DELAY = "5" }

# The Windows Forms designer: build a sign-in form in VB.NET, add a Click handler, save, and check that it compiles.
dotnet new winforms -lang VB -o "$work\LoginVB" -n LoginVB | Out-Null
Shot "7-designer-vb" @{ CF_OPEN = "$work\LoginVB;$work\LoginVB\Form1.vb"; CF_ACTIONS = "design,select:form,set:Text=Sign in,set:ClientSize=420;260,add:Label:24:28,set:Text=User name:,add:TextBox:130:24,add:Label:24:72,set:Text=Password:,add:TextBox:130:68,set:PasswordChar=*,add:Button:130:120,set:Text=Sign in,handler,design,saveall" }
# The same in C#, with a container (a GroupBox with a RadioButton inside), a CheckBox and a DataGridView.
dotnet new winforms -o "$work\FormsCS" -n FormsCS | Out-Null
Shot "8-designer-cs" @{ CF_OPEN = "$work\FormsCS;$work\FormsCS\Form1.cs"; CF_ACTIONS = "dark,design,add:GroupBox:20:20,add:RadioButton:44:56,add:CheckBox:300:24,add:DataGridView:20:170,select:checkBox1,handler,design,saveall" }
foreach ($project in "LoginVB", "FormsCS") {
    Write-Host "== $project designer file"
    Get-Content (Get-ChildItem "$work\$project" -Filter "Form1.Designer.*").FullName
    Write-Host "== $project code file"
    Get-Content (Get-ChildItem "$work\$project" -Filter "Form1.*" | Where-Object { $_.Name -notlike "*.Designer.*" }).FullName
    dotnet build "$work\$project" -nologo -v q
    if ($LASTEXITCODE -ne 0) { $failed += "$project (the designer's code doesn't compile)" }
}

if (Test-Path "$env:APPDATA\CodeForge\errors.log") { Get-Content "$env:APPDATA\CodeForge\errors.log" }
Get-ChildItem $work -Filter errors.log -Recurse | ForEach-Object { Write-Host "== $($_.FullName)"; Get-Content $_.FullName }
if ($failed.Count -gt 0) { throw "Screenshots failed: $($failed -join ', ')" }
