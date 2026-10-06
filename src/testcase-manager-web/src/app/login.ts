import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="auth-screen">
      <form class="auth-card" (ngSubmit)="submit()">
        <img src="atcm-logo.png?v=4" alt="" class="auth-logo" />
        <h1>Sign in to ATCM</h1>
        <p>Use the account provided by your administrator.</p>

        @if (auth.startupError()) {
          <p class="auth-error" role="alert">{{ auth.startupError() }}</p>
        }
        @if (error()) {
          <p class="auth-error" role="alert">{{ error() }}</p>
        }

        <label for="login-user">Username</label>
        <input
          id="login-user"
          name="userName"
          type="text"
          autocomplete="username"
          [(ngModel)]="userName"
          required
        />

        <label for="login-password">Password</label>
        <input
          id="login-password"
          name="password"
          type="password"
          autocomplete="current-password"
          [(ngModel)]="password"
          required
        />

        <button class="primary" type="submit" [disabled]="busy()">
          {{ busy() ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
    </div>
  `
})
export class Login {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly error = signal('');

  userName = '';
  password = '';

  async submit(): Promise<void> {
    if (this.busy() || !this.userName.trim() || !this.password) return;

    this.error.set('');
    this.busy.set(true);

    try {
      const user = await this.auth.login(
        this.userName.trim(),
        this.password
      );

      this.password = '';

      await this.router.navigateByUrl(
        user.mustChangePassword ? '/change-password' : '/'
      );
    } catch (error) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 401
          ? 'Invalid username or password.'
          : 'Could not sign in. Check the API and try again.'
      );
    } finally {
      this.busy.set(false);
    }
  }
}