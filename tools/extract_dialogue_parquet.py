"""Extract conversation turns from train or test Parquet shards as UTF-8 lines."""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any, Iterable

try:
    import pyarrow.parquet as parquet
except ImportError as error:
    raise SystemExit("Install the extraction tool dependency with: python -m pip install pyarrow") from error


def conversation_lines(row: dict[str, Any], columns: set[str]) -> Iterable[str]:
    for column in ("messages", "turns"):
        if column not in columns:
            continue
        for turn in row.get(column) or []:
            if not isinstance(turn, dict):
                continue
            text = turn.get("content") or turn.get("text")
            if isinstance(text, str) and text.strip():
                yield text.strip()
        return

    text = row.get("text")
    if isinstance(text, str) and text.strip():
        yield text.strip()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("shards", type=Path, nargs="+")
    arguments = parser.parse_args()

    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    line_count = 0
    with arguments.output.open("w", encoding="utf-8", newline="\n") as output:
        for shard in arguments.shards:
            parquet_file = parquet.ParquetFile(shard)
            columns = set(parquet_file.schema_arrow.names)
            field = next((name for name in ("messages", "turns", "text") if name in columns), None)
            if field is None:
                raise ValueError(f"No conversation text column found in {shard}")

            for batch in parquet_file.iter_batches(columns=[field], batch_size=1_024):
                for row in batch.to_pylist():
                    for text in conversation_lines(row, columns):
                        output.write(text.replace("\r", " ").replace("\n", " "))
                        output.write("\n")
                        line_count += 1

    print(f"Extracted {line_count:,} conversation turns to {arguments.output}")


if __name__ == "__main__":
    main()
