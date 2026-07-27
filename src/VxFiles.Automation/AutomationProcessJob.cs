// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;

namespace VxFiles.Automation;

/// <summary>
/// A kill-on-close Job Object owning one Automation Action process tree. Disposing it terminates every
/// surviving descendant, so no child outlives the run that started it.
/// </summary>
internal sealed unsafe class AutomationProcessJob : IDisposable
{
	private readonly SafeFileHandle _handle;

	private AutomationProcessJob(SafeFileHandle handle)
		=> _handle = handle;

	public static AutomationProcessJob CreateAndAssign(Process process)
	{
		var jobObjectHandle = PInvoke.CreateJobObject(null, null);
		if (jobObjectHandle.IsInvalid)
			throw new Win32Exception();

		try
		{
			var jobHandle = new HANDLE(jobObjectHandle.DangerousGetHandle());
			var information = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
			information.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
			if (!PInvoke.SetInformationJobObject(
				jobHandle,
				JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
				&information,
				(uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
			{
				throw new Win32Exception();
			}

			if (!PInvoke.AssignProcessToJobObject(jobHandle, new HANDLE(process.SafeHandle.DangerousGetHandle())))
				throw new Win32Exception();

			return new(jobObjectHandle);
		}
		catch
		{
			jobObjectHandle.Dispose();
			throw;
		}
	}

	public void Dispose() => _handle.Dispose();
}
