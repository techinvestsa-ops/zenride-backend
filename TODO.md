# Izigo API — Outstanding Work

> Last updated: 2026-08-25  
> Current state: 390 endpoints implemented, 190/190 tests passing.  
> Critical launch blockers (section 1) completed. Remaining work is high/medium priority hardening.  
> This document tracks everything still needed before the platform is production-ready.

---

## 1. Critical — Launch Blockers ✅ ALL COMPLETE

### 1.1 Dispatch Engine ✅
**File:** `Infrastructure/Jobs/Processors/DispatchJobProcessor.cs`

- [x] `DispatchJobProcessor` queries nearby online drivers, sends `job.offered` via Pusher + FCM
- [x] Offer timeout: `ExpireOfferAsync` scheduled after `DispatchDefaults:OfferTimeoutSeconds`
- [x] Driver acceptance: `POST /driver/jobs/{id}/accept` transitions `Offered → Accepted`
- [x] Sequential dispatch with exclusion list from `TripStateHistory`
- [x] `no_drivers_found` → `JobState.Expired` + `ride.no_drivers_found` event to rider
- [x] `DeclineJobHandler` resets trip to `Broadcasting` and re-enqueues dispatch

### 1.2 Payment Gateway (CinetPay + Wave) ✅
**File:** `Infrastructure/Services/PaymentGatewayService.cs`

- [x] CinetPay API for `orange_money`, `moov_money`, `mtn_momo`, `card`
- [x] Wave API for `wave` payment method
- [x] Webhook signature: CinetPay (`cpm_site_id` check), Wave (HMAC-SHA256 `X-Wave-Signature`)
- [x] `HandleWebhookCommand` parses both gateway payloads with idempotent deduplication
- [x] Wallet credit on topup webhooks; `PaymentGateway:*` config keys added to appsettings
- [x] Credential-absent fallback — stub responses + `[Payment:DEV]` log (same pattern as SMS/Email)

### 1.3 Google Maps / Geo Proxy ✅
**File:** `Infrastructure/Services/GoogleMapsService.cs`

- [x] `AutocompleteAsync` → Places Autocomplete API
- [x] `GetPlaceDetailAsync` → Place Details API
- [x] `ReverseGeocodeAsync` → Geocoding API (latlng)
- [x] `GeocodeAsync` → Geocoding API (address)
- [x] `GetRouteAsync` → Routes API v2 (`computeRoutes`) with traffic-aware duration
- [x] `GetEtaAsync` → Distance Matrix API
- [x] `GoogleMaps:ApiKey` added to appsettings; Haversine fallback when key is absent

### 1.4 Driver Job Lifecycle ✅
**Files:** `Driver/Commands/DriverJobCommands.cs`, `DriverController.cs`

- [x] All job transitions: accept, en-route, arrived-pickup, start (OTP), arrived-dropoff, complete, cancel, decline
- [x] `POST /driver/jobs/{id}/en-route` — new endpoint: `Accepted→EnRouteToPickup`, `PickedUp→EnRouteToDropoff`
- [x] FCM push to rider at every state transition (accept, arrive, start, complete, cancel)
- [x] Rider wallet debit at completion when `PaymentMethod == Wallet`
- [x] Re-dispatch on decline: `Broadcasting` reset + new `DispatchJobProcessor` job

---

## 2. High Priority — Core Features Not Wired

### 2.1 Driver Payout Disbursement
**File:** `Driver/Commands/DriverEarningsCommands.cs:65`  
Withdrawal request is recorded but never sent to a payment gateway.

- [ ] Connect `WithdrawCommand` to payment gateway payout API
- [ ] Update payout status from `Processing → Succeeded/Failed` via webhook
- [ ] Handle `NetOffCash` settlement flow for cash drivers

### 2.2 Masked Calls (Privacy Proxy)
**File:** `Chat/Commands/ChatCommands.cs:158`  
`POST /chat/call` endpoint exists but doesn't create a real proxy session.

- [ ] Integrate Twilio Proxy or Africa's Talking number masking API
- [ ] Store proxy session, expire after trip completion
- [ ] Wire call events back to the trip audit trail

### 2.3 Trip Market Assignment
**File:** `Rides/Commands/RidesCommands.cs:92`  
Trip market is hardcoded to `"ci"` (Ivory Coast). Nigerian trips will be misclassified.

- [ ] Read market from the authenticated user's registered market or the `X-Market` header
- [ ] Apply correct currency (`XOF` for `ci`, `NGN` for `ng`) at quote and trip creation time

### 2.4 SOS Incident Market
**File:** `Safety/Commands/SafetyCommands.cs:30`  
Market is hardcoded to `"ci"` when raising an SOS.

- [ ] Derive market from the authenticated user's profile or device locale

### 2.5 Social / OAuth Login
**File:** `Auth/Commands/SocialLoginCommand.cs`  
Google and Apple sign-in return stub accounts without verifying the `id_token`.

- [ ] Verify Google `id_token` against Google's public keys
- [ ] Verify Apple `id_token` against Apple's public keys
- [ ] Map verified email/sub to existing user or create account

### 2.6 Bank Account Verification
**File:** `Driver/Queries/OnboardingQueries.cs:118`  
Bank account resolve returns a stub response, allowing drivers to submit unverified accounts.

- [ ] Integrate Paystack `/bank/resolve` (NG) or CinetPay equivalent (CI)
- [ ] Block payout method creation for accounts that fail verification

---

## 3. Medium Priority — Production Hardening

### 3.1 File Storage — Cloud Blob
**File:** `Infrastructure/Services/LocalFileStorageService.cs`  
Files are stored on local disk. This breaks on any multi-instance or serverless deploy.

- [ ] Implement `S3FileStorageService` or `R2FileStorageService` (Cloudflare R2 recommended — no egress fees)
- [ ] Generate short-lived signed URLs for KYC documents (currently returns plain path)
- [ ] Add `Storage:Provider`, `Storage:Bucket`, `Storage:AccessKey`, `Storage:SecretKey` to appsettings
- [ ] Register correct implementation based on `Storage:Provider` config

### 3.2 CSAT on Support Tickets
Not modelled — the spec includes a satisfaction score flow after ticket resolution.

- [ ] Add `CsatScore` (1–5) and `CsatSubmittedAt` to `SupportTicket` entity
- [ ] Add `POST /support/tickets/{id}/csat` endpoint
- [ ] Wire CSAT to admin ticket detail view

### 3.3 Geo-Index for Driver Demand
**File:** `Geo/Queries/GeoQueries.cs`  
`GET /driver/demand` uses simple bounding-box proximity with no spatial index.

- [ ] Add a PostGIS extension or use EF spatial types (`NetTopologySuite`) for proper geo queries
- [ ] Or maintain a Redis GEOADD-based index updated on each driver heartbeat

### 3.4 Fare Calculator Default Rules — DB Seeding
**File:** `Quotes/Helpers/FareCalculator.cs`  
`DefaultRule()` fallback values are used when no `FareRule` exists in the DB. Production needs real seeded rules.

- [ ] Seed `FareRules` for CI market (ZenCar, ZenBike, ZenCoRide, PackageSmall, PackageLarge) in `DbSeeder`
- [ ] Seed `FareRules` for NG market with NGN equivalents
- [ ] Seed initial `DispatchConfig` for both markets (offer timeout, radius, strategy)
- [ ] Seed initial `FeatureFlags` for both markets (co_ride_enabled, package_enabled, etc.)

### 3.5 Cancellation Fee — From Fare Rule
**File:** `Rides/Commands/RidesCommands.cs`, `Rides/Helpers/RideDetailMapper.cs`  
Cancellation fee is hardcoded as 500 XOF in the cancel handler and the ride detail mapper.

- [ ] Read cancellation fee from the trip's associated `FareRule.CancellationFee`
- [ ] Pass correct fee through `RideDetailMapper` (already accepts it as a parameter via `QuoteSettings:DefaultCancellationFee`)

### 3.6 Promotions — Driver Incentive Progress
**File:** `Promotions/Queries/PromoQueries.cs:53`  
Driver incentive campaigns return null progress; the `ConditionJson` is never evaluated.

- [ ] Implement condition evaluation: parse `ConditionJson` and compute trips completed / earnings vs target
- [ ] Return real `Progress` and `Target` values in `GET /driver/promotions`

### 3.7 Realtime Notifications — Missing Wires
Several places have `// TODO: notify driver via realtime + push`:

- [ ] `CancelRideHandler` — notify driver when rider cancels
- [ ] `ChangeDestinationHandler` — notify driver of new dropoff
- [ ] `AddNoteHandler` — notify driver of rider note
- [ ] `WalletTransferHandler` — already wired ✅
- [ ] `SosRaisedHandler` — push `sos.raised` to admin channel (already wired in admin realtime)

### 3.8 Wallet Withdrawal — Pending Payout Linking
**File:** `Wallet/Commands/WalletCommands.cs:171`  
Withdrawal creates a `Processing` transaction but no `Payout` record is created for the admin to approve.

- [ ] Create a `Payout` record when `WithdrawCommand` succeeds
- [ ] Link payout to wallet transaction via `ReferenceId`
- [ ] Surface in `GET /admin/payouts`

---

## 4. Infrastructure & DevOps

- [ ] **Docker** — `Dockerfile` + `docker-compose.yml` with SQL Server, Hangfire, API
- [ ] **CI/CD** — GitHub Actions pipeline: build → test → publish image
- [ ] **Environment config** — separate `appsettings.Production.json` with secrets pulled from Azure Key Vault or Doppler
- [ ] **Serilog sink** — add seq/cloud sink for structured logs; currently only console output
- [ ] **Health checks** — `app.MapHealthChecks("/health")` wired to DB + Pusher + Hangfire
- [ ] **Rate limiting** — review OTP (`5/hour`) and global (`120/min`) limits for production traffic
- [ ] **CORS** — replace `Cors:Origins: ["http://localhost:3000"]` with production admin console URL
- [ ] **JWT Secret** — rotate `Jwt:Secret` away from the placeholder in appsettings

---

## 5. Security

- [ ] **Admin console session cookie** — refresh token currently stored in `localStorage` in the Next.js console; move to `httpOnly; Secure; SameSite=Lax` cookie before public exposure
- [ ] **IP allowlist for admin surface** — `/api/v1/admin/*` should be unreachable from public internet; add middleware or WAF rule
- [ ] **KYC document URLs** — `LocalFileStorageService.GetSignedUrl` returns an unsigned plain URL; requires cloud storage with real signed URLs (see 3.1)
- [ ] **Payment credentials rotation** — CinetPay site_id + apikey and Wave bearer token are currently not set; ensure they are provisioned as secrets, never committed
- [ ] **Google Maps key restriction** — restrict the API key to server IP and required APIs only in Google Cloud Console
- [ ] **2FA enforcement** — consider making 2FA mandatory for `finance` and `super_admin` roles (currently optional)
- [ ] **Audit log immutability** — the append-only guarantee is enforced at the application level; consider a DB trigger or separate append-only table for compliance

---

## 6. Test Coverage Gaps

- [ ] **Packages module** — `CreatePackage`, `CancelPackage`, `RatePackage`, `UploadProof`, `ReportFailedDelivery`
- [ ] **Chat module** — `SendMessage`, `MarkRead`, `RequestCall`
- [ ] **Notifications** — `MarkRead`, `MarkAllRead`, broadcast delivery
- [ ] **Geo queries** — nearby drivers, autocomplete, route calculation (requires mocking `IGeoService`)
- [ ] **Wallet queries** — balance, transaction history, payment method CRUD
- [ ] **Profile commands** — `UpdateProfile`, `UploadPhoto`, `AddEmergencyContact`, `CloseAccount`
- [ ] **Driver onboarding** — KYC step progression, document upload, submit for review
- [ ] **Ride queries** — `GetActiveRide`, `GetRideHistory`, `GetReceipt`
- [ ] **Admin moderation** — suspend/block rider, block driver, force reset flows
- [ ] **Admin pricing** — fare rule versioning, surge override approval flow
- [ ] **Integration: app-side authenticated flows** — rider login → quote → ride → cancel end-to-end
- [ ] **Load / performance tests** — no baseline established for dispatch, geo, or wallet operations

---

## 7. Minor / Polish

- [ ] `GET /driver/demand` ETA speed is hardcoded in two query files — consolidate to single `OpsSettings:EtaSpeedKmh` read (already in appsettings, just applied in two places)
- [ ] `CoRide/Commands:115` — promo code lookup placeholder when `BookSeats` is called
- [ ] `Profile/Commands/ExportDataCommand` — GDPR data export is a stub
- [ ] Scheduled rides — spec references them but `ScheduledAt` field on `Trip` is not fully exposed through the dispatch or cancellation flows
- [ ] Driver co-ride earnings — co-ride trip completion should credit per-seat earnings, not full-trip fare
- [ ] `GET /driver/stats` performance — runs several aggregate queries without caching; add a daily materialized view or Redis cache for high-traffic markets
- [ ] Biometric login (`POST /auth/biometric/challenge` + `/verify`) — implemented but the `BiometricKey` storage has no expiry cleanup job
- [ ] Referral code generation — currently set during `VerifyOtp` new-user creation but not enforced unique on the DB level (application-level uniqueness check only)

---

## Priority Order (Recommended)

1. ~~**Dispatch engine**~~ ✅ Done
2. ~~**Payment gateway**~~ ✅ Done
3. ~~**Google Maps**~~ ✅ Done
4. ~~**Driver job lifecycle**~~ ✅ Done
5. **File storage** (cloud) — required for KYC documents in any deployed environment
6. **DB seeding** (fare rules + dispatch config) — required for correct quotes
7. **Market assignment** — required for NG launch
8. **Docker + CI** — required for reliable deployment
9. Everything else above
