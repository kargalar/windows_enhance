"""Consolidate WinForge archives offline, retaining source stores and SQLite backups.

Run with WinForge closed. The database is prepared and checked before activation;
settings are changed last. Re-running is safe, including after interruption.
"""
import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
from datetime import datetime, timezone, timedelta
from contextlib import closing


def connect(path, readonly=False):
    db = sqlite3.connect(path.resolve().as_uri() + ("?mode=ro" if readonly else "?mode=rw"), uri=True)
    db.row_factory = sqlite3.Row
    return db


def snapshot(source, destination):
    with closing(connect(source, True)) as src, closing(sqlite3.connect(destination)) as dst:
        src.backup(dst)


def timestamp(value):
    date = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if date.tzinfo is None:
        date = date.replace(tzinfo=timezone.utc)
    return date.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f") + "0Z"


def ensure_stopped():
    if os.name == "nt":
        output = subprocess.check_output(["tasklist", "/FI", "IMAGENAME eq WinForge.exe", "/FO", "CSV", "/NH"], text=True)
        if any(row and row[0].lower() == "winforge.exe" for row in csv.reader(io.StringIO(output))):
            raise RuntimeError("Close WinForge before repairing its archive.")


class Repair:
    def __init__(self, userdata):
        self.userdata = Path(userdata).resolve()
        self.history = self.userdata / "History"
        self.media = self.history / "media"
        self.backup = self.userdata / "Backups" / ("archive-repair-" + datetime.now().strftime("%Y%m%d-%H%M%S-%f"))
        self.backup.mkdir(parents=True)
        self.report = {"backup": str(self.backup), "native": [], "legacy": [], "orphan_images": 0}
        shutil.copy2(self.userdata / "settings.json", self.backup / "settings.json")
        self.settings = json.loads((self.backup / "settings.json").read_text(encoding="utf-8-sig"))
        snapshot(self.history / "history.db", self.backup / "original-history.db")
        self.stage = self.backup / "prepared-history.db"
        shutil.copy2(self.backup / "original-history.db", self.stage)
        self.db = connect(self.stage)
        self.db.execute("PRAGMA journal_mode=DELETE")
        self.db.execute("CREATE TABLE IF NOT EXISTS archive_repair_origins(database_path TEXT,table_name TEXT,source_id INTEGER,target_id INTEGER,PRIMARY KEY(database_path,table_name,source_id))")
        self.sources = {r["source"]: r["id"] for r in self.db.execute("SELECT id,source FROM items WHERE source IS NOT NULL")}
        self.referenced_names = set()
        for row in self.db.execute("SELECT metadata FROM items WHERE metadata<>''"):
            try:
                image = json.loads(row[0]).get("image_path")
                if image:
                    self.referenced_names.add(Path(image).name.lower())
            except (ValueError, TypeError):
                pass
        self.copied = {}

    def copy_media(self, path):
        path = Path(path).resolve()
        if path in self.copied:
            return self.copied[path]
        if not path.is_file():
            raise FileNotFoundError(path)
        with path.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest().upper()
        name = digest + path.suffix.lower()
        target = self.media / name
        if target.exists():
            if target.stat().st_size != path.stat().st_size:
                raise ValueError("Content-addressed media size mismatch: " + str(target))
        else:
            temporary = target.with_name(name + ".repair-tmp")
            shutil.copy2(path, temporary)
            with temporary.open("rb") as stream:
                if hashlib.file_digest(stream, "sha256").hexdigest().upper() != digest:
                    raise ValueError("Media copy verification failed: " + str(path))
            os.replace(temporary, target)
        self.copied[path] = name
        return name

    def add(self, values):
        columns = list(values)
        cursor = self.db.execute("INSERT INTO items(" + ",".join(columns) + ") VALUES(" + ",".join("?" for _ in columns) + ")", list(values.values()))
        if values.get("source"):
            self.sources[values["source"]] = cursor.lastrowid
        return cursor.lastrowid

    def native(self, path):
        path = Path(path).resolve()
        if path == (self.history / "history.db").resolve():
            return
        saved = self.backup / ("native-" + hashlib.sha256(str(path).encode()).hexdigest()[:16] + ".db")
        snapshot(path, saved)
        with closing(connect(saved, True)) as source:
            self._native(path, source)

    def _native(self, path, source):
        counts = {}
        for table in ("items", "usage", "context_usage"):
            imported = 0
            for row in source.execute("SELECT * FROM " + table):
                prior = self.db.execute("SELECT target_id FROM archive_repair_origins WHERE database_path=? AND table_name=? AND source_id=?", (str(path), table, row["id"])).fetchone()
                if prior:
                    continue
                values = dict(row)
                source_id = values.pop("id")
                target_id = self.sources.get(values.get("source")) if table == "items" else None
                if target_id is None:
                    if table == "items":
                        for column in ("media", "icon"):
                            if values[column]:
                                name = values[column]
                                if Path(name).name != name:
                                    raise ValueError("Unsafe media reference: " + name)
                                values[column] = self.copy_media(path.parent / "media" / name)
                        target_id = self.add(values)
                    else:
                        columns = list(values)
                        target_id = self.db.execute("INSERT INTO " + table + "(" + ",".join(columns) + ") VALUES(" + ",".join("?" for _ in columns) + ")", list(values.values())).lastrowid
                    imported += 1
                self.db.execute("INSERT INTO archive_repair_origins VALUES(?,?,?,?)", (str(path), table, source_id, target_id))
            counts[table] = imported
        for table in ("tags", "app_tags"):
            for row in source.execute("SELECT * FROM " + table):
                self.db.execute("INSERT OR IGNORE INTO " + table + " VALUES(" + ",".join("?" for _ in row) + ")", tuple(row))
        self.db.commit()
        self.report["native"].append({"database": str(path), **counts})
        print("Merged native archive:", counts, flush=True)

    @staticmethod
    def resolve(database, stored, images, icon=False):
        folder = database.parent
        options = [Path(stored), folder / stored, folder / ("icons" if icon else "images") / Path(stored).name]
        if not icon:
            options.extend(root / Path(stored).name for root in images)
        return next((p for p in options if p.is_file()), None)

    def legacy(self, database, kind, images=()):
        database = Path(database).resolve()
        saved = self.backup / (kind + "-" + hashlib.sha256(str(database).encode()).hexdigest()[:16] + ".db")
        snapshot(database, saved)
        with closing(connect(saved, True)) as source:
            self._legacy(database, kind, images, source)

    def _legacy(self, database, kind, images, source):
        images = [Path(p).resolve() for p in images]
        config = database.parent / "config.json"
        if config.exists():
            configured = json.loads(config.read_text(encoding="utf-8-sig")).get("images_path")
            if configured:
                images.append(database.parent / configured)
        table = "activities" if kind == "recall" else "transcripts"
        imported = 0
        repaired_media = 0
        missing = []
        for row in source.execute("SELECT * FROM " + table + " ORDER BY id"):
            data = {key: None if value is None else str(value) for key, value in dict(row).items()}
            created = timestamp(data["timestamp" if kind == "recall" else "created_at"])
            key = kind + ":" + created + ":" + data["id"]
            if data.get("image_path"):
                self.referenced_names.add(Path(data["image_path"]).name.lower())
            existing = self.db.execute("SELECT id,media,icon FROM items WHERE source=?", (key,)).fetchone() if key in self.sources else None
            media = {}
            for column, destination in (("image_path", "media"), ("icon_path", "icon")):
                media[destination] = existing[destination] if existing else ""
                if data.get(column) and not media[destination]:
                    resolved = self.resolve(database, data[column], images, column == "icon_path")
                    if resolved:
                        media[destination] = self.copy_media(resolved)
                    else:
                        missing.append({"id": data["id"], "column": column, "path": data[column]})
            if existing:
                if any(media[column] != existing[column] for column in ("media", "icon")):
                    self.db.execute("UPDATE items SET media=?,icon=? WHERE id=?", (media["media"], media["icon"], existing["id"]))
                    repaired_media += 1
                continue
            seconds = float(data.get("interval_at_capture") or 60) if kind == "recall" else 0
            self.add(dict(kind=kind, created=created, app=data.get("app_name") or "",
                          title=("Large" if data.get("model") == "1" else "Turbo") if kind == "dictation" else data.get("window_title") or "",
                          text=data.get("ocr_text" if kind == "recall" else "text") or "", **media,
                          seconds=max(0, seconds), estimated=int(kind == "recall"), source=key,
                          ocr_state="done", metadata=json.dumps(data, ensure_ascii=False)))
            imported += 1
        tables = {r[0] for r in source.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        if kind == "recall":
            if "tags" in tables:
                for row in source.execute("SELECT name FROM tags"):
                    self.db.execute("INSERT OR IGNORE INTO tags VALUES(?)", tuple(row))
            if "app_tags" in tables:
                for row in source.execute("SELECT app_name,tag_name FROM app_tags"):
                    self.db.execute("INSERT OR IGNORE INTO app_tags VALUES(?,?)", tuple(row))
        self.db.commit()
        self.report["legacy"].append({"database": str(database), "kind": kind, "imported": imported, "repaired_media": repaired_media, "missing": missing})
        print("Merged legacy archive:", kind, imported, "new records;", len(missing), "missing files", flush=True)

    def orphan_images(self, directory):
        for path in sorted(Path(directory).iterdir()):
            if not path.is_file() or path.suffix.lower() not in (".webp", ".png", ".jpg", ".jpeg") or path.name.lower() in self.referenced_names:
                continue
            # RecallClone names images with local wall time, while database timestamps are UTC.
            created = datetime.strptime(path.stem, "%Y%m%d_%H%M%S").replace(tzinfo=timezone(timedelta(hours=3)))
            key = "archive-image:" + path.name.lower()
            if key not in self.sources:
                self.add(dict(kind="recall", created=timestamp(created.isoformat()), app="", title="Arşiv görseli (kaynak kayıt bulunamadı)",
                              text="", media=self.copy_media(path), seconds=0, estimated=0, source=key, ocr_state="done", icon="",
                              metadata=json.dumps({"archive_only": True, "image_path": str(path)}, ensure_ascii=False)))
                self.report["orphan_images"] += 1
        self.db.commit()

    def finish(self):
        for row in self.db.execute("SELECT media FROM items WHERE media<>'' UNION SELECT icon FROM items WHERE icon<>''"):
            if Path(row[0]).name != row[0] or not (self.media / row[0]).is_file():
                raise FileNotFoundError("Unresolved archived media: " + row[0])
        self.db.execute("INSERT INTO search(search,rank) VALUES('integrity-check',1)")
        if self.db.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise RuntimeError("Archive integrity check failed")
        self.report["totals"] = [dict(row) for row in self.db.execute("SELECT kind,count(*) records,min(created) oldest,max(created) newest,count(distinct nullif(media,'')) unique_media FROM items GROUP BY kind")]
        self.report["tables"] = {t: self.db.execute("SELECT count(*) FROM " + t).fetchone()[0] for t in ("tags", "app_tags", "usage", "context_usage")}
        self.db.commit()
        self.db.close()
        # Keep the prepared database as an additional post-repair recovery point.
        ready = self.history / "history.db.repair-ready"
        shutil.copy2(self.stage, ready)
        ensure_stopped()
        with connect(self.history / "history.db") as live:
            live.execute("PRAGMA wal_checkpoint(TRUNCATE)")
            live.execute("PRAGMA journal_mode=DELETE")
        live.close()
        os.replace(ready, self.history / "history.db")
        for module in ("screen-history", "voice-dictation", "screen-capture"):
            settings = self.settings["Values"].setdefault(module, {})
            settings.pop("portable-history-id", None)
            settings.pop("storage-directory", None)
        settings_ready = self.userdata / "settings.json.repair-ready"
        settings_ready.write_text(json.dumps(self.settings, ensure_ascii=False, indent=2), encoding="utf-8")
        os.replace(settings_ready, self.userdata / "settings.json")
        (self.backup / "report.json").write_text(json.dumps(self.report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(self.report, ensure_ascii=False, indent=2), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--userdata", type=Path, required=True)
    parser.add_argument("--recall", type=Path, action="append", default=[])
    parser.add_argument("--dictation", type=Path, action="append", default=[])
    parser.add_argument("--images", type=Path, action="append", default=[])
    args = parser.parse_args()
    ensure_stopped()
    repair = Repair(args.userdata)
    for database in sorted((repair.history / "Imported").glob("*/history.db")):
        repair.native(database)
    for database in args.recall:
        repair.legacy(database, "recall", args.images)
    for database in args.dictation:
        repair.legacy(database, "dictation")
    for images in args.images:
        repair.orphan_images(images)
    repair.finish()


if __name__ == "__main__":
    main()
