import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly message = signal('');
  private timer: ReturnType<typeof setTimeout> | null = null;

  show(message: string): void {
    if (this.timer) clearTimeout(this.timer);
    this.message.set(message);
    this.timer = setTimeout(() => {
      this.message.set('');
      this.timer = null;
    }, 6000);
  }

  dismiss(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.message.set('');
  }
}
