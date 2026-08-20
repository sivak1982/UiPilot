export const LOOPBACK_HOST = "127.0.0.1";
export const DEFAULT_PORT = 17831;

export interface RawConfig {
  host: unknown;
  port: unknown;
  token: unknown;
}

export interface UiPilotConfig {
  host: typeof LOOPBACK_HOST;
  port: number;
  token: string;
  httpBaseUrl: string;
  eventsUrl: string;
}

export function parseConfig(raw: RawConfig): UiPilotConfig {
  if (raw.host !== LOOPBACK_HOST) {
    throw new Error(`UiPilot status host must be ${LOOPBACK_HOST}.`);
  }
  if (!Number.isInteger(raw.port) || (raw.port as number) < 1 || (raw.port as number) > 65535) {
    throw new Error("UiPilot status port must be an integer between 1 and 65535.");
  }
  if (typeof raw.token !== "string" || raw.token.trim().length === 0) {
    throw new Error("Configure uipilotStatus.token before connecting.");
  }

  const port = raw.port as number;
  const token = raw.token as string;
  return {
    host: LOOPBACK_HOST,
    port,
    token,
    httpBaseUrl: `http://${LOOPBACK_HOST}:${port}`,
    eventsUrl: `ws://${LOOPBACK_HOST}:${port}/v1/events`,
  };
}
