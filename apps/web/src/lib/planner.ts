import type { ThemePack, Track } from "./themes";

export interface PlannedTopic {
  date: string;
  track: Track;
  topic: string | null;
  index: number;
}

const DAY_KEYS = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];
export const DAY_LABELS: Record<string, string> = { mon: "월", tue: "화", wed: "수", thu: "목", fri: "금", sat: "토", sun: "일" };

export const isoDate = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

export const dayKey = (iso: string) => DAY_KEYS[new Date(iso + "T00:00:00").getDay()];

/** 요일별 트랙과 진도(index)로 그 날 쓸 주제. 쉬는 날이면 null. */
export function plan(theme: ThemePack, schedule: Record<string, string>, date: string, progress: Record<string, number>): PlannedTopic | null {
  const s = Object.keys(schedule).length ? schedule : theme.defaultSchedule;
  const key = s[dayKey(date)];
  if (!key || key === "rest") return null;
  const track = theme.curriculum[key];
  if (!track) return null;
  const index = progress[key] ?? 0;
  return { date, track, topic: track.topics[index] ?? null, index };
}

/** 일상·리뷰처럼 요일과 상관없이 고른 트랙의 다음 주제. */
export function planTrack(track: Track, date: string, progress: Record<string, number>): PlannedTopic {
  const index = progress[track.key] ?? 0;
  return { date, track, topic: track.topics[index] ?? null, index };
}
