# Third-party notices

## ZemberekDotNet 0.19.5

WinForge.Modules.Extras uses the Morphology, Core and Tokenization packages from
[ZemberekDotNet](https://github.com/JnRMnT/ZemberekDotNet) to analyze Turkish word
forms. The package metadata declares MIT; the pinned source revision
(`6b9e66c5ca694cb62edff439aa160560a8804742`) contains an Apache 2.0 license.
Both upstream license texts are included in
[`third_party/ZemberekDotNet`](../third_party/ZemberekDotNet/).

The Core and Tokenization packages are referenced directly because NuGet does
not copy their runtime content files transitively. These files are required by
the analyzer at runtime.

## Turkish n-gram model

The embedded Turkish frequency model is derived from the training split of
[trnews-64](https://github.com/tdd-ai/trnews-64), licensed under CC BY 4.0.
Its attribution and reproduction instructions are in
[`AUTOCORRECT.md`](AUTOCORRECT.md).

## English n-gram model

The English word and phrase frequency model is derived from the training split
of [WikiText-103](https://huggingface.co/datasets/Salesforce/wikitext), licensed
under CC BY-SA 4.0. The dataset is based on verified Good and Featured Wikipedia
articles and is distributed with the WikiText paper citation in
[`AUTOCORRECT.md`](AUTOCORRECT.md). The model is distributed under the same
share-alike terms.

## English spelling error model

The high-confidence English typo correction resource is derived from the
TOEFL-Spell annotations published by Educational Testing Service. The dataset is
licensed under CC BY-SA 4.0. The derived correction resource is distributed
under the same terms. Its citation and reproducible file-level split are in
[`AUTOCORRECT.md`](AUTOCORRECT.md).

## Conversational n-gram models

The separate English and Turkish conversational frequency models are derived
from the training splits of [English Daily Dialogues 10K](https://huggingface.co/datasets/3nesdeniz/english-daily-dialogues-10k)
and [Turkish Daily Dialogues 5K](https://huggingface.co/datasets/3nesdeniz/turkish-daily-dialogues-5k),
respectively. Both datasets are synthetic and licensed under CC BY 4.0. Their
attribution and training/test split instructions are in
[`AUTOCORRECT.md`](AUTOCORRECT.md).

The Turkish conversational model also uses the Turkish-only user and assistant
turns from the `casual_conversation` subset of
[Turkish Chat Max 25K](https://huggingface.co/datasets/cisimcik/turkish-chat-max-25k),
published by Cisimcik AI Lab under Apache License 2.0. The license text is in
[`third_party/TurkishChatMax/LICENSE`](../third_party/TurkishChatMax/LICENSE).
No other task subsets or reasoning fields are included.
