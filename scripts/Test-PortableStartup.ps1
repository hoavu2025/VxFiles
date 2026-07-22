param(
	[Parameter(Mandatory = $true)]
	[string]$ExecutablePath,

	[int]$TimeoutSeconds = 45,

	[int]$ExitTimeoutSeconds = 5
)

$ErrorActionPreference = "Stop"
$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$installDirectory = Split-Path -Parent $resolvedExecutablePath
$requiredAssets = @(
	(Join-Path $installDirectory "Assets\AppTiles\Dev\Logo.ico"),
	(Join-Path $installDirectory "Assets\AppTiles\Dev\SplashScreen.scale-200.png")
)

foreach ($assetPath in $requiredAssets)
{
	if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf))
	{
		throw "Required portable asset is missing: $assetPath"
	}
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# The standalone smoke test cannot reference the app's internal CsWin32 wrappers.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class VxFilesSmokeNativeMethods
{
	public const uint WmClose = 0x0010;
	public const uint WmGetIcon = 0x007F;
	public const int IconSmall = 0;
	public const int IconBig = 1;
	public const int IconSmall2 = 2;
	public const int GclpIcon = -14;
	public const int GclpIconSmall = -34;

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
	public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);
}
"@

function Wait-ForShell([System.Diagnostics.Process]$Process)
{
	$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
	$processCondition = New-Object System.Windows.Automation.PropertyCondition(
		[System.Windows.Automation.AutomationElement]::ProcessIdProperty,
		$Process.Id)
	$settingsButtonCondition = New-Object System.Windows.Automation.PropertyCondition(
		[System.Windows.Automation.AutomationElement]::AutomationIdProperty,
		"SettingsButton")
	$shellCondition = New-Object System.Windows.Automation.AndCondition(
		$processCondition,
		$settingsButtonCondition)

	do
	{
		if ($Process.HasExited)
		{
			throw "VxFiles exited before its shell became ready (exit code $($Process.ExitCode))."
		}

		$shellElement = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
			[System.Windows.Automation.TreeScope]::Descendants,
			$shellCondition)

		if ($null -ne $shellElement)
		{
			Write-Host "PASS: VxFiles shell is ready (SettingsButton found for process $($Process.Id))."
			return
		}

		Start-Sleep -Milliseconds 250
	}
	while ([DateTime]::UtcNow -lt $deadline)

	throw "VxFiles did not expose SettingsButton within $TimeoutSeconds seconds; startup remained before the real shell."
}

function Assert-SplashImageLoaded([System.Diagnostics.Process]$Process)
{
	$logPath = Join-Path $env:LOCALAPPDATA "VxFiles\debug.log"
	$loadedMessage = "Splash image loaded for process $($Process.Id)."
	$failedMessage = "Splash image failed to load for process $($Process.Id)."
	$deadline = [DateTime]::UtcNow.AddSeconds(5)

	do
	{
		if (Test-Path -LiteralPath $logPath -PathType Leaf)
		{
			$recentLog = Get-Content -LiteralPath $logPath -Tail 200
			if ($recentLog -match [regex]::Escape($failedMessage))
			{
				throw $failedMessage
			}

			if ($recentLog -match [regex]::Escape($loadedMessage))
			{
				Write-Host "PASS: VxFiles splash image rendered."
				return
			}
		}

		Start-Sleep -Milliseconds 100
	}
	while ([DateTime]::UtcNow -lt $deadline)

	throw "VxFiles did not report whether its splash image rendered."
}

function Assert-WindowIcon([System.Diagnostics.Process]$Process)
{
	$Process.Refresh()
	if ($Process.MainWindowHandle -eq [IntPtr]::Zero)
	{
		throw "VxFiles shell has no main window handle."
	}

	$iconHandles = @(
		[VxFilesSmokeNativeMethods]::SendMessage($Process.MainWindowHandle, [VxFilesSmokeNativeMethods]::WmGetIcon, [IntPtr][VxFilesSmokeNativeMethods]::IconSmall, [IntPtr]::Zero),
		[VxFilesSmokeNativeMethods]::SendMessage($Process.MainWindowHandle, [VxFilesSmokeNativeMethods]::WmGetIcon, [IntPtr][VxFilesSmokeNativeMethods]::IconBig, [IntPtr]::Zero),
		[VxFilesSmokeNativeMethods]::SendMessage($Process.MainWindowHandle, [VxFilesSmokeNativeMethods]::WmGetIcon, [IntPtr][VxFilesSmokeNativeMethods]::IconSmall2, [IntPtr]::Zero),
		[VxFilesSmokeNativeMethods]::GetClassLongPtr($Process.MainWindowHandle, [VxFilesSmokeNativeMethods]::GclpIcon),
		[VxFilesSmokeNativeMethods]::GetClassLongPtr($Process.MainWindowHandle, [VxFilesSmokeNativeMethods]::GclpIconSmall)
	)

	if (-not ($iconHandles | Where-Object { $_ -ne [IntPtr]::Zero }))
	{
		throw "VxFiles shell has no window icon."
	}

	Write-Host "PASS: VxFiles shell exposes a window icon."
}

function Close-AndWait([System.Diagnostics.Process]$Process)
{
	$Process.Refresh()
	if (-not [VxFilesSmokeNativeMethods]::PostMessage($Process.MainWindowHandle, [VxFilesSmokeNativeMethods]::WmClose, [IntPtr]::Zero, [IntPtr]::Zero))
	{
		throw "Failed to request that VxFiles close."
	}

	if (-not $Process.WaitForExit($ExitTimeoutSeconds * 1000))
	{
		throw "VxFiles remained running after its window was closed."
	}
}

$process = $null
$relaunchedProcess = $null

try
{
	$process = Start-Process -FilePath $resolvedExecutablePath -PassThru
	Wait-ForShell $process
	Assert-SplashImageLoaded $process
	Assert-WindowIcon $process
	Close-AndWait $process

	Write-Host "PASS: VxFiles exited after its window was closed."
	$relaunchedProcess = Start-Process -FilePath $resolvedExecutablePath -PassThru
	Wait-ForShell $relaunchedProcess
	Assert-SplashImageLoaded $relaunchedProcess
	Write-Host "PASS: VxFiles reached its shell after relaunch."
	Close-AndWait $relaunchedProcess
}
finally
{
	if ($null -ne $process -and -not $process.HasExited)
	{
		Stop-Process -Id $process.Id -Force
	}

	if ($null -ne $relaunchedProcess -and -not $relaunchedProcess.HasExited)
	{
		Stop-Process -Id $relaunchedProcess.Id -Force
	}
}
