# Browser test layout

- `auth.spec.ts`: anonymous login and access checks.
- `setup/admin.setup.ts` and `setup/user.setup.ts`: log in once and save local browser state in `.auth/`.
- `admin/*.spec.ts` and `user/*.spec.ts`: role-specific tests that reuse that saved state through `playwright.config.ts`.
- `legacy/case-workflow.spec.ts`: an older, broad workflow that is **not** part of the default run. Its selectors and export expectations need updating before it is enabled.

The config starts an isolated API on port 5186 and web app on port 4301. It stores test data in `.e2e-data`, not the normal application database. Supply existing test-account credentials using `ATCM_E2E_ADMIN_USER`, `ATCM_E2E_ADMIN_PASSWORD`, `ATCM_E2E_USER`, and `ATCM_E2E_USER_PASSWORD`. The older `ATCM_E2E_ADMIN` name is still accepted. The `.auth/` browser-state files contain session data and must not be committed.

Run `npm run test:e2e -- --list` to see exactly which tests the current projects include.
