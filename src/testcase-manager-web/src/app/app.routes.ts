import { Routes } from '@angular/router';
import { Workspace } from './workspace';
import { CaseDetail } from './case-detail';
import { ArchiveCases } from './archive-cases';
import { Login } from './login';
import { ChangePassword } from './change-password';
import { AdminUsers } from './admin-users';
import {
  adminGuard,
  guestGuard,
  signedInGuard,
  workspaceGuard
} from './auth.guards';

export const routes: Routes = [
  {
    path: 'login',
    component: Login,
    canActivate: [guestGuard],
    title: 'Sign in | ATCM'
  },
  {
    path: 'change-password',
    component: ChangePassword,
    canActivate: [signedInGuard],
    title: 'Change password | ATCM'
  },
  {
    path: 'admin/users',
    component: AdminUsers,
    canActivate: [adminGuard],
    title: 'Manage users | ATCM'
  },
  {
    path: '',
    component: Workspace,
    canActivate: [workspaceGuard],
    title: 'Workspace | ATCM'
  },
  {
    path: 'archive',
    component: ArchiveCases,
    canActivate: [workspaceGuard],
    title: 'Archive | ATCM'
  },
  {
    path: 'projects/:projectKey',
    component: Workspace,
    canActivate: [workspaceGuard],
    title: 'Project | ATCM'
  },
  {
    path: 'projects/:projectKey/modules/:moduleKey/cases/:id',
    component: CaseDetail,
    canActivate: [workspaceGuard],
    canDeactivate: [(component: CaseDetail) => component.canLeaveCase()],
    title: 'Test case | ATCM'
  },
  {
    path: 'cases/:id',
    component: CaseDetail,
    canActivate: [workspaceGuard],
    canDeactivate: [(component: CaseDetail) => component.canLeaveCase()],
    title: 'Test case | ATCM'
  },
  { path: '**', redirectTo: '' }
];