import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { Tag, TagInput, TagUpdate } from './tag.models';

export type FiltroTagsArquivadas = 'false' | 'true' | 'all';

/** Acesso à API de tags (`/api/v1/tags`, 004 contracts/openapi.yaml). */
@Injectable({ providedIn: 'root' })
export class TagsService {
  private readonly api = inject(ApiClient);

  list(archived: FiltroTagsArquivadas = 'false'): Observable<Tag[]> {
    return this.api.get<Tag[]>(`/tags?archived=${archived}`);
  }

  /** Nome já existente (normalizado) → 409 `tag.name_taken` com `existingTagId` (US1.3). */
  create(input: TagInput): Observable<Tag> {
    return this.api.post<Tag>('/tags', input);
  }

  update(id: string, input: TagUpdate): Observable<Tag> {
    return this.api.put<Tag>(`/tags/${id}`, input);
  }

  /** Só tag sem uso; com uso → 409 `tag.in_use_archive_instead` (US5.3). */
  remove(id: string): Observable<void> {
    return this.api.delete<void>(`/tags/${id}`);
  }

  archive(id: string): Observable<Tag> {
    return this.api.post<Tag>(`/tags/${id}/archive`);
  }

  unarchive(id: string): Observable<Tag> {
    return this.api.post<Tag>(`/tags/${id}/unarchive`);
  }

  /** Junta `id` em `targetTagId` (US5.1); duas tags de acerto num lançamento → 409 com `conflicts`. */
  merge(id: string, targetTagId: string): Observable<Tag> {
    return this.api.post<Tag>(`/tags/${id}/merge`, { targetTagId });
  }
}
