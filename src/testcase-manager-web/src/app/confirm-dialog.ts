import { Component, ElementRef, ViewChild, effect, inject, signal } from '@angular/core';
import { ConfirmDialogService } from './confirm-dialog.service';

@Component({
  selector: 'app-confirm-dialog',
  template: `
    <dialog #dialog class="confirm-dialog" aria-labelledby="confirm-heading" aria-describedby="confirm-message"
      (cancel)="cancel($event)">
      @if (confirm.request(); as request) {
        <h2 id="confirm-heading">{{ request.title }}</h2>
        <p id="confirm-message" class="preserve">{{ request.message }}</p>
        @if (request.requiredText; as requiredText) {
          <label for="delete-confirm-text">Type <strong>{{ requiredText }}</strong> to confirm</label>
          <input id="delete-confirm-text" type="text" autocomplete="off" [value]="typedText()"
            (input)="typedText.set($any($event.target).value)" />
        }
        <div class="form-actions">
          <button type="button" (click)="answer(false)">Cancel</button>
          <button type="button" class="danger" [disabled]="!!request.requiredText && typedText() !== request.requiredText"
            (click)="answer(true)">{{ request.confirmLabel }}</button>
        </div>
      }
    </dialog>
  `,
})
export class ConfirmDialog {
  readonly confirm = inject(ConfirmDialogService);
  readonly typedText = signal('');
  @ViewChild('dialog') dialog!: ElementRef<HTMLDialogElement>;

  constructor() {
    effect(() => {
      const request = this.confirm.request();
      queueMicrotask(() => {
        if (!this.dialog) return;
        const element = this.dialog.nativeElement;
        if (request && !element.open) {
          this.typedText.set('');
          element.showModal();
        }
        else if (!request && element.open) element.close();
      });
    });
  }

  answer(value: boolean): void {
    const request = this.confirm.request();
    if (value && request?.requiredText && this.typedText() !== request.requiredText) return;
    this.confirm.answer(value);
  }
  cancel(event: Event): void { event.preventDefault(); this.answer(false); }
}
