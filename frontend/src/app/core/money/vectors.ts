export interface VectorCase<I, E> {
  id: string;
  description: string;
  input: I;
  expected: E;
}

export interface VectorFile<I, E> {
  rule: string;
  cases: VectorCase<I, E>[];
}

/** Ids repetidos no arquivo de vetores (deve ser vazio) e o total de casos. */
export function duplicateIds(file: VectorFile<unknown, unknown>): string[] {
  const seen = new Set<string>();
  return file.cases.map((c) => c.id).filter((id) => seen.size === seen.add(id).size);
}
