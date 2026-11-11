"""설정·커리큘럼·진행 상태 관리."""
from __future__ import annotations

import datetime as dt
import json
from dataclasses import dataclass
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parent.parent
WEEKDAYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"]


def load_config() -> dict:
    with open(ROOT / "config.yaml", encoding="utf-8") as f:
        return yaml.safe_load(f)


@dataclass
class Track:
    key: str
    name: str
    guide: str
    topics: list[str]


def load_track(key: str) -> Track:
    path = ROOT / "curriculum" / f"{key}.yaml"
    if not path.exists():
        available = ", ".join(p.stem for p in (ROOT / "curriculum").glob("*.yaml"))
        raise SystemExit(f"트랙 '{key}'을 찾을 수 없어요. 사용 가능: {available}")
    with open(path, encoding="utf-8") as f:
        data = yaml.safe_load(f)
    return Track(key, data.get("name", key), data.get("guide", ""), data.get("topics") or [])


def track_for_today(config: dict, today: dt.date) -> str | None:
    key = config.get("schedule", {}).get(WEEKDAYS[today.weekday()])
    return None if not key or key == "rest" else key


class State:
    """state.json: 트랙별 진행 위치와 작성 이력."""

    def __init__(self):
        self.path = ROOT / "state.json"
        if self.path.exists():
            self.data = json.loads(self.path.read_text(encoding="utf-8"))
        else:
            self.data = {"progress": {}, "history": []}

    def next_index(self, track: str) -> int:
        return self.data["progress"].get(track, 0)

    def history(self, track: str) -> list[dict]:
        return [h for h in self.data["history"] if h["track"] == track]

    def written_today(self, today: dt.date) -> list[dict]:
        return [h for h in self.data["history"] if h["date"] == today.isoformat()]

    def record(self, track: str, topic: str, title: str, folder: str, today: dt.date, advance: bool):
        if advance:
            self.data["progress"][track] = self.next_index(track) + 1
        self.data["history"].append(
            {"date": today.isoformat(), "track": track, "topic": topic, "title": title, "folder": folder}
        )
        self.path.write_text(json.dumps(self.data, ensure_ascii=False, indent=2), encoding="utf-8")
