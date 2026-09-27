import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ProjectNavigationService } from './project-navigation.service';
import { projectPath } from './paths';
import { ConfirmDialog } from './confirm-dialog';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, ConfirmDialog],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  readonly projectPath = projectPath;
  readonly projectNavigation = inject(ProjectNavigationService);
  readonly projectsExpanded = signal(true);
  readonly sidebarError = signal('');

  ngOnInit(): void {
    void this.loadProjects();
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
}
