import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = auth.user();

  if (!user) return true;

  return router.parseUrl(
    user.mustChangePassword ? '/change-password' : '/'
  );
};

export const signedInGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.user() ? true : router.parseUrl('/login');
};

export const workspaceGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = auth.user();

  if (!user) return router.parseUrl('/login');
  if (user.mustChangePassword) {
    return router.parseUrl('/change-password');
  }

  return true;
};

export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = auth.user();

  if (!user) return router.parseUrl('/login');
  if (user.mustChangePassword) {
    return router.parseUrl('/change-password');
  }

  return user.isAdmin ? true : router.parseUrl('/');
};