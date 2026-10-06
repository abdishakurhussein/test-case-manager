import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-change-password',
  imports: [FormsModule],
  template: `
    <div class="auth-screen">
      <form class="auth-card" (ngSubmit)="submit()">
        <img src="atcm-logo.png?v=4" alt="" class="auth-logo" />
        <h1>Change your password</h1>

        @if (auth.user()?.mustChangePassword) {
          <p>Your temporary password must be changed before using ATCM.</p>
        } @else {
          <p>Choose a new password for your account.</p>
        }

        @if (error()) {
          <p class="auth-error" role="alert">{{ error() }}</p>
        }

        <label for="current-password">Current password</label>
        <input
          id="current-password"
          name="currentPassword"
          type="password"
          autocomplete="current-password"
          [(ngModel)]="currentPassword"
          required
        />

        <label for="new-password">New password</label>
        <input
          id="new-password"
          name="newPassword"
          type="password"
          autocomplete="new-password"
          [(ngModel)]="newPassword"
          required
          minlength="15"
        />
        <small>Use at least 15 characters.</small>

        <label for="confirm-password">Confirm new password</label>
        <input
          id="confirm-password"
          name="confirmPassword"
          type="password"
          autocomplete="new-password"
          [(ngModel)]="confirmPassword"
          required
        />

        <button class="primary" type="submit" [disabled]="busy()">
          {{ busy() ? 'Saving…' : 'Change password' }}
        </button>

        <button type="button" (click)="logout()" [disabled]="busy()">
          Log out
        </button>
      </form>
    </div>
  `
})
export class ChangePassword {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly error = signal('');

  currentPassword = '';
  newPassword = '';
  confirmPassword = '';

  async submit(): Promise<void> {
    if (this.busy()) return;

    if (this.newPassword.length < 15) {
      this.error.set('Use at least 15 characters.');
      return;
    }

    if (this.newPassword !== this.confirmPassword) {
      this.error.set('The new passwords do not match.');
      return;
    }

    this.error.set('');
    this.busy.set(true);

    try {
      await this.auth.changePassword(
        this.currentPassword,
        this.newPassword
      );

      this.currentPassword = '';
      this.newPassword = '';
      this.confirmPassword = '';

      await this.router.navigateByUrl('/');
    } catch (error) {
      const apiErrors =
        error instanceof HttpErrorResponse && Array.isArray(error.error?.errors)
          ? error.error.errors.join(' ')
          : 'Could not change the password. Check your current password.';

      this.error.set(apiErrors);
    } finally {
      this.busy.set(false);
    }
  }

  async logout(): Promise<void> {
    try {
      await this.auth.logout();
      await this.router.navigateByUrl('/login');
    } catch {
      this.error.set('Could not log out. Please try again.');
    }
  }
}