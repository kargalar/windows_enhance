import csv
import gzip
import math
import re
from collections import defaultdict


def load_model(path):
    unigrams = {}
    bigrams = defaultdict(list)
    trigrams = defaultdict(list)
    with gzip.open(path, "rt", encoding="utf-8") as model_file:
        for line in model_file:
            fields = line.rstrip("\n").split("\t")
            if fields[0] == "u":
                unigrams[fields[1]] = int(fields[2])
            elif fields[0] == "b":
                bigrams[fields[1]].append((fields[2], int(fields[3])))
            elif fields[0] == "t":
                trigrams[(fields[1], fields[2])].append((fields[3], int(fields[4])))
    total = sum(unigrams.values())
    outgoing = {word: sum(count for _, count in values) for word, values in bigrams.items()}
    common = sorted(unigrams.items(), key=lambda item: (-item[1], item[0]))[:64]
    return unigrams, total, common, bigrams, outgoing, trigrams


def turkish_lower(word):
    return word.replace("I", "ı").replace("İ", "i").lower()


def normalize_count_candidates(values):
    total = sum(count for _, count in values)
    return [(word, count, count / total) for word, count in values]


def predict(model, history):
    history = history[-2:]
    _, _, common, bigrams, _, trigrams = model
    distribution = normalize_count_candidates(common)
    bigram = normalize_count_candidates(bigrams.get(history[-1], []))
    if bigram:
        distribution = interpolate(bigram, distribution)
    trigram = normalize_count_candidates(trigrams.get(tuple(history), []))
    if trigram:
        distribution = interpolate(trigram, distribution)
    return distribution


def interpolate(high, low):
    count = sum(item[1] for item in high)
    weight = count / (count + len(high))
    mixed = {word: [amount, probability * (1 - weight)] for word, amount, probability in low[:64]}
    for word, amount, probability in high[:64]:
        existing = mixed.setdefault(word, [amount, 0.0])
        existing[0] = max(existing[0], amount)
        existing[1] += probability * weight
    return sorted(((word, value[0], value[1]) for word, value in mixed.items()),
                  key=lambda item: (-item[2], -item[1], item[0]))[:64]


def context_log_probability(model, history):
    unigrams, total, _, bigrams, outgoing, _ = model
    vocabulary_size = len(unigrams)
    probabilities = []
    for index, word in enumerate(history[-12:]):
        word_count = unigrams.get(word, 0)
        unigram_probability = (word_count + 0.1) / (total + 0.1 * vocabulary_size)
        probability = unigram_probability
        if index > 0:
            previous = history[-12:][index - 1]
            context_count = outgoing.get(previous, 0)
            pair_count = next((count for candidate, count in bigrams.get(previous, []) if candidate == word), 0)
            if context_count > 0:
                probability = (pair_count + 10 * unigram_probability) / (context_count + 10)
        probabilities.append(math.log(probability))
    return sum(probabilities) / len(probabilities) if probabilities else -math.inf


def load_dialogue_contexts(path):
    for line in open(path, encoding="utf-8"):
        for sentence in re.split(r"[.!?\r\n]+", line):
            words = [turkish_lower(word) for word in sentence.split() if word.isalpha() and len(word) > 1]
            for index in range(2, len(words)):
                yield words[max(0, index - 12):index], words[index]


def load_tweet_contexts(path):
    with open(path, encoding="utf-8-sig", newline="") as source:
        for row in csv.reader(source, delimiter="\t"):
            if len(row) < 3 or row[0].lower().startswith("id"):
                continue
            words = [turkish_lower(word) for word in row[2].split()]
            for index in range(2, len(words)):
                if all(word.isalpha() and 1 < len(word) <= 40 for word in words[index - 2:index + 1]):
                    yield words[max(0, index - 12):index], words[index]


def evaluate(contexts, models):
    rows = []
    for history, expected in contexts:
        general = predict(models[0], history)
        conversation = predict(models[1], history)
        if not general or not conversation:
            continue
        difference = context_log_probability(models[1], history) - context_log_probability(models[0], history)
        rows.append((expected, general, conversation, difference))
    return rows


def main():
    root = "src/WinForge.Modules.Extras/Resources/"
    models = [load_model(root + "TurkishNgramModel.tsv.gz"),
              load_model(root + "TurkishDialogueNgramModel.tsv.gz")]
    datasets = {
        "dialogues": evaluate(load_dialogue_contexts("work/corpus/turkish-dialogues.test.raw"), models),
        "tweets": evaluate(load_tweet_contexts("work/corpus/TurkishTweets-data.txt"), models),
    }
    thresholds = [-1.0, -0.5, -0.2, -0.1, 0, 0.1, 0.2, 0.5, 1.0]
    for name, rows in datasets.items():
        print(name, "contexts", len(rows))
        for threshold in thresholds:
            hits = [0, 0]
            for expected, general, conversation, difference in rows:
                weight = 0.9 if difference >= threshold else 0.2
                scores = defaultdict(float)
                for word, _, probability in general:
                    scores[word] += probability * (1 - weight)
                for word, _, probability in conversation:
                    scores[word] += probability * weight
                ranked = sorted(scores, key=lambda word: (-scores[word], word))[:3]
                hits[0] += bool(ranked and ranked[0] == expected)
                hits[1] += expected in ranked
            print("threshold", threshold, "weight .9 if dialogue context logprob advantage >= threshold:",
                  f"top1={hits[0] / len(rows):.4%}", f"top3={hits[1] / len(rows):.4%}")


if __name__ == "__main__":
    main()
