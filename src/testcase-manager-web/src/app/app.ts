import { Component, HostListener, OnInit, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ProjectNavigationService } from './project-navigation.service';
import { projectPath } from './paths';
import { ConfirmDialog } from './confirm-dialog';
import { ToastService } from './toast.service';
import { AuthService } from './auth.service';


@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, ConfirmDialog],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly projectPath = projectPath;
  readonly projectNavigation = inject(ProjectNavigationService);
  readonly toast = inject(ToastService);
  readonly sidebarVisible = signal(true);
  readonly mobileViewport = signal(false);
  readonly projectsExpanded = signal(true);
  readonly projectSearch = signal('');
  readonly filteredProjects = computed(() => this.projectNavigation.projects()
    .filter(project => project.name.toLowerCase().includes(this.projectSearch().trim().toLowerCase())));
  readonly sidebarError = signal('');

  ngOnInit(): void {
    this.syncViewport();
  }

  @HostListener('window:resize')
  syncViewport(): void {
    const narrow = typeof window !== 'undefined' && !!window.matchMedia?.('(max-width: 850px)').matches;
    if (narrow !== this.mobileViewport()) {
      this.mobileViewport.set(narrow);
      this.sidebarVisible.set(!narrow);
    }
  }

  @HostListener('window:keydown.escape')
  closeMobileSidebar(): void {
    if (this.mobileViewport()) this.sidebarVisible.set(false);
  }

  async loadProjects(): Promise<void> {
    this.sidebarError.set('');

    try {
      await this.projectNavigation.refresh();
    } catch {
      this.sidebarError.set('Could not load projects.');
    }
  }

  toggleProjects(): void {
    this.projectsExpanded.update((expanded) => !expanded);
  }

  toggleSidebar(): void {
    this.sidebarVisible.update((visible) => !visible);
  }

  constructor() {
  effect(() => {
    if (this.auth.canUseWorkspace()) {
      void this.loadProjects();
    } else {
      this.projectNavigation.projects.set([]);
    }
  });
}

async logout(): Promise<void> {
  try {
    await this.auth.logout();
    this.projectNavigation.projects.set([]);
    await this.router.navigateByUrl('/login');
  } catch {
    this.toast.show('Could not log out. Please try again.');
  }
}

}
