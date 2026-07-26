// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Runtime;

public static class ProcessLaunchFailurePolicy
{
	public static bool ShouldShowCannotRunDialog(int nativeErrorCode, bool isExecutable)
		=> isExecutable && nativeErrorCode is 193 or 216;
}
