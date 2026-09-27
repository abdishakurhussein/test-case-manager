import { Component, DestroyRef, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService, CaseSummary, Module, Project, errorMessage } from './api.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProjectNavigationService } from './project-navigation.service';
import { casePath, idFromSegment, projectPath } from './paths';
import { ConfirmDialogService } from './confirm-dialog.service';

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
  readonly projectNavigation = inject(ProjectNavigationService);
  @ViewChild('editor') editor!: ElementRef<HTMLDialogElement>;
  readonly projects = this.projectNavigation.projects;
  readonly recentProjects = this.projectNavigation.recentProjects;
  readonly cases = signal<CaseSummary[]>([]);
  readonly modules = signal<Module[]>([]);
  readonly selectedProjectId = signal<number | null>(null);
  readonly selectedProject = computed(() =>
    this.projects().find((p) => p.id === this.selectedProjectId()),
  );
  readonly visibleCases = computed(() =>
    this.cases().filter(
      (c) => !this.selectedProjectId() || c.projectId === this.selectedProjectId(),
    ),
  );
  readonly loading = signal(true);
  readonly moduleLoading = signal(false);
  readonly saving = signal(false);
  readonly deletingId = signal<number | null>(null);
  readonly deletingKind = signal<'project' | 'module' | null>(null);
  readonly error = signal('');
  readonly formError = signal('');
  readonly notice = signal('');
  readonly mode = signal<'project' | 'module' | 'case'>('project');
  private moduleRequest = 0;

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
    this.notice.set(deleted === 'case' ? 'Test case deleted.' : 'Project deleted.');
    void this.router.navigate([], {
      queryParams: { deleted: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  this.route.paramMap
  .pipe(takeUntilDestroyed(this.destroyRef))
  .subscribe((params) => {
    const rawId = params.get('projectKey');
    const projectId = rawId === null ? null : idFromSegment(rawId);

    void this.loadRoute(projectId, rawId !== null);
  });
}

private async loadRoute(projectId: number | null, hasProjectKey: boolean): Promise<void> {
  await this.reload();

  if (this.error()) return;

  if (
    hasProjectKey &&
    (projectId === null || !this.projects().some((project) => project.id === projectId))
  ) {
    await this.selectProject(null);
    this.error.set('That project does not exist or has been deleted.');
    return;
  }

  if (projectId !== null) {
    this.projectNavigation.remember(projectId);
    const project = this.projects().find((item) => item.id === projectId)!;
    const canonical = this.router.serializeUrl(this.router.createUrlTree(projectPath(project)));
    if (this.router.url.split('?')[0] !== canonical) {
      await this.router.navigateByUrl(canonical, { replaceUrl: true });
      return;
    }
  }

  await this.selectProject(projectId);
}

  async reload() {
    this.loading.set(true);
    this.error.set('');
    try {
      const [, cases] = await Promise.all([
        this.projectNavigation.refresh(),
        firstValueFrom(this.api.cases()),
      ]);

      this.cases.set(cases);
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
    if (id === null) return;
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
    this.notice.set(`Project "${project.name}" deleted.`);
  } catch (error) {
    this.error.set(errorMessage(error));
  } finally {
    this.deletingId.set(null);
    this.deletingKind.set(null);
  }
}

async deleteModule(module: Module): Promise<void> {
  if (this.deletingKind() !== null) return;

  if (module.testCaseCount > 0) {
    this.error.set('Delete this module’s test cases before deleting the module.');
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
    await this.reload();

    if (projectId !== null) {
      await this.selectProject(projectId);
    }

    this.notice.set(`Module "${module.name}" deleted.`);
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
        this.notice.set('Project created. Add its first module next.');
        await this.projectNavigation.refresh();
        await this.router.navigate(projectPath(project));
      } else {
        const projectId = this.selectedProjectId();
        if (projectId === null) throw new Error('Select a project first.');
        await firstValueFrom(this.api.createModule({ ...this.nameForm.getRawValue(), projectId }));
        this.editor.nativeElement.close();
        this.notice.set('Module created. You can now create a test case.');
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
