import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { Tag, TagInput } from './tag.models';

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
}
