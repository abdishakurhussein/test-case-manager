import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface AuthUser {
  userName: string;
  isAdmin: boolean;
  mustChangePassword: boolean;
}

export interface UserSummary {
  id: string;
  userName: string;
  isAdmin: boolean;
  mustChangePassword: boolean;
}

export interface TemporaryCredential {
  userId: string;
  userName: string;
  temporaryPassword: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly user = signal<AuthUser | null>(null);
  readonly startupError = signal('');
  readonly canUseWorkspace = computed(() => {
    const user = this.user();
    return user !== null && !user.mustChangePassword;
  });

  private async refreshCsrf(): Promise<void> {
    await firstValueFrom(this.http.get<void>('/api/auth/csrf'));
    this.startupError.set('');
  }

  async initialize(): Promise<void> {
    try {
      await this.refreshCsrf();
      this.user.set(
        await firstValueFrom(this.http.get<AuthUser>('/api/auth/me'))
      );
    } catch (error) {
      this.user.set(null);

      if (!(error instanceof HttpErrorResponse && error.status === 401)) {
        this.startupError.set('Cannot reach the API. Check that it is running.');
      }
    }
  }

  async login(userName: string, password: string): Promise<AuthUser> {
    await this.refreshCsrf();

    const user = await firstValueFrom(
      this.http.post<AuthUser>('/api/auth/login', { userName, password })
    );

    // The signed-in user needs a fresh antiforgery token.
    await this.refreshCsrf();
    this.user.set(user);
    return user;
  }

  async changePassword(
    currentPassword: string,
    newPassword: string
  ): Promise<AuthUser> {
    const user = await firstValueFrom(
      this.http.post<AuthUser>('/api/auth/change-password', {
        currentPassword,
        newPassword
      })
    );

    await this.refreshCsrf();
    this.user.set(user);
    return user;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
    } catch (error) {
      // An expired session is already effectively logged out.
      if (!(error instanceof HttpErrorResponse && error.status === 401)) {
        throw error;
      }
    }

    this.user.set(null);
    try {
      await this.refreshCsrf();
    } catch {
      // The login page can retry obtaining a token when the API returns.
    }
  }

  listUsers(): Promise<UserSummary[]> {
    return firstValueFrom(
      this.http.get<UserSummary[]>('/api/admin/users')
    );
  }

  createUser(userName: string): Promise<TemporaryCredential> {
    return firstValueFrom(
      this.http.post<TemporaryCredential>('/api/admin/users', { userName })
    );
  }

  resetPassword(id: string): Promise<TemporaryCredential> {
    return firstValueFrom(
      this.http.post<TemporaryCredential>(
        `/api/admin/users/${encodeURIComponent(id)}/reset-password`,
        {}
      )
    );
  }
}