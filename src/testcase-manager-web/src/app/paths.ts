import { CaseSummary, Module, Project } from './api.service';

// Names keep URLs readable; the ID suffix keeps them unambiguous when names repeat.
function segment(name: string, id: number): string {
  const slug = name.normalize('NFKD').toLowerCase().replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'item';
  return `${slug}--${id}`;
}

export function projectPath(project: Pick<Project, 'id' | 'name'>): string[] {
  return ['/projects', segment(project.name, project.id)];
}

export function casePath(item: CaseSummary): string[] {
  return [
    '/projects', segment(item.projectName, item.projectId),
    'modules', segment(item.moduleName, item.moduleId),
    'cases', String(item.id),
  ];
}

export function idFromSegment(value: string | null): number | null {
  if (value && /^[1-9]\d*$/.test(value)) return Number(value); // old /projects/10 links
  const match = value?.match(/--([1-9]\d*)$/);
  return match ? Number(match[1]) : null;
}
