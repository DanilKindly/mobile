import { defineConfig, devices } from '@playwright/test'

const e2eApiUrl = process.env.E2E_API_URL || ''
const e2eSignalRUrl = process.env.E2E_SIGNALR_URL || (e2eApiUrl ? `${e2eApiUrl}/hubs/chat` : '')

export default defineConfig({
  testDir: './tests/e2e',
  timeout: 30_000,
  expect: {
    timeout: 8_000,
  },
  use: {
    baseURL: 'http://127.0.0.1:5173',
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: e2eApiUrl
    ? {
        command: 'npm run dev -- --host 127.0.0.1',
        url: 'http://127.0.0.1:5173',
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
        env: {
          VITE_API_URL: e2eApiUrl,
          VITE_SIGNALR_URL: e2eSignalRUrl,
        },
      }
    : undefined,
})
