import { Injectable, signal } from '@angular/core';

export interface Confirmation {
  title: string;
  message: string;
  confirmLabel: string;
  requiredText?: string;
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  readonly request = signal<Confirmation | null>(null);
  private resolve: ((confirmed: boolean) => void) | null = null;

  confirm(request: Confirmation): Promise<boolean> {
    if (this.resolve) return Promise.resolve(false);
    this.request.set(request);
    return new Promise<boolean>(resolve => { this.resolve = resolve; });
  }

  answer(confirmed: boolean): void {
    this.request.set(null);
    this.resolve?.(confirmed);
    this.resolve = null;
  }
}
