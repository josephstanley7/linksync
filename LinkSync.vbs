Set WshShell = CreateObject("WScript.Shell")
WshShell.Environment("PROCESS")("DOTNET_ROOT") = "C:\Users\thenp\AppData\Local\Microsoft\dotnet"
WshShell.CurrentDirectory = "C:\Users\thenp\Desktop\linksync\desktop\src\LinkSync.Desktop\bin\Debug\net9.0-windows"
WshShell.Run """C:\Users\thenp\Desktop\linksync\desktop\src\LinkSync.Desktop\bin\Debug\net9.0-windows\LinkSync.Desktop.exe""", 1, False
