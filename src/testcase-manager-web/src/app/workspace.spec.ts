import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from './api.service';
import { Workspace } from './workspace';

describe('Workspace forms', () => {
  const api = {
    projects: vi.fn(() => of([])),
    cases: vi.fn(() => of([])),
    modules: vi.fn(() => of([])),
    createCase: vi.fn(),
    createProject: vi.fn(),
    createModule: vi.fn(),
  };
  beforeEach(async () => {
    vi.clearAllMocks();
    await TestBed.configureTestingModule({
      imports: [Workspace],
      providers: [provideRouter([]), { provide: ApiService, useValue: api }],
    }).compileComponents();
  });
  async function setup() {
    const fixture = TestBed.createComponent(Workspace);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;
    await component.reload();
    fixture.detectChanges();
    component.editor.nativeElement.showModal = vi.fn();
    component.editor.nativeElement.close = vi.fn();
    return { fixture, component };
  }
  it('shows a useful empty workspace', async () => {
    const { fixture } = await setup();
    expect(fixture.nativeElement.textContent).toContain('Create your first project');
  });
  it('blocks blank case submission', async () => {
    const { component } = await setup();
    component.open('case');
    await component.save();
    expect(api.createCase).not.toHaveBeenCalled();
    expect(component.formError()).toContain('required');
  });
  it('adds, reorders and removes steps without losing their values', async () => {
    const { component } = await setup();
    component.steps.at(0).patchValue({ action: 'First', expectedResult: 'One' });
    component.addStep();
    component.steps.at(1).patchValue({ action: 'Second', expectedResult: 'Two' });
    component.moveStep(1, -1);
    expect(component.steps.at(0).value.action).toBe('Second');
    component.removeStep(1);
    component.removeStep(0);
    expect(component.steps.length).toBe(1);
  });
  it('retains input and displays an error when the API is unavailable', async () => {
    api.createCase.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { component } = await setup();
    component.open('case');
    component.caseForm.patchValue({ title: 'Login', moduleId: 1 });
    component.steps.at(0).patchValue({ action: 'Open', expectedResult: 'Form' });
    await component.save();
    expect(component.caseForm.value.title).toBe('Login');
    expect(component.formError()).toContain('Cannot reach');
    expect(component.saving()).toBe(false);
    expect(component.editor.nativeElement.close).not.toHaveBeenCalled();
  });
  it('opens the saved case after a successful submission', async () => {
    api.createCase.mockReturnValue(of({ id: 42, projectId: 7, projectName: 'Portal', moduleId: 3, moduleName: 'Login' }));
    const { component } = await setup();
    component.open('case');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    component.caseForm.patchValue({ title: 'Login', moduleId: 1 });
    component.steps.at(0).patchValue({ action: 'Open', expectedResult: 'Form' });
    await component.save();
    expect(navigate).toHaveBeenCalledWith(['/projects', 'portal--7', 'modules', 'login--3', 'cases', '42']);
    expect(component.editor.nativeElement.close).toHaveBeenCalled();
  });
});
