"""Extract Turkish user-facing turns from the Turkish Chat Max casual subset."""

from __future__ import annotations

import argparse
from pathlib import Path

import pyarrow.parquet as parquet


def extract(input_path: Path, output_path: Path) -> tuple[int, int]:
    table = parquet.read_table(input_path, columns=["language_mode", "prompt_language", "messages"])
    output_path.parent.mkdir(parents=True, exist_ok=True)

    conversation_count = 0
    turn_count = 0
    with output_path.open("w", encoding="utf-8", newline="\n") as output:
        for row in table.to_pylist():
            if row["prompt_language"] != "tr" or row["language_mode"] not in {"tr_only", "tr_en"}:
                continue

            turns = [
                message["content"].strip()
                for message in row["messages"]
                if message["role"] in {"user", "assistant"} and message["content"].strip()
            ]
            if not turns:
                continue

            conversation_count += 1
            for turn in turns:
                output.write(turn.replace("\r", " ").replace("\n", " "))
                output.write("\n")
                turn_count += 1

    return conversation_count, turn_count


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="casual_conversation/train.parquet")
    parser.add_argument("output", type=Path, help="UTF-8 text output for the n-gram trainer")
    args = parser.parse_args()

    conversations, turns = extract(args.input, args.output)
    print(f"Extracted {turns:,} Turkish turns from {conversations:,} conversations")


if __name__ == "__main__":
    main()
