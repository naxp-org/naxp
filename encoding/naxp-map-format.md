# naxp map files

**Status: draft, 2026-10-12.** A proposal for a JSON file format that maps the encoded values of a naxp to data, such as postcode to cluster or postcode to coordinates. Not yet normative. Section references are to the specification, v0.10.1.

## Purpose

A naxp map holds a value for some or all of the encoded values of one naxp, the **key naxp**. The file carries the key naxp and the type of the values with it, so a reader needs nothing else to use it. JSON is chosen over a binary format because the file must be easy to write and read in any language and easy to inspect. The file is always gzipped, which leaves little difference in size from a binary format.

## Example

Five London postcodes mapped to a cluster number. The keys are real encoded values under the postcode naxp from the site; the clusters are illustrative.

```json
{
	"format": "naxp-map/0.1",
	"naxp": "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA",
	"naxpVersion": "0.10",
	"naxpMaxEncodedValue": 1755842401,
	"count": 5,
	"valueType": "uint8",
	"description": "UK postcodes mapped to clusters, 2026-10-12.",
	"keyDescription": "UK postcode.",
	"valueDescription": "cluster index",
	"keys": [278793893, 1, 2, 676, 673],
	"values": [3, 3, 3, 7, 7]
}
```

The keys decode to `EC4M 7AA`, `EC4M 7AB`, `EC4M 7AD`, `EC4M 8AD` and `EC4M 9AA`. A reader recovers each key as a running total of the `keys` entries.

## Compression

A naxp map file is a single JSON object, UTF-8 encoded and then compressed with gzip (RFC 1952). Its name ends `.naxpmap.json.gz`, so that standard tools recognise both layers.

A reader must also accept the JSON uncompressed. A file served over HTTP with `Content-Encoding: gzip` is decompressed by the client before the reader sees it, and a user may unzip a file to inspect it and forget to zip it again. The two cases are told apart by the first two bytes: `1F 8B` starts every gzip stream and cannot start JSON. A server should send the file as `Content-Type: application/gzip` with no `Content-Encoding`.

## Members

The JSON object has these members:

| Member | Required | Content |
|:---|:--|:---|
| `format` | Yes | The string `naxp-map/` followed by this format's version. |
| `naxp` | Yes | The text of the key naxp. |
| `naxpVersion` | Yes | The specification version under which the keys were encoded, as `MAJOR.MINOR`. |
| `naxpMaxEncodedValue` | Yes | The key naxp's largest encoded value. |
| `count` | Yes | The number of keys that have a value, which is the length of `values` and, in a sparse map, of `keys`. Always a JSON number. |
| `valueType` | Yes | The type of the values -- see [Value types](#value-types). |
| `valueFieldNames` | No | For a tuple, an array of names, one per element, e.g. `["lat", "long"]`. |
| `description` | No | Text description (sentences) of file contents. |
| `keyDescription` | No | Text description (sentences) of the naxp key. |
| `valueDescription` | No | Text description (sentences) of the value. |
| `keys` | No | The keys, sorted and gap encoded -- see [Keys](#keys). Absent for a dense map. |
| `values` | Yes | One value per key, in key order. |

A reader must ignore any other member, so that later versions of the format can add members.

A writer must put `keys` and `values` last, so that a streaming reader has every other member, and so the size of each array, before it meets them. Both arrays hold `count` entries.

Every naxp contains backslashes, and JSON requires each one to be escaped. `\A` in a naxp is written `"\\A"` in the file. A writer that builds the file with a JSON library gets this for free; one that builds it by string concatenation does not.

## Required reader checks

A reader must parse `naxp` and refuse the file if any of the following apply:

- Its implementation's specification version differs from `naxpVersion` in a way that may change encoded values. Under section 1.1 that is a different minor version for major version 0, and a different major version from 1 onwards.
- The parsed naxp's largest encoded value is not `naxpMaxEncodedValue`. This catches a naxp damaged in transit, most often by a lost backslash.
- The length of `values`, or of `keys` when present, is not `count`.
- `keys` is absent and `count` is not `naxpMaxEncodedValue`.
- An entry of `keys` is less than 1, or the last key, the total of all the entries, exceeds `naxpMaxEncodedValue`.
- A value is `null`, or a value or the length of `valueFieldNames` does not match `valueType`.

## Keys

A **sparse map** has a `keys` array. Its first entry is the first key. Every later entry is the difference between that key and the one before, so every entry is at least 1 and keys are strictly increasing. `values` has one entry per key, and a key with no value is simply left out.

Encoded values in one area tend to be close together, so most gaps are short numbers. That shrinks the file before compression and helps gzip after it.

A **dense map** is one in which every key has a value. It omits `keys`, `count` equals `naxpMaxEncodedValue`, and entry *i* of `values` (counting from 0) holds the value for key *i* + 1.

A map with any key missing must be sparse, so `null` never appears in `values`. A dense form with gaps would gain almost nothing: in a nearly full map almost every gap is `1`, and gzip shrinks a run of them to almost nothing. A reader that wants a dense array in memory can build one from a sparse map, with whatever marker for a missing value suits its language.

## Value types

`valueType` is one of:

- `"float64"`, a double. It must be finite, since JSON cannot write NaN or an infinity. A writer should write the shortest text that reads back as the same double, and may round first: five decimal places of a degree is about a metre.
- `"int8"`, `"int16"`, `"int32"`, `"int64"`, `"uint8"`, `"uint16"`, `"uint32"` or `"uint64"`, an integer of that range.
- `{"naxp": "...", "naxpVersion": "...", "naxpMaxEncodedValue": ...}`, an encoded value of another naxp, from 1 to its `naxpMaxEncodedValue`. A reader checks it as it checks the key naxp.
- an array of two or more of the above, a tuple, written in `values` as an array of the same length. A tuple is how a map holds more than one value per key. Tuples do not nest: a nested structure is written flat, with `valueFieldNames` carrying the structure, e.g. `["fromLat", "fromLong", "toLat", "toLong"]`.

Coordinates as latitude then longitude are `"valueType": ["float64", "float64"]` with `"valueFieldNames": ["lat", "long"]`. The names are worth giving. GeoJSON puts longitude first, and a file without names leaves the reader to guess.

## Large integers

Section 16 notes that most JSON parsers read a number into a double, which is exact only up to 2<sup>53</sup>. So:

- `int64` and `uint64` values are always written as decimal strings, e.g. `"18446744073709551615"`;
- a naxp-valued element, `naxpMaxEncodedValue`, and every entry of `keys` is written as a string when the relevant naxp's `naxpMaxEncodedValue` exceeds 2<sup>53</sup>, and as a number otherwise.

Each rule follows from the declared types alone, so a reader knows in advance which form to expect.
