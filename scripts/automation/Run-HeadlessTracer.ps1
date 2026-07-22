# Copyright (c) Files Community
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

& (Join-Path $PSScriptRoot 'Acquire-Python.ps1')
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    throw "Pinned Python acquisition failed with exit code $LASTEXITCODE."
}

$project = Join-Path $repositoryRoot 'tests\VxFiles.Automation.Tests\VxFiles.Automation.Tests.csproj'
$filter = @(
	'Catalog_revisions_are_monotonic_stale_invocations_fail_and_parse_errors_keep_last_valid_catalog',
	'Catalog_change_during_trust_consent_cannot_launch_the_stale_action',
	'Invalid_package_does_not_block_an_unrelated_valid_action',
    'Trusted_json_run_preserves_ordered_unicode_paths_and_reaches_success',
    'Strict_ndjson_violations_and_nonzero_exit_fail_the_run',
    'Output_and_stderr_budgets_are_enforced_independently',
    'Cooperative_cancellation_closes_the_owned_child_process_job',
    'Closing_session_forces_an_uncooperative_process_tree_after_two_seconds',
    'Manifest_timeout_wins_even_when_worker_exits_cleanly_after_signal',
    'Unchanged_relocation_preserves_trust_but_content_change_renews_it',
    'Changed_app_local_runner_content_renews_trust',
	'Python_executable_must_match_the_pinned_hash_before_launch',
	'Argv_paths_are_exact_and_oversized_windows_command_lines_fail_before_launch',
	'Run_history_evicts_expired_records_and_stays_within_its_size_budget',
	'Cancellation_during_request_transport_is_reported_as_cancelled',
    'Settings_and_explicit_external_tool_identity_are_validated_and_transported',
    'Concurrency_is_single_per_action_two_global_and_never_queues',
    'Successful_result_intents_are_routed_with_the_captured_host_revision'
) -join '|'

dotnet test $project -c $Configuration -p:Platform=x64 -v:minimal --filter $filter
if ($LASTEXITCODE -ne 0) {
    throw "Headless Automation Action tracer failed with exit code $LASTEXITCODE."
}
