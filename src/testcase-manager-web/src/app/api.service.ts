import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';

export interface Project {
  id: number;
  name: string;
  description: string;
  moduleCount: number;
  testCaseCount: number;
  archivedCaseCount: number;
}
export interface Module {
  id: number;
  projectId: number;
  name: string;
  description: string;
  testCaseCount: number;
  archivedCaseCount: number;
}
export interface CaseSummary {
  id: number;
  title: string;
  moduleId: number;
  moduleName: string;
  projectId: number;
  projectName: string;
  priority: string;
  status: string;
  updatedAt: string;
  runCount: number;
  latestRunResult: 'Passed' | 'Failed' | null;
  latestRunAt: string | null;
  archivedAt: string | null;
  statusBeforeArchive: string | null;
}
export interface ProjectOverview {
  totalCases: number;
  draft: number;
  ready: number;
  complete: number;
  archived: number;
  latestPassed: number;
  latestFailed: number;
  neverRun: number;
}
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
export interface CaseSearchOptions {
  projectId?: number;
  moduleId?: number;
  q?: string;
  status?: string;
  priority?: string;
  sort?: string;
  page?: number;
  pageSize?: number;
}
export interface TestStep {
  id: number;
  position: number;
  action: string;
  expectedResult: string;
}
export interface TestCase extends CaseSummary {
  description: string;
  preconditions: string;
  createdAt: string;
  steps: TestStep[];
}
export interface CreateCase {
  title: string;
  description: string;
  preconditions: string;
  moduleId: number;
  priority: string;
  status: string;
  steps: { action: string; expectedResult: string }[];
}
export interface UpdateCase {
  title: string;
  description: string;
  preconditions: string;
  priority: string;
  expectedUpdatedAt: string;
  steps: { id: number | null; action: string; expectedResult: string }[];
}

export type StepOutcome = 'Passed' | 'Failed';
export interface ManualStepDecision {
  stepId: number;
  outcome: StepOutcome;
  actualResult?: string;
  canReplicate?: boolean | null;
  onlyUserAffected?: boolean | null;
  canReplicateUnknown?: boolean;
  onlyUserAffectedUnknown?: boolean;
}
export interface ManualRun {
  id: number;
  testCaseId: number;
  completedAt: string;
  result: StepOutcome;
  steps: {
    originalStepId: number;
    position: number;
    action: string;
    expectedResult: string;
    outcome: StepOutcome;
    actualResult: string | null;
    canReplicate: boolean | null;
    onlyUserAffected: boolean | null;
  }[];
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  // Relative URLs go through Angular's local proxy, not a cloud service.
  projects() {
    return this.http.get<Project[]>('/api/projects');
  }
  projectOverview(id: number) {
    return this.http.get<ProjectOverview>(`/api/projects/${id}/overview`);
  }
  modules(projectId: number) {
    return this.http.get<Module[]>('/api/modules', { params: { projectId } });
  }
  cases() {
    return this.http.get<CaseSummary[]>('/api/testcases');
  }
  searchCases(options: CaseSearchOptions) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(options)) {
      if (value !== undefined && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<CaseSummary>>('/api/testcases/search', { params });
  }
  case(id: number) {
    return this.http.get<TestCase>(`/api/testcases/${id}`);
  }
  updateCaseStatus(id: number, status: 'Draft' | 'Ready' | 'Complete') {
    return this.http.patch<void>(`/api/testcases/${id}/status`, { status });
  }
  archiveCase(id: number) {
    return this.http.post<void>(`/api/testcases/${id}/archive`, {});
  }
  restoreCase(id: number) {
    return this.http.post<void>(`/api/testcases/${id}/restore`, {});
  }
  createProject(value: { name: string; description: string }) {
    return this.http.post<Project>('/api/projects', value);
  }
  createModule(value: { projectId: number; name: string; description: string }) {
    return this.http.post<Module>('/api/modules', value);
  }
  createCase(value: CreateCase) {
    return this.http.post<TestCase>('/api/testcases', value);
  }
  updateCase(id: number, value: UpdateCase) {
    return this.http.put<TestCase>(`/api/testcases/${id}`, value);
  }
  deleteCase(id: number) {
    return this.http.delete<void>(`/api/testcases/${id}`);
  }

  deleteStep(caseId: number, stepId: number) {
    return this.http.delete<void>(`/api/testcases/${caseId}/steps/${stepId}`);
  }

  addStep(caseId: number, step: { action: string; expectedResult: string }) {
    return this.http.post<TestCase>(`/api/testcases/${caseId}/steps`, step);
  }

  runs(caseId: number) {
    return this.http.get<ManualRun[]>(`/api/testcases/${caseId}/runs`);
  }

  saveRun(caseId: number, steps: ManualStepDecision[]) {
    return this.http.post<ManualRun>(`/api/testcases/${caseId}/runs`, { steps });
  }

  deleteModule(id: number) {
    return this.http.delete<void>(`/api/modules/${id}`);
  }

  deleteProject(id: number) {
    return this.http.delete<void>(`/api/projects/${id}`);
  }
}

export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0)
      return 'Cannot reach the local API. Check that it is running, then try again. Your form has been kept.';
    if (error.status >= 500)
      return 'The local API returned a server error. Check its terminal output and database migrations, then try again. Your form has been kept.';
    const errors = error.error?.errors;
    if (errors) return Object.values(errors).flat().join(' ');
    return error.error?.detail ?? error.error?.title ?? 'The request could not be completed.';
  }
  return 'Something went wrong. Please try again.';
}
