// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// Covers what a frame's message becomes once it leaves the protocol reader. The message is action-authored text
/// that ends up on the run row and in run history, so its bounds are the reader's responsibility rather than the
/// UI's.
/// </summary>
[TestClass]
public sealed class AutomationOutputProtocolReaderTests
{
	[TestMethod]
	public void Result_message_is_kept_as_the_terminal_message()
	{
		var reader = new AutomationOutputProtocolReader();
		var frame = Parse(reader, """{"protocol":"ndjson-v1","sequence":1,"type":"result","outcome":"succeeded","message":"Renamed 3 files."}""");

		Assert.AreEqual("Renamed 3 files.", frame.Message);
		Assert.AreEqual("Renamed 3 files.", reader.TerminalMessage);
	}

	/// <summary>
	/// An action that ends without a message has nothing to say, which the runner reports as the run state rather
	/// than as an empty status.
	/// </summary>
	[TestMethod]
	public void Result_without_a_message_leaves_the_terminal_message_unset()
	{
		var reader = new AutomationOutputProtocolReader();
		Parse(reader, """{"protocol":"ndjson-v1","sequence":1,"type":"result","outcome":"succeeded"}""");

		Assert.IsNull(reader.TerminalMessage);
	}

	/// <summary>
	/// A frame may carry up to 64 KiB. The status is one line beside the run and is retained in history, so an
	/// action cannot use it to store its output.
	/// </summary>
	[TestMethod]
	public void Long_message_is_clamped_to_a_status_line()
	{
		var reader = new AutomationOutputProtocolReader();
		var frame = Parse(reader, $$"""{"protocol":"ndjson-v1","sequence":1,"type":"log","level":"info","message":"{{new string('x', 5000)}}"}""");

		Assert.AreEqual(200, frame.Message!.Length);

		// The log entry keeps the whole thing; only the status is a summary.
		Assert.AreEqual(5000, frame.Log!.Message.Length);
	}

	/// <summary>
	/// A multi-line message would otherwise push the rest of the pane down as it arrived.
	/// </summary>
	[TestMethod]
	public void Newlines_are_collapsed_in_the_status()
	{
		var reader = new AutomationOutputProtocolReader();
		var frame = Parse(reader, """{"protocol":"ndjson-v1","sequence":1,"type":"progress","percent":50,"message":"first\nsecond"}""");

		Assert.AreEqual("first second", frame.Message);
	}

	private static AutomationOutputFrame Parse(AutomationOutputProtocolReader reader, string line)
		=> reader.Parse(line, System.Text.Encoding.UTF8.GetByteCount(line));
}
