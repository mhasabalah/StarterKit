# Text and languages

Search prepares text the same way for every engine, so results do not change when the engine does: documents and
queries go through one normalizer, queries through one parser, and highlights through one highlighter
(`src/Modules/Search/Search/Engine/Text/`).

Part of the [Search module](../Modules/Search.md).

## Query syntax

What people type, the same grammar as web search engines:

| Typed | Matches |
|---|---|
| `red lamp` | documents with both words, in any field and order |
| `"red lamp"` | the phrase: the words next to each other, in one field |
| `lamp -red` | lamps without the word red (`-"red lamp"` excludes a phrase) |
| `red OR blue lamp` | (red or blue) and lamp |
| `ab-123` | the phrase `ab 123`: punctuation separates words, so codes and SKUs match as typed |
| `lam` (autocomplete) | words starting with `lam`: in autocomplete mode the last word is a prefix |

- Every word must match. Words beyond `Search:Query:MaxTerms` (10) are dropped; text beyond `Query:MaxTextLength`
  (200) is refused.
- A query of excluded words only is no query: it would match nearly everything, so the search browses instead.
- When nothing matches and a word has at least `Query:Fuzzy:MinTermLength` (3) letters, a second pass allows close
  spellings ("lantren" finds "lantern") and the result says `Fuzzy`. A caller can turn this off per query
  (`Fuzzy = false`).

## Normalization

Applied to documents and queries alike, following Lucene's Arabic rules (`SearchTextNormalizer`):

| Rule | Example |
|---|---|
| Arabic diacritics (tashkeel), Quranic marks and tatweel are removed | `مُحَمَّد` and `مـحـمـد` match `محمد` |
| Alef forms fold: أ إ آ ٱ become ا | `أحمد` matches `احمد` |
| ى becomes ي, ة becomes ه | `مستشفى` matches `مستشفي`; `مدرسة` matches `مدرسه` |
| Hamza on waw and ya folds: ؤ to و, ئ to ي | `مسؤول` matches `مسوول` |
| Arabic-Indic and Persian digits become 0-9 | `١٢٣` matches `123` |
| Latin loses case and accents | `Café` matches `cafe` |
| Ligatures and full-width forms are decomposed; invisible characters are removed | `ﬁle` matches `file` |

A map back to the original text is kept, so highlights mark the text as it was written. Changing a rule bumps
`RegisteredIndex.FormatVersion`, which changes every index's fingerprint, so reconcile rebuilds them all.

## Analyzers and word forms

Each text field of an index declares an analyzer:

| Analyzer | Use for | Word forms ("lamps" finds "lamp") |
|---|---|---|
| `Simple` | codes, SKUs, names that must not be stemmed | no |
| `English` | English prose | yes on PostgreSQL |
| `Arabic` | Arabic prose | yes on PostgreSQL |
| `Bilingual` | a field that may hold either language | yes on PostgreSQL |

Meilisearch has no stemming: the analyzers map to its language settings and it relies on prefixes and typo tolerance
instead ([differences](Meilisearch.md#at-a-glance)).

## Highlights

With `Highlight = true` (and text in the query), each hit carries, for each highlight field, a snippet of the
**original** text: about 160 characters around the first match, cut on word boundaries, HTML-encoded, with matches
wrapped in `<mark>`. A word is marked when its normalized form equals a query word, starts with it, is a stem of it,
or does so after an Arabic article or prefix (ال، وال، بال، كال، فال، لل). Excluded words are never marked.

Snippets are safe to insert as HTML: everything except the `<mark>` tags is encoded.

## Related

[Search module](../Modules/Search.md), [Searching from a client](Using-Search.md),
[PostgreSQL engine](Postgres-Engine.md#analyzers).
