# Reusable API test support

- `ApiTestFactory.cs` starts ATCM with a new, migrated temporary SQLite database for each factory instance. `OpenAsync()` returns an anonymous client; `OpenAsUserAsync()` and `OpenAsAdminAsync()` return signed-in clients with a CSRF header ready for writes. Dispose the factory with `await using`.
- `AuthTestHelpers.cs` contains reusable HTTP steps: `CreateTemporaryUserAsync()`, `LoginWithTemporaryPasswordAsync()`, and `RefreshCsrfAsync()`.

Keep scenario assertions in the `*Tests.cs` files. Put a helper here only when more than one test needs the same setup or HTTP sequence. Neither helper uses the application's `App_Data` database.
