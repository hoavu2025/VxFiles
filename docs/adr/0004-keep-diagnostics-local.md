# Keep diagnostics local

VxFiles builds will not configure Sentry or another telemetry service and will retain troubleshooting information locally. This protects coworker privacy and avoids silently inheriting upstream reporting configuration, at the cost of requiring users to share diagnostic logs manually when reporting a problem.
