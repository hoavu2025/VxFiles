// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;

namespace VxFiles.Automation;

internal sealed unsafe class AutomationProcessJob : IDisposable
{
	private readonly SafeFileHandle _handle;

	private AutomationProcessJob(SafeFileHandle handle)
	{
		_handle = handle;
	}

	public static AutomationProcessJob CreateAndAssign(Process process)
	{
		var handle = PInvoke.CreateJobObject(null, null);
		if (handle.IsInvalid)
			throw new Win32Exception();

		try
		{
			var jobHandle = new HANDLE(handle.DangerousGetHandle());
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

			return new AutomationProcessJob(handle);
		}
		catch
		{
			handle.Dispose();
			throw;
		}
	}

	public void Dispose() => _handle.Dispose();
}
