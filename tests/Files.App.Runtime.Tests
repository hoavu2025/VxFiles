// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.Runtime.Tests;

[TestClass]
public sealed class ProcessLaunchFailurePolicyTests
{
	[TestMethod]
	[DataRow(193)]
	[DataRow(216)]
	public void Document_format_errors_retry_through_the_shell(int nativeErrorCode)
	{
		Assert.IsFalse(ProcessLaunchFailurePolicy.ShouldShowCannotRunDialog(nativeErrorCode, false));
	}

	[TestMethod]
	[DataRow(193)]
	[DataRow(216)]
	public void Executable_format_errors_show_the_cannot_run_dialog(int nativeErrorCode)
	{
		Assert.IsTrue(ProcessLaunchFailurePolicy.ShouldShowCannotRunDialog(nativeErrorCode, true));
	}
}
