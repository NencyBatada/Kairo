# Kairo Brand Kit and Catalogue — design

*14 October 2026 · Approved in conversation; this is the written record*

## Goal

Give every agent an approved picture of the brand and an accurate product list. Kairo drafts the Brand Kit from the brand's website and best ads, admins edit it, add guideline files and rules, and approve it as a version, and agents only ever see the approved version. Products come in from a file or a feed link in Google's Merchant Center format, refresh every hour, and any product missing a detail is listed so it can be fixed.

Tasks: `docs/task.md` §2, Talha: **Brand rules and versions**, **Brand Kit drafting** and **Catalogue import**, built in that order, each on its own branch. PRD US-1.1 to US-1.4 and §7, SAD §3, §4, §6 and §7. The screens are Nency's **Brand Kit screens** and **Catalogue screens**; the API below is the contract between the two sides.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Kit storage | One JSON document per version, rules and guideline file references included | A version is approved or restored as a whole, so rules can't drift from the kit they were approved with. SAD §6: "Kit content stored as JSON". |
| Versions | At most one draft and one approved version per brand; older approved versions become superseded | PRD §7 and US-1.4 |
| Restore | Copies an old version into the draft, which then needs approval | Every change to what agents see is approved. Chosen by the product owner. |
| How agents get the kit | `IBrandKit.GetApprovedAsync` in `Kairo.Contracts` | The core sends each agent stage its state (SAD §4), so the module that builds the request reads the kit through the contract |
| Who reads the website | The core fetches pages and ads; the agent service only calls Gemini | SAD §7: the agent service talks only to Google's AI services. SSRF guards and size limits stay in one place, and Model Armor screening (§14) has one place to plug in. |
| Drafting model call | The google-genai SDK with a Pydantic response schema, not an ADK agent | One structured call needs no agent loop or session. ADK stays for the multi-step agents from §3 on. |
| Drafting runs | In the background, from a SQL table with leases, processed by a loop in the API | The same pattern as the email outbox; no new infrastructure. The general agent job queue is §5's **Agent jobs**. |
| Files | Cloud Storage through a new `IFileStore`, with a disk store for development and tests | SAD §2 and §6 |
| Catalogue format | Google's Merchant Center product data spec: CSV, TSV and RSS or Atom XML | Shops already export it, and Shopping campaigns need it later. Chosen by the product owner. |
| Feed refresh | Hourly through Cloud Scheduler, plus a refresh endpoint | Stock pauses must happen within an hour of a sell-out (PRD §4, day 3). Chosen by the product owner. |
| Fixes | A fix fills a field only while the feed leaves it empty | Fixes survive hourly refreshes and the feed stays the source of truth. Chosen by the product owner. |
| Google APIs and the web | Plain REST through `HttpClient`, as the Brands and Measurement modules do, with fake handlers in tests | One style for every outside call |
| Comments | None in code | `CLAUDE.md` §3 |

## Notes on the documentation

1. **Agent framework.** SAD §2 and §4 build every agent with ADK. Brand Kit drafting is one structured Gemini call, so it uses the Gen AI SDK alone. This task adds drafting to SAD §4's table with that note.
2. **Brand Kit tables.** SAD §6 lists `BrandKitVersion`, `BrandRule`, `BrandFile` and `BestAd`. Rules live inside each version's JSON instead of their own table, so they are versioned with the kit. Best ads are read from Google Ads when drafting and recorded on the draft run, not stored as their own table. This task updates SAD §6.
3. **Notifications.** PRD §9 has no Brand Kit event. Drafting takes up to a minute in the background, so the person who started it gets an in-app notification, of a new kind `brand_kit.drafted`, when it finishes or fails. A failing feed uses the existing `connection.problem`. This task adds a row to PRD §9.
4. **Prompt-injection screening.** SAD §4 screens web pages and catalogue text with Model Armor. That is its own task, §14 **AI safety screening**. Until then, page and ad text is passed to the model only as quoted data, and the prompt says never to follow it.
5. **New schedule and buckets.** SAD §2 gains the `kairo-catalogue-hourly` schedule, and SAD §6 the `{project}-brand-files` and `{project}-catalogue-imports` buckets.

## Shared building blocks

### File store

`Kairo.SharedKernel` gains `Files/IFileStore`:

```csharp
public interface IFileStore
{
    Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken);
}
```

- **Buckets.** Logical names `brand-files` and `catalogue-imports`, mapped to `{project}-brand-files` and `{project}-catalogue-imports`. `setup.ps1` creates both with uniform bucket-level access and public access blocked, grants `kairo-api` and `kairo-worker` `roles/storage.objectAdmin` on them, and gives `catalogue-imports` a 30-day delete lifecycle rule.
- **Keys.** `{brandId:N}/{area}/{id:N}`, for example `…/guidelines/…`, `…/logos/…` or `…/imports/…`. File names stay in SQL, never in keys.
- **Cloud Storage.** JSON API over REST (`https://storage.googleapis.com/storage/v1` and `/upload/storage/v1`), with the service account's Application Default Credentials scoped to `devstorage.read_write`. Files reach the browser only through the core's download endpoints, as attachments, so no signed URLs are needed.
- **Development and tests.** When `Kairo:Files:Project` isn't set, a disk store writes under `Kairo:Files:Root` (default: the `kairo-files` folder in the system temp folder).

### Safe web client

`Kairo.SharedKernel` gains `Http/SafeWebClient`, used to read brand websites and catalogue feeds, which are untrusted addresses.

- Only `http` and `https`, on ports 80 and 443, with no user name or password in the URL.
- The host name is resolved before connecting, through a `SocketsHttpHandler.ConnectCallback`, and the connection is refused when any address is loopback, private (RFC 1918, RFC 4193), link-local, carrier-grade NAT, multicast, unspecified, or the metadata server. Because the check is in the connect callback, it applies to every redirect as well.
- At most 3 redirects, followed by hand so each one is checked.
- A caller-chosen body limit and timeout. The body is read as a stream and stops at the limit with a `SafeFetchException` (`too_large`, `timeout`, `blocked_address`, `bad_status`, `not_html`, `unreachable`).
- gzip and deflate are accepted. The user agent is `KairoBot/1.0 (+{Kairo:PublicOrigin})`.
- `robots.txt` is read once per host per run and honoured for the `KairoBot` agent and `*`, for website reading only. Feed links are addresses the brand gave us, so they aren't checked against it.

## Brand Kit

Module `Kairo.Modules.BrandKit`, SQL schema `brand_kit`.

### Content

Every version holds one JSON document:

```json
{
  "voice": {
    "summary": "Friendly, upbeat and practical, like a running coach.",
    "traits": ["energetic", "plain-spoken"],
    "dos": ["Talk about comfort over long distances"],
    "donts": ["Use slang"],
    "examples": ["Built for your longest run yet."],
    "sources": [{ "kind": "page", "ref": "https://acme.example/about" }]
  },
  "colours": [
    { "hex": "#E4572E", "name": "Ember", "role": "primary", "sources": [{ "kind": "page", "ref": "https://acme.example/" }] }
  ],
  "logo": { "fileId": "6f1c…", "sourceUrl": "https://acme.example/logo.svg" },
  "keyMessages": [{ "text": "Free returns for 30 days", "sources": [{ "kind": "ad", "ref": "123456789" }] }],
  "productCategories": [{ "name": "Running shoes", "sources": [{ "kind": "page", "ref": "https://acme.example/running" }] }],
  "rules": {
    "bannedWords": ["cheap"],
    "requiredDisclaimers": [{ "text": "T&C apply.", "when": "Any offer or discount" }],
    "tone": ["Never use all capitals"],
    "competitors": [{ "name": "Rival Shoes", "policy": "never" }]
  },
  "guidelineFileIds": ["0b9e…"]
}
```

- `sources` is optional everywhere and only set by drafting. `kind` is `page` (an absolute URL) or `ad` (a Google Ads ad ID).
- `colours[].role` is `primary`, `secondary`, `accent`, `background` or `text`. `hex` is `#RRGGBB`, upper case.
- `competitors[].policy` is `never` (never mention) or `neutral` (mention only factually, never disparagingly).
- **Limits.** Voice summary 500 characters; up to 10 traits, 20 dos, 20 don'ts and 10 examples of 200 characters each; up to 12 colours; up to 20 key messages of 200 characters; up to 50 product categories of 100 characters; up to 200 banned words of 50 characters; up to 20 disclaimers of 500 characters; up to 20 tone rules of 200 characters; up to 50 competitors; up to 20 guideline files. Every list is trimmed and empty entries removed.
- `logo.fileId` and every `guidelineFileIds` entry must be a file of this brand.
- An empty kit is valid, so an admin can start from nothing when the site can't be read.

### Tables

| Schema | Table | Belongs to a workspace | Holds |
| --- | --- | --- | --- |
| `brand_kit` | `KitVersions` | Yes | `Id`, brand, `Number` (1, 2, 3… per brand), `Status` (`Draft`, `Approved`, `Superseded`), `Content` (JSON), `Revision` (int; a new draft starts one above the highest revision any of the brand's versions has had, and each save adds 1, so a revision never names two drafts), `Origin` (`manual`, `drafted` or `restored`), `RestoredFrom` (number or null), `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`, `ApprovedBy`, `ApprovedAt` |
| `brand_kit` | `KitFiles` | Yes | `Id`, brand, `Kind` (`guideline` or `logo`), `FileName`, `ContentType`, `Size`, `StorageKey`, `UploadedBy`, `UploadedAt`, `DeletedAt` |
| `brand_kit` | `DraftRuns` | Yes | See **Drafting** |

- Filtered unique indexes allow one `Draft` and one `Approved` row per brand, and `Number` is unique per brand.
- A version's number is fixed when its draft is created. A discarded draft is kept with status `Discarded`, hidden from history, so its number is not reused.
- Files are never changed. Removing a guideline file sets `DeletedAt` and takes it out of the draft; versions that referred to it still show its name, and the stored object is kept so an older version can be restored.

### API

All under `/api/brands/{brandId}/brand-kit`. Every member can read; only admins can change anything.

| Method and path | What it does |
| --- | --- |
| `GET /` | `{ approved, draft }`, each a version view or `null` |
| `PUT /draft` | Saves the whole content. The body is `{ content, revision }`. With no draft, it creates one from the request (`revision` must be `0`). With a draft, `revision` must match its current `Revision`, or the response is a `409` problem. Returns the draft. |
| `DELETE /draft` | Discards the draft. `204`, or `404` with no draft. |
| `POST /draft/approve` | The body is `{ revision }`. The draft becomes `Approved`, the previous approved version `Superseded`. `409` on a stale revision. Returns the approved version. |
| `GET /versions` | Every version, newest first, without content: number, status, origin, restored from, created, updated and approved by and at |
| `GET /versions/{number}` | One version with its content |
| `POST /versions/{number}/restore` | Copies that version's content into the draft, replacing any draft, with origin `restored`. The body is `{ revision }` of the current draft, or `0` with none. Restoring the approved or draft version is a `400`. Returns the draft. |
| `POST /files` | Multipart upload, one `file` field, at most 20 MB, of PDF, PNG, JPEG, WebP, SVG or DOCX, checked by content type and the file's first bytes. `kind` is `guideline` (default) or `logo`. Returns `201` with the file view. Uploading does not change the draft; adding it to the draft is a `PUT /draft`. |
| `GET /files` | Guideline and logo files that aren't deleted |
| `GET /files/{fileId}/download` | The file itself, as an attachment, with `X-Content-Type-Options: nosniff` and `Content-Security-Policy: sandbox` |
| `DELETE /files/{fileId}` | Marks the file deleted and removes it from the draft, raising the draft's revision |

A version view is `{ number, status, origin, restoredFrom, revision, content, files, createdBy, createdAt, updatedBy, updatedAt, approvedBy, approvedAt }`, where `files` lists `{ id, kind, fileName, contentType, size, deleted }` for every file the content refers to.

Validation problems are `400` with field paths such as `content.colours[2].hex`.

### Contract for other modules

```csharp
namespace Kairo.Contracts.BrandKit;

public interface IBrandKit
{
    Task<ApprovedBrandKit?> GetApprovedAsync(CancellationToken cancellationToken);
}

public sealed record ApprovedBrandKit(int Version, JsonElement Content, IReadOnlyList<BrandKitFile> Files);

public sealed record BrandKitFile(Guid Id, string Kind, string FileName, string ContentType);
```

It reads in the current brand's scope, like `IConnections`. Drafts are never returned.

### Side effects

- **Audit.** `brand_kit.draft_saved`, `brand_kit.draft_discarded`, `brand_kit.approved` (with the version number), `brand_kit.restored`, `brand_kit.file_uploaded` and `brand_kit.file_deleted`.
- **Live events.** `brand_kit.changed` with `{ number, status }` to everyone in the workspace, after every change to a version, and `brand_kit.files_changed` with `{ fileId }` after a file is uploaded or deleted.

## Brand Kit drafting

### Starting and following a run

| Method and path | Who | What it does |
| --- | --- | --- |
| `POST /api/brands/{brandId}/brand-kit/drafting` | Admin | The body is `{ websiteUrl, replaceDraft }`. Returns `202` with the run. `409` when a run is `pending` or `running`, or when a draft exists and `replaceDraft` isn't `true`. `400` when the URL isn't an absolute `http` or `https` address. |
| `GET /api/brands/{brandId}/brand-kit/drafting/latest` | Member | The latest run, or `204` with none |

A run view is `{ id, status, websiteUrl, pages: [{ url, title }], ads: [{ id, headline }], error: { code, message } | null, requestedBy, requestedAt, finishedAt, version }`, where `version` is the draft number it produced.

`brand_kit.DraftRuns` holds `Id`, brand, `WebsiteUrl`, `Status` (`pending`, `running`, `succeeded`, `failed`), `ErrorCode`, `ErrorMessage`, `Pages` (JSON), `Ads` (JSON), `Model`, `PromptVersion`, `Attempts`, `ClaimedUntil`, `RequestedBy`, `RequestedAt`, `StartedAt`, `FinishedAt` and `VersionNumber`.

### Processing

A hosted loop in the API, `DraftDispatcher`, wakes every 2 seconds and claims up to 2 due runs with `UPDATE TOP … WITH (READPAST, UPDLOCK, ROWLOCK)` and a 5-minute lease, like the email outbox. Each run:

1. **Reads the website.** The homepage, then up to 8 more pages on the same host, picked from the homepage's links. Links whose path or text mention about, story, our-story, mission, collections, collection, shop, category, products, men, women or kids come first, then the rest in page order. Each page is at most 2 MB and 10 seconds, the whole read at most 60 seconds, and only `text/html` is kept. AngleSharp takes the title, meta description, `h1`–`h3` headings and the main text (`main`, else `body`, without `script`, `style`, `nav`, `footer` or `form`), each page cut to 6,000 characters.
2. **Collects colour and logo candidates.**
   - Colours: `meta[name=theme-color]`, CSS custom properties and `#rgb`, `#rrggbb` or `rgb()` values in `style` attributes, `<style>` blocks and up to 3 same-site stylesheets of at most 500 KB each. Each is normalised to `#RRGGBB` and counted, greys and near-white or near-black are dropped (saturation under 10%, or lightness under 8% or over 95%), and the top 12 are kept, with where each came from.
   - Logos: images in `header` or with `logo` in their `src`, `alt`, `class` or `id`, the `logo` of JSON-LD `Organization`, `link[rel=apple-touch-icon]`, `link[rel=icon]` and `meta[property=og:image]`. At most 10, each with an ID.
3. **Reads the best ads**, when Google Ads is Connected. One `searchStream` query, the same API version and headers as the analytics feeds:

   ```sql
   SELECT ad_group_ad.ad.id, ad_group_ad.ad.type,
          ad_group_ad.ad.responsive_search_ad.headlines, ad_group_ad.ad.responsive_search_ad.descriptions,
          ad_group_ad.ad.responsive_display_ad.headlines, ad_group_ad.ad.responsive_display_ad.descriptions,
          ad_group_ad.ad.final_urls, metrics.conversions, metrics.clicks
   FROM ad_group_ad
   WHERE segments.date DURING LAST_90_DAYS AND ad_group_ad.status = 'ENABLED' AND metrics.impressions > 0
   ORDER BY metrics.conversions DESC, metrics.clicks DESC
   LIMIT 10
   ```

   A missing or broken connection means no ads, not a failure.
4. **Stops early** with `site_unreadable` when no page could be read and there are no ads: "Kairo couldn't read {website}. You can fill in the Brand Kit by hand."
5. **Calls the agent service** at `POST /brand-kit/draft` (below), with a 90-second timeout.
6. **Checks the answer.** It must match the schema. Each source ID is mapped back to its page URL or ad ID, items whose sources are all unknown are dropped, colours are normalised and duplicates removed, and every limit in **Content** is applied by cutting lists, not by failing.
7. **Saves the logo.** When the agent chose a logo candidate, the core downloads it (at most 2 MB; PNG, JPEG, WebP, SVG or ICO, checked by its first bytes) as a `logo` file. A download failure just leaves the logo empty.
8. **Saves the draft.** Replacing any draft, with origin `drafted`, and records the model, prompt version, pages and ads on the run.

Then it publishes `brand_kit.drafting` with `{ runId, status }` to admins, `brand_kit.changed` to everyone, and a `brand_kit.drafted` in-app notification to the person who started it, linking to `/{handle}/brand-kit`. `running` is also published when the run starts.

**Failures.** An agent error, timeout or invalid answer releases the lease and retries once. The second failure ends the run as `drafting_failed`: "Kairo couldn't draft the Brand Kit this time. Try again, or fill it in by hand." A run whose lease runs out twice is failed the same way. Every failure is logged with the run ID.

### Calling the agent service

- **Address.** `Kairo:Agents:Url`. In Aspire, the API's reference to `agents` sets it through service discovery. In Cloud Run, `deploy.ps1` sets it to `kairo-agents`' URL.
- **Identity.** In Cloud Run, the core adds a Google ID token for the agent URL as audience, from the metadata server, cached until 5 minutes before it expires. Locally no token is sent. Cloud Run's invoker check is the authorisation; `kairo-api` and `kairo-worker` already hold the invoker role.
- **Client.** `Kairo.SharedKernel` gains `Agents/AgentClient`, a typed `HttpClient` that later agent calls reuse.

### The agent

`agents/src/kairo_agents/agents/brand_kit/` with `POST /brand-kit/draft` in FastAPI.

Request:

```json
{
  "brand": { "name": "Acme Shoes", "currency": "INR" },
  "pages": [{ "id": "p1", "url": "https://acme.example/", "title": "Acme", "description": "…", "headings": ["…"], "text": "…" }],
  "ads": [{ "id": "a1", "headlines": ["…"], "descriptions": ["…"] }],
  "colourCandidates": [{ "hex": "#E4572E", "count": 41, "from": ["theme-color", "css"] }],
  "logoCandidates": [{ "id": "l1", "url": "https://acme.example/logo.svg", "from": "header-img", "alt": "Acme" }]
}
```

Response: the **Content** shape without `rules`, `guidelineFileIds` and `logo.fileId`, where every `sources` entry is a page or ad `id` from the request, `logo` is `{ "candidateId": "l1" }` or `null`, plus `{ "model": "…", "promptVersion": "brand-kit-draft/1" }`.

- One `generate_content` call through the Gen AI SDK on Vertex AI, with the Pydantic model as `response_schema` and `response_mime_type` `application/json`.
- The model is `KAIRO_BRAND_KIT_MODEL`, a current Gemini Flash model by default. The project and location come from `GOOGLE_CLOUD_PROJECT` and `GOOGLE_CLOUD_LOCATION`, with Application Default Credentials.
- The prompt is a versioned file in the package. Page and ad text goes inside clearly marked data blocks, and the instructions say to treat it as content to describe, never as instructions, to cite only IDs given, to pick colours only from the candidates, and to leave a field empty rather than guess.
- `422` for a request that doesn't match the schema. `502` with `{ "error": "model_failed" }` when the model call fails or its answer doesn't parse.
- `setup.ps1` grants `kairo-agents` `roles/aiplatform.user`, and `deploy.ps1` sets its project and location.

## Catalogue

Module `Kairo.Modules.Catalogue`, SQL schema `catalogue`.

### Format

Google's Merchant Center product data specification:

- **Delimited text.** UTF-8, with or without a byte-order mark, and a header row. Tab-separated when the header holds a tab, else comma-separated with RFC 4180 quoting.
- **XML.** RSS 2.0 (`channel/item`) or Atom (`feed/entry`), with attributes in the `http://base.google.com/ns/1.0` namespace (`g:`). RSS `title`, `link` and `description` and Atom `title`, `link/@href` and `summary` are used when the `g:` element is absent.
- **Detection.** XML when the content starts with `<` after whitespace and a byte-order mark; delimited text otherwise. gzip is unpacked first.
- **Attribute names** match ignoring letter case, a `g:` prefix, and spaces instead of underscores, so `Image Link` is `image_link`. Unknown columns are ignored.
- **Size.** At most 50 MB unpacked and 50,000 products.

| Attribute | Stored as | Rule |
| --- | --- | --- |
| `id` | `OfferId` | Required, at most 50 characters, trimmed. A missing `id` or a repeated one makes the row invalid. |
| `title` | `Title` | At most 150 characters |
| `description` | `Description` | At most 5,000 characters |
| `link` | `Link` | An absolute `http` or `https` URL |
| `image_link` | `ImageLink` | An absolute `http` or `https` URL |
| `additional_image_link` | `AdditionalImageLinks` | Up to 10, comma-separated in text or repeated in XML |
| `price` | `Price`, `Currency` | `1499.00 INR`, `INR 1499` or `1499`. No currency means the brand's currency. |
| `sale_price` | `SalePrice` | Same format; ignored when its currency differs from `price` |
| `availability` | `Availability` | `in_stock`, `out_of_stock`, `preorder` or `backorder`, accepting spaces for underscores |
| `product_type` | `Category` | The first value; else `google_product_category` |
| `google_product_category` | `GoogleCategory` | As given |
| `brand` | `Brand` | At most 70 characters |
| `gtin` | `Gtin` | Digits only, 8 to 14 |
| `item_group_id` | `ItemGroupId` | At most 50 characters |

A value that breaks its rule is left empty and counted as a warning, not an invalid row, so one bad price doesn't drop the product. The import keeps the first 50 errors and warnings as `{ row, attribute, message }`, where row 1 is the header and XML rows count items from 1.

### Tables

| Schema | Table | Belongs to a workspace | Holds |
| --- | --- | --- | --- |
| `catalogue` | `Products` | Yes | `Id`, brand, the attributes above, `Fixes` (JSON), `Missing` (flags: title, price, link, image, stock), `Status` (`active` or `removed`), `FirstSeenAt`, `UpdatedAt`, `RemovedAt`, `LastImportId` |
| `catalogue` | `Sources` | Yes | One row per brand: `Kind` (`file` or `feed`), `FeedUrl`, `SetBy`, `SetAt`, `LastCheckedAt`, `LastSucceededAt`, `FailuresInRow` |
| `catalogue` | `Imports` | Yes | `Id`, brand, `Kind` (`file` or `feed`), `FileName` or `FeedUrl`, `Status` (`succeeded`, `unchanged`, `failed`, `suspicious`), `Rows`, `Valid`, `Invalid`, `Added`, `Updated`, `Removed`, `Unchanged`, `Problems` (JSON), `ErrorCode`, `ErrorMessage`, `ContentHash` (SHA-256), `StorageKey`, `StartedBy` (null for the schedule), `StartedAt`, `FinishedAt` |
| `catalogue` | `StockEvents` | Yes | `Id`, brand, `ProductId`, `From`, `To` (an availability or `removed`), `ImportId`, `At` |

`OfferId` is unique per brand. `Missing` is recomputed on every import and fix, and indexed with `Status`.

### Importing

Every import is a full snapshot of the catalogue:

1. Store the raw file or feed body in `catalogue-imports`, and hash it. The same hash as the last `succeeded` import of the same source ends as `unchanged`, touching no product.
2. Parse. A parse failure (not delimited text or XML, no `id` column, too large) ends as `failed`.
3. **Safeguard.** No valid rows, or removing more than half of the active products (when there are at least 20), ends as `suspicious` and changes nothing. A file upload can pass `allowLargeRemoval=true` to go ahead.
4. Match on `OfferId`. New products are added; changed ones updated; active products not in the import become `removed` with `RemovedAt`; removed products that come back become `active`.
5. Each change of availability writes a `StockEvent`, including to and from `removed`.
6. Drop each fix whose field the import now supplies, and recompute `Missing`.
7. Save in batches, so 50,000 products import within 60 seconds.

A product is missing a detail when the import's value and its fix are both empty. The effective value of a fixable field is the import's value, else the fix.

### API

All under `/api/brands/{brandId}/catalogue`. Every member can read. Admins manage the source and imports; admins and marketers can fix products.

| Method and path | Who | What it does |
| --- | --- | --- |
| `POST /imports` | Admin | Multipart, one `file` field, plus `allowLargeRemoval`. Imports the file now and returns `201` with the import, in any status. Makes the source `file` and stops any feed refresh. |
| `GET /imports` | Member | The latest 50 imports, newest first, without problems |
| `GET /imports/{importId}` | Member | One import with its problems |
| `GET /source` | Member | The source, or `204` with none |
| `PUT /feed` | Admin | The body is `{ url }`. Checks the URL, saves the source as `feed`, imports it now and returns `200` with `{ source, import }`. A fetch that fails is a `400` naming why (for example, "Kairo couldn't reach this address."), and the source isn't saved. |
| `DELETE /feed` | Admin | Stops refreshing. Products stay. |
| `POST /feed/refresh` | Admin | Imports the feed now and returns the import. `409` with no feed, or when an import of this brand's feed is already running. |
| `GET /summary` | Member | `{ products, active, inStock, outOfStock, missing: { title, price, link, image, stock, any }, categories: [{ name, products }], lastImport, source }` |
| `GET /products` | Member | Paged with `page` (from 1) and `pageSize` (default 50, at most 200). Filters: `search` (title or `id`), `category`, `availability`, `missing` (`any`, `title`, `price`, `link`, `image` or `stock`) and `status` (default `active`). Returns `{ items, page, pageSize, total }`. |
| `GET /products/{productId}` | Member | One product with its fixes and its last 20 stock events |
| `PATCH /products/{productId}/fixes` | Admin, marketer | Any of `title`, `price` (with `currency`), `link`, `imageLink` and `category`; `null` clears a fix. Setting a fix for a field the import already supplies is a `400`. Stock can't be fixed, because stale stock would mislead stock pauses; it is fixed in the file or feed. Returns the product. |

A product view is `{ id, offerId, title, description, link, imageLink, additionalImageLinks, price, salePrice, currency, availability, category, googleCategory, brand, gtin, itemGroupId, fixed: [fields], missing: [fields], status, updatedAt }`, with each fixable field showing its effective value.

### Hourly refresh

- `POST /jobs/catalogue/refresh` on the worker, with an optional `?brandId={guid}`, imports every brand whose source is a feed. Brands run 4 at a time, each in its own scope, and one brand's failure doesn't stop the rest.
- Cloud Scheduler job `kairo-catalogue-hourly`: schedule `0 * * * *`, time zone `Asia/Kolkata`, an OIDC token from `kairo-scheduler`, `--attempt-deadline 1800s` and no retries.
- A feed fetch is at most 50 MB and 120 seconds.
- Each run sets `LastCheckedAt`; a `succeeded` or `unchanged` import sets `LastSucceededAt` and clears `FailuresInRow`. A `failed` or `suspicious` import adds one, and on reaching 3 sends one `connection.problem` notification to admins: "Kairo couldn't refresh your product feed 3 times in a row." It doesn't send again until a refresh succeeds.
- An import of the same brand's feed already running (in this process or another, through an application lock on the brand) makes the run skip that brand.

### Side effects

- **Audit.** `catalogue.file_imported`, `catalogue.feed_set`, `catalogue.feed_removed`, `catalogue.feed_refreshed` and `catalogue.product_fixed`. Scheduled imports aren't audited one by one; they are in `Imports`.
- **Live events.** `catalogue.imported` with `{ importId, status }` to everyone after every import, and `catalogue.product_changed` with `{ productId }` after a fix.

## Errors

Every problem response uses the existing problem details shape. Messages are plain sentences meant for the screen, as in the analytics feeds.

## Testing

- **Core integration tests**, on `KairoApiFactory` and `KairoWorkerFactory` with SQL Server:
  - Brand Kit: draft save, revision conflicts, approve, supersede, restore, file upload, delete and download, role checks, brand separation, audit and live events, and `IBrandKit` returning only the approved version.
  - Drafting: the dispatcher end to end with fake websites, fake Google Ads and a fake agent, through `HttpMessageHandler`; retries, `site_unreadable`, the logo download and the notification.
  - Catalogue: file and feed imports, `unchanged`, `suspicious` and `allowLargeRemoval`, removal and return, stock events, fixes that fill gaps and are dropped, paging and filters, the hourly job, failure counting and the alert.
- **Core unit tests**: CSV, TSV, RSS and Atom parsing on real-shaped samples, attribute name matching, price and availability parsing, colour normalisation, logo and page picking, and the safe web client against blocked addresses, redirects, oversized and slow bodies.
- **Agent tests** (pytest): the `/brand-kit/draft` endpoint with a fake Gen AI client, schema enforcement, the `422` and `502` paths, and the prompt keeping data in its data blocks.
- **Architecture tests**: the new modules reach others only through `Kairo.Contracts`.
- **Deploy tests**: the new buckets, roles and schedule in `setup.ps1` and `deploy.ps1`.

## Done when

- **Brand rules and versions:** an admin can save, approve and restore versions and manage guideline files through the API; agents' contract returns only the approved kit; every change is audited and live.
- **Brand Kit drafting:** drafting the demo brand's site and test ads in the development project produces a draft with sources in under a minute, and an unreadable site gives the hand-fill message.
- **Catalogue import:** the demo brand's catalogue imports from a file and a feed link, the hourly refresh runs in the development project, missing details are listed and fixable, and stock changes are recorded.
