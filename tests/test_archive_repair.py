"""Offline archive repair checks with disposable databases and media."""
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from contextlib import closing

spec = importlib.util.spec_from_file_location("archive_repair", Path(__file__).parents[1] / "scripts/repair-history-archive.py")
repair_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(repair_module)


def store(directory):
    (directory / "media").mkdir(parents=True)
    db = sqlite3.connect(directory / "history.db")
    db.executescript("""
        CREATE TABLE items(id INTEGER PRIMARY KEY AUTOINCREMENT,kind TEXT NOT NULL,created TEXT NOT NULL,app TEXT NOT NULL DEFAULT '',title TEXT NOT NULL DEFAULT '',text TEXT NOT NULL DEFAULT '',media TEXT NOT NULL DEFAULT '',seconds REAL NOT NULL DEFAULT 0,estimated INTEGER NOT NULL DEFAULT 0,source TEXT UNIQUE,ocr_state TEXT NOT NULL DEFAULT 'done',icon TEXT NOT NULL DEFAULT '',metadata TEXT NOT NULL DEFAULT '');
        CREATE TABLE tags(name TEXT PRIMARY KEY);
        CREATE TABLE app_tags(app TEXT,tag TEXT,PRIMARY KEY(app,tag));
        CREATE TABLE usage(id INTEGER PRIMARY KEY,app TEXT,started TEXT,ended TEXT,seconds REAL);
        CREATE TABLE context_usage(id INTEGER PRIMARY KEY,app TEXT,type TEXT,context TEXT,started TEXT,ended TEXT,seconds REAL);
        CREATE VIRTUAL TABLE search USING fts5(app,title,text,content=items,content_rowid=id);
        CREATE TRIGGER item_insert AFTER INSERT ON items BEGIN INSERT INTO search(rowid,app,title,text) VALUES(new.id,new.app,new.title,new.text); END;
    """)
    return db


class ArchiveRepairTests(unittest.TestCase):
    def test_legacy_full_metadata_missing_media_recovery_and_orphan(self):
        with tempfile.TemporaryDirectory() as root:
            userdata = Path(root) / "userdata"
            default = userdata / "History"
            store(default).close()
            (userdata / "settings.json").write_text('{"Values":{}}')
            legacy = Path(root) / "legacy"
            legacy.mkdir()
            images = legacy / "images"
            images.mkdir()
            database = legacy / "recall.db"
            with closing(sqlite3.connect(database)) as source, source:
                source.executescript("""
                    CREATE TABLE activities(id INTEGER PRIMARY KEY,timestamp TEXT,image_path TEXT,app_name TEXT,window_title TEXT,ocr_text TEXT,interval_at_capture INTEGER,is_idle INTEGER,url TEXT);
                    INSERT INTO activities VALUES(1,'2026-05-09 21:26:11.735703','later.webp','editor','Pencere','Türkçe OCR',60,0,'https://example.com');
                    INSERT INTO activities VALUES(2,'2026-05-09 21:27:11.735703','','editor','Boşta','',60,1,'');
                """)
            (images / "20260510_003000.webp").write_bytes(b"orphan image")
            before = database.read_bytes()
            repair = repair_module.Repair(userdata)
            repair.legacy(database, "recall", [images])
            self.assertEqual(len(repair.report["legacy"][0]["missing"]), 1)
            (images / "later.webp").write_bytes(b"recovered image")
            repair.legacy(database, "recall", [images])
            self.assertEqual(repair.report["legacy"][1]["imported"], 0)
            self.assertEqual(repair.report["legacy"][1]["repaired_media"], 1)
            repair.orphan_images(images)
            rows = repair.db.execute("SELECT * FROM items ORDER BY id").fetchall()
            self.assertEqual(len(rows), 3)
            self.assertEqual(rows[0]["created"], "2026-05-09T21:26:11.7357030Z")
            self.assertEqual(rows[0]["text"], "Türkçe OCR")
            self.assertEqual(json.loads(rows[0]["metadata"])["url"], "https://example.com")
            self.assertTrue(rows[0]["media"])
            self.assertEqual(json.loads(rows[1]["metadata"])["is_idle"], "1")
            self.assertEqual(rows[2]["created"], "2026-05-09T21:30:00.0000000Z")
            self.assertEqual(repair.report["orphan_images"], 1)
            self.assertEqual(before, database.read_bytes())
            repair.db.close()

    def test_all_kinds_metadata_tags_usage_search_and_rerun(self):
        with tempfile.TemporaryDirectory() as root:
            userdata = Path(root) / "userdata"
            default = userdata / "History"
            native = default / "Imported" / ("a" * 64)
            with closing(store(default)) as db, db:
                db.execute("INSERT INTO items(kind,created,text) VALUES('dictation','2026-05-10T00:00:00.0000000Z','önceki dikte')")
            with closing(store(native)) as db, db:
                db.execute("INSERT INTO items(kind,created,text,media,metadata) VALUES('recall','2026-11-01T00:00:00.0000000Z','aranabilir OCR','capture.webp','{\"cursorX\":12}')")
                db.execute("INSERT INTO items(kind,created,text) VALUES('capture','2026-11-01T00:00:00.0000000Z','kayıt')")
                db.execute("INSERT INTO usage VALUES(1,'editor','a','b',60)")
                db.execute("INSERT INTO context_usage VALUES(1,'browser','website','https://example.com','a','b',60)")
                db.execute("INSERT INTO tags VALUES('İş')")
                db.execute("INSERT INTO app_tags VALUES('editor','İş')")
            (native / "media/capture.webp").write_bytes(b"image")
            settings = {"Enabled": ["screen-history", "voice-dictation"], "Values": {"screen-history": {"portable-history-id": "a" * 64}, "voice-dictation": {"language": "tr"}}}
            (userdata / "settings.json").write_text(json.dumps(settings), encoding="utf-8")
            source_bytes = (native / "history.db").read_bytes()
            repair = repair_module.Repair(userdata)
            repair.native(native / "history.db")
            # Avoid checking the real desktop process during fixture tests.
            original_guard = repair_module.ensure_stopped
            repair_module.ensure_stopped = lambda: None
            try:
                repair.finish()
                again = repair_module.Repair(userdata)
                again.native(native / "history.db")
                again.finish()
            finally:
                repair_module.ensure_stopped = original_guard
            with closing(sqlite3.connect(default / "history.db")) as db:
                self.assertEqual(db.execute("SELECT count(*) FROM items").fetchone()[0], 3)
                for table in ("usage", "context_usage", "tags", "app_tags"):
                    self.assertEqual(db.execute("SELECT count(*) FROM " + table).fetchone()[0], 1)
                self.assertEqual(db.execute("SELECT count(*) FROM search WHERE search MATCH 'OCR'").fetchone()[0], 1)
                self.assertEqual(json.loads(db.execute("SELECT metadata FROM items WHERE kind='recall'").fetchone()[0]), {"cursorX": 12})
            self.assertEqual(source_bytes, (native / "history.db").read_bytes())
            result = json.loads((userdata / "settings.json").read_text(encoding="utf-8"))
            self.assertEqual(result["Values"]["screen-history"], {})
            self.assertEqual(result["Values"]["voice-dictation"]["language"], "tr")
            self.assertTrue((repair.backup / "original-history.db").exists())

    def test_missing_native_media_leaves_live_database_and_settings_unchanged(self):
        with tempfile.TemporaryDirectory() as root:
            userdata = Path(root)
            default = userdata / "History"
            native = default / "Imported" / ("b" * 64)
            store(default).close()
            with closing(store(native)) as db, db:
                db.execute("INSERT INTO items(kind,created,media) VALUES('recall','2026-01-01','missing.webp')")
            (userdata / "settings.json").write_text('{"Values":{}}')
            original = (default / "history.db").read_bytes()
            repair = repair_module.Repair(userdata)
            with self.assertRaises(FileNotFoundError):
                repair.native(native / "history.db")
            repair.db.close()
            self.assertEqual(original, (default / "history.db").read_bytes())
            self.assertEqual((userdata / "settings.json").read_text(), '{"Values":{}}')


if __name__ == "__main__":
    unittest.main()
