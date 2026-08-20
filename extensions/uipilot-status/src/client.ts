import * as http from "node:http";
import WebSocket from "ws";
import { BackoffOptions, backoffDelay, DEFAULT_BACKOFF } from "./backoff";
import { UiPilotConfig } from "./config";
import {
  applyStatusEvent,
  OperationDto,
  parseEventEnvelope,
  StatusDto,
  validateStatusDto,
} from "./model";
import { ConnectionState } from "./statusText";

export interface StatusClientCallbacks {
  onState(state: ConnectionState, detail?: string): void;
  onStatus(status: StatusDto): void;
  onOperation(operation: OperationDto): void;
  log(message: string): void;
}

export interface StatusClientOptions {
  backoff?: BackoffOptions;
}

export class UiPilotStatusClient {
  private socket: WebSocket | undefined;
  private reconnectTimer: NodeJS.Timeout | undefined;
  private generation = 0;
  private reconnectAttempt = 0;
  private stopped = true;
  private readonly backoff: BackoffOptions;

  constructor(
    private readonly getConfig: () => UiPilotConfig,
    private readonly callbacks: StatusClientCallbacks,
    options: StatusClientOptions = {},
  ) {
    this.backoff = options.backoff ?? DEFAULT_BACKOFF;
  }

  connect(): void {
    this.disconnectSocket();
    this.stopped = false;
    this.reconnectAttempt = 0;
    const generation = ++this.generation;
    void this.connectOnce(generation);
  }

  reconnect(): void {
    this.connect();
  }

  async refresh(): Promise<void> {
    try {
      const status = await this.fetchStatus(this.getConfig());
      this.callbacks.onStatus(status);
      this.callbacks.log("Status refreshed over HTTP.");
    } catch (error) {
      const message = errorMessage(error);
      this.callbacks.onState("error", message);
      this.callbacks.log(`HTTP recovery failed: ${message}`);
      throw error;
    }
  }

  dispose(): void {
    this.stopped = true;
    ++this.generation;
    this.clearReconnectTimer();
    this.disconnectSocket();
    this.callbacks.onState("disconnected");
  }

  private async connectOnce(generation: number): Promise<void> {
    if (this.stopped || generation !== this.generation) return;
    this.callbacks.onState("connecting");

    let config: UiPilotConfig;
    try {
      config = this.getConfig();
    } catch (error) {
      this.connectionFailed(generation, error);
      return;
    }

    let snapshot: StatusDto | undefined;
    try {
      snapshot = await this.fetchStatus(config);
      this.callbacks.onStatus(snapshot);
      this.callbacks.log("HTTP status snapshot recovered.");
    } catch (error) {
      this.callbacks.log(`HTTP status recovery deferred: ${errorMessage(error)}`);
    }
    if (this.stopped || generation !== this.generation) return;

    try {
      const socket = new WebSocket(config.eventsUrl, {
        headers: { Authorization: `Bearer ${config.token}` },
      });
      this.socket = socket;
      socket.on("open", () => {
        if (generation !== this.generation) return;
        if (snapshot) {
          this.markConnected(config);
        } else {
          this.callbacks.log("Waiting for WebSocket hello snapshot.");
        }
      });
      socket.on("message", (data, isBinary) => {
        if (generation !== this.generation || isBinary) return;
        try {
          const event = parseEventEnvelope(JSON.parse(data.toString()));
          if (event.type === "operation") {
            this.callbacks.onOperation(event.operation);
            if (snapshot) snapshot = applyStatusEvent(snapshot, event);
          } else {
            snapshot = applyStatusEvent(snapshot, event);
            this.callbacks.onStatus(snapshot);
          }
          this.markConnected(config);
        } catch (error) {
          this.callbacks.log(`Ignored invalid event: ${errorMessage(error)}`);
          void this.recoverOverHttp(generation, config);
        }
      });
      socket.on("error", (error) => {
        this.callbacks.log(`WebSocket error: ${error.message}`);
      });
      socket.on("close", () => {
        if (this.socket === socket) this.socket = undefined;
        if (generation !== this.generation || this.stopped) return;
        this.callbacks.onState("disconnected", "Event stream closed; reconnecting.");
        this.scheduleReconnect(generation);
      });
    } catch (error) {
      this.connectionFailed(generation, error);
    }
  }

  private markConnected(config: UiPilotConfig): void {
    this.reconnectAttempt = 0;
    this.callbacks.onState("connected");
    this.callbacks.log(`Connected to ${config.httpBaseUrl}.`);
  }

  private async recoverOverHttp(generation: number, config: UiPilotConfig): Promise<void> {
    if (this.stopped || generation !== this.generation) return;
    try {
      this.callbacks.onStatus(await this.fetchStatus(config));
      this.callbacks.log("Recovered snapshot over HTTP.");
    } catch (error) {
      this.callbacks.log(`HTTP recovery after invalid event failed: ${errorMessage(error)}`);
    }
  }

  private fetchStatus(config: UiPilotConfig): Promise<StatusDto> {
    return new Promise((resolve, reject) => {
      const request = http.get(
        `${config.httpBaseUrl}/v1/status`,
        {
          headers: {
            Authorization: `Bearer ${config.token}`,
            Accept: "application/json",
          },
          timeout: 5_000,
        },
        (response) => {
          const chunks: Buffer[] = [];
          response.on("data", (chunk: Buffer) => chunks.push(chunk));
          response.on("end", () => {
            const body = Buffer.concat(chunks).toString("utf8");
            if (response.statusCode !== 200) {
              reject(new Error(`Status request returned HTTP ${response.statusCode ?? "unknown"}.`));
              return;
            }
            try {
              resolve(validateStatusDto(JSON.parse(body)));
            } catch (error) {
              reject(error);
            }
          });
        },
      );
      request.on("timeout", () => request.destroy(new Error("Status request timed out.")));
      request.on("error", reject);
    });
  }

  private connectionFailed(generation: number, error: unknown): void {
    if (generation !== this.generation || this.stopped) return;
    const message = errorMessage(error);
    this.callbacks.onState("error", message);
    this.callbacks.log(`Connection failed: ${message}`);
    this.scheduleReconnect(generation);
  }

  private scheduleReconnect(generation: number): void {
    this.clearReconnectTimer();
    const delay = backoffDelay(this.reconnectAttempt++, this.backoff);
    this.callbacks.log(`Reconnecting in ${delay} ms.`);
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = undefined;
      void this.connectOnce(generation);
    }, delay);
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = undefined;
  }

  private disconnectSocket(): void {
    this.clearReconnectTimer();
    const socket = this.socket;
    this.socket = undefined;
    if (socket && socket.readyState !== WebSocket.CLOSED) socket.close();
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
