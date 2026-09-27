# Test Case Manager

A local-only manual test-case library built with Angular 21, ASP.NET Core 10 and SQLite.

## Current local workflow

Create a project, open it from the sidebar, create a module, and create a test case with ordered steps.
Project and case URLs include readable project/module names and stable ID suffixes, for example
`/projects/login-platform--10/modules/authentication--3/cases/25`.
Refreshing a URL reads the saved record again. Duplicate names remain unambiguous.
The API assigns IDs, step positions and timestamps. It validates required text, lengths,
parent records, statuses and priorities. A case and its steps save in one transaction.
Draft and Ready cases can record completed manual runs: mark every step Passed or Failed, then save.
Failed steps require an actual result and answers to two Yes/No questions. Run history snapshots
the step instructions and remains intact if a current step is changed or removed. Actions can
also be appended to an existing case (up to 100 steps).

A case can move from Draft to Ready, and from Ready to Complete after at least one saved manual
run, whether that run passed or failed. Complete cases are read-only except for deletion. The
API enforces this lock as well as the UI. Case deletion permanently removes its steps and saved
run history; the UI requires typing `Confirm`. Modules must be emptied before deletion, and
projects must have their modules removed first.

The home screen shows the last three opened projects, stored in this browser; all projects
remain in the sidebar. Projects and test data are stored in SQLite, not browser storage.
No real workplace information should be used without approval.

## Run locally

Requires .NET SDK 10, Node.js compatible with Angular 21, npm and Git.
From the repository root, restore tools and apply the database migration:

```powershell
dotnet tool restore
dotnet ef database update --project src/TestCaseManager.Api --startup-project src/TestCaseManager.Api
dotnet run --project src/TestCaseManager.Api --launch-profile http
```

In a second terminal:

```powershell
Set-Location src/testcase-manager-web
npm ci
npm start
```

Open http://localhost:4200. The frontend proxies `/api` to http://localhost:5084.
Keep both terminals running. Stop with Ctrl+C. There is no cloud deployment or login.
Do not expose these development servers to your network or the internet.

The application database lives under `src/TestCaseManager.Api/App_Data/` and is ignored by Git.
Migrations are committed; data files are not. Stop the API before making a backup of that folder.
The optional `Storage__Directory` environment variable changes the database directory (used by tests).

## Checks

```powershell
# From the repository root.
dotnet test TestCaseManager.sln
dotnet ef migrations has-pending-model-changes --project src/TestCaseManager.Api --startup-project src/TestCaseManager.Api

# From src/testcase-manager-web.
npm run build
npm test -- --watch=false
npx playwright install chromium
npm run test:e2e
```

HTTP integration tests use unique temporary SQLite databases and clean them up.
Browser tests start their own servers on ports 5186 and 4301, and use the ignored
`.e2e-data` directory, never the application database. They leave synthetic records
in that separate directory for inspection. Tests fail rather than reuse a server on those ports.
Screenshots and failure traces go to the ignored `test-results` directory.

## API

- `GET/POST /api/projects`, `GET/DELETE /api/projects/{id}`
- `GET /api/modules?projectId={id}`, `POST /api/modules`, `GET/DELETE /api/modules/{id}`
- `GET/POST /api/testcases`, `GET/DELETE /api/testcases/{id}`
- `PATCH /api/testcases/{id}/status`, `POST /api/testcases/{id}/steps`,
  `DELETE /api/testcases/{id}/steps/{stepId}`
- `GET/POST /api/testcases/{id}/runs`, `GET /api/testcases/{id}/runs/{runId}`

New cases require one to 100 complete steps, a valid module, and Draft or Ready status.
Case titles and project/module names need not be unique. IDs distinguish them.
The lists are intentionally unpaginated for this personal prototype.

## Next checkpoints

Search and advanced filtering, editing existing case details, archiving, spreadsheet
import/export, and CI remain separate work. Authentication, shared hosting and concurrency
protection are required before a workplace rollout. No execution runner, WPF integration,
or cloud infrastructure is included. The checkmark logo is temporary branding.
