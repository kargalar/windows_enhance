"""Fetch a tiny FLEURS Turkish test sample for local dictation benchmarking.

FLEURS: https://huggingface.co/datasets/google/fleurs (CC BY 4.0).
No recordings or model files are added to the source tree by this script.
"""
import csv
import io
import pathlib
import tarfile
import urllib.request

BASE = "https://huggingface.co/datasets/google/fleurs/resolve/main/data/tr_tr/"
target = pathlib.Path("artifacts/capture-verification/fleurs-mini")
target.mkdir(parents=True, exist_ok=True)
with urllib.request.urlopen(BASE + "test.tsv", timeout=30) as response:
    rows = list(csv.reader(io.TextIOWrapper(response, encoding="utf-8"), delimiter="\t"))
lookup = {row[1]: row for row in rows if len(row) >= 7 and row[1].endswith(".wav")}
selected = []
transcripts = set()
with urllib.request.urlopen(BASE + "audio/test.tar.gz", timeout=60) as response:
    with tarfile.open(fileobj=response, mode="r|gz") as archive:
        for entry in archive:
            name = pathlib.PurePosixPath(entry.name).name
            row = lookup.get(name)
            if not entry.isfile() or row is None or row[3] in transcripts or entry.size > 2_000_000:
                continue
            stream = archive.extractfile(entry)
            if stream is None:
                continue
            (target / name).write_bytes(stream.read())
            selected.append((name, row[2], row[6]))
            transcripts.add(row[3])
            if len(selected) == 5:
                break
if len(selected) != 5:
    raise RuntimeError("Five distinct FLEURS samples were not found")
with (target / "manifest.tsv").open("w", encoding="utf-8", newline="") as output:
    writer = csv.writer(output, delimiter="\t", lineterminator="\n")
    writer.writerows(selected)
print(f"Prepared {len(selected)} FLEURS Turkish test clips in {target}")
