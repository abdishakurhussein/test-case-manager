import { Component, DestroyRef, ElementRef, ViewChild, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, firstValueFrom, switchMap, tap } from 'rxjs';
import { ApiService, ManualRun, ManualStepDecision, StepOutcome, TestCase, errorMessage } from './api.service';
import { casePath, projectPath } from './paths';
import { ConfirmDialogService } from './confirm-dialog.service';

interface FailureDetails {
  actualResult: string;
  canReplicate: boolean;
  onlyUserAffected: boolean;
}

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
  readonly notice = signal('');
  readonly savingStatus = signal(false);
  readonly statusError = signal('');
  readonly deleting = signal(false);
  readonly deleteError = signal('');
  readonly removingStepId = signal<number | null>(null);
  readonly runs = signal<ManualRun[]>([]);
  readonly decisions = signal<Record<number, StepOutcome>>({});
  readonly failureDetails = signal<Record<number, FailureDetails>>({});
  readonly failureStepId = signal<number | null>(null);
  readonly actualResult = signal('');
  readonly canReplicate = signal<boolean | null>(null);
  readonly onlyUserAffected = signal<boolean | null>(null);
  readonly failureError = signal('');
  @ViewChild('failureDialog') failureDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('addActionDialog') addActionDialog!: ElementRef<HTMLDialogElement>;
  readonly newAction = signal('');
  readonly newExpectedResult = signal('');
  readonly addingAction = signal(false);
  readonly addActionError = signal('');
  readonly savingRun = signal(false);
  readonly runError = signal('');
  readonly markedCount = computed(() =>
    this.item()?.steps.filter(step => this.decisions()[step.id]).length ?? 0,
  );
  readonly canSaveRun = computed(() => {
    const item = this.item();
    return !!item && item.status !== 'Archived' && item.status !== 'Complete' && item.steps.length > 0 &&
      this.markedCount() === item.steps.length && !this.savingRun();
  });

  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmDialog = inject(ConfirmDialogService);

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
        void this.loadRuns(item.id);

        // Old or renamed links still work, then become the canonical readable URL.
        const canonical = this.router.serializeUrl(this.router.createUrlTree(casePath(item)));
        if (this.router.url.split('?')[0] !== canonical) {
          void this.router.navigateByUrl(canonical, { replaceUrl: true });
        }
      });
  }

  private async loadRuns(caseId: number): Promise<void> {
    try {
      this.runs.set(await firstValueFrom(this.api.runs(caseId)));
    } catch (error) {
      this.runError.set(errorMessage(error));
    }
  }

  setDecision(stepId: number, outcome: StepOutcome | null): void {
    if (this.item()?.status === 'Complete') return;
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
    this.notice.set('');
  }

  openAddAction(): void {
    if (!this.item() || this.item()!.status === 'Complete' || this.item()!.steps.length >= 100) return;
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
    if (!item || item.status === 'Complete' || this.addingAction()) return;
    if (!action || !expectedResult || action.length > 2000 || expectedResult.length > 2000) {
      this.addActionError.set('Enter an action and expected result (up to 2,000 characters each).');
      return;
    }
    this.addingAction.set(true);
    this.addActionError.set('');
    try {
      this.item.set(await firstValueFrom(this.api.addStep(item.id, { action, expectedResult })));
      this.addActionDialog.nativeElement.close();
      this.notice.set('Action added to this test case. Previous run history is unchanged.');
    } catch (error) {
      this.addActionError.set(errorMessage(error));
    } finally {
      this.addingAction.set(false);
    }
  }

  openFailure(stepId: number): void {
    if (this.item()?.status === 'Complete') return;
    const existing = this.failureDetails()[stepId];
    this.failureStepId.set(stepId);
    this.actualResult.set(existing?.actualResult ?? '');
    this.canReplicate.set(existing?.canReplicate ?? null);
    this.onlyUserAffected.set(existing?.onlyUserAffected ?? null);
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
      ...current, [stepId]: { actualResult, canReplicate, onlyUserAffected },
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
      this.notice.set(`Run saved: ${run.result}.`);
    } catch (error) {
      this.runError.set(errorMessage(error));
    } finally {
      this.savingRun.set(false);
    }
  }

  async removeStep(stepId: number): Promise<void> {
    const item = this.item();
    const step = item?.steps.find(candidate => candidate.id === stepId);
    if (!item || item.status === 'Complete' || !step || this.removingStepId() !== null || this.deleting()) return;
    if (item.steps.length === 1) {
      await this.deleteCase();
      return;
    }
    if (!await this.confirmDialog.confirm({
      title: `Remove step ${step.position}?`,
      message: 'This removes the step from the reusable test case. Saved run history will remain unchanged.',
      confirmLabel: 'Remove step',
    })) return;

    this.removingStepId.set(stepId);
    this.runError.set('');
    try {
      await firstValueFrom(this.api.deleteStep(item.id, stepId));
      this.item.set(await firstValueFrom(this.api.case(item.id)));
      this.decisions.set({});
      this.failureDetails.set({});
      this.notice.set('Test step removed. Any unsaved run selections were cleared.');
    } catch (error) {
      this.runError.set(errorMessage(error));
    } finally {
      this.removingStepId.set(null);
    }
  }

  async changeStatus(status: 'Draft' | 'Ready' | 'Complete'): Promise<void> {
    const current = this.item();
    if (!current || current.status === 'Complete' || this.savingStatus()) return;
    if (status === 'Complete' && (current.status !== 'Ready' || this.runs().length === 0)) return;
    this.savingStatus.set(true);
    this.statusError.set('');
    try {
      await firstValueFrom(this.api.updateCaseStatus(current.id, status));
      this.item.set(await firstValueFrom(this.api.case(current.id)));
      this.decisions.set({});
      this.failureDetails.set({});
      this.notice.set(`Test case marked ${status.toLowerCase()}.`);
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
      await this.router.navigate(projectPath({ id: current.projectId, name: current.projectName }),
        { queryParams: { deleted: 'case' } });
    } catch (error) {
      this.deleteError.set(errorMessage(error));
    } finally {
      this.deleting.set(false);
    }
  }
}
