// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

[TestClass]
public sealed class AutomationPackageCatalogTests
{
	[TestMethod]
	public void Discover_returns_package_with_sorted_action_children()
	{
		using var packages = new TestPackageRoot();
		packages.AddPackage(
			"media",
			Manifest(
				"hoa.media",
				Action("thumbnails", "Generate thumbnails", "thumbnails.py"),
				Action("convert", "Convert video", "convert.py")),
			"thumbnails.py",
			"convert.py");

		var snapshot = Discover(packages);

		Assert.AreEqual(1, snapshot.Packages.Length);
		var package = snapshot.Packages[0];
		Assert.AreEqual("hoa.media", package.Id.Value);
		Assert.AreEqual(AutomationAvailability.Available, package.Availability);
		CollectionAssert.AreEqual(
			new[] { "hoa.media/convert", "hoa.media/thumbnails" },
			package.Actions.Select(action => action.Id.Value).ToArray());
	}

	[TestMethod]
	public void Discover_sorts_package_roots_by_display_name()
	{
		using var packages = new TestPackageRoot();
		packages.AddPackage(
			"zebra",
			Manifest("hoa.zebra", Action("run", "Run", "run.py"))
				.Replace("\"displayName\": \"Media tools\"", "\"displayName\": \"Zebra tools\"", StringComparison.Ordinal),
			"run.py");
		packages.AddPackage(
			"alpha",
			Manifest("hoa.alpha", Action("run", "Run", "run.py"))
				.Replace("\"displayName\": \"Media tools\"", "\"displayName\": \"Alpha tools\"", StringComparison.Ordinal),
			"run.py");

		var snapshot = Discover(packages);

		CollectionAssert.AreEqual(
			new[] { "hoa.alpha", "hoa.zebra" },
			snapshot.Packages.Select(package => package.Id.Value).ToArray());
	}

	[TestMethod]
	public void Discover_keeps_valid_sibling_when_one_action_is_invalid()
	{
		using var packages = new TestPackageRoot();
		packages.AddPackage(
			"media",
			Manifest(
				"hoa.media",
				Action("convert", "Convert video", "convert.py"),
				Action("missing", "Missing tool", "missing.py")),
			"convert.py");

		var package = Discover(packages).Packages.Single();

		Assert.AreEqual(AutomationAvailability.Available, package.Availability);
		Assert.AreEqual(AutomationAvailability.Available, package.Actions[0].Availability);
		Assert.AreEqual(AutomationAvailability.Disabled, package.Actions[1].Availability);
		StringAssert.Contains(package.Actions[1].Diagnostics.Single(), "entryPoint");
	}

	[TestMethod]
	public void Discover_keeps_valid_sibling_when_action_structure_is_invalid()
	{
		using var packages = new TestPackageRoot();
		var invalidAction = Action("broken", "Broken action", "broken.py")
			.Replace(
				"""
				"input": {
				    "mode": "json-stdin",
				    "selection": {
				      "minItems": 1,
				      "maxItems": 100,
				      "itemKinds": ["file"],
				      "extensions": []
				    }
				  }
				""",
				"""
				"input": "broken"
				""",
				StringComparison.Ordinal);
		packages.AddPackage(
			"media",
			Manifest(
				"hoa.media",
				Action("convert", "Convert video", "convert.py"),
				invalidAction),
			"convert.py",
			"broken.py");

		var package = Discover(packages).Packages.Single();
		var validAction = package.Actions.Single(action => action.Id.LocalId.Value is "convert");
		var invalidActionSnapshot = package.Actions.Single(action => action.Id.LocalId.Value is "broken");

		Assert.AreEqual(AutomationAvailability.Available, package.Availability);
		Assert.AreEqual(AutomationAvailability.Available, validAction.Availability);
		Assert.AreEqual(AutomationAvailability.Disabled, invalidActionSnapshot.Availability);
		StringAssert.Contains(invalidActionSnapshot.Diagnostics.Single(), "action.input must be an object");
	}

	[TestMethod]
	public void Discover_disables_package_when_action_ids_are_duplicated()
	{
		using var packages = new TestPackageRoot();
		packages.AddPackage(
			"media",
			Manifest(
				"hoa.media",
				Action("convert", "Convert video", "convert.py"),
				Action("convert", "Convert another video", "other.py")),
			"convert.py",
			"other.py");

		var package = Discover(packages).Packages.Single();

		Assert.AreEqual("hoa.media", package.Id.Value);
		Assert.AreEqual(AutomationAvailability.Disabled, package.Availability);
		Assert.AreEqual(0, package.Actions.Length);
		StringAssert.Contains(package.Diagnostics.Single(), "Duplicate action id");
	}

	[TestMethod]
	public void Discover_disables_package_when_entry_point_escapes_package()
	{
		using var packages = new TestPackageRoot();
		packages.AddPackage(
			"media",
			Manifest("hoa.media", Action("convert", "Convert video", "../outside.py")));

		var package = Discover(packages).Packages.Single();

		Assert.AreEqual(AutomationAvailability.Disabled, package.Availability);
		StringAssert.Contains(package.Diagnostics.Single(), "inside the package");
	}

	[TestMethod]
	public void Discover_disables_all_packages_with_duplicate_package_id()
	{
		using var firstRoot = new TestPackageRoot();
		using var secondRoot = new TestPackageRoot();
		firstRoot.AddPackage("first", Manifest("hoa.shared", Action("one", "One", "one.py")), "one.py");
		secondRoot.AddPackage("second", Manifest("hoa.shared", Action("two", "Two", "two.py")), "two.py");

		var options = new AutomationCatalogOptions(
			[firstRoot.Path, secondRoot.Path],
			new Version(2, 1, 0),
			new Version(3, 14));
		var snapshot = AutomationManifestCatalog.Discover(options).Snapshot;

		Assert.AreEqual(2, snapshot.Packages.Length);

		// Host surfaces must never key a lookup on the package id: both collided packages stay in the snapshot
		// carrying the same id, so the collision remains visible and repairable.
		Assert.AreEqual(1, snapshot.Packages.Select(package => package.Id).Distinct().Count());
		Assert.IsTrue(snapshot.Packages.All(package => package.Availability is AutomationAvailability.Disabled));
		Assert.IsTrue(snapshot.Packages.All(package => package.Diagnostics.Single().Contains("Duplicate package id", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Discover_disables_package_containing_reparse_point()
	{
		using var packages = new TestPackageRoot();
		var packagePath = packages.AddPackage(
			"media",
			Manifest("hoa.media", Action("convert", "Convert video", "convert.py")),
			"convert.py");
		var outsidePath = System.IO.Path.Combine(packages.Path, "outside.py");
		File.WriteAllText(outsidePath, string.Empty);

		try
		{
			File.CreateSymbolicLink(System.IO.Path.Combine(packagePath, "linked.py"), outsidePath);
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
		{
			Assert.Inconclusive($"Symbolic links are unavailable in this test environment: {exception.Message}");
		}

		var package = Discover(packages).Packages.Single();

		Assert.AreEqual(AutomationAvailability.Disabled, package.Availability);
		StringAssert.Contains(package.Diagnostics.Single(), "reparse point");
	}

	[TestMethod]
	public void Discover_accepts_planned_package_and_action_fields()
	{
		using var packages = new TestPackageRoot();
		var manifest = Manifest("hoa.media", Action("convert", "Convert video", "convert.py"))
			.Replace(
				"""
				  "author": "Hoa",
				""",
				"""
				  "author": "Hoa",
				  "icon": "package.png",
				  "externalTools": [
				    {
				      "id": "hoa.ffmpeg",
				      "displayName": "FFmpeg",
				      "homepage": "https://ffmpeg.org",
				      "minimumFileVersion": "7.0"
				    }
				  ],
				""",
				StringComparison.Ordinal)
			.Replace(
				"""
				  "entryPoint": "convert.py",
				""",
				"""
				  "icon": "convert.png",
				  "entryPoint": "convert.py",
				  "settings": [
				    {
				      "key": "quality",
				      "displayName": "Quality",
				      "description": "Encoding quality",
				      "type": "integer",
				      "default": 20,
				      "minimum": 0,
				      "maximum": 51
				    }
				  ],
				  "externalTools": ["hoa.ffmpeg"],
				""",
				StringComparison.Ordinal)
			.Replace(
				"""
				    "maxOutputBytes": 1048576
				""",
				"""
				    "maxOutputBytes": 1048576,
				    "concurrency": "single"
				""",
				StringComparison.Ordinal);
		packages.AddPackage("media", manifest, "convert.py", "package.png", "convert.png");

		var package = Discover(packages).Packages.Single();

		Assert.AreEqual(AutomationAvailability.Available, package.Availability);
		Assert.AreEqual("package.png", package.Icon);
		Assert.AreEqual("convert.png", package.Actions.Single().Icon);
	}

	[TestMethod]
	public void Discover_disables_only_action_with_malformed_entry_point_text()
	{
		using var packages = new TestPackageRoot();
		packages.AddPackage(
			"media",
			Manifest(
				"hoa.media",
				Action("convert", "Convert video", "convert.py"),
				Action("broken", "Broken action", "bad\\u0000.py")),
			"convert.py");

		var package = Discover(packages).Packages.Single();
		var broken = package.Actions.Single(action => action.Id.LocalId.Value is "broken");

		Assert.AreEqual(AutomationAvailability.Available, package.Availability);
		Assert.AreEqual(AutomationAvailability.Disabled, broken.Availability);
		StringAssert.Contains(broken.Diagnostics.Single(), "entryPoint");
	}

	[TestMethod]
	public void Discover_uses_content_stable_identity_for_action_with_invalid_id()
	{
		using var packages = new TestPackageRoot();
		var invalid = Action("INVALID", "Broken action", "broken.py");
		var valid = Action("convert", "Convert video", "convert.py");
		var packagePath = packages.AddPackage(
			"media",
			Manifest("hoa.media", invalid, valid),
			"broken.py",
			"convert.py");
		var firstId = Discover(packages).Packages.Single().Actions
			.Single(action => action.DisplayName is "Broken action").Id;

		File.WriteAllText(
			System.IO.Path.Combine(packagePath, "vxpackage.json"),
			Manifest("hoa.media", valid, invalid),
			new UTF8Encoding(false));
		var secondId = Discover(packages).Packages.Single().Actions
			.Single(action => action.DisplayName is "Broken action").Id;

		Assert.AreEqual(firstId, secondId);
	}

	[TestMethod]
	[DataRow("1.0.0-01")]
	[DataRow("1.0.0-..")]
	public void Discover_rejects_invalid_semantic_package_version(string version)
	{
		using var packages = new TestPackageRoot();
		var manifest = Manifest("hoa.media", Action("convert", "Convert video", "convert.py"))
			.Replace(
				"""
				  "packageVersion": "1.0.0",
				""",
				$$"""
				  "packageVersion": "{{version}}",
				""",
				StringComparison.Ordinal);
		packages.AddPackage("media", manifest, "convert.py");

		var package = Discover(packages).Packages.Single();

		Assert.AreEqual(AutomationAvailability.Disabled, package.Availability);
		StringAssert.Contains(package.Diagnostics.Single(), "SemVer 2.0");
	}

	[TestMethod]
	public void Domain_ids_reject_invalid_values()
	{
		Assert.ThrowsExactly<ArgumentException>(() => AutomationPackageId.Parse("media"));
		Assert.ThrowsExactly<ArgumentException>(() => AutomationActionLocalId.Parse("INVALID"));
	}

	[TestMethod]
	public void Discover_returns_diagnostic_for_invalid_package_in_long_folder_name()
	{
		using var packages = new TestPackageRoot();
		packages.AddPackage(new string('a', 150), "{ malformed");

		var package = Discover(packages).Packages.Single();

		Assert.AreEqual(AutomationAvailability.Disabled, package.Availability);
		Assert.IsTrue(package.Id.Value.Length <= 128);
		Assert.AreEqual(1, package.Diagnostics.Length);
	}

	private static AutomationCatalogSnapshot Discover(TestPackageRoot packages)
	{
		var options = new AutomationCatalogOptions(
			[packages.Path],
			new Version(2, 1, 0),
			new Version(3, 14));
		return AutomationManifestCatalog.Discover(options).Snapshot;
	}

	private static string Manifest(string packageId, params string[] actions)
		=> $$"""
		{
		  "schemaVersion": 1,
		  "id": "{{packageId}}",
		  "packageVersion": "1.0.0",
		  "displayName": "Media tools",
		  "description": "Media utilities",
		  "author": "Hoa",
		  "minimumHostVersion": "2.1.0",
		  "python": { "requires": ">=3.14,<3.15" },
		  "actions": [
		    {{string.Join("," + Environment.NewLine, actions)}}
		  ]
		}
		""";

	private static string Action(string id, string displayName, string entryPoint)
		=> $$"""
		{
		  "id": "{{id}}",
		  "displayName": "{{displayName}}",
		  "description": "{{displayName}} description",
		  "entryPoint": "{{entryPoint}}",
		  "input": {
		    "mode": "json-stdin",
		    "selection": {
		      "minItems": 1,
		      "maxItems": 100,
		      "itemKinds": ["file"],
		      "extensions": []
		    }
		  },
		  "execution": {
		    "timeoutSeconds": 30,
		    "maxOutputBytes": 1048576
		  },
		  "output": { "protocol": "ndjson-v1" }
		}
		""";

	private sealed class TestPackageRoot : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"VxFiles-Automation-{Guid.NewGuid():N}");

		public TestPackageRoot()
			=> Directory.CreateDirectory(Path);

		public string AddPackage(string folderName, string manifest, params string[] entryPoints)
		{
			var packagePath = System.IO.Path.Combine(Path, folderName);
			Directory.CreateDirectory(packagePath);
			File.WriteAllText(
				System.IO.Path.Combine(packagePath, "vxpackage.json"),
				manifest,
				new UTF8Encoding(false));
			foreach (var entryPoint in entryPoints)
			{
				var entryPointPath = System.IO.Path.Combine(packagePath, entryPoint);
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(entryPointPath)!);
				File.WriteAllText(entryPointPath, string.Empty);
			}

			return packagePath;
		}

		public void Dispose()
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, true);
		}
	}
}
