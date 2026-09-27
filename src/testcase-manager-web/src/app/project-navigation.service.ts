import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService, Project } from './api.service';

const RECENT_PROJECTS_KEY = 'test-case-manager:recent-projects';

function readRecentIds(): number[] {
  try {
    const value: unknown = JSON.parse(
      localStorage.getItem(RECENT_PROJECTS_KEY) ?? '[]',
    );

    if (!Array.isArray(value)) return [];

    return value
      .filter((id): id is number => Number.isInteger(id) && id > 0)
      .slice(-3);
  } catch {
    return [];
  }
}

@Injectable({ providedIn: 'root' })
export class ProjectNavigationService {
  private readonly api = inject(ApiService);

  readonly projects = signal<Project[]>([]);
  readonly recentIds = signal<number[]>(readRecentIds());

  // Oldest of the three on the left; most recently opened on the right.
  readonly recentProjects = computed(() =>
    this.recentIds()
      .map((id) => this.projects().find((project) => project.id === id))
      .filter((project): project is Project => project !== undefined),
  );

  async refresh(): Promise<void> {
    const projects = await firstValueFrom(this.api.projects());
    this.projects.set(projects);

    // Remove IDs for projects deleted since the previous visit.
    const existingIds = new Set(projects.map((project) => project.id));
    this.recentIds.update((ids) => ids.filter((id) => existingIds.has(id)));
    this.persist();
  }

  remember(id: number): void {
    this.recentIds.update((ids) =>
      [...ids.filter((existingId) => existingId !== id), id].slice(-3),
    );
    this.persist();
  }

  forget(id: number): void {
    this.recentIds.update((ids) => ids.filter((existingId) => existingId !== id));
    this.persist();
  }

  private persist(): void {
    try {
      localStorage.setItem(
        RECENT_PROJECTS_KEY,
        JSON.stringify(this.recentIds()),
      );
    } catch {
      // Navigation still works if browser storage is unavailable.
    }
  }
}