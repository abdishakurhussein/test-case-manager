# TestCaseManager

A local-only manual test-case library built with Angular 21, ASP.NET Core 10 and SQLite.

## Current local workflow

Create a project, open it from the sidebar, create a module, and create a test case with ordered steps.
Project and case URLs include readable project/module names and stable ID suffixes, for example
`/projects/login-platform--10/modules/authentication--3/cases/25`.
Refreshing a URL reads the saved record again. Duplicate names remain unambiguous.
The API assigns IDs, step positions and timestamps. It validates required text, lengths,
parent records, statuses and priorities. A case and its steps save in one transaction.
Ready cases can record completed manual runs: mark every step Passed or Failed, then save.
Draft cases can still be edited and have actions added, but cannot record results until marked Ready.
Failed steps require an actual result and explicit Yes, No or Unknown answers to two questions. Run history snapshots
the step instructions and remains intact if a current step is changed or removed. Actions can
also be appended to an existing case (up to 100 steps).
The manual-run screen shows progress and passed/failed/unrecorded counts, and can jump to the
next unrecorded step. Choices remain unsaved until the full run is submitted. An in-app warning
appears before leaving with an unfinished run; browser reload/close uses the browser's own
unsaved-changes warning. There is no resumable draft run yet.
Draft and Ready cases can also be edited: title, description, preconditions, priority, and
step text/order. Earlier run snapshots are not rewritten. An edit made against an older
version of the case is rejected so the user can reload before retrying.

A case can move from Draft to Ready, and from Ready to Complete after at least one saved manual
run, whether that run passed or failed. Complete cases are read-only except for archiving or deletion. The
API enforces this lock as well as the UI. Case deletion permanently removes its steps and saved
run history; the UI requires typing `Confirm`. Modules must be emptied before deletion, and
projects must have their modules removed first.

Archive a case to hide it from active lists while keeping its current steps and every saved run.
The Archive screen can restore it to its previous Draft, Ready or Complete status. Archived cases
are view-only until restored, but can still be deliberately deleted. Active project/module counts
exclude archived cases; archived counts are shown separately. A module with archived cases cannot
be deleted until those cases are restored and removed or deliberately deleted.

The home screen shows the last three opened projects, stored in this browser, and the ten
most recently updated cases. All projects remain in the searchable sidebar. Within a project,
click a module to focus its cases; the module selection is kept in the URL. Case search,
status/priority filters, sorting and paging run through the API rather than loading the full
library into the browser. Projects and test data are stored in SQLite, not browser storage.
Project pages show a snapshot of Draft/Ready/Complete counts and how many cases have a
latest saved run of Passed or Failed. The case table shows each case's latest run result,
date and run count separately from its lifecycle status; a case can be Complete even if
its latest run Failed. These are counts of cases, not percentages or counts of all runs.
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
Migrations are committed; data files are not. The archive feature adds two columns to the existing
database. Before updating an existing installation, stop the API and back up the entire `App_Data`
folder, then run the migration command above. The migration keeps existing records; it does not
archive or delete any of them. Check migration status on each installation before using its
archive features; the isolated automated tests do not migrate the application database.
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
The browser walkthrough covers desktop and mobile navigation, create/edit/run flows,
failed-run details, completion, archive/export/restore, and deletion in reverse hierarchy.
It uses synthetic data only; before sharing the tool, also review the generated screenshots
and repeat the core flows with representative non-sensitive data on the intended screen sizes.

## API

- `GET/POST /api/projects`, `GET/DELETE /api/projects/{id}`
- `GET /api/projects/{id}/overview` for case status and latest-run counts
- `GET /api/modules?projectId={id}`, `POST /api/modules`, `GET/DELETE /api/modules/{id}`
- `GET/POST /api/testcases`, `GET/PUT/DELETE /api/testcases/{id}`
- `GET /api/testcases/search` with optional `projectId`, `moduleId`, `q`, `status`,
  `priority`, `sort`, `page` and `pageSize` (up to 100); returns items, latest-run metadata
  and a total count
- `PATCH /api/testcases/{id}/status`, `POST /api/testcases/{id}/steps`,
  `DELETE /api/testcases/{id}/steps/{stepId}`
- `GET/POST /api/testcases/{id}/runs`, `GET /api/testcases/{id}/runs/{runId}`
- `POST /api/testcases/{id}/archive`, `POST /api/testcases/{id}/restore`
- `GET /api/archive/export?format=json|csv[&projectId={id}]` downloads all archived cases,
  optionally for one project; `csv` downloads a ZIP containing `cases.csv`, `case_steps.csv`,
  `runs.csv` and `run_steps.csv`

JSON export includes the case definition, project/module identity, ordered current steps,
and all historical run snapshots and failure details. CSV fields are quoted and potentially
formula-like user text is prefixed for safer Excel opening. Export covers all archived cases
in scope, not only the visible page or search result. It is a portable report, **not** a
replacement for a SQLite backup or a tested import/restore process.

For a failed run step, send an actual result plus a `canReplicate` and `onlyUserAffected`
boolean, or set the corresponding `canReplicateUnknown` / `onlyUserAffectedUnknown` flag
to true. Omitted answers and contradictory boolean-plus-Unknown choices are rejected.

New cases require one to 100 complete steps, a valid module, and Draft or Ready status.
`PUT` requires the last observed `updatedAt` as `expectedUpdatedAt`; unchanged steps retain
their IDs, and new steps omit the ID. A stale edit or a Complete/Archived case returns `409`.
Case titles and project/module names need not be unique. IDs distinguish them.
The original `GET /api/testcases` remains available for existing clients; the web UI uses
the paged search endpoint.

## Next checkpoints

Spreadsheet import, CI, and backup/import recovery remain separate work. Authentication, shared hosting and stronger concurrency
protection are required before a workplace rollout. No execution runner, WPF integration,
or cloud infrastructure is included. The ATCM logo is in `src/testcase-manager-web/public/atcm-logo.png`.
