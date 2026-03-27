# StudentApp — Platforma Studencka z AI

Aplikacja webowa zbudowana jako monorepo mikroserwisowe. Przekształcona z bota Discord (LaskBot) w pełnoprawną platformę webową z frontendem w React i backendem w ASP.NET Core.

---

## Architektura

```
┌──────────────┐     ┌──────────────────────┐     ┌──────────────────┐
│   Frontend   │────▶│   Backend API        │────▶│   PostgreSQL     │
│  React/Vite  │     │   ASP.NET Core 9     │     │                  │
│  port: 3000  │     │   port: 8080         │     │   port: 5432     │
└──────────────┘     │   JWT + SignalR       │     └──────────────────┘
                     │   Rate Limiting       │
                     └───────┬───────┬───────┘
                             │       │
                   ┌─────────▼──┐  ┌─▼──────────────┐
                   │  OpenClaw  │  │  UniScraper     │
                   │  FastAPI   │  │  FastAPI         │
                   │  port:8000 │  │  port: 8001     │
                   │  Claude AI │  │  Scraping PK    │
                   └────────────┘  └─────────────────┘
```

## Struktura monorepo

```
student-app/
├── docker-compose.yml          # Orkiestracja wszystkich kontenerów
├── .env.example                # Szablon zmiennych środowiskowych
├── .gitignore
├── README.md
│
├── backend/                    # ASP.NET Core 9 Web API
│   ├── Dockerfile
│   └── StudentApp.Api/
│       ├── Controllers/        # AuthController, ChatController, AdminController, ConfigurationController
│       ├── Data/               # ApplicationDbContext, DbInitializer, Entities/
│       ├── Hubs/               # ChatHub (SignalR streaming)
│       ├── Infrastructure/     # ServiceCollectionExtensions (DI, Polly, HttpClientFactory)
│       ├── Models/             # DTOs, Requests, Responses
│       ├── Security/           # AES-GCM EncryptionService
│       ├── Services/           # AuthService, ChatService, GeminiService, OpenClawService, etc.
│       └── Program.cs          # Entry point (JWT, SignalR, Rate Limiting, CORS)
│
├── frontend/                   # React + Vite
│   ├── Dockerfile              # Build → Nginx
│   ├── package.json
│   ├── vite.config.js
│   └── src/
│       ├── App.jsx             # Main layout (messenger-style)
│       ├── components/         # Auth, Chat, Layout, Settings, Admin
│       ├── contexts/           # AuthContext (JWT + refresh)
│       ├── hooks/              # useChat (SignalR)
│       ├── services/           # API client
│       └── styles/             # global.css (gray-beige theme)
│
├── openclaw/                   # Agent AI (Claude/Anthropic)
│   ├── Dockerfile              # Python 3.12 + .NET 9 + Node 20
│   ├── requirements.txt
│   └── src/
│       ├── app.py              # FastAPI endpoints (/tasks, /api/chat)
│       ├── core/               # engine.py, anthropic_client.py, chat_handler.py, prompts.py
│       └── utils/              # file_manager.py, shell_executor.py
│
└── uniscraper/                 # Scraper uczelniany
    ├── Dockerfile
    ├── requirements.txt
    └── src/
        ├── app.py              # FastAPI endpoints (/api/scrape, /api/links)
        └── scrapers/           # base.py (interfejs), pk_scraper.py (Politechnika Krakowska)
```

---

## Szybki start

### 1. Przygotuj plik `.env`

```bash
cp .env.example .env
```

Wygeneruj wymagane sekrety:

```bash
# JWT Secret (64 bajty, base64)
openssl rand -base64 64

# AES-GCM Encryption Key (32 bajty = 64 hex)
openssl rand -hex 32

# Hasło PostgreSQL
openssl rand -base64 24
```

Wklej wartości do `.env`.

### 2. Uruchom kontenery

```bash
docker compose up -d --build
```

### 3. Domyślne konto admina

Po pierwszym uruchomieniu system tworzy konto Super Admina:
- **Login:** `admin`
- **Hasło:** wartość `ADMIN_DEFAULT_PASSWORD` z `.env` (domyślnie `Admin123!`)

**Zmień hasło natychmiast po pierwszym logowaniu!**

### 4. Otwórz aplikację

- Frontend: `http://localhost:3000`
- Backend API: `http://localhost:8080`
- Health check: `http://localhost:8080/health`

---

## Konfiguracja użytkownika

Po zalogowaniu, w panelu **Ustawienia** (ikona zębatki):

1. **Klucze API** — wklej swoje klucze Gemini i/lub Anthropic. Klucze są szyfrowane AES-GCM i przechowywane w bazie; deszyfrowane wyłącznie w RAM backendu w momencie użycia.

2. **Dane uczelni** — ustaw uczelnię, wydział, kierunek, rok, grupę dziekańską. Te parametry są przekazywane do UniScrapera, który filtruje wyniki pod Twoje potrzeby.

3. **Modele AI** — wybierz preferowany model Gemini i Anthropic.

---

## Bezpieczeństwo

| Mechanizm | Opis |
|---|---|
| JWT + Refresh Tokens | Access token: 15 min. Refresh token: HTTP-Only cookie, 7 dni. |
| AES-GCM | Klucze API szyfrowane w bazie, deszyfrowane in-memory |
| Rate Limiting | 10 req/min na użytkownika (chat), 20 req/min (auth) |
| Soft Delete | Usunięci użytkownicy zachowują dane historyczne |
| RBAC | Admin: zarządzanie użytkownikami. User: dostęp do czatów i ustawień |
| GUID routing | Publiczne identyfikatory zamiast sekwencyjnych ID |
| Brak rejestracji | Konta tworzy wyłącznie Admin |

---

## Zmienne środowiskowe (.env)

| Zmienna | Wymagana | Opis |
|---|---|---|
| `POSTGRES_PASSWORD` | ✅ | Hasło do bazy danych |
| `JWT_SECRET_KEY` | ✅ | Klucz podpisu JWT (base64, min 64 bajty) |
| `ENCRYPTION_KEY` | ✅ | Klucz AES-GCM (hex, 64 znaki = 32 bajty) |
| `ADMIN_DEFAULT_PASSWORD` | ❌ | Hasło admina (domyślnie: Admin123!) |
| `POSTGRES_DB` | ❌ | Nazwa bazy (domyślnie: studentapp) |
| `POSTGRES_USER` | ❌ | Użytkownik bazy (domyślnie: studentapp) |
| `BACKEND_PORT` | ❌ | Port backendu (domyślnie: 8080) |
| `FRONTEND_PORT` | ❌ | Port frontendu (domyślnie: 3000) |

---

## Migracje EF Core

Aby wygenerować migrację (rozwój lokalny):

```bash
cd backend/StudentApp.Api
dotnet ef migrations add InitialCreate
dotnet ef database update
```

W trybie produkcyjnym migracje są stosowane automatycznie przy starcie kontenera (`DbInitializer.InitializeAsync`).

---

## Rozwój

### Dodawanie nowej uczelni do UniScraper

1. Utwórz plik `uniscraper/src/scrapers/nowa_uczelnia_scraper.py`
2. Zaimplementuj klasę dziedziczącą po `BaseScraper`
3. Zarejestruj w `app.py`: `registry.register("Nowa Uczelnia", NowaUczelniaScraper())`

### Dodawanie nowego serwisu AI

1. Utwórz interfejs i implementację w `backend/StudentApp.Api/Services/`
2. Dodaj kategorię w `ChatCategory` enum
3. Obsłuż w `ChatHub.SendMessage()`
4. Dodaj przycisk kategorii w frontendzie

---

## Pochodzenie kodu

Projekt jest refaktoryzacją Discord Bota **LaskBot**:
- `GeminiService` — zaadaptowany z oryginalnego, z dodaniem sliding window i IAsyncEnumerable
- `OpenClawService` — przekształcony z `OpenClawAgentClient`, teraz klient HTTP do FastAPI
- `PolitechnikaKrakowskaScraper` — przepisany z `PolitechnikaService.cs` na Python
- Polly retry policies — zachowane z oryginału z pełnym wsparciem Retry-After
- Wzorce DI — ServiceCollectionExtensions zaadaptowane, Riot/Discord usunięte
