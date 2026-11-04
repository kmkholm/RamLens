@echo off
cd /d "%~dp0"
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /out:RamLens.exe /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Data.dll /r:System.Core.dll src\Program.cs
