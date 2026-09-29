import { Component, DestroyRef, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { combineLatest, firstValueFrom } from 'rxjs';
import { ApiService, CaseSummary, Module, Project, ProjectOverview, errorMessage } from './api.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProjectNavigationService } from './project-navigation.service';
import { casePath, idFromSegment, projectPath } from './paths';
import { ConfirmDialogService } from './confirm-dialog.service';
import { ToastService } from './toast.service';

const requiredText = [Validators.required, Validators.pattern(/\S/)];

@Component({
  selector: 'app-workspace',
  imports: [ReactiveFormsModule, RouterLink, DatePipe],
  templateUrl: './workspace.html',
})
export class Workspace implements OnInit {
  readonly projectPath = projectPath;
  readonly casePath = casePath;
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  readonly projectNavigation = inject(ProjectNavigationService);
  @ViewChild('editor') editor!: ElementRef<HTMLDialogElement>;
  readonly projects = this.projectNavigation.projects;
  readonly recentProjects = this.projectNavigation.recentProjects;
  readonly cases = signal<CaseSummary[]>([]);
  readonly totalCases = signal(0);
  readonly caseLoading = signal(false);
  readonly searchTerm = signal('');
  readonly statusFilter = signal('');
  readonly priorityFilter = signal('');
  readonly sortOrder = signal('updated-desc');
  readonly page = signal(1);
  readonly pageSize = 10;
  readonly modules = signal<Module[]>([]);
  readonly projectOverview = signal<ProjectOverview | null>(null);
  readonly overviewLoading = signal(false);
  readonly overviewError = signal('');
  readonly selectedProjectId = signal<number | null>(null);
  readonly selectedModuleId = signal<number | null>(null);
  readonly selectedProject = computed(() =>
    this.projects().find((p) => p.id === this.selectedProjectId()),
  );
  readonly selectedModule = computed(() =>
    this.modules().find(module => module.id === this.selectedModuleId()),
  );
  readonly visibleCases = this.cases;
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCases() / this.pageSize)));
  readonly isRecentView = computed(() => this.selectedProjectId() === null &&
    !this.searchTerm().trim() && !this.statusFilter() && !this.priorityFilter() &&
    this.sortOrder() === 'updated-desc');
  readonly loading = signal(true);
  readonly moduleLoading = signal(false);
  readonly saving = signal(false);
  readonly deletingId = signal<number | null>(null);
  readonly deletingKind = signal<'project' | 'module' | null>(null);
  readonly archivingCaseId = signal<number | null>(null);
  readonly error = signal('');
  readonly formError = signal('');
  readonly mode = signal<'project' | 'module' | 'case'>('project');
  private moduleRequest = 0;
  private caseRequest = 0;
  private routeRequest = 0;
  private overviewRequest = 0;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => {
      if (this.searchTimer) clearTimeout(this.searchTimer);
    });
  }

  readonly nameForm = this.fb.group({
    name: ['', [...requiredText, Validators.maxLength(120)]],
    description: ['', Validators.maxLength(2000)],
  });
  readonly caseForm = this.fb.group({
    title: ['', [...requiredText, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(4000)],
    preconditions: ['', Validators.maxLength(4000)],
    moduleId: [0, Validators.min(1)],
    priority: ['Major'],
    status: ['Draft'],
    steps: this.fb.array([this.newStep()]),
  });
  get steps() {
    return this.caseForm.controls.steps;
  }
  private newStep() {
    return this.fb.group({
      action: ['', [...requiredText, Validators.maxLength(2000)]],
      expectedResult: ['', [...requiredText, Validators.maxLength(2000)]],
    });
  }
  ngOnInit() {
    const deleted = new URLSearchParams(window.location.search).get('deleted');
    if (deleted === 'case' || deleted === 'project') {
      this.toast.show(deleted === 'case' ? 'Test case deleted.' : 'Project deleted.');
      void this.router.navigate([], {
        queryParams: { deleted: null }, queryParamsHandling: 'merge', replaceUrl: true,
      });
    }

    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([params, query]) => {
        const rawId = params.get('projectKey');
        const projectId = rawId === null ? null : idFromSegment(rawId);
        const rawModule = query.get('module');
        const moduleId = rawModule && /^[1-9]\d*$/.test(rawModule) ? Number(rawModule) : null;
        void this.loadRoute(projectId, rawId !== null, moduleId, rawModule !== null);
      });
  }

  private async loadRoute(projectId: number | null, hasProjectKey: boolean,
    moduleId: number | null, hasModuleQuery: boolean): Promise<void> {
    const request = ++this.routeRequest;
    if (projectId !== this.selectedProjectId()) {
      if (this.searchTimer) clearTimeout(this.searchTimer);
      this.searchTerm.set('');
      this.statusFilter.set('');
      this.priorityFilter.set('');
      this.sortOrder.set('updated-desc');
    }
    this.loading.set(true);
    this.error.set('');
    this.cases.set([]);
    this.totalCases.set(0);
    this.projectOverview.set(null);
    try {
      await this.projectNavigation.refresh();
      if (request !== this.routeRequest) return;
      if (hasProjectKey && (projectId === null || !this.projects().some(p => p.id === projectId))) {
        this.selectedProjectId.set(null);
        this.error.set('That project does not exist or has been deleted.');
        return;
      }

      this.selectedProjectId.set(projectId);
      await this.selectProject(projectId);
      if (request !== this.routeRequest) return;
      if (this.error()) return;
      if (hasModuleQuery && (projectId === null || moduleId === null ||
        !this.modules().some(module => module.id === moduleId))) {
        this.error.set('That module does not belong to this project.');
        return;
      }
      this.selectedModuleId.set(moduleId);
      this.page.set(1);

      if (projectId !== null) void this.loadOverview(projectId);

      if (projectId !== null) {
        this.projectNavigation.remember(projectId);
        const project = this.projects().find(item => item.id === projectId)!;
        const canonical = this.router.serializeUrl(this.router.createUrlTree(projectPath(project),
          { queryParams: moduleId ? { module: moduleId } : {} }));
        if (this.router.url !== canonical && !this.router.url.includes('deleted=')) {
          await this.router.navigateByUrl(canonical, { replaceUrl: true });
          return;
        }
      }
      await this.loadCases();
    } catch (error) {
      if (request === this.routeRequest) this.error.set(errorMessage(error));
    } finally {
      if (request === this.routeRequest) this.loading.set(false);
    }
  }

  async reload() {
    this.loading.set(true);
    this.error.set('');
    try {
      await this.projectNavigation.refresh();
      if (this.selectedProjectId() !== null) void this.loadOverview(this.selectedProjectId()!);
      await this.loadCases();
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  async selectProject(id: number | null) {
    this.selectedProjectId.set(id);
    this.modules.set([]);
    this.error.set('');
    const request = ++this.moduleRequest;
    this.moduleLoading.set(id !== null);
    if (id === null) {
      this.moduleLoading.set(false);
      return;
    }
    try {
      const modules = await firstValueFrom(this.api.modules(id));
      // Ignore an older response when the user has selected another project.
      if (request === this.moduleRequest) this.modules.set(modules);
    } catch (error) {
      if (request === this.moduleRequest) this.error.set(errorMessage(error));
    } finally {
      if (request === this.moduleRequest) this.moduleLoading.set(false);
    }
  }

  async loadOverview(projectId: number): Promise<void> {
    const request = ++this.overviewRequest;
    this.overviewLoading.set(true);
    this.overviewError.set('');
    try {
      const overview = await firstValueFrom(this.api.projectOverview(projectId));
      if (request === this.overviewRequest && this.selectedProjectId() === projectId)
        this.projectOverview.set(overview);
    } catch (error) {
      if (request === this.overviewRequest) this.overviewError.set(errorMessage(error));
    } finally {
      if (request === this.overviewRequest) this.overviewLoading.set(false);
    }
  }

  async loadCases(): Promise<void> {
    const request = ++this.caseRequest;
    this.caseLoading.set(true);
    try {
      const result = await firstValueFrom(this.api.searchCases({
        projectId: this.selectedProjectId() ?? undefined,
        moduleId: this.selectedModuleId() ?? undefined,
        q: this.searchTerm().trim(), status: this.statusFilter(),
        priority: this.priorityFilter(), sort: this.sortOrder(),
        page: this.page(), pageSize: this.pageSize,
      }));
      if (request !== this.caseRequest) return;
      this.cases.set(result.items);
      this.totalCases.set(result.totalCount);
    } catch (error) {
      if (request === this.caseRequest) {
        this.cases.set([]);
        this.totalCases.set(0);
        this.error.set(errorMessage(error));
      }
    } finally {
      if (request === this.caseRequest) this.caseLoading.set(false);
    }
  }

  onSearchInput(value: string): void {
    this.searchTerm.set(value);
    this.page.set(1);
    this.error.set('');
    ++this.caseRequest;
    this.caseLoading.set(true);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.loadCases(), 300);
  }

  setFilter(kind: 'status' | 'priority' | 'sort', value: string): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    if (kind === 'status') this.statusFilter.set(value);
    else if (kind === 'priority') this.priorityFilter.set(value);
    else this.sortOrder.set(value);
    this.page.set(1);
    this.error.set('');
    void this.loadCases();
  }

  clearFilters(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTerm.set('');
    this.statusFilter.set('');
    this.priorityFilter.set('');
    this.sortOrder.set('updated-desc');
    this.page.set(1);
    const project = this.selectedProject();
    if (project && this.selectedModuleId() !== null) {
      void this.router.navigate(projectPath(project));
    } else {
      this.error.set('');
      void this.loadCases();
    }
  }

  chooseModule(value: string): void {
    const project = this.selectedProject();
    if (!project) return;
    if (this.searchTimer) clearTimeout(this.searchTimer);
    void this.router.navigate(projectPath(project), { queryParams: value ? { module: value } : {} });
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page()) return;
    this.page.set(page);
    this.error.set('');
    void this.loadCases();
  }

  async archiveCase(item: CaseSummary): Promise<void> {
    if (this.archivingCaseId() !== null || this.selectedProjectId() === null) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Archive test case?',
      message: `Move “${item.title}” out of this project's active test cases? Its steps and saved runs will remain in the Archive and the case can be restored.`,
      confirmLabel: 'Archive case',
    });
    if (!confirmed) return;

    this.archivingCaseId.set(item.id);
    this.error.set('');
    try {
      await firstValueFrom(this.api.archiveCase(item.id));
      this.page.set(1);
      await Promise.all([this.loadCases(), this.projectNavigation.refresh()]);
      const projectId = this.selectedProjectId();
      if (projectId !== null) {
        await this.selectProject(projectId);
        await this.loadOverview(projectId);
      }
      this.toast.show(`“${item.title}” moved to the Archive.`);
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.archivingCaseId.set(null);
    }
  }

  async deleteProject(project: Project): Promise<void> {
  if (this.deletingKind() !== null) return;

  if (project.moduleCount > 0) {
    this.error.set('Delete this project’s modules before deleting the project.');
    return;
  }

  const confirmed = await this.confirmDialog.confirm({
    title: 'Delete project?',
    message: `Delete “${project.name}”? This cannot be undone.`,
    confirmLabel: 'Delete project',
  });

  if (!confirmed) return;

  this.deletingKind.set('project');
  this.deletingId.set(project.id);
  this.error.set('');

  try {
    await firstValueFrom(this.api.deleteProject(project.id));
    this.projectNavigation.forget(project.id);
    await this.projectNavigation.refresh();
    await this.router.navigate(['/']);
    this.toast.show(`Project "${project.name}" deleted.`);
  } catch (error) {
    this.error.set(errorMessage(error));
  } finally {
    this.deletingId.set(null);
    this.deletingKind.set(null);
  }
}

async deleteModule(module: Module): Promise<void> {
  if (this.deletingKind() !== null) return;

  if (module.testCaseCount + module.archivedCaseCount > 0) {
    this.error.set('Delete or restore this module’s archived and active test cases before deleting the module.');
    return;
  }

  const confirmed = await this.confirmDialog.confirm({
    title: 'Delete module?',
    message: `Delete “${module.name}”? This cannot be undone.`,
    confirmLabel: 'Delete module',
  });

  if (!confirmed) return;

  this.deletingKind.set('module');
  this.deletingId.set(module.id);
  this.error.set('');

  try {
    await firstValueFrom(this.api.deleteModule(module.id));

    const projectId = this.selectedProjectId();
      if (this.selectedModuleId() === module.id && this.selectedProject()) {
        await this.router.navigate(projectPath(this.selectedProject()!));
        this.toast.show(`Module "${module.name}" deleted.`);
        return;
      }
    await this.reload();

    if (projectId !== null) {
      await this.selectProject(projectId);
    }

    this.toast.show(`Module "${module.name}" deleted.`);
  } catch (error) {
    this.error.set(errorMessage(error));
  } finally {
    this.deletingId.set(null);
    this.deletingKind.set(null);
  }
}

  open(mode: 'project' | 'module' | 'case', moduleId = 0) {
    this.mode.set(mode);
    this.formError.set('');
    this.nameForm.reset();
    this.steps.clear();
    this.steps.push(this.newStep());
    this.caseForm.reset({
      title: '',
      description: '',
      preconditions: '',
      moduleId: moduleId || this.modules()[0]?.id || 0,
      priority: 'Major',
      status: 'Draft',
    });
    this.editor.nativeElement.showModal();
  }
  close() {
    if (this.saving()) return;
    const dirty = this.mode() === 'case' ? this.caseForm.dirty : this.nameForm.dirty;
    if (dirty && !window.confirm('Discard your unsaved changes?')) return;
    this.editor.nativeElement.close();
  }
  cancel(event: Event) {
    event.preventDefault();
    this.close();
  }
  addStep() {
    if (this.steps.length < 100) {
      this.steps.push(this.newStep());
      this.caseForm.markAsDirty();
    }
  }
  removeStep(index: number) {
    if (this.steps.length > 1) {
      this.steps.removeAt(index);
      this.caseForm.markAsDirty();
    }
  }
  moveStep(index: number, offset: number) {
    const next = index + offset;
    if (next < 0 || next >= this.steps.length) return;
    const step = this.steps.at(index);
    this.steps.removeAt(index);
    this.steps.insert(next, step);
    this.caseForm.markAsDirty();
  }

  async save() {
    if (this.saving()) return;
    const form = this.mode() === 'case' ? this.caseForm : this.nameForm;
    form.markAllAsTouched();
    this.formError.set('');
    if (form.invalid) {
      this.formError.set(
        'Complete all required fields and stay within the displayed character limits.',
      );
      return;
    }
    this.saving.set(true);
    try {
      if (this.mode() === 'case') {
        const item = await firstValueFrom(this.api.createCase(this.caseForm.getRawValue()));
        this.editor.nativeElement.close();
        await this.router.navigate(casePath(item));
      } else if (this.mode() === 'project') {
        const project = await firstValueFrom(this.api.createProject(this.nameForm.getRawValue()));
        this.editor.nativeElement.close();
        this.toast.show('Project created. Add its first module next.');
        await this.projectNavigation.refresh();
        await this.router.navigate(projectPath(project));
      } else {
        const projectId = this.selectedProjectId();
        if (projectId === null) throw new Error('Select a project first.');
        await firstValueFrom(this.api.createModule({ ...this.nameForm.getRawValue(), projectId }));
        this.editor.nativeElement.close();
        this.toast.show('Module created. You can now create a test case.');
        await this.reload();
        await this.selectProject(projectId);
      }
    } catch (error) {
      this.formError.set(errorMessage(error));
    } finally {
      this.saving.set(false);
    }
  }
}
