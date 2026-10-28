"""Extract the text column from WikiText Parquet shards as UTF-8 lines."""

from __future__ import annotations

import argparse
from pathlib import Path

try:
    import pyarrow.parquet as parquet
except ImportError as error:
    raise SystemExit("Install the extraction tool dependency with: python -m pip install pyarrow") from error


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("shards", type=Path, nargs="+")
    arguments = parser.parse_args()

    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    line_count = 0
    with arguments.output.open("w", encoding="utf-8", newline="\n") as output:
        for shard in arguments.shards:
            for batch in parquet.ParquetFile(shard).iter_batches(columns=["text"], batch_size=8_192):
                for text in batch.column(0).to_pylist():
                    if not text:
                        continue
                    output.write(text.replace("\r", " ").replace("\n", " ").strip())
                    output.write("\n")
                    line_count += 1

    print(f"Extracted {line_count:,} text rows to {arguments.output}")


if __name__ == "__main__":
    main()
