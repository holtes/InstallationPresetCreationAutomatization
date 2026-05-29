#!/bin/bash
# ============================================================
# Quick start script for the Interactive Installations Platform
# ============================================================
# Usage:
#   ./start.sh          — start all services via Docker Compose
#   ./start.sh dev      — start in development mode (local Python)
#   ./start.sh stop     — stop all Docker services
# ============================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

# Ensure .env exists
if [ ! -f .env ]; then
    echo "Creating .env from .env.example..."
    cp .env.example .env
    echo ">>> Please edit .env and set your OPENAI_API_KEY, then re-run."
    exit 1
fi

case "${1:-docker}" in
    dev)
        echo "=== Starting in development mode ==="
        echo "Make sure PostgreSQL and Redis are running locally."
        echo ""

        # Create venv if needed
        if [ ! -d .venv ]; then
            echo "Creating virtual environment..."
            python -m venv .venv
        fi

        source .venv/bin/activate 2>/dev/null || source .venv/Scripts/activate

        echo "Installing dependencies..."
        pip install -r requirements.txt -q

        echo "Starting Celery worker in background..."
        celery -A app.tasks.celery_app:celery_app worker --loglevel=info --concurrency=2 &
        CELERY_PID=$!

        echo "Starting API server..."
        echo "API docs: http://localhost:8000/docs"
        uvicorn app.main:app --host 0.0.0.0 --port 8000 --reload

        # Cleanup
        kill $CELERY_PID 2>/dev/null
        ;;

    stop)
        echo "=== Stopping Docker services ==="
        docker compose down
        ;;

    docker|"")
        echo "=== Starting with Docker Compose ==="
        docker compose up --build -d
        echo ""
        echo "Services started:"
        echo "  API Server:  http://localhost:8000"
        echo "  API Docs:    http://localhost:8000/docs"
        echo "  PostgreSQL:  localhost:5432"
        echo "  Redis:       localhost:6379"
        echo ""
        echo "View logs: docker compose logs -f"
        echo "Stop:      docker compose down"
        ;;

    *)
        echo "Usage: $0 [dev|docker|stop]"
        exit 1
        ;;
esac
