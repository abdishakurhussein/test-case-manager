import { Component, DestroyRef, OnInit, inject, signal, computed } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { ApiService, CaseSummary, errorMessage } from './api.service';
import { ProjectNavigationService } from './project-navigation.service';
import { casePath } from './paths';
import { ConfirmDialogService } from './confirm-dialog.service';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-archive-cases',
  imports: [RouterLink, DatePipe],
  templateUrl: './archive-cases.html',
})
export class ArchiveCases implements OnInit {
  readonly casePath = casePath;
  readonly projectNavigation = inject(ProjectNavigationService);
  readonly projectId = signal<number | null>(null);
  readonly search = signal('');
  readonly page = signal(1);
  readonly pageSize = 10;
  readonly cases = signal<CaseSummary[]>([]);
  readonly total = signal(0);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
  readonly loading = signal(true);
  readonly error = signal('');
  readonly restoringId = signal<number | null>(null);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private request = 0;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => {
      if (this.searchTimer) clearTimeout(this.searchTimer);
    });
    void this.projectNavigation.refresh().catch(() => this.error.set('Could not load project names.'));
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const raw = params.get('projectId');
      this.projectId.set(raw && /^[1-9]\d*$/.test(raw) ? Number(raw) : null);
      this.page.set(1);
      void this.load();
    });
  }

  chooseProject(value: string): void {
    void this.router.navigate(['/archive'], { queryParams: value ? { projectId: value } : {} });
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
    ++this.request;
    this.loading.set(true);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.load(), 300);
  }

  async load(): Promise<void> {
    const request = ++this.request;
    this.loading.set(true);
    this.error.set('');
    try {
      const result = await firstValueFrom(this.api.searchCases({
        status: 'Archived', projectId: this.projectId() ?? undefined,
        q: this.search().trim(), page: this.page(), pageSize: this.pageSize,
      }));
      if (request !== this.request) return;
      if (this.page() > 1 && result.totalCount <= (this.page() - 1) * this.pageSize) {
        this.page.update(page => page - 1);
        void this.load();
        return;
      }
      this.cases.set(result.items);
      this.total.set(result.totalCount);
    } catch (error) {
      if (request === this.request) this.error.set(errorMessage(error));
    } finally {
      if (request === this.request) this.loading.set(false);
    }
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page()) return;
    this.page.set(page);
    void this.load();
  }

  async restore(item: CaseSummary): Promise<void> {
    if (this.restoringId() !== null) return;
    if (!await this.confirmDialog.confirm({
      title: 'Restore test case?',
      message: `Restore “${item.title}” to ${item.statusBeforeArchive ?? 'Draft'}? Its steps and run history will stay intact.`,
      confirmLabel: 'Restore case',
    })) return;
    this.restoringId.set(item.id);
    this.error.set('');
    try {
      await firstValueFrom(this.api.restoreCase(item.id));
      await this.projectNavigation.refresh();
      await this.load();
      this.toast.show(`“${item.title}” restored.`);
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.restoringId.set(null);
    }
  }

  exportUrl(format: 'json' | 'csv'): string {
    const project = this.projectId();
    return `/api/archive/export?format=${format}${project ? `&projectId=${project}` : ''}`;
  }
}
