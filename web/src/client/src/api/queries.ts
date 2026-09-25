import { useQuery, type UseQueryOptions } from '@tanstack/react-query';
import { api } from './http';
import type {
  CharacterView, Currencies, Cutoffs, Envelope, Header, HomeworkBoard, Inventory, Life, Meta, Missions, Nearby, Overview, Status,
} from './types';

/** 쿼리 키 (상세설계 §4.3). SSE 이벤트가 이 키들을 무효화한다. */
export const keys = {
  header: ['header'] as const,
  overview: ['overview'] as const,
  character: ['character'] as const,
  inventory: ['inventory'] as const,
  currencies: ['currencies'] as const,
  missions: ['missions'] as const,
  life: ['life'] as const,
  nearby: ['nearby'] as const,
  cutoffs: ['cutoffs'] as const,
  homework: ['homework'] as const,
  meta: ['meta'] as const,
  status: ['status'] as const,
  settings: ['settings'] as const,
};

function q<T>(key: readonly string[], path: string, opts?: Partial<UseQueryOptions<Envelope<T>>>) {
  return useQuery<Envelope<T>>({ queryKey: key, queryFn: ({ signal }) => api.get<T>(path, signal), ...opts });
}

export const useHeader = () => q<Header>(keys.header, '/api/header');
export const useOverview = () => q<Overview>(keys.overview, '/api/overview');
export const useCharacter = () => q<CharacterView>(keys.character, '/api/character');
export const useInventory = () => q<Inventory>(keys.inventory, '/api/inventory');
export const useCurrencies = () => q<Currencies>(keys.currencies, '/api/currencies');
export const useMissions = () => q<Missions>(keys.missions, '/api/missions');
export const useLife = () => q<Life>(keys.life, '/api/life');
export const useNearby = () => q<Nearby>(keys.nearby, '/api/nearby');
export const useCutoffs = () => q<Cutoffs>(keys.cutoffs, '/api/cutoffs');
export const useHomework = () => q<HomeworkBoard>(keys.homework, '/api/homework');
export const useMeta = () => q<Meta>(keys.meta, '/api/meta', { staleTime: 30_000 });
export const useStatus = () => q<Status>(keys.status, '/api/status');
