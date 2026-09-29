import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

export type NotificationType = 'error' | 'warning' | 'success' | 'info';

const DEFAULT_DURATION_MS = 5000;

@Injectable({
  providedIn: 'root',
})
export class NotificationService {
  private readonly snackBar = inject(MatSnackBar);

  /**
   * Show an error toast. Use for failed API calls, validation, etc.
   */
  error(message: string, detail?: string): void {
    const display = detail ? `${message}: ${detail}` : message;
    this.snackBar.open(display, 'Close', {
      duration: DEFAULT_DURATION_MS,
      panelClass: ['notification-error'],
      horizontalPosition: 'end',
      verticalPosition: 'bottom',
    });
  }

  /**
   * Show a success toast.
   */
  success(message: string): void {
    this.snackBar.open(message, 'Close', {
      duration: DEFAULT_DURATION_MS,
      panelClass: ['notification-success'],
      horizontalPosition: 'end',
      verticalPosition: 'bottom',
    });
  }

  /**
   * Show a warning toast.
   */
  warning(message: string): void {
    this.snackBar.open(message, 'Close', {
      duration: DEFAULT_DURATION_MS,
      panelClass: ['notification-warning'],
      horizontalPosition: 'end',
      verticalPosition: 'bottom',
    });
  }

  /**
   * Show an info toast.
   */
  info(message: string): void {
    this.snackBar.open(message, 'Close', {
      duration: DEFAULT_DURATION_MS,
      panelClass: ['notification-info'],
      horizontalPosition: 'end',
      verticalPosition: 'bottom',
    });
  }

  /**
   * Show a toast from a caught error (extracts message from Error or string).
   */
  showError(contextMessage: string, err: unknown): void {
    this.error(contextMessage, errorMessage(err));
  }
}

/** Best human-readable message from an HttpErrorResponse (ProblemDetails or text body), Error, or string. */
export function errorMessage(err: unknown): string {
  const e = err as { error?: unknown; message?: string; statusText?: string } | null;
  const body = e?.error;
  if (typeof body === 'string' && body.trim()) return body;
  if (body && typeof body === 'object') {
    const problem = body as { detail?: string; title?: string };
    if (problem.detail) return problem.detail;
    if (problem.title) return problem.title;
  }
  if (typeof err === 'string') return err;
  return e?.message ?? e?.statusText ?? 'Unknown error';
}
