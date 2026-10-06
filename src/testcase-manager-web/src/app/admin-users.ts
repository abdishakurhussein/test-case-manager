import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AuthService,
  TemporaryCredential,
  UserSummary
} from './auth.service';
import { ConfirmDialogService } from './confirm-dialog.service';

@Component({
  selector: 'app-admin-users',
  imports: [FormsModule],
  template: `
    <section class="users-page">
      <h1>Manage users</h1>
      <p>Create accounts for colleagues. Each receives a temporary password.</p>

      <div class="users-card">
        <h2>Create a user</h2>
        <form class="users-create-form" (ngSubmit)="create()">
          <label for="new-user-name">Username</label>
          <input
            id="new-user-name"
            name="newUserName"
            [(ngModel)]="newUserName"
            required
          />
          <button class="primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Creating…' : 'Create user' }}
          </button>
        </form>
      </div>

      @if (temporary(); as credential) {
        <div class="users-card credential-card" role="status">
          <h2>Temporary password for {{ credential.userName }}</h2>
          <p>Copy it now and share it privately. It will not be shown again.</p>
          <input
            type="text"
            aria-label="Temporary password"
            [value]="credential.temporaryPassword"
            readonly
          />
          <button type="button" (click)="temporary.set(null)">
            Dismiss
          </button>
        </div>
      }

      @if (error()) {
        <p class="auth-error" role="alert">{{ error() }}</p>
      }

      <div class="users-card">
        <h2>Accounts</h2>

        @if (loading()) {
          <p role="status">Loading users…</p>
        } @else {
          <div class="users-list">
            @for (account of users(); track account.id) {
              <div class="users-row">
                <div>
                  <strong>{{ account.userName }}</strong>
                  <small>
                    {{ account.isAdmin ? 'Admin' : 'User' }}
                    @if (account.mustChangePassword) {
                      · Password change required
                    }
                  </small>
                </div>

                @if (!account.isAdmin) {
                  <button
                    type="button"
                    [disabled]="busy()"
                    (click)="reset(account)"
                  >
                    Reset password
                  </button>
                }
              </div>
            }
          </div>
        }
      </div>
    </section>
  `
})
export class AdminUsers {
  private readonly auth = inject(AuthService);
  private readonly confirmDialog = inject(ConfirmDialogService);

  readonly users = signal<UserSummary[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly temporary = signal<TemporaryCredential | null>(null);

  newUserName = '';

  constructor() {
    void this.reload();
  }

  async reload(): Promise<void> {
    this.loading.set(true);

    try {
      this.users.set(await this.auth.listUsers());
    } catch {
      this.error.set('Could not load users.');
    } finally {
      this.loading.set(false);
    }
  }

  async create(): Promise<void> {
    if (this.busy() || !this.newUserName.trim()) return;

    this.busy.set(true);
    this.error.set('');
    this.temporary.set(null);

    try {
      this.temporary.set(
        await this.auth.createUser(this.newUserName.trim())
      );
      this.newUserName = '';
      await this.reload();
    } catch {
      this.error.set('Could not create the account. The username may already exist.');
    } finally {
      this.busy.set(false);
    }
  }

  async reset(account: UserSummary): Promise<void> {
    if (this.busy() || account.isAdmin) return;

    const confirmed = await this.confirmDialog.confirm({
      title: `Reset ${account.userName}'s password?`,
      message: 'Their current password will stop working. A new temporary password will be shown once.',
      confirmLabel: 'Reset password'
    });

    if (!confirmed) return;

    this.busy.set(true);
    this.error.set('');
    this.temporary.set(null);

    try {
      this.temporary.set(
        await this.auth.resetPassword(account.id)
      );
      await this.reload();
    } catch {
      this.error.set('Could not reset the password.');
    } finally {
      this.busy.set(false);
    }
  }
}