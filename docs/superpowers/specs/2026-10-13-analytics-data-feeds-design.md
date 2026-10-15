# Kairo Analytics Data Feeds — design

*13 October 2026 · Approved in conversation; this is the written record*

## Goal

Every night, bring each brand's Google Ads performance, its GA4 outcomes and its sales into a BigQuery dataset of its own, by day and region, so the Prove agent and Impact Lab have the history they measure lift against. Brands can also upload sales as a CSV file, or point Kairo at a sales table they already keep in BigQuery.

Task: `docs/task.md` §1, Talha, **Analytics data feeds**. SAD §6 (BigQuery) and §7. It builds on **Account connections** through `IConnections`.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Where the data lives | Kairo's own Google Cloud project, one dataset per brand, `brand_<brand ID>`, in the environment's region | SAD §6: access is granted and removed per brand, and Kairo's service accounts write without any brand-side setup |
| Google Ads data | Kairo pulls daily reports with the Google Ads REST API, using the brand's existing connection | The BigQuery Data Transfer Service can't use the brand's OAuth consent. It needs a person's own sign-in with 2-Step Verification, or a Kairo service account that each brand adds to its Google Ads account by hand. Chosen by the product owner. |
| GA4 data | Kairo pulls daily totals with the GA4 Data API, using the brand's existing connection | It needs no BigQuery export, and it includes history from before an export existed. Measurement works by region and day, which the totals give. Chosen by the product owner. |
| Sales data | Both a CSV upload and the brand's own BigQuery table | Chosen by the product owner |
| How loads write | Rows go to a staging table, then one script deletes the days being loaded and inserts the new rows | Reruns are safe, and the last load for a day wins. That also settles a day loaded by both sales sources. |
| Scheduling | Cloud Scheduler calls the worker every night at 05:00 India time, signed as a new `kairo-scheduler` service account | Before the 07:00 refresh (PRD §9). Cloud Run's invoker check is the authorisation, so the worker needs no auth code of its own. |
| Owning module | `Kairo.Modules.Measurement`, in a new `Feeds` folder, with SQL schema `measurement` | Measurement is the only reader of this data so far (Prove, Impact Lab), and using its existing stub keeps the module list unchanged. The routes still read `/analytics`. |
| Google APIs | Plain REST through `HttpClient`, as the Brands module already does, with a fake handler in tests | One style for every Google call, and work never waits on Google |
| Comments | None in code | `CLAUDE.md` §3 |

## Notes on the documentation

1. **BigQuery Data Transfer Service.** SAD §2, §6 and §7 feed Google Ads data through the Data Transfer Service. This design pulls it with the Google Ads API instead (see Decisions). The trade-off: Kairo owns the table layout instead of using Google's standard transfer tables. This task updates those three sections.
2. **GA4 export.** SAD §6 and §7 feed GA4 data from GA4's BigQuery link. This design reads totals with the GA4 Data API instead. A brand that wants raw events can still link GA4 to its own BigQuery, but Kairo doesn't read that export. This task updates those sections, and the BRD wording "the GA4 export" in `docs/task.md` stays as written.
3. **The worker and brand tokens.** Today only the API holds the OAuth client ID and secret. Refreshing a brand's access token in the worker needs both, so the worker gets the same two secrets, in Cloud Run and in Aspire.
4. **The worker's timeout.** A night's run for every brand can take longer than Cloud Run's default 5 minutes. The worker is deployed with a 60-minute timeout, and Cloud Scheduler waits up to 30 minutes (its limit for HTTP targets). The run keeps going after Scheduler stops waiting.
5. **Alerting admins.** PRD §9 asks for an email and in-app alert to admins on a connection or data problem. **Notifications** is being built at the same time, so this task records failures in the run records and the log. The alert is a follow-up once both have merged.

## Datasets and tables

Each brand's dataset is `brand_` followed by the brand ID in lowercase hex without dashes (`Guid.ToString("N")`). It lives in `Kairo:Analytics:Project` and `Kairo:Analytics:Location`. The first run for a brand creates the dataset and any missing table, so nothing is set up by hand.

Every table is partitioned by day on `date`, and every row has `loaded_at TIMESTAMP`, the time the load ran. Money is `NUMERIC` in the account's own currency, named in `currency`. IDs are `STRING`.

| Table | Columns, after `date DATE` | Source |
| --- | --- | --- |
| `ads_campaign_daily` | `customer_id`, `campaign_id`, `campaign_name`, `channel`, `campaign_status`, `currency`, `impressions INT64`, `clicks INT64`, `cost NUMERIC`, `conversions FLOAT64`, `conversions_value NUMERIC` | Google Ads `campaign` |
| `ads_ad_group_daily` | `customer_id`, `campaign_id`, `ad_group_id`, `ad_group_name`, `currency`, and the same five metrics | Google Ads `ad_group` |
| `ads_ad_daily` | `customer_id`, `campaign_id`, `ad_group_id`, `ad_id`, `ad_type`, `currency`, and the same five metrics | Google Ads `ad_group_ad` |
| `ads_region_daily` | `customer_id`, `campaign_id`, `region_id`, `region_name`, `country_code`, `currency`, and the same five metrics | Google Ads `geographic_view` |
| `ga4_region_daily` | `property_id`, `country`, `region`, `currency`, `sessions INT64`, `key_events FLOAT64`, `transactions INT64`, `purchase_revenue NUMERIC` | GA4 Data API |
| `sales_daily` | `region`, `orders INT64`, `revenue NUMERIC`, `source` (`upload` or `table`) | CSV uploads and the brand's table |
| `ads_city_daily` | `customer_id`, `campaign_id`, `city_id`, `city_name`, `state_name` (from the geo target's canonical name), `country_code`, `currency`, and the five metrics | Google Ads `geographic_view` with `segments.geo_target_city` |
| `ga4_city_daily` | `property_id`, `country`, `region`, `city`, `currency`, and the GA4 metrics | GA4 Data API with the `city` dimension |

*Added for holdout design (section 6):* the two city tables were added after the first release. Each successful run records the tables it loaded in `FeedRuns.Tables`, and a table the account hasn't loaded yet gets the 13-month first-run window while the others load the last 7 days.

- `cost` is `metrics.cost_micros` divided by 1,000,000.
- `region_id` is the geo target constant's ID, and `region_name` its name. A row Google doesn't place in a state has both empty.
- `sales_daily` holds no currency. Sales are taken to be in the brand's own currency.

## Replacing days

One internal service, `BigQueryTables.ReplaceDaysAsync(brandId, table, dates, rows)`, does every write:

1. Make sure the dataset and the table exist. Datasets and tables found or created are remembered for the life of the process.
2. If there are rows, load them as newline-delimited JSON into `_staging_<table>_<run ID>` in the same dataset, with `WRITE_TRUNCATE`, through a multipart `jobs.insert` upload. Then wait for the job.
3. Run one script through `jobs.query`, with `@dates` as an `ARRAY<DATE>` parameter:

   ```sql
   BEGIN TRANSACTION;
   DELETE FROM `{project}.{dataset}.{table}` WHERE date IN UNNEST(@dates);
   INSERT INTO `{project}.{dataset}.{table}` ({columns}) SELECT {columns} FROM `{project}.{dataset}.{staging}`;
   COMMIT TRANSACTION;
   DROP TABLE `{project}.{dataset}.{staging}`;
   ```

   With no rows, the script is the `DELETE` alone, so a day that now has nothing is emptied too.

`dates` is every day in the window for a feed, and every distinct date in the file for an upload. A day absent from `dates` is never touched.

Kairo's own BigQuery calls use the service account's Application Default Credentials, with the `https://www.googleapis.com/auth/bigquery` scope, through an `IServiceAccessToken` interface that tests replace. Calls go to `https://bigquery.googleapis.com/bigquery/v2` and `https://bigquery.googleapis.com/upload/bigquery/v2`.

## The nightly run

### Trigger

- `POST /jobs/analytics/daily` on the worker, with an optional `?brandId={guid}` to run one brand.
- Cloud Scheduler job `kairo-analytics-daily` in the environment's region:
  - schedule `0 5 * * *`, time zone `Asia/Kolkata`
  - an OIDC token from `kairo-scheduler`, with the worker's URL as the audience
  - `--attempt-deadline 1800s` and `--max-retry-attempts 0`. A missed night is caught up by the next night's 7-day window.
- The run takes its cancellation from the application stopping, not from the request, so it finishes when Scheduler stops waiting.
- The response is `200` with `{ "brands": 3, "runs": [{ "brandId", "feed", "status", "rows" }] }`, and `404` for an unknown `brandId`.
- When `Kairo:Analytics:Project` isn't set, the endpoint returns a `503` problem and does nothing.

### Each brand

The worker lists every brand through a new `IBrandDirectory.ListAllAsync`, then handles each brand in its own DI scope with `KairoDatabase.UseBrandAsync`. One brand's failure is recorded and logged, and the run moves on.

For each brand, three feeds run one after the other:

| Feed | Runs when | Window |
| --- | --- | --- |
| `google-ads` | The Google Ads connection is Connected | In the account's time zone |
| `ga4` | The GA4 connection is Connected | In the property's time zone |
| `sales-table` | A sales source is set and the BigQuery connection is Connected | In `Asia/Kolkata` |

- **Window.** From 7 days ago through yesterday. The first successful run for a feed and an external ID (the Ads customer, the GA4 property or the source table) reaches back 13 months instead, so measurement has a before-period.
- **A missing connection.** The feed is recorded as `skipped`, with the reason `not_connected` or `needs_reconnect`. `ConnectionUnavailableException` from `GetAccessAsync` counts the same way.
- **One at a time.** A feed is skipped with the reason `already_running` if the same brand and feed has a `running` record that started in the last 2 hours.

### Google Ads

Calls go to `https://googleads.googleapis.com/{Kairo:Google:AdsApiVersion, default v25}/customers/{customerId}/googleAds:searchStream`, with the brand's access token and, when `ConnectionAccess.ManagerId` is set, the `login-customer-id` header.

1. `SELECT customer.id, customer.time_zone, customer.currency_code FROM customer` gives the time zone and currency.
2. Four reports, each `WHERE segments.date BETWEEN '{from}' AND '{to}'`:
   - `SELECT segments.date, campaign.id, campaign.name, campaign.advertising_channel_type, campaign.status, {metrics} FROM campaign`
   - `SELECT segments.date, campaign.id, ad_group.id, ad_group.name, {metrics} FROM ad_group`
   - `SELECT segments.date, campaign.id, ad_group.id, ad_group_ad.ad.id, ad_group_ad.ad.type, {metrics} FROM ad_group_ad`
   - `SELECT segments.date, campaign.id, segments.geo_target_state, {metrics} FROM geographic_view`, also filtered on `geographic_view.location_type = 'LOCATION_OF_PRESENCE'`, so that people's interests aren't counted on top of where they were
3. `{metrics}` is `metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value`.
4. Region names come from `SELECT geo_target_constant.id, geo_target_constant.name, geo_target_constant.country_code FROM geo_target_constant WHERE geo_target_constant.resource_name IN (...)`, in batches of 500.
5. Each report replaces its table's days.

Indian states are geo targets of type State, so `segments.geo_target_state` is the region segment. Before relying on it, the plan checks this with the Google Ads Query Validator and the demo test account.

### GA4

1. Analytics Admin `GET https://analyticsadmin.googleapis.com/v1beta/properties/{propertyId}` gives `timeZone` and `currencyCode`.
2. `POST https://analyticsdata.googleapis.com/v1beta/properties/{propertyId}:runReport` sends:
   - `dateRanges`: `[{ startDate, endDate }]`, as `YYYY-MM-DD`
   - `dimensions`: `date`, `country`, `region`
   - `metrics`: `sessions`, `keyEvents`, `transactions`, `purchaseRevenue`
   - `limit` of 100,000, with `offset` paging until `rowCount` is reached
3. `date` comes back as `YYYYMMDD`. A `(not set)` region is kept as it is.
4. The rows replace `ga4_region_daily`'s days.

### Sales from the brand's table

1. Read the saved source.
2. Run this query through `jobs.query` in the BigQuery connection's project, with the brand's access token, in the table's location:

   ```sql
   SELECT DATE(`{date}`) AS date, CAST(`{region}` AS STRING) AS region,
          CAST(SUM(`{orders}`) AS INT64) AS orders, CAST(SUM(`{revenue}`) AS NUMERIC) AS revenue
   FROM `{table}`
   WHERE DATE(`{date}`) BETWEEN @from AND @to
   GROUP BY 1, 2
   ```

   The brand's project pays for the query.
3. Read all result pages, through `getQueryResults` with `pageToken`.
4. The rows replace `sales_daily`'s days, with `source = 'table'`.

## Sales uploads

`POST /api/brands/{brandId}/analytics/sales/uploads`, for admins and marketers, takes a multipart form with one `file` field of at most 10 MB.

- **The format.** A UTF-8 CSV with a header row holding exactly `date`, `region`, `orders` and `revenue`, in any order and any letter case.
  - `date` is `YYYY-MM-DD` and not after today in `Asia/Kolkata`.
  - `region` isn't empty and is at most 100 characters, trimmed.
  - `orders` is a whole number, 0 or more.
  - `revenue` is a number, 0 or more, with a dot for decimals and up to 2 decimal places.
  - The same date and region appears only once.
- **Checking.** The whole file is checked before anything is loaded. On any error the response is a `400` validation problem, with `errors` listing up to 20 problems as `"row 14: orders must be a whole number"`. Row 1 is the header.
- **Loading.** A valid file replaces each date it contains in `sales_daily`, with `source = 'upload'`, then saves an upload record.
- **Response.** `201` with `{ id, fileName, rows, firstDate, lastDate, days, uploadedBy, uploadedAt }`. When `Kairo:Analytics:Project` isn't set, the response is a `503` problem.

`GET /api/brands/{brandId}/analytics/sales/uploads` lists the latest 50 uploads, newest first, to every member.

## The brand's sales table

| Method and path | Who | What it does |
| --- | --- | --- |
| `GET /api/brands/{brandId}/analytics/sales/source` | Member | The saved source, or `204` when none is set |
| `PUT /api/brands/{brandId}/analytics/sales/source` | Admin | Checks and saves a source |
| `DELETE /api/brands/{brandId}/analytics/sales/source` | Admin | Stops copying. Rows already loaded stay. |

The `PUT` body:

```json
{
  "table": "acme-data.shop.daily_orders",
  "columns": { "date": "order_date", "region": "state", "orders": "order_count", "revenue": "net_revenue" }
}
```

The `PUT` checks:

1. **The connection.** The BigQuery connection must be Connected, or the response is a `409` problem.
2. **The table name.** `table` must be `project.dataset.table`. The project and dataset are made of letters, digits, `_` and `-`, and the table of letters, digits and `_`. Otherwise it's a `400`.
3. **The table.** `GET …/projects/{p}/datasets/{d}/tables/{t}`, with the brand's access token, must succeed. A `403` or `404` from Google is a `400` saying "Kairo can't read this table with the connected Google account."
4. **The columns.** Each mapped column must exist with a matching type:
   - date: `DATE`, `DATETIME` or `TIMESTAMP`
   - region: `STRING`
   - orders: `INTEGER`, `INT64`, `NUMERIC` or `BIGNUMERIC`
   - revenue: `NUMERIC`, `BIGNUMERIC`, `FLOAT`, `FLOAT64`, `INTEGER` or `INT64`

   Column names must match `[A-Za-z_][A-Za-z0-9_]*`, so they can be quoted safely in the query.

It saves the table, the mapping and the table's `location`, and returns `200` with the saved source. The next nightly run copies it.

## Run records and status

| Schema | Table | Belongs to a workspace | Holds |
| --- | --- | --- | --- |
| `measurement` | `FeedRuns` | Yes | `Id`, brand, `Feed` (`google-ads`, `ga4`, `sales-table`), `ExternalId`, `Status` (`running`, `succeeded`, `failed`, `skipped`), `Reason`, `FromDate`, `ToDate`, `Rows`, `Error` (up to 1,000 characters), `StartedAt`, `FinishedAt` |
| `measurement` | `SalesUploads` | Yes | `Id`, brand, `FileName`, `Rows`, `FirstDate`, `LastDate`, `Days`, `UploadedBy`, `UploadedAt` |
| `measurement` | `SalesSources` | Yes | Brand (the key), `TableRef`, `DateColumn`, `RegionColumn`, `OrdersColumn`, `RevenueColumn`, `Location`, `SetBy`, `SetAt` |

- All three tables are under the `measurement` schema's row-level security policy.
- `FeedRuns` has an index on brand, feed and started time. Records older than 90 days are deleted at the end of each nightly run.

`GET /api/brands/{brandId}/analytics/feeds`, for every member, returns:

```json
{
  "dataset": "brand_3f2a…",
  "feeds": [
    {
      "feed": "google-ads",
      "state": "ok",
      "lastRunAt": "2026-10-13T23:30:04Z",
      "lastSuccessAt": "2026-10-13T23:30:04Z",
      "coveredThrough": "2026-10-13",
      "lastError": null
    }
  ]
}
```

`state` is decided per feed from the connection's current status and the feed's latest record. The first rule that matches wins:

| `state` | When |
| --- | --- |
| `not_set_up` | The feed's connection is Not connected, or `sales-table` has no source |
| `needs_connection` | The connection is Needs reconnect or Choosing account |
| `waiting` | There's no record yet, or the latest one was skipped |
| `running` | The latest record is `running` and started in the last 2 hours |
| `failing` | The latest record failed, or is `running` but started more than 2 hours ago |
| `ok` | The latest record succeeded |

`coveredThrough` is the `ToDate` of the latest success, and `lastError` is the latest failure's `Error` when the state is `failing`.

## Audit

The following actions are recorded:

- `analytics.sales_uploaded`, with the upload record
- `analytics.sales_source_set`, before and after
- `analytics.sales_source_removed`, before

Nightly runs aren't audited. They are system work recorded in `FeedRuns`.

## Code layout

| Project | Adds |
| --- | --- |
| `Kairo.Contracts` | `IBrandDirectory.ListAllAsync`, returning every brand's `BrandSummary` |
| `Kairo.Modules.Brands` | `ListAllAsync`, read in a system scope |
| `Kairo.Modules.Measurement` | `MeasurementDbContext` and its migration, and a `Feeds` folder with these parts: |

The `Feeds` folder holds:

- `AnalyticsOptions`
- `IServiceAccessToken`, with an Application Default Credentials version
- `BigQueryTables`: datasets, tables, loads and the replace script
- `TableSchemas`
- `GoogleAdsReports`
- `Ga4Reports`
- `SalesTableReports`
- `SalesCsv`: parsing and validation
- `FeedRunner`: brands, feeds, windows and run records
- `FeedEndpoints`, `SalesEndpoints` and `FeedJobs`

`MeasurementModule.MapJobs` maps `/analytics/daily`, and `MapApi` maps the API routes. Measurement has its own named `HttpClient`, `kairo-measurement-google`, and depends on `Google.Apis.Auth` for Application Default Credentials.

## Configuration

| Setting | Meaning | Where it's set |
| --- | --- | --- |
| `Kairo:Analytics:Project` | The project that holds brand datasets. When empty, feeds and uploads are off. | `Release.ps1`, from the environment's project. In Aspire, from the `Parameters:analytics-project` setting when present. |
| `Kairo:Analytics:Location` | The dataset location, `asia-south1` by default | `Release.ps1`, from the environment's region |
| `Authentication:Google:ClientId`, `…:ClientSecret` | Now needed by the worker too | `Release.ps1` and the AppHost |

## Deployment

**`setup.ps1`:**

- Enables `analyticsdata.googleapis.com` and `cloudscheduler.googleapis.com`.
- Adds `kairo-scheduler` to the service accounts.
- Grants `roles/bigquery.dataOwner` and `roles/bigquery.jobUser` on the project to `kairo-api` and `kairo-worker`.
- Grants the Google secrets' `secretAccessor` to `kairo-worker` as well as `kairo-api`.

**`Release.ps1`:**

- Deploys the worker with:
  - `--timeout 3600`
  - the Google secrets, when they're set
  - `Kairo__Analytics__Project` and `Kairo__Analytics__Location`
- Passes the same two analytics settings to the API.
- Grants `kairo-scheduler` the invoker role on `kairo-worker`.
- Creates `kairo-analytics-daily`, or updates it when `gcloud scheduler jobs describe` finds it, with:

  ```
  gcloud scheduler jobs create http kairo-analytics-daily --location {region} --schedule "0 5 * * *" --time-zone Asia/Kolkata
    --uri {workerUrl}/jobs/analytics/daily --http-method POST
    --oidc-service-account-email {kairo-scheduler} --oidc-token-audience {workerUrl}
    --attempt-deadline 1800s --max-retry-attempts 0 --project {project}
  ```

  The update uses `gcloud scheduler jobs update http` with the same flags.

## Testing

**Integration tests** run against SQL Server in Testcontainers. The fake Google handler `FakeGoogleApis` is extended to cover:

- Google Ads `searchStream`, with the `login-customer-id` check it already has
- the GA4 Admin `properties.get` and Data API `runReport`
- BigQuery datasets, tables, multipart loads, `jobs.query` (including the replace script) and `getQueryResults`

The fake BigQuery keeps rows in memory, so tests can read back what a table holds.

Worker tests use a new `KairoWorkerFactory`, which references the worker project through the extern alias `worker`, so they can sit next to the API's `Program`.

- **The nightly run:**
  - creates the dataset and tables once
  - loads the four Ads tables, `ga4_region_daily` and `sales_daily` from the brand's table
  - sends the manager header for a client reached through a manager
- **Windows:** 13 months on the first run, 7 days after that, in the account's and the property's time zones.
- **Replacing:**
  - a rerun gives the same rows
  - a day with no rows is emptied
  - days outside the window are untouched
  - an upload and a table load for the same day leave the later one
- **Skipping:**
  - a missing connection, Needs reconnect and an already running feed are recorded as skipped
  - a `?brandId=` run touches one brand
  - one brand failing still lets the others finish
- **Uploads:**
  - roles: admins and marketers can upload, approvers can't
  - the 10 MB limit
  - every validation rule, up to 20 errors
  - only the dates in the file are replaced
  - the record and the audit entry
- **The sales source:**
  - roles
  - a `409` without the BigQuery connection
  - a bad table name, an unreadable table, a missing column and a wrong type
  - a source saved with its location
- **Status:** each `state` value, and `coveredThrough`.
- **Separation:** brand separation covers the three new tables, and one brand can't read another's uploads, source or runs.
- **No project set:** feeds and uploads return `503`.

**Unit tests** cover `SalesCsv`, mapping report rows to table rows (micros, dates, empty regions), and the generated GAQL and SQL.

**Deployment tests** (Pester) cover:

- the new APIs, service account and role bindings
- the worker's secrets, timeout and settings
- the invoker grant
- creating the Scheduler job, and updating it when it already exists

The OpenAPI document test picks up the new endpoints.

## Out of scope

- The web screens: the upload, the source picker and feed status. Nency builds them on these endpoints.
- Alerting admins about failing feeds, which comes after **Notifications** merges (note 5).
- The "Kairo history" and "Measurement" datasets from SAD §6. Their own tasks create them.
- Reading the brand's raw GA4 export.
- Same-day pacing, which reads the Google Ads API directly (SAD §7).
- Giving brands direct access to their dataset.

## Done criteria

1. `dotnet build core/Kairo.slnx` and `dotnet test --solution core/Kairo.slnx` pass with Docker running, with 0 warnings.
2. `npm --prefix web run generate:api` regenerates the types with the new endpoints, and the web checks still pass.
3. The deployment tests pass with the new setup and release steps.
4. With Aspire running, `Parameters:analytics-project` set to `kairo2` and the demo Google Ads test account connected, a manual run works:
   - `POST {worker}/jobs/analytics/daily` fills `brand_<id>` in `kairo2`
   - a CSV upload replaces its days
   - the feeds status shows `ok`
5. These are updated:
   - SAD §2: the Analytics row, and the Jobs and events row naming the Scheduler job
   - SAD §3: the Measurement row, which gains the data feeds
   - SAD §6: the BigQuery table's "Fed by" column
   - SAD §7: the BigQuery Data Transfer Service row, removed, and the Google Ads API and Google Analytics 4 rows
   - SAD §9: the core worker's 60-minute timeout
   - the README: how to run the feeds locally
