"""Partition TDD's independent synthetic word benchmark without redistributing it."""
import argparse
import csv
import hashlib
from pathlib import Path

SOURCE_SHA256 = "6e264ced55ff53b6108799d8fcaae181104cf2f6795b6776dc8ce680fc9b0469"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    data = args.source.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != SOURCE_SHA256:
        raise SystemExit(f"Unexpected source SHA-256: {digest}")
    args.output.mkdir(parents=True, exist_ok=True)
    partitions = {"dev": [], "test": []}
    with args.source.open(encoding="utf-8-sig", newline="") as source:
        for row in csv.DictReader(source):
            gold, word = row["gold"], row["input"]
            # Empty gold denotes random/foreign words, not an annotated correction.
            if not gold or not word or not gold.isalpha() or not word.isalpha():
                continue
            # Group by reference word, so variants of one reference cannot cross splits.
            key = gold.casefold().encode("utf-8")
            bucket = int.from_bytes(hashlib.sha256(key).digest()[:8], "big") % 10
            split = "test" if bucket < 3 else "dev"
            tag = "IV" if gold.casefold() == word.casefold() else "OOV-ill_formed"
            partitions[split].append((word, gold, tag))
    for split, rows in partitions.items():
        path = args.output / f"tdd-{split}.tsv"
        with path.open("w", encoding="utf-8", newline="") as target:
            writer = csv.writer(target, delimiter="\t", lineterminator="\n")
            writer.writerow(("raw", "input", "gold", "annotation"))
            writer.writerows((word, word, gold, tag) for word, gold, tag in rows)
        print(f"{split}: {len(rows)} rows, sha256 {hashlib.sha256(path.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    main()
