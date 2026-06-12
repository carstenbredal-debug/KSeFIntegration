# KSeF Integration

Azure Functions middleware for integrating **Business Central** with **KSeF** (Krajowy System e-Faktur — Poland's National e-Invoice System).

## Architecture

```
Business Central → Azure Functions (this project) → KSeF API
                                                   ↓
                                              KSeF Demo/Prod
```

BC sends invoice data (JSON) to this middleware, which:
1. Converts the invoice to FA(2) XML format (schema v1-0E)
2. Authenticates with KSeF using a token
3. Submits the invoice to KSeF
4. Returns the KSeF reference number back to BC

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/invoice/submit` | Submit an invoice to KSeF |
| GET | `/api/invoice/status/{ref}?nip={nip}` | Check invoice processing status |
| POST | `/api/invoice/preview` | Generate XML preview (without sending) |
| GET | `/api/health` | Health check |

## Configuration

| Setting | Description |
|---------|-------------|
| `KSeF:BaseUrl` | KSeF API base URL (`https://ksef-demo.mf.gov.pl/api` for demo) |
| `KSeF:Token` | Authentication token for KSeF |

## Development

```bash
dotnet restore
dotnet build
cd src/KSeF.Functions
func start
```

## KSeF Environments

| Environment | URL |
|-------------|-----|
| Demo | `https://ksef-demo.mf.gov.pl/api` |
| Test | `https://ksef-test.mf.gov.pl/api` |
| Production | `https://ksef.mf.gov.pl/api` |

## Invoice XML Schema

Uses FA(2) schema v1-0E:
- Namespace: `http://crd.gov.pl/wzor/2023/06/29/12648/`
- Required sections: Naglowek (Header), Podmiot1 (Seller), Podmiot2 (Buyer), Fa (Invoice body with lines)
