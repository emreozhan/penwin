@echo off
setlocal
cd /d "%~dp0"
if errorlevel 1 exit /b 1
rem Windows ile gelen .NET Framework 4 C# derleyicisi kullanilir; ek kurulum gerekmez.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4 derleyicisi bulunamadi.
  exit /b 1
)
if not exist bin mkdir bin
"%CSC%" /nologo /codepage:65001 /optimize+ /platform:anycpu /target:exe /r:System.Drawing.dll /out:bin\penwin.exe ^
  /resource:web\index.html,index.html /resource:web\app.js,app.js /resource:web\style.css,style.css ^
  src\*.cs
if errorlevel 1 exit /b 1
echo Derlendi: bin\penwin.exe
exit /b %errorlevel%
