export interface BackoffOptions {
  initialMs: number;
  maximumMs: number;
  factor: number;
}

export const DEFAULT_BACKOFF: BackoffOptions = {
  initialMs: 500,
  maximumMs: 30_000,
  factor: 2,
};

export function backoffDelay(attempt: number, options = DEFAULT_BACKOFF): number {
  if (!Number.isInteger(attempt) || attempt < 0) throw new Error("Attempt must be a non-negative integer.");
  if (options.initialMs <= 0 || options.maximumMs < options.initialMs || options.factor < 1) {
    throw new Error("Invalid backoff options.");
  }
  return Math.min(options.maximumMs, options.initialMs * options.factor ** attempt);
}
