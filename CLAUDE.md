## Project Overview
StudentApp is a microservices web application functioning as a student assistant. It features a messenger-style UI where chats represent functional agents (Gemini, OpenClaw Anthropic, University Scraper). 

## Repository Structure and Tech Stack
Monorepo with 5 core Docker containers.

### 1. /backend (API Gateway and Core Logic)
* Stack: C# 13, ASP.NET Core 9 Web API, Entity Framework Core, PostgreSQL, SignalR.
* Responsibilities: JWT Auth, RBAC (Admin/User), AES-GCM encryption of API keys, rate limiting, direct Google Gemini API integration.
* Key Files: Services/GeminiService.cs, Security/EncryptionService.cs, Hubs/ChatHub.cs.

### 2. /frontend (Web UI)
* Stack: React 19, Vite, JavaScript/JSX, CSS.
* Responsibilities: Messenger-style interface. Sidebar for categories, main window with real-time SSE/SignalR streaming, Admin Panel, Settings Panel.
* Key Files: src/hooks/useChat.js, src/components/Chat/ChatWindow.jsx.

### 3. /openclaw (Agent Microservice)
* Stack: Python 3, FastAPI, Anthropic SDK.
* Responsibilities: Handles tool_use loops and agent tasks. Receives decrypted Anthropic API key per-request from C# backend. Returns SSE.

### 4. /uniscraper (University Scraper Microservice)
* Stack: Python 3, FastAPI (Migrating to MCP).
* Responsibilities: Parameterized scraping of university data based on user context.

### 5. Database
* Stack: PostgreSQL 16.

## Core Architectural Rules

### Security and Encryption
* API Keys must be encrypted at rest in database using AES-GCM.
* Decryption happens strictly in-memory in C# backend during external API calls.
* Use HTTP-Only cookies for Refresh Tokens. Short-lived JWT Access Tokens.

### User Management (RBAC)
* Closed System: Users cannot register.
* Admin only: Creates users, sets default passwords, manages accounts.

### Database Rules
* Soft Deletes (IsDeleted = true) for users and chat sessions.
* PublicId (GUID) for routing and API endpoints.
* Tenant isolation: Query entities strictly with UserId.

### LLM Context Management (Sliding Window Optimized)
* Do not send entire chat history to LLMs.
* Use sliding window (e.g., max 20 latest messages) managed at DB query level to prevent context window overflows.

