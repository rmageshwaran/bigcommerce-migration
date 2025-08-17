import { useState, useEffect, useCallback, useRef } from 'react';
import type { MigrationProgress } from '../types';
import config from '../config/environment';

interface ProgressiveLoadingOptions {
  migrationId: string;
  threshold?: number; // Entity count threshold for progressive loading
  enabled?: boolean;
}

interface ProgressiveLoadingState {
  isProgressiveMode: boolean;
  loadedChunks: number;
  totalChunks: number;
  isLoadingMore: boolean;
  canLoadMore: boolean;
  lastLoadTime: Date | null;
}

interface ProgressiveLoadingReturn extends ProgressiveLoadingState {
  loadNextChunk: () => Promise<void>;
  loadAll: () => Promise<void>;
  reset: () => void;
  getProgressiveProgress: (progress: MigrationProgress | null) => MigrationProgress | null;
}

/**
 * 🆕 TASK 4.2: Progressive Loading Hook
 * 
 * Implements progressive loading for large migrations to improve performance
 * and user experience when dealing with migrations that have thousands of entities
 */
export const useProgressiveLoading = (
  options: ProgressiveLoadingOptions
): ProgressiveLoadingReturn => {
  const {
    migrationId,
    threshold = config.dashboard.progressiveLoadingThreshold,
    enabled = true
  } = options;

  const [state, setState] = useState<ProgressiveLoadingState>({
    isProgressiveMode: false,
    loadedChunks: 0,
    totalChunks: 0,
    isLoadingMore: false,
    canLoadMore: false,
    lastLoadTime: null
  });

  const loadingQueue = useRef<Promise<void> | null>(null);

  // Determine if progressive loading should be enabled
  const shouldUseProgressiveLoading = useCallback((progress: MigrationProgress | null): boolean => {
    if (!enabled || !progress) return false;
    
    // Use progressive loading for large migrations
    return progress.totalEntities > threshold;
  }, [enabled, threshold]);

  // Calculate progressive chunks
  const calculateChunks = useCallback((totalEntities: number): number => {
    // Each chunk represents ~1000 entities for UI performance
    return Math.ceil(totalEntities / 1000);
  }, []);

  // Load next chunk of data
  const loadNextChunk = useCallback(async (): Promise<void> => {
    if (state.isLoadingMore || !state.canLoadMore) {
      return;
    }

    // Prevent concurrent loading
    if (loadingQueue.current) {
      await loadingQueue.current;
      return;
    }

    setState(prev => ({ ...prev, isLoadingMore: true }));

    loadingQueue.current = (async () => {
      try {
        // Simulate progressive loading delay for UX
        await new Promise(resolve => setTimeout(resolve, 200));

        setState(prev => ({
          ...prev,
          loadedChunks: Math.min(prev.loadedChunks + 1, prev.totalChunks),
          isLoadingMore: false,
          canLoadMore: prev.loadedChunks + 1 < prev.totalChunks,
          lastLoadTime: new Date()
        }));
      } catch (error) {
        console.error('Progressive loading failed:', error);
        setState(prev => ({ ...prev, isLoadingMore: false }));
      } finally {
        loadingQueue.current = null;
      }
    })();

    await loadingQueue.current;
  }, [state.isLoadingMore, state.canLoadMore, state.loadedChunks, state.totalChunks]);

  // Load all remaining chunks
  const loadAll = useCallback(async (): Promise<void> => {
    if (state.isLoadingMore || !state.canLoadMore) {
      return;
    }

    setState(prev => ({ 
      ...prev, 
      loadedChunks: prev.totalChunks,
      canLoadMore: false,
      lastLoadTime: new Date()
    }));
  }, [state.isLoadingMore, state.canLoadMore, state.totalChunks]);

  // Reset progressive loading state
  const reset = useCallback((): void => {
    setState({
      isProgressiveMode: false,
      loadedChunks: 0,
      totalChunks: 0,
      isLoadingMore: false,
      canLoadMore: false,
      lastLoadTime: null
    });
  }, []);

  // Get progress data filtered by progressive loading state
  const getProgressiveProgress = useCallback((progress: MigrationProgress | null): MigrationProgress | null => {
    if (!progress || !state.isProgressiveMode) {
      return progress;
    }

    // For progressive mode, show data proportional to loaded chunks
    const progressRatio = state.totalChunks > 0 ? state.loadedChunks / state.totalChunks : 1;
    
    // Don't artificially limit the actual progress data - just UI rendering hints
    return {
      ...progress,
      // Add progressive loading metadata for UI components (as any to avoid type conflicts)
      ...(state.isProgressiveMode && {
        _progressiveLoading: {
          isEnabled: true,
          loadedChunks: state.loadedChunks,
          totalChunks: state.totalChunks,
          progressRatio,
          canLoadMore: state.canLoadMore
        }
      })
    };
  }, [state]);

  // Monitor progress changes and update progressive loading state
  useEffect(() => {
    // Reset when migration changes
    reset();
  }, [migrationId, reset]);

  // Update progressive loading state based on current progress
  const updateProgressiveState = useCallback((progress: MigrationProgress | null) => {
    if (!progress) return;

    const shouldUseProgressive = shouldUseProgressiveLoading(progress);
    
    if (shouldUseProgressive && !state.isProgressiveMode) {
      // Enable progressive loading
      const chunks = calculateChunks(progress.totalEntities);
      setState(prev => ({
        ...prev,
        isProgressiveMode: true,
        totalChunks: chunks,
        loadedChunks: 1, // Start with first chunk loaded
        canLoadMore: chunks > 1
      }));
    } else if (!shouldUseProgressive && state.isProgressiveMode) {
      // Disable progressive loading
      setState(prev => ({
        ...prev,
        isProgressiveMode: false,
        loadedChunks: 0,
        totalChunks: 0,
        canLoadMore: false
      }));
    }
  }, [shouldUseProgressiveLoading, state.isProgressiveMode, calculateChunks]);

  return {
    ...state,
    loadNextChunk,
    loadAll,
    reset,
    getProgressiveProgress
  };
};