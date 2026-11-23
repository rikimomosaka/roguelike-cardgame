export function normalizeMotionPlaybackSpeed(playbackSpeed: number | undefined): number {
  return Math.max(0.25, playbackSpeed ?? 1)
}

/** Keeps CSS one-shot duration synchronized with replay's JS wait duration. */
export function scaledOneShotMotionDuration(
  durationMs: number,
  playbackSpeed: number | undefined,
): number {
  return Math.max(1, Math.round(durationMs / normalizeMotionPlaybackSpeed(playbackSpeed)))
}
