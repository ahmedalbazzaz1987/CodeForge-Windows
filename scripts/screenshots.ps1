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
    if (-not $p.WaitForExit(180000)) { $p.Kill(); $script:failed += "$Name (timed out)" }
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
    if ($project -eq "LoginVB" -and -not (Select-String -Path "$work\LoginVB\Form1.Designer.vb" -SimpleMatch 'TextBox2.PasswordChar = "*"c' -Quiet)) { $failed += "LoginVB (PasswordChar wasn't saved)" }
    if ($LASTEXITCODE -ne 0) { $failed += "$project (the designer's code doesn't compile)" }
}

# Find in Files, Replace in Files, the Task List, snippets, bookmarks and navigation.
$demo = "$work\ToolsDemo"
New-Item -ItemType Directory -Force $demo | Out-Null
Set-Content "$demo\first.py" "# TODO: read the total from a file`r`ntotal = 1`r`nprint(total)`r`n"
Set-Content "$demo\second.py" "# FIXME: rename this`r`noldname = 2`r`nprint(oldname)`r`nnote = 'TODO not a task'`r`n"
Set-Content "$demo\app.cs" "class App`r`n{`r`n}`r`n"
Shot "10-gray-task-list" @{ CF_OPEN = "$demo;$demo\first.py"; CF_ACTIONS = "gray,tasks"; CF_DELAY = "8" }
Shot "11-midnight-find" @{ CF_OPEN = "$demo;$demo\first.py"; CF_ACTIONS = "midnight,find:total"; CF_DELAY = "8" }

# Replace in a closed file and in an open one (saved afterwards).
Shot "12-replace" @{ CF_OPEN = "$demo;$demo\first.py"; CF_ACTIONS = "replace:oldname=>newname,replace:total=>sum,saveall" }
if (-not (Select-String -Path "$demo\second.py" -SimpleMatch "newname = 2" -Quiet)) { $failed += "Replace in Files (closed file)" }
if (-not (Select-String -Path "$demo\first.py" -SimpleMatch "sum = 1" -Quiet)) { $failed += "Replace in Files (open file)" }

# A snippet from Tab; a bookmark that follows its line when two lines go in above it.
Shot "13-snippet-bookmark" @{ CF_OPEN = "$demo;$demo\app.cs"; CF_ACTIONS = "goto:3,togglebookmark,goto:1,type:\n\n,goto:1,type:prop\t,saveall" }
if (-not (Select-String -Path "$demo\app.cs" -SimpleMatch "public int MyProperty { get; set; }" -Quiet)) { $failed += "Snippet expansion" }
$saved = Get-Content "$work\settings-13-snippet-bookmark\settings.json" -Raw | ConvertFrom-Json
$lines = @($saved.Bookmarks."$demo\app.cs")
if ($lines -notcontains 5) { $failed += "Bookmark didn't follow its line (lines: $($lines -join ','))" }

# Navigate Backward and Forward between two files.
$report = "$work\navigation.txt"
Shot "14-navigation" @{ CF_OPEN = "$demo;$demo\first.py;$demo\second.py"; CF_ACTIONS = "report,back,report,forward,report"; CF_REPORT = $report }
$visited = (Get-Content $report | ForEach-Object { ($_ -split ":")[0] }) -join ","
if ($visited -ne "second.py,first.py,second.py") { $failed += "Navigation went: $visited" }

# Running programs: Stop, a line that redraws itself, a missing tool, a class library, a test project, switching projects.
function Report([string]$Name, [hashtable]$Env) {
    $file = "$work\report-$Name.txt"
    Remove-Item $file -ErrorAction SilentlyContinue
    Shot $Name ($Env + @{ CF_REPORT = $file })
    if (Test-Path $file) { return Get-Content $file -Raw } else { return "" }
}
$runs = "$work\Runs"
New-Item -ItemType Directory -Force $runs | Out-Null
Set-Content "$runs\forever.py" "import time`r`nprint('running', flush=True)`r`nwhile True:`r`n    time.sleep(1)`r`n"
Set-Content "$runs\progress.py" "import sys, time`r`nfor i in range(0, 101, 25):`r`n    sys.stdout.write(f'\rProgress {i}%')`r`n    sys.stdout.flush()`r`n    time.sleep(0.2)`r`nprint()`r`nprint('done')`r`n"
Set-Content "$runs\hello.lua" "print('hi')`r`n"

$text = Report "15-stop" @{ CF_OPEN = "$runs\forever.py"; CF_ACTIONS = "start,stop,dump"; CF_DELAY = "8" }
if ($text -notmatch "STATUS: Stopped") { $failed += "Stop (status wasn't Stopped)" }
$text = Report "16-progress" @{ CF_OPEN = "$runs\progress.py"; CF_ACTIONS = "start,dump"; CF_ACTION_GAP = "7"; CF_DELAY = "10" }
if ($text -notmatch "Progress 100%" -or $text -match "Progress 25%") { $failed += "Progress line (not redrawn)" }
$text = Report "17-missing-tool" @{ CF_OPEN = "$runs\hello.lua"; CF_ACTIONS = "start,dump"; CF_DELAY = "6" }
if ((Get-Command lua -ErrorAction SilentlyContinue) -eq $null -and $text -notmatch "isn't installed") { $failed += "Missing tool message" }

dotnet new classlib -o "$work\Lib1" -n Lib1 | Out-Null
$text = Report "18-library" @{ CF_OPEN = "$work\Lib1;$work\Lib1\Class1.cs"; CF_ACTIONS = "start,dump"; CF_DELAY = "6" }
if ($text -notmatch "class library") { $failed += "Class library message" }
dotnet new xunit -o "$work\Tests1" -n Tests1 | Out-Null
$text = Report "19-test-project" @{ CF_OPEN = "$work\Tests1;$work\Tests1\UnitTest1.cs"; CF_ACTIONS = "start,dump"; CF_ACTION_GAP = "75"; CF_DELAY = "78" }
if ($text -notmatch "dotnet test" -or $text -notmatch "Passed!") { $failed += "Test project (tests didn't run)" }
$text = Report "20-switch-project" @{ CF_OPEN = "$demo;$demo\first.py"; CF_ACTIONS = "open:$work\Lib1,dump"; CF_DELAY = "6" }
if ($text -match "TABS: .*first.py") { $failed += "Switching projects left the old tabs open" }

# IntelliSense without a server (VB.NET): keywords, snippets and the file's words.
Shot "21-vb-completion" @{ CF_OPEN = "$work\BrokenVB;$work\BrokenVB\Program.vb"; CF_ACTIONS = "caret:Console.WriteLine(to,complete"; CF_DELAY = "3" }
# IntelliSense from a language server: Jedi, installed with pip the way Edit > IntelliSense > Install does it.
$sense = "$work\Sense"
New-Item -ItemType Directory -Force $sense | Out-Null
Set-Content "$sense\main.py" "import os`r`n`r`nclass Greeter:`r`n    def greet(self, name):`r`n        return 'Hello ' + name`r`n`r`ng = Greeter()`r`ng.gr`r`nprint(missing +)`r`n"
python -m pip install --disable-pip-version-check --quiet --target "$env:LOCALAPPDATA\CodeForge\LanguageServers\jedi" jedi-language-server
$text = Report "22-python-intellisense" @{ CF_OPEN = "$sense;$sense\main.py"; CF_ACTIONS = "caret:g.gr,complete,dump"; CF_ACTION_GAP = "5"; CF_DELAY = "4" }
if ($text -notmatch "SERVER: Jedi") { $failed += "Jedi didn't start for Python" }
if ($text -notmatch "main.py:9:") { $failed += "No live error from Jedi" }
$text = Report "23-python-definition" @{ CF_OPEN = "$sense;$sense\main.py"; CF_ACTIONS = "caret:= Greet,definition,report,caret:class Gree,references,dump"; CF_ACTION_GAP = "4" }
if ($text -notmatch "main.py:3") { $failed += "Go To Definition (Jedi) didn't reach line 3" }
if ($text -notmatch "(?s)FINDRESULTS:.*main.py:3.*main.py:7") { $failed += "Find All References (Jedi)" }

# Refactoring: Extract Method (VB.NET, no server), Rename without a server (C#) and with one (Jedi).
$refactor = "$work\Refactor"
New-Item -ItemType Directory -Force $refactor | Out-Null
Set-Content "$refactor\Program.vb" "Module Program`r`n    Sub Main()`r`n        Dim price As Double = 9.5`r`n        Dim count = 3`r`n        Dim total As Double = price * count`r`n        total = total * 1.2`r`n        Console.WriteLine(total)`r`n    End Sub`r`nEnd Module`r`n"
$text = Report "24-extract-method" @{ CF_OPEN = "$refactor;$refactor\Program.vb"; CF_ACTIONS = "lines:5-6,extract,text"; CF_DELAY = "2" }
if ($text -notmatch "Private Function NewMethod\(price As Double, count As Integer\) As Double") { $failed += "Extract Method (VB)" }
Set-Content "$refactor\Calc.cs" "static int Square(int x) => x * x;`r`nConsole.WriteLine(Square(3) + Square(4)); // Square stays in comments`r`n"
$env:CF_ANSWER = "Cube"
$text = Report "25-rename" @{ CF_OPEN = "$refactor;$refactor\Calc.cs"; CF_ACTIONS = "caret:static int Squ,rename,text" }
Remove-Item Env:CF_ANSWER
if ($text -notmatch "Cube\(3\) \+ Cube\(4\)" -or $text -notmatch "// Square stays") { $failed += "Rename without a server" }
$env:CF_ANSWER = "Welcomer"
$text = Report "26-rename-jedi" @{ CF_OPEN = "$sense;$sense\main.py"; CF_ACTIONS = "caret:class Gree,rename,text"; CF_ACTION_GAP = "5" }
Remove-Item Env:CF_ANSWER
if ($text -notmatch "class Welcomer" -or $text -notmatch "g = Welcomer\(\)") { $failed += "Rename with Jedi" }
# Git Changes: commit everything in a new repository.
$repo = "$work\GitDemo"
New-Item -ItemType Directory -Force $repo | Out-Null
git -C $repo init -b main | Out-Null
git -C $repo config user.email "ci@example.com"
git -C $repo config user.name "CI"
Set-Content "$repo\hello.py" "print('hello')"
$text = Report "27-git-changes" @{ CF_OPEN = "$repo"; CF_ACTIONS = "git,message:First commit,commit,dump"; CF_ACTION_GAP = "3" }
if ($text -notmatch "Committed: First commit") { $failed += "Git commit from Git Changes" }
if ((git -C $repo log --oneline) -notmatch "First commit") { $failed += "Git commit didn't happen" }

if (Test-Path "$env:APPDATA\CodeForge\errors.log") { Get-Content "$env:APPDATA\CodeForge\errors.log" }
Get-ChildItem $work -Filter errors.log -Recurse | ForEach-Object { Write-Host "== $($_.FullName)"; Get-Content $_.FullName }
if ($failed.Count -gt 0) { throw "Screenshots failed: $($failed -join ', ')" }
