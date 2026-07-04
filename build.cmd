@echo off
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /r:System.Windows.Forms.dll /win32icon:icon.ico /out:UbuntuDesktop.exe UbuntuDesktop.cs
echo Done.
