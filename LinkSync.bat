@echo off
set "DOTNET_ROOT=C:\Users\thenp\AppData\Local\Microsoft\dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
cd /d "C:\Users\thenp\Desktop\linksync\desktop\src\LinkSync.Desktop\bin\Debug\net9.0-windows"
start "" "C:\Users\thenp\AppData\Local\Microsoft\dotnet\dotnet.exe" "LinkSync.Desktop.dll"
