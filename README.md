# EQLogParser
Everquest Log Parser for Live/TLP servers with basic support for P99.

Link to DOWNLOAD the latest Installer:</br>
https://github.com/kauffman12/EQLogParser/raw/master/Release/EQLogParser-install-2.2.11.exe

### IMPORTANT --- If after install the Log Search feature crashes and you're missing Syncfusion.Edit.WPF.dll and Syncfusion.GridCommon.WPF.dll from where you installed  EQLogParser.. try running the installer again and choosing Repair to fix the problem.

Minimum Requirements:
1. Windows 10 x64
2. .Net 8.0 Desktop Runtime for x64

.Net 8.0 is provided by Microsoft but is not included with Windows. It can be downloaded from here:</br>
https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.4-windows-x64-installer

If Everquest is in windowed mode but the Damage Meter gets hidden when you switch to the game. Make sure Overlap with Taskbar is turned off:</br>

![Parser](./examples/eqsetting.png)

Note for Developers:</br>
Syncfusion components used by this application require a license. If you apply for a community license you should be able to get one for free.

Additional Notes:</br>
The installer for EQLogParser has been signed with a certificate. It's recommended that the following steps are done ONCE so that you're sure you have an official version. After your system trusts the certificate you'll notice the install prompt will be blue in color and no longer say Unknown Publisher. Then in the future if it returns to yellow/Unknown Publisher you'll know that the installer either wasn't from me or I had to change certificates. Which I will mention here if I have change them.

The last version of the MSI installer: EQLogParser-2.2.11.msi wraps the .exe installer so auto update works for older parsers and will stay in the release directory for a while.

1. right-click the msi file and choose properties
2. under the digital signatures tab select the one signature and click details
3. click View Certificate
4. click Install Certificate

# Example
![Parser](./examples/example1.png)

# Damage Meter and Timer Overlay Example
![Damage Meter](./examples/example2.png)
