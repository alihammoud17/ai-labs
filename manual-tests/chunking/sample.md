A line before any heading.

# Billing Guide

## Overview

Invoices are generated at the end of each billing period.

## Webhooks

### Retries

Failed deliveries are retried up to five times with exponential backoff. Each retry uses the same event id, so receivers can drop duplicates. After the fifth failure the delivery is marked dead and an operator can replay it from the admin endpoint. Replays keep the original event id and add a replay header. Dead deliveries are kept for thirty days and then deleted by a nightly job.

#### Headers

```bash
# Sign the body
openssl dgst -sha256 -hmac "$SECRET" body.json
```

## Empty section

## Limits

#### Skipped level

Tenants on the free plan get 100 calls per day.
