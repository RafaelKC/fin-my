import type { Tag } from './tag.models';

/** Tag fictícia para os testes (nomes: Bruna, Carla, Ifood…). */
export function tag(parcial: Partial<Tag> = {}): Tag {
  return {
    id: 't-ifood',
    name: 'Ifood',
    color: '#E5484D',
    isSettlement: false,
    archived: false,
    uses90d: 0,
    inUse: false,
    version: 1,
    ...parcial,
  };
}

export const bruna = tag({
  id: 't-bruna',
  name: 'Bruna',
  isSettlement: true,
  uses90d: 9,
  color: '#3E63DD',
});
export const carla = tag({ id: 't-carla', name: 'Carla', isSettlement: true, uses90d: 2 });
export const ifood = tag({ id: 't-ifood', name: 'Ifood', uses90d: 5 });
