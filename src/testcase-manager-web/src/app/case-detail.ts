import { Component, DestroyRef, ElementRef, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { DatePipe, DOCUMENT } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, firstValueFrom, switchMap, tap } from 'rxjs';
import { ApiService, ManualRun, ManualStepDecision, StepOutcome, StoredAttachment, TestCase, TestStep, UpdateCase, errorMessage } from './api.service';
import { casePath, projectPath } from './paths';
import { ConfirmDialogService } from './confirm-dialog.service';
import { ToastService } from './toast.service';

interface FailureDetails {
  actualResult: string;
  canReplicate: boolean | null;
  onlyUserAffected: boolean | null;
  canReplicateUnknown: boolean;
  onlyUserAffectedUnknown: boolean;
}
type AnswerChoice = 'Yes' | 'No' | 'Unknown' | null;

@Component({
  selector: 'app-case-detail',
  imports: [RouterLink, DatePipe],
  templateUrl: './case-detail.html',
})
export class CaseDetail {
  readonly projectPath = projectPath;
  readonly item = signal<TestCase | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly savingStatus = signal(false);
  readonly statusError = signal('');
  readonly deleting = signal(false);
  readonly archiveBusy = signal(false);
  readonly archiveError = signal('');
  readonly deleteError = signal('');
  readonly removingStepId = signal<number | null>(null);
  readonly runs = signal<ManualRun[]>([]);
  readonly evidence = signal<StoredAttachment[]>([]);
  readonly evidenceError = signal('');
  readonly uploadingEvidenceId = signal<number | null>(null);
  readonly runsLoading = signal(true);
  readonly newRunStarted = signal(false);
  readonly decisions = signal<Record<number, StepOutcome>>({});
  readonly failureDetails = signal<Record<number, FailureDetails>>({});
  readonly failureStepId = signal<number | null>(null);
  readonly actualResult = signal('');
  readonly canReplicate = signal<AnswerChoice>(null);
  readonly onlyUserAffected = signal<AnswerChoice>(null);
  readonly failureError = signal('');
  @ViewChild('failureDialog') failureDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('addActionDialog') addActionDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('editDialog') editDialog!: ElementRef<HTMLDialogElement>;
  readonly editValue = signal<UpdateCase | null>(null);
  readonly editOriginal = signal('');
  readonly editError = signal('');
  readonly savingEdit = signal(false);
  readonly newAction = signal('');
  readonly newExpectedResult = signal('');
  readonly addingAction = signal(false);
  readonly addActionError = signal('');
  readonly savingRun = signal(false);
  readonly runError = signal('');
  readonly latestSavedSteps = computed(() => new Map(
    (this.runs()[0]?.steps ?? []).map(step => [step.originalStepId, step]),
  ));
  readonly recordingRun = computed(() => {
    const status = this.item()?.status;
    return status === 'Ready' && !this.runsLoading() &&
      (this.runs().length === 0 || this.newRunStarted());
  });

  isStepDone(step: TestStep): boolean {
    const saved = this.latestSavedSteps().get(step.id);
    return !!saved && saved.position === step.position &&
      saved.action === step.action && saved.expectedResult === step.expectedResult;
  }
  readonly markedCount = computed(() =>
    this.item()?.steps.filter(step => this.decisions()[step.id]).length ?? 0,
  );
  readonly passedCount = computed(() =>
    this.item()?.steps.filter(step => this.decisions()[step.id] === 'Passed').length ?? 0,
  );
  readonly failedCount = computed(() =>
    this.item()?.steps.filter(step => this.decisions()[step.id] === 'Failed').length ?? 0,
  );
  readonly nextUnrecordedStep = computed(() =>
    this.item()?.steps.find(step => !this.decisions()[step.id]) ?? null,
  );
  readonly hasUnsavedRun = computed(() => this.markedCount() > 0 ||
    (this.failureStepId() !== null && this.actualResult().trim().length > 0));
  readonly canSaveRun = computed(() => {
    const item = this.item();
    return !!item && item.status === 'Ready' && this.recordingRun() && item.steps.length > 0 &&
      this.markedCount() === item.steps.length && !this.savingRun();
  });

  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);

  async canLeaveCase(): Promise<boolean> {
    if (!this.hasUnsavedRun()) return true;
    return this.confirmDialog.confirm({
      title: 'Leave this unfinished run?',
      message: 'Your Passed/Failed choices and failure details have not been saved. Leaving will discard them.',
      confirmLabel: 'Leave without saving',
    });
  }

  @HostListener('window:beforeunload', ['$event'])
  warnBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedRun()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }

  jumpToNextUnrecorded(): void {
    if (!this.recordingRun()) return;
    const step = this.nextUnrecordedStep();
    if (!step) return;
    const target = this.document.getElementById(`run-step-${step.id}`);
    const reducedMotion = this.document.defaultView?.matchMedia('(prefers-reduced-motion: reduce)').matches;
    target?.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'center' });
    target?.focus({ preventScroll: true });
  }

  constructor() {
    this.route.paramMap
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set('');
          this.item.set(null);
        }),
        switchMap(params => this.api.case(Number(params.get('id'))).pipe(
          catchError(error => {
            this.error.set(error.status === 404
              ? 'This test case does not exist.' : errorMessage(error));
            this.loading.set(false);
            return EMPTY;
          }),
        )),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(item => {
        this.item.set(item);
        this.loading.set(false);
        this.runs.set([]);
        this.evidence.set([]);
        this.runsLoading.set(true);
        this.newRunStarted.set(false);
        void this.loadRuns(item.id);
        void this.loadEvidence(item.id);

        // Old or renamed links still work, then become the canonical readable URL.
        const canonical = this.router.serializeUrl(this.router.createUrlTree(casePath(item)));
        if (this.router.url.split('?')[0] !== canonical) {
          void this.router.navigateByUrl(canonical, { replaceUrl: true });
        }
      });
  }

  private async loadRuns(caseId: number): Promise<void> {
    try {
      const runs = await firstValueFrom(this.api.runs(caseId));
      if (this.item()?.id === caseId) this.runs.set(runs);
    } catch (error) {
      this.runError.set(errorMessage(error));
    } finally {
      if (this.item()?.id === caseId) this.runsLoading.set(false);
    }
  }

  private async loadEvidence(caseId: number): Promise<void> {
    try {
      const files = await firstValueFrom(this.api.caseEvidence(caseId));
      if (this.item()?.id === caseId) this.evidence.set(files);
    } catch (error) {
      if (this.item()?.id === caseId) this.evidenceError.set(errorMessage(error));
    }
  }

  evidenceForStep(stepResultId: number): StoredAttachment[] {
    return this.evidence().filter(file => file.stepResultId === stepResultId);
  }

  async uploadEvidence(runId: number, stepResultId: number, fileInput: HTMLInputElement,
    captionInput: HTMLInputElement): Promise<void> {
    const item = this.item();
    const file = fileInput.files?.[0];
    if (!item || !file || this.uploadingEvidenceId() !== null) return;
    this.uploadingEvidenceId.set(stepResultId);
    this.evidenceError.set('');
    try {
      await firstValueFrom(this.api.uploadEvidence(item.id, runId, stepResultId, file, captionInput.value));
      fileInput.value = '';
      captionInput.value = '';
      await this.loadEvidence(item.id);
      this.toast.show('Image evidence saved with this failed step.');
    } catch (error) {
      this.evidenceError.set(errorMessage(error));
    } finally {
      this.uploadingEvidenceId.set(null);
    }
  }

  startAnotherRun(): void {
    if (this.item()?.status !== 'Ready' || this.runsLoading()) return;
    this.decisions.set({});
    this.failureDetails.set({});
    this.runError.set('');
    this.newRunStarted.set(true);
  }

  async openEdit(): Promise<void> {
    const item = this.item();
    if (!item || item.status === 'Complete' || item.status === 'Archived') return;
    if (this.markedCount() > 0 && !await this.confirmDialog.confirm({
      title: 'Edit while a run is in progress?',
      message: 'Saving case edits will clear the Passed/Failed choices you have not saved as a run yet.',
      confirmLabel: 'Continue editing',
    })) return;
    const value: UpdateCase = {
      title: item.title,
      description: item.description,
      preconditions: item.preconditions,
      priority: item.priority,
      expectedUpdatedAt: item.updatedAt,
      steps: item.steps.map(step => ({ id: step.id, action: step.action, expectedResult: step.expectedResult })),
    };
    this.editValue.set(value);
    this.editOriginal.set(JSON.stringify(value));
    this.editError.set('');
    this.editDialog.nativeElement.showModal();
  }

  editField(field: 'title' | 'description' | 'preconditions' | 'priority', value: string): void {
    this.editValue.update(current => current ? { ...current, [field]: value } : null);
  }

  editStep(index: number, field: 'action' | 'expectedResult', value: string): void {
    this.editValue.update(current => current ? {
      ...current, steps: current.steps.map((step, at) => at === index ? { ...step, [field]: value } : step),
    } : null);
  }

  addEditStep(): void {
    this.editValue.update(current => current && current.steps.length < 100 ? {
      ...current, steps: [...current.steps, { id: null, action: '', expectedResult: '' }],
    } : current);
  }

  moveEditStep(index: number, direction: -1 | 1): void {
    this.editValue.update(current => {
      if (!current || index + direction < 0 || index + direction >= current.steps.length) return current;
      const steps = [...current.steps];
      [steps[index], steps[index + direction]] = [steps[index + direction], steps[index]];
      return { ...current, steps };
    });
  }

  removeEditStep(index: number): void {
    this.editValue.update(current => current && current.steps.length > 1 ? {
      ...current, steps: current.steps.filter((_, at) => at !== index),
    } : current);
  }

  async closeEdit(): Promise<void> {
    if (this.savingEdit()) return;
    const value = this.editValue();
    if (value && JSON.stringify(value) !== this.editOriginal() && !await this.confirmDialog.confirm({
      title: 'Discard case edits?',
      message: 'Your changes to this test case have not been saved.',
      confirmLabel: 'Discard changes',
    })) return;
    this.editDialog.nativeElement.close();
    this.editValue.set(null);
  }

  cancelEdit(event: Event): void {
    event.preventDefault();
    void this.closeEdit();
  }

  async saveEdit(): Promise<void> {
    const item = this.item();
    const value = this.editValue();
    if (!item || !value || this.savingEdit()) return;
    const clean: UpdateCase = {
      ...value, title: value.title.trim(), description: value.description.trim(),
      preconditions: value.preconditions.trim(),
      steps: value.steps.map(step => ({ ...step, action: step.action.trim(), expectedResult: step.expectedResult.trim() })),
    };
    if (!clean.title || clean.title.length > 200 || clean.description.length > 4000 ||
      clean.preconditions.length > 4000 || clean.steps.length < 1 || clean.steps.length > 100 ||
      clean.steps.some(step => !step.action || !step.expectedResult ||
        step.action.length > 2000 || step.expectedResult.length > 2000)) {
      this.editError.set('Add a title and at least one complete step. Title: 200 characters; details: 4,000; each step field: 2,000.');
      return;
    }
    this.savingEdit.set(true);
    this.editError.set('');
    try {
      const updated = await firstValueFrom(this.api.updateCase(item.id, clean));
      this.item.set(updated);
      this.decisions.set({});
      this.failureDetails.set({});
      this.editDialog.nativeElement.close();
      this.editValue.set(null);
      this.toast.show('Test case updated. Saved run history is unchanged; unsaved run selections were cleared.');
    } catch (error) {
      this.editError.set(errorMessage(error));
    } finally {
      this.savingEdit.set(false);
    }
  }

  setDecision(stepId: number, outcome: StepOutcome | null): void {
    if (!this.recordingRun()) return;
    this.decisions.update(current => {
      const next = { ...current };
      if (outcome === null) delete next[stepId];
      else next[stepId] = outcome;
      return next;
    });
    this.failureDetails.update(current => {
      const next = { ...current };
      if (outcome !== 'Failed') delete next[stepId];
      return next;
    });
    this.runError.set('');
    this.toast.dismiss();
  }

  openAddAction(): void {
    if (!this.item() || this.item()!.status === 'Complete' ||
      this.item()!.status === 'Archived' || this.item()!.steps.length >= 100) return;
    this.newAction.set('');
    this.newExpectedResult.set('');
    this.addActionError.set('');
    this.addActionDialog.nativeElement.showModal();
  }

  closeAddAction(): void {
    if (!this.addingAction()) this.addActionDialog.nativeElement.close();
  }

  cancelAddAction(event: Event): void {
    event.preventDefault();
    this.closeAddAction();
  }

  async addAction(): Promise<void> {
    const item = this.item();
    const action = this.newAction().trim();
    const expectedResult = this.newExpectedResult().trim();
    if (!item || item.status === 'Complete' || item.status === 'Archived' || this.addingAction()) return;
    if (!action || !expectedResult || action.length > 2000 || expectedResult.length > 2000) {
      this.addActionError.set('Enter an action and expected result (up to 2,000 characters each).');
      return;
    }
    this.addingAction.set(true);
    this.addActionError.set('');
    try {
      this.item.set(await firstValueFrom(this.api.addStep(item.id, { action, expectedResult })));
      this.addActionDialog.nativeElement.close();
      this.toast.show('Action added to this test case. Previous run history is unchanged.');
    } catch (error) {
      this.addActionError.set(errorMessage(error));
    } finally {
      this.addingAction.set(false);
    }
  }

  openFailure(stepId: number): void {
    if (!this.recordingRun()) return;
    const existing = this.failureDetails()[stepId];
    this.failureStepId.set(stepId);
    this.actualResult.set(existing?.actualResult ?? '');
    this.canReplicate.set(existing ? existing.canReplicateUnknown ? 'Unknown' : existing.canReplicate ? 'Yes' : 'No' : null);
    this.onlyUserAffected.set(existing ? existing.onlyUserAffectedUnknown ? 'Unknown' : existing.onlyUserAffected ? 'Yes' : 'No' : null);
    this.failureError.set('');
    this.failureDialog.nativeElement.showModal();
  }

  closeFailure(): void {
    this.failureDialog.nativeElement.close();
    this.failureStepId.set(null);
  }

  cancelFailure(event: Event): void {
    event.preventDefault();
    this.closeFailure();
  }

  recordFailure(): void {
    if (!this.recordingRun()) return;
    const stepId = this.failureStepId();
    const actualResult = this.actualResult().trim();
    const canReplicate = this.canReplicate();
    const onlyUserAffected = this.onlyUserAffected();
    if (stepId === null) return;
    if (!actualResult || actualResult.length > 4000 || canReplicate === null || onlyUserAffected === null) {
      this.failureError.set('Enter the actual result (up to 4,000 characters) and answer both questions.');
      return;
    }
    this.failureDetails.update(current => ({
      ...current, [stepId]: {
        actualResult,
        canReplicate: canReplicate === 'Unknown' ? null : canReplicate === 'Yes',
        onlyUserAffected: onlyUserAffected === 'Unknown' ? null : onlyUserAffected === 'Yes',
        canReplicateUnknown: canReplicate === 'Unknown',
        onlyUserAffectedUnknown: onlyUserAffected === 'Unknown',
      },
    }));
    this.setDecision(stepId, 'Failed');
    this.closeFailure();
  }

  async saveRun(): Promise<void> {
    const item = this.item();
    if (!item || !this.canSaveRun()) return;
    this.savingRun.set(true);
    this.runError.set('');
    try {
      const steps: ManualStepDecision[] = item.steps.map(step => ({
        stepId: step.id,
        outcome: this.decisions()[step.id],
        ...(this.decisions()[step.id] === 'Failed' ? this.failureDetails()[step.id] : {}),
      }));
      const run = await firstValueFrom(this.api.saveRun(item.id, steps));
      this.runs.update(existing => [run, ...existing]);
      this.decisions.set({});
      this.failureDetails.set({});
      this.newRunStarted.set(false);
      this.toast.show(`Run saved: ${run.result}.`);
    } catch (error) {
      this.runError.set(errorMessage(error));
    } finally {
      this.savingRun.set(false);
    }
  }

  async removeStep(stepId: number): Promise<void> {
    const item = this.item();
    const step = item?.steps.find(candidate => candidate.id === stepId);
    if (!item || item.status === 'Complete' || item.status === 'Archived' ||
      !step || this.removingStepId() !== null || this.deleting()) return;
    if (item.steps.length === 1) {
      await this.deleteCase();
      return;
    }
    if (!await this.confirmDialog.confirm({
      title: `Remove step ${step.position}?`,
      message: 'This removes the step from the reusable test case. Saved run history stays unchanged, but any unsaved run choices will be cleared.',
      confirmLabel: 'Remove step',
    })) return;

    this.removingStepId.set(stepId);
    this.runError.set('');
    try {
      await firstValueFrom(this.api.deleteStep(item.id, stepId));
      this.item.set(await firstValueFrom(this.api.case(item.id)));
      this.decisions.set({});
      this.failureDetails.set({});
      this.toast.show('Test step removed. Any unsaved run selections were cleared.');
    } catch (error) {
      this.runError.set(errorMessage(error));
    } finally {
      this.removingStepId.set(null);
    }
  }

  async changeStatus(status: 'Draft' | 'Ready' | 'Complete'): Promise<void> {
    const current = this.item();
    if (!current || current.status === 'Complete' || current.status === 'Archived' || this.savingStatus()) return;
    if (status === 'Complete' && (current.status !== 'Ready' || this.runs().length === 0)) return;
    if (this.hasUnsavedRun() && !await this.confirmDialog.confirm({
      title: 'Discard unfinished run?',
      message: 'Changing this case’s status will clear the Passed/Failed choices you have not saved.',
      confirmLabel: 'Change status',
    })) return;
    this.savingStatus.set(true);
    this.statusError.set('');
    try {
      await firstValueFrom(this.api.updateCaseStatus(current.id, status));
      this.item.set(await firstValueFrom(this.api.case(current.id)));
      this.decisions.set({});
      this.failureDetails.set({});
      this.newRunStarted.set(false);
      this.toast.show(`Test case marked ${status.toLowerCase()}.`);
    } catch (error) {
      this.statusError.set(errorMessage(error));
    } finally {
      this.savingStatus.set(false);
    }
  }

  async deleteCase(): Promise<void> {
    const current = this.item();
    if (!current || this.deleting()) return;
    if (!await this.confirmDialog.confirm({
      title: 'Delete test case?',
      message: `Permanently delete “${current.title}”, its ${current.steps.length} test step(s), and all saved run history and failure details? This cannot be undone.`,
      confirmLabel: 'Delete test case',
      requiredText: 'Confirm',
    })) return;
    this.deleting.set(true);
    this.deleteError.set('');
    try {
      await firstValueFrom(this.api.deleteCase(current.id));
      this.decisions.set({});
      this.failureDetails.set({});
      if (current.status === 'Archived') {
        await this.router.navigate(['/archive'], { queryParams: { projectId: current.projectId } });
      } else {
        await this.router.navigate(projectPath({ id: current.projectId, name: current.projectName }),
          { queryParams: { deleted: 'case' } });
      }
    } catch (error) {
      this.deleteError.set(errorMessage(error));
    } finally {
      this.deleting.set(false);
    }
  }

  async restoreCase(): Promise<void> {
    const current = this.item();
    if (!current || current.status !== 'Archived' || this.archiveBusy()) return;
    if (!await this.confirmDialog.confirm({
      title: 'Restore test case?',
      message: `Return “${current.title}” to ${current.statusBeforeArchive ?? 'Draft'}? Saved runs will remain unchanged.`,
      confirmLabel: 'Restore case',
    })) return;
    this.archiveBusy.set(true);
    this.archiveError.set('');
    try {
      await firstValueFrom(this.api.restoreCase(current.id));
      this.item.set(await firstValueFrom(this.api.case(current.id)));
      this.toast.show('Test case restored to its previous status.');
    } catch (error) {
      this.archiveError.set(errorMessage(error));
    } finally {
      this.archiveBusy.set(false);
    }
  }
}
