# ============================================================
# OpenClaw Agent — Python FastAPI + .NET SDK + Node.js
# Adapted from original Dockerfile (Flask → FastAPI/Uvicorn)
# ============================================================

FROM python:3.12-slim-bookworm AS base

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl wget git zip unzip ca-certificates build-essential \
    && rm -rf /var/lib/apt/lists/*

# ── .NET SDK ──────────────────────────────────────────────
FROM base AS dotnet-installer
RUN wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet \
    && rm dotnet-install.sh

# ── Final image ──────────────────────────────────────────
FROM python:3.12-slim-bookworm AS final

LABEL maintainer="studentapp" \
      version="2.0" \
      description="OpenClaw AI Agent — FastAPI + Claude + .NET + Node.js"

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH="/usr/share/dotnet:/opt/venv/bin:$PATH" \
    WORKSPACE_DIR=/workspace \
    PORT=8000

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl wget git zip unzip ca-certificates libicu72 libssl3 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=dotnet-installer /usr/share/dotnet /usr/share/dotnet

# Node.js 20 LTS
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -gid 1000 agentuser \
    && useradd --uid 1000 --gid agentuser --shell /bin/bash --create-home agentuser

RUN mkdir -p /workspace /app && chown -R agentuser:agentuser /workspace /app

WORKDIR /app

RUN python -m venv /opt/venv && chown -R agentuser:agentuser /opt/venv

COPY requirements.txt /app/requirements.txt
RUN /opt/venv/bin/pip install --upgrade pip \
    && /opt/venv/bin/pip install -r requirements.txt

COPY src/ /app/src/
RUN chown -R agentuser:agentuser /app

USER agentuser

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8000/health || exit 1

# Uvicorn replaces Gunicorn+Flask
CMD ["/opt/venv/bin/uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "2", "--app-dir", "/app/src"]
