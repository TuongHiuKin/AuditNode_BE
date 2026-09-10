# Backend testing and quality signals

## Local verification

Fast tests:

    dotnet test AuditNode.Tests/AuditNode.Tests.csproj --filter "Category!=PostgreSQL&Category!=ApiIntegration"

PostgreSQL and API integration tests require a disposable PostgreSQL instance through AUDITNODE_TEST_POSTGRES. CI provides PostgreSQL with a Docker service container and fails when the required TRX evidence or coverage files are missing.

Coverage uses coverage.runsettings and emits OpenCover XML. Coverage is diagnostic evidence, not a percentage target. Test recommendations must still be based on behavior and risk.

## Sonar configuration

Sonar analysis is disabled by default. Enable it with these repository settings:

- Variable SONAR_ENABLED=true
- Variable SONAR_HOST_URL
- Variable SONAR_PROJECT_KEY
- Optional variable SONAR_ORGANIZATION for SonarCloud
- Secret SONAR_TOKEN

The workflow supports SonarQube Server and SonarCloud without selecting a provider in source control. When enabled, it downloads the exact coverage artifacts produced by the required test jobs, verifies that all three OpenCover reports exist, scans the Release build, and waits for the server-side quality gate.

Initial server-side policy:

- New blocker issues: zero
- New critical issues: zero
- Security hotspots: review required
- Coverage and duplication: report only until a stable baseline exists

Do not configure a 100 percent coverage gate or add tests only to increase the metric.
