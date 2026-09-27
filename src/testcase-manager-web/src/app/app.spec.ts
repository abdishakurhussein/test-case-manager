import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { ApiService } from './api.service';
import { of } from 'rxjs';

describe('App shell', () => {
  it('shows the local workspace and navigation', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: ApiService, useValue: { projects: () => of([]) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('TestCaseManager');
    expect(fixture.nativeElement.textContent).toContain('Local workspace');
  });
});
