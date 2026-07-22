param(
	[Parameter(Mandatory = $true)]
	[string]$ExecutablePath,

	[int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Start-Process -FilePath $resolvedExecutablePath -PassThru

try
{
	$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
	$processCondition = New-Object System.Windows.Automation.PropertyCondition(
		[System.Windows.Automation.AutomationElement]::ProcessIdProperty,
		$process.Id)
	$settingsButtonCondition = New-Object System.Windows.Automation.PropertyCondition(
		[System.Windows.Automation.AutomationElement]::AutomationIdProperty,
		"SettingsButton")
	$shellCondition = New-Object System.Windows.Automation.AndCondition(
		$processCondition,
		$settingsButtonCondition)

	do
	{
		if ($process.HasExited)
		{
			throw "VxFiles exited before its shell became ready (exit code $($process.ExitCode))."
		}

		$shellElement = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
			[System.Windows.Automation.TreeScope]::Descendants,
			$shellCondition)

		if ($null -ne $shellElement)
		{
			Write-Host "PASS: VxFiles shell is ready (SettingsButton found for process $($process.Id))."
			exit 0
		}

		Start-Sleep -Milliseconds 250
	}
	while ([DateTime]::UtcNow -lt $deadline)

	throw "VxFiles did not expose SettingsButton within $TimeoutSeconds seconds; startup remained before the real shell."
}
finally
{
	if (-not $process.HasExited)
	{
		Stop-Process -Id $process.Id -Force
	}
}
