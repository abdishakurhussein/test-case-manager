import { Routes } from '@angular/router';
import { Workspace } from './workspace';
import { CaseDetail } from './case-detail';
import { ArchiveCases } from './archive-cases';

export const routes: Routes = [
  { path: '', component: Workspace, title: 'Workspace | ATCM' },
  { path: 'archive', component: ArchiveCases, title: 'Archive | ATCM' },
  { path: 'projects/:projectKey', component: Workspace, title: 'Project | ATCM' },
  { path: 'projects/:projectKey/modules/:moduleKey/cases/:id', component: CaseDetail,
    canDeactivate: [(component: CaseDetail) => component.canLeaveCase()], title: 'Test case | ATCM' },
  { path: 'cases/:id', component: CaseDetail,
    canDeactivate: [(component: CaseDetail) => component.canLeaveCase()], title: 'Test case | ATCM' },
  { path: '**', redirectTo: '' },
];
