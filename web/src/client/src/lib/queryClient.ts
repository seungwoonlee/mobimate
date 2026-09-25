import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/http';

/** 서버 캐시(3초)와 맞춘 설정 (상세설계 §4.3). 갱신은 적응형 훅이 맡는다. */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 2000,
      retry: (count, err) => !(err instanceof ApiError && (err.status === 401 || err.status === 404)) && count < 1,
      refetchOnWindowFocus: false,
    },
  },
});
