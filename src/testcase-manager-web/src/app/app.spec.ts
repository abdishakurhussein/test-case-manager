import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { ApiService } from './api.service';
import { of } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage } from './api.service';
import { ToastService } from './toast.service';

describe('App shell', () => {
  it('shows the local workspace and navigation', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: ApiService, useValue: { projects: () => of([]) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('ATCM');
    expect(fixture.nativeElement.textContent).toContain('Local workspace');
    const navigation = fixture.nativeElement.querySelector('nav');
    expect(navigation.textContent.indexOf('Projects')).toBeLessThan(navigation.textContent.indexOf('Archive'));

    const toggle = fixture.nativeElement.querySelector('.sidebar-visibility-toggle') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#main-sidebar').hidden).toBe(true);
    expect(toggle.getAttribute('aria-label')).toBe('Show navigation');
    toggle.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#main-sidebar').hidden).toBe(false);
  });

  it('distinguishes a server error from an unreachable API', () => {
    expect(errorMessage(new HttpErrorResponse({ status: 500 }))).toContain('server error');
    expect(errorMessage(new HttpErrorResponse({ status: 0 }))).toContain('Cannot reach');
  });

  it('shows and dismisses a success notification', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: ApiService, useValue: { projects: () => of([]) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    TestBed.inject(ToastService).show('Project created.');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.toast').textContent).toContain('Project created.');
    (fixture.nativeElement.querySelector('.toast button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.toast')).toBeNull();
  });
});
