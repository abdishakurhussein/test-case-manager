import { defineConfig } from '@playwright/test';
import path from 'node:path';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: { baseURL: 'http://localhost:4301', trace: 'retain-on-failure' },
  webServer: [
    {
      command:
        'dotnet ef database update --project ../TestCaseManager.Api --startup-project ../TestCaseManager.Api && dotnet run --project ../TestCaseManager.Api --no-launch-profile --urls http://localhost:5186',
      url: 'http://localhost:5186/api/projects',
      env: { ASPNETCORE_ENVIRONMENT: 'Development', Storage__Directory: path.resolve('.e2e-data') },
      reuseExistingServer: false,
      timeout: 120000,
    },
    {
      command: 'npm start -- --port 4301',
      url: 'http://localhost:4301',
      env: { API_PROXY_TARGET: 'http://localhost:5186' },
      reuseExistingServer: false,
      timeout: 120000,
    },
  ],
});
