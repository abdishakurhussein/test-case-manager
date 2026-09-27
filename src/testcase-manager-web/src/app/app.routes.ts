import { Routes } from '@angular/router';
import { Workspace } from './workspace';
import { CaseDetail } from './case-detail';

export const routes: Routes = [
  { path: '', component: Workspace, title: 'Workspace | TestCaseManager' },
  { path: 'projects/:projectKey', component: Workspace, title: 'Project | TestCaseManager' },
  { path: 'projects/:projectKey/modules/:moduleKey/cases/:id', component: CaseDetail, title: 'Test case | TestCaseManager' },
  { path: 'cases/:id', component: CaseDetail, title: 'Test case | TestCaseManager' },
  { path: '**', redirectTo: '' },
];
